using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Caching;
using RiveTT.Core.Discovery;
using RiveTT.Core.Results;
using RiveTT.Core.Security;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Plugin.Threading;

namespace RiveTT.Plugin;

public class CortexRouter
{
    private readonly Dictionary<string, ICortexTool> _tools = new();
    private readonly Dictionary<string, ToolSafetyRegistration> _toolSafety =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CortexSession _session;
    private readonly IDocumentAnalyzer _analyzer;
    private readonly AuditLogger _auditLogger;
    // volatile: set once from OnStartup (UI thread) but read from the pipe
    // worker thread inside Route. The cheap guarantee is a full acquire/release
    // barrier so the worker never sees a partially-initialised dispatcher.
    private volatile RevitThreadDispatcher? _dispatcher;

    /// <summary>
    /// Prefixes that identify read-only (query-only) tools for safety metadata.
    /// </summary>
    private static readonly string[] ReadOnlyPrefixes = new[]
    {
        "get_", "list_", "find_", "analyze_", "check_",
        "measure_", "audit_", "export_", "ping_revit",
        "detect_clashes", "count_lines_per_view",
        "ifc_get_", "ifc_list_", "ifc_export_", "ifc_validate_",
        "ifc_analyze_", "ifc_compare_"
    };

    /// <summary>
    /// Tools that change which file the session is working on. Their success must
    /// flush every cache scope, Session included.
    /// </summary>
    private static readonly HashSet<string> LifecycleWriteTools =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "save_document", "save_as_document", "create_document", "open_document"
        };

    private sealed class ToolSafetyRegistration
    {
        public ToolSafetyRegistration(bool readOnly, bool destructive, bool declared)
        {
            ReadOnly = readOnly;
            Destructive = destructive;
            Declared = declared;
        }

        public bool ReadOnly { get; }
        public bool Destructive { get; }
        public bool Declared { get; }
    }

    public CortexRouter(CortexSession session, IDocumentAnalyzer analyzer,
        AuditLogger? auditLogger = null)
    {
        _session = session;
        _analyzer = analyzer;
        _auditLogger = auditLogger ?? new AuditLogger();
    }

    /// <summary>
    /// Scan an assembly for all ICortexTool implementations and register them.
    /// </summary>
    public void RegisterToolsFromAssembly(Assembly assembly)
    {
        var toolTypes = assembly.GetTypes()
            .Where(t => typeof(ICortexTool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var type in toolTypes)
        {
            try
            {
                var tool = (ICortexTool)Activator.CreateInstance(type)!;
                RegisterTool(tool, type);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[RiveTT] Failed to register tool {type.Name}: {ex.Message}");
            }
        }
    }

    public void RegisterTool(ICortexTool tool)
    {
        RegisterTool(tool, tool.GetType());
    }

    private void RegisterTool(ICortexTool tool, Type toolType)
    {
        _tools[tool.Name] = tool;

        var safety = ResolveToolSafety(tool, toolType);
        _toolSafety[tool.Name] = safety;

        var prefixReadOnly = IsReadOnlyTool(tool.Name);
        if (safety.Declared && safety.ReadOnly != prefixReadOnly)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RiveTT] Tool safety mismatch for {tool.Name}: " +
                $"declared ReadOnly={safety.ReadOnly}, prefix ReadOnly={prefixReadOnly}.");
        }
        else if (!safety.Declared)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RiveTT] Tool {tool.Name} has no [ToolSafety]; using prefix fallback.");
        }
    }

    private static ToolSafetyRegistration ResolveToolSafety(ICortexTool tool, Type toolType)
    {
        var aware = tool as IToolSafetyAware;
        if (aware != null)
        {
            var info = aware.GetToolSafety();
            return new ToolSafetyRegistration(info.ReadOnly, info.Destructive, declared: true);
        }

        var attribute = (ToolSafetyAttribute?)Attribute.GetCustomAttribute(
            toolType, typeof(ToolSafetyAttribute), inherit: true);
        if (attribute != null)
        {
            return new ToolSafetyRegistration(
                attribute.ReadOnly, attribute.Destructive, declared: true);
        }

        return new ToolSafetyRegistration(
            IsReadOnlyTool(tool.Name), destructive: false, declared: false);
    }

    public CortexResult<object> Route(string toolName, JObject input, string? publicToolName = null)
    {
        // The name to show the caller: several agent-facing MCP tools route
        // through one shared generic RiveTT tool (create_wall, create_door and
        // create_window all reach here as create_line_based_element or
        // create_point_based_element). Messages must name what was actually
        // called, not the internal handler — see P1.6 in PLAN_CORRECTION.md.
        var displayName = string.IsNullOrEmpty(publicToolName) ? toolName : publicToolName;

        if (!_tools.TryGetValue(toolName, out var tool))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{displayName}' not found",
                suggestion: $"Available tools: {string.Join(", ", GetAvailableToolNames())}");

        // The ribbon write lock, checked before everything else: a locked session
        // must answer the same whether or not a document is open, and the refusal
        // must never be mistaken for a missing document. No tool can lift it —
        // only a human, from the RiveTT panel of the Revit ribbon.
        if (!_session.WriteAccess.WritesAllowed && !IsToolReadOnly(toolName))
        {
            var refusal = CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                $"'{displayName}' can modify the model and RiveTT is currently in read-only mode.",
                suggestion: "In Revit: Add-Ins tab (Complements) > RiveTT panel > Write. " +
                            "No tool can unlock it; it is a human decision taken in Revit. " +
                            "Read tools keep working meanwhile.",
                context: new Dictionary<string, object>
                {
                    ["stage"] = "permission",
                    ["writesAllowed"] = false,
                    ["toolReadOnly"] = false,
                    ["modelChanged"] = false,
                    ["lockedSince"] = _session.WriteAccess.ChangedUtc.ToString("o"),
                    ["lockedBy"] = _session.WriteAccess.ChangedBy,
                    ["unlockFrom"] = "Revit ribbon, Add-Ins tab, RiveTT panel"
                });

            // Refusals belong in the audit log as much as writes do: this is the
            // trace showing an agent tried to write while the model was locked.
            _auditLogger.LogWithPerf(toolName, BuildInputSummary(toolName, input),
                success: false, errorCode: CortexErrorCode.PermissionDenied,
                elementsAffected: 0, durationMs: 0, responseBytes: 0,
                errorMessage: refusal.Error?.Message);
            return refusal;
        }

        if (tool.RequiresDocument && _session.Store.Get<object>("activeDocument") == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No document open in Revit",
                suggestion: "Open a Revit document before using this tool");

        if (tool.IsDynamic && !_session.Capabilities.IsToolEnabled(toolName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{toolName}' is not available for this document",
                suggestion: "This tool requires specific document features (e.g., worksets, phases)");

        var stopwatch = Stopwatch.StartNew();
        CortexResult<object> result;

        // Cache lookup for read-only tools that opted into ICacheableTool.
        // On hit, skip the dispatcher entirely — no UI-thread marshal is needed
        // to return a previously-computed value.
        var cacheable = tool as ICacheableTool;
        string? paramHash = null;
        if (cacheable != null)
        {
            paramHash = HashParams(input);
            if (_session.Cache.TryGet(toolName, paramHash, cacheable.CacheScope,
                    _session.DocumentVersion, out var cachedResult, out var cachedBytes))
            {
                // A cached answer must say so. A 0 ms reply carrying the pre-Save-As
                // project path was indistinguishable from a fresh read of stale state.
                var cached = MarkCached(cachedResult);
                stopwatch.Stop();
                _auditLogger.LogWithPerf(toolName, BuildInputSummary(toolName, input),
                    cached.Success, cached.Error?.Code,
                    elementsAffected: EstimateElementsAffected(cached),
                    durationMs: stopwatch.ElapsedMilliseconds,
                    // The entry's stored estimate: re-serializing the result on every
                    // hit would defeat the point of caching it.
                    responseBytes: cachedBytes,
                    errorMessage: cached.Error?.Message,
                    outputSummary: BuildOutputSummary(cached));
                return cached;
            }
        }

        try
        {
            // Named-pipe requests arrive off the Revit UI thread. ExternalEvent is
            // therefore the single execution path for every Revit operation.
            if (_dispatcher != null)
            {
                var timeoutSeconds = (tool as ICommandTimeoutTool)?.CommandTimeoutSeconds ?? 120;
                result = _dispatcher.Execute(tool, input, _session, timeoutSeconds * 1000);
            }
            else
            {
                result = tool.Execute(input, _session);
            }
        }
        catch (Exception ex)
        {
            // Route-wide backstop: NOTHING may escape Route unstructured —
            // an escaping exception would skip audit and surface
            // as a raw JSON-RPC -32603 (paid-readiness spec, P1 finding).
            System.Diagnostics.Trace.WriteLine(
                $"[RiveTT] Route('{toolName}') unhandled: {ex}");
            result = CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Unhandled exception: {ex.Message}",
                suggestion: "Retry the command; if it persists, inspect the local audit log.");
        }

        // Belt and braces around the Revit DocumentSavedAs/DocumentSaved events: a
        // lifecycle write changes the document's identity, so nothing cached about
        // the previous file may survive it, even if the event never reaches us.
        if (result.Success && LifecycleWriteTools.Contains(toolName) &&
            input["dryRun"]?.Value<bool>() != true)
        {
            _session.BumpDocumentVersion();
            _session.Cache.InvalidateAll();
        }

        result = EnrichResult(toolName, input, result);
        // One serialization serves both the audit byte count and the cache entry's
        // estimate — Set used to re-serialize the same result a second time.
        var responseBytes = EstimateResponseBytes(result);

        // Only cache successful results. Failures must always re-execute so a
        // transient error doesn't get stuck in the cache.
        if (cacheable != null && paramHash != null && result.Success)
        {
            _session.Cache.Set(toolName, paramHash, cacheable.CacheScope,
                _session.DocumentVersion, result, knownBytes: responseBytes);
        }

        stopwatch.Stop();

        // Audit log (schema v2): every invocation, with duration and response size.
        // send_code_to_revit also gets a code snapshot (truncated) + SHA-256 hash.
        var inputSummary = BuildInputSummary(toolName, input);
        string? codeSnippet = null;
        string? codeHash = null;
        if (toolName == "send_code_to_revit")
        {
            var code = input["code"]?.Value<string>();
            if (!string.IsNullOrEmpty(code))
            {
                codeSnippet = code!.Length <= 500 ? code : code.Substring(0, 500);
                codeHash = ComputeSha256(code!);
            }
        }

        _auditLogger.LogWithPerf(toolName, inputSummary, result.Success,
            result.Error?.Code, elementsAffected: EstimateElementsAffected(result),
            durationMs: stopwatch.ElapsedMilliseconds,
            responseBytes: responseBytes,
            codeSnippet: codeSnippet,
            codeHash: codeHash,
            errorMessage: result.Error?.Message,
            outputSummary: BuildOutputSummary(result));

        return result;
    }

    private CortexResult<object> EnrichResult(
        string toolName, JObject input, CortexResult<object> result)
    {
        if (!result.Success)
        {
            if (result.Error?.Code != CortexErrorCode.TransactionFailed)
                return result;

            var context = result.Error.Context != null
                ? new Dictionary<string, object>(result.Error.Context)
                : new Dictionary<string, object>();
            if (!context.ContainsKey("warnings")) context["warnings"] = Array.Empty<string>();
            if (!context.ContainsKey("errors")) context["errors"] = new[] { result.Error.Message };
            if (!context.ContainsKey("rolledBack")) context["rolledBack"] = true;
            if (!context.ContainsKey("failedElementIds")) context["failedElementIds"] = Array.Empty<long>();
            if (!context.ContainsKey("repairHints"))
                context["repairHints"] = string.IsNullOrWhiteSpace(result.Error.Suggestion)
                    ? Array.Empty<string>()
                    : new[] { result.Error.Suggestion! };

            return CortexResult<object>.Fail(
                result.Error.Code, result.Error.Message, result.Error.Suggestion, context);
        }

        var data = result.Data == null
            ? new JObject()
            : JToken.FromObject(result.Data);
        var obj = data as JObject ?? new JObject { ["value"] = data };
        var isReadOnly = IsToolReadOnly(toolName);
        var dryRun = input["dryRun"]?.Value<bool>() == true;

        if (dryRun && !isReadOnly)
        {
            obj["dryRun"] = true;
            obj["mutated"] = false;
        }

        obj["execution"] = new JObject
        {
            ["connector"] = "RiveTT",
            ["serverVersion"] = typeof(CortexRouter).Assembly.GetName().Version?.ToString() ?? "0.0.0.0",
            ["revitVersion"] = GetActiveRevitVersion(),
            ["mode"] = "automatic",
            // toolReadOnly classifies THIS tool. It was named "readOnly", which read
            // as a server-wide lock and made callers believe writes were forbidden.
            // writesAllowed is the session-wide fact, driven by the ribbon toggle:
            // when it is false, every tool with toolReadOnly=false is refused.
            ["toolReadOnly"] = isReadOnly,
            ["toolDestructive"] = IsToolDestructive(toolName),
            ["writesAllowed"] = _session.WriteAccess.WritesAllowed,
            ["cached"] = false
        };
        return CortexResult<object>.Ok(obj);
    }

    /// <summary>
    /// Reads the real Revit version from the active document's Application, so this
    /// connector reports "2026" correctly when its plugin/tools DLLs were built with
    /// -p:RevitVersion=2026 and are running inside Revit 2026.5 — not a literal that
    /// only ever matched the 2027-targeted build.
    /// </summary>
    private string GetActiveRevitVersion()
    {
        try
        {
            return (_session.Store.Get<object>("activeDocument") as Autodesk.Revit.DB.Document)
                ?.Application?.VersionNumber ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Flags a cache hit in the response's execution block. The stored entry is
    /// left untouched — flipping the flag in place would make the next hit lie.
    /// </summary>
    private static CortexResult<object> MarkCached(CortexResult<object> cached)
    {
        try
        {
            if (JToken.FromObject(cached.Data ?? new JObject()) is not JObject data)
                return cached;

            var clone = (JObject)data.DeepClone();
            if (clone["execution"] is JObject execution)
                execution["cached"] = true;
            else
                clone["execution"] = new JObject { ["cached"] = true };

            return CortexResult<object>.Ok(clone);
        }
        catch
        {
            return cached;
        }
    }

    private static int EstimateElementsAffected(CortexResult<object> result)
    {
        if (!result.Success || result.Data == null) return 0;
        try
        {
            var data = JToken.FromObject(result.Data);
            foreach (var key in new[]
                     {
                         "modified", "modifiedCount", "successCount", "createdCount",
                         "deletedCount", "processed", "processedCount", "elementCount"
                     })
            {
                var count = data[key]?.Value<int?>();
                if (count.HasValue) return Math.Max(0, count.Value);
            }

            foreach (var key in new[]
                     {
                         "createdElementIds", "modifiedElementIds", "deletedElementIds", "elementIds"
                     })
            {
                if (data[key] is JArray ids) return ids.Count;
            }
        }
        catch { }
        return 0;
    }

    private static string BuildOutputSummary(CortexResult<object> result)
    {
        if (!result.Success)
            return $"error={result.Error?.Code}; rolledBack={result.Error?.Context?.ContainsKey("rolledBack") == true}";
        if (result.Data == null) return "(no data)";

        try
        {
            var data = JToken.FromObject(result.Data);
            if (data is not JObject obj) return FormatValue("result", data);
            var parts = new List<string>();
            foreach (var prop in obj.Properties())
            {
                if (prop.Name == "execution") continue;
                parts.Add($"{prop.Name}={FormatValue(prop.Name, prop.Value)}");
                if (parts.Count >= 12) break;
            }
            return parts.Count == 0 ? "(empty)" : string.Join(", ", parts);
        }
        catch { return "(unserializable)"; }
    }

    private static long EstimateResponseBytes(CortexResult<object> result)
    {
        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            return Encoding.UTF8.GetByteCount(json);
        }
        catch
        {
            return 0;
        }
    }

    private static string ComputeSha256(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// Canonical SHA-256 of a tool's input. Keys are sorted recursively and
    /// the JSON is emitted without whitespace, so calls that differ only in
    /// key order or formatting hit the same cache entry.
    /// </summary>
    internal static string HashParams(JObject input)
    {
        // Emit the canonical JSON directly (object keys sorted recursively, no whitespace)
        // instead of building a parallel sorted JToken tree and deep-cloning every leaf.
        // The byte output is identical to the previous Canonicalize(...).ToString(Formatting.None),
        // so cache keys are unchanged (locked by CortexRouterHashStabilityTests).
        var sw = new System.IO.StringWriter(new StringBuilder(256),
            System.Globalization.CultureInfo.InvariantCulture);
        using (var writer = new JsonTextWriter(sw) { Formatting = Formatting.None })
        {
            WriteCanonical(writer, input);
        }
        return ComputeSha256(sw.ToString());
    }

    private static void WriteCanonical(JsonTextWriter writer, JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                writer.WriteStartObject();
                foreach (var prop in ((JObject)token).Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Name);
                    WriteCanonical(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;
            case JTokenType.Array:
                writer.WriteStartArray();
                foreach (var item in (JArray)token)
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                token.WriteTo(writer);
                break;
        }
    }

    /// <summary>
    /// Determines if a tool is read-only (query-only) based on naming convention.
    /// </summary>
    public static bool IsReadOnlyTool(string toolName)
    {
        foreach (var prefix in ReadOnlyPrefixes)
        {
            if (toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public bool IsToolReadOnly(string toolName)
    {
        return _toolSafety.TryGetValue(toolName, out var safety)
            ? safety.ReadOnly
            : IsReadOnlyTool(toolName);
    }

    public bool IsToolDestructive(string toolName)
    {
        return _toolSafety.TryGetValue(toolName, out var safety) && safety.Destructive;
    }

    private static string BuildInputSummary(string toolName, JObject input)
    {
        if (input == null || !input.HasValues) return "(no params)";

        var parts = new List<string>(input.Count);
        foreach (var prop in input.Properties())
        {
            parts.Add($"{prop.Name}={FormatValue(prop.Name, prop.Value)}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "(no params)";
    }

    private static string FormatValue(string name, JToken token)
    {
        // send_code_to_revit and similar tools pass large C# snippets — log
        // length only, never the body.
        if (token.Type == JTokenType.String &&
            (name == "code" || name == "snippet"))
        {
            return $"({token.ToString().Length} chars)";
        }

        switch (token.Type)
        {
            case JTokenType.Null:
            case JTokenType.Undefined:
                return "null";
            case JTokenType.Array:
                var arr = (JArray)token;
                return $"[{arr.Count} items]";
            case JTokenType.Object:
                return token.ToString(Newtonsoft.Json.Formatting.None);
            case JTokenType.String:
                var s = token.ToString();
                return s.Length > 80 ? s.Substring(0, 80) + "..." : s;
            default:
                return token.ToString();
        }
    }

    public void SetDispatcher(RevitThreadDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void OnDocumentChanged(object document, string? locale = null)
    {
        var caps = new DocumentCapabilities();
        _analyzer.Analyze(document, caps);

        _session.Reinitialize(caps, locale ?? "en");
        _session.Store.Set("activeDocument", document);
    }

    public IReadOnlyList<string> GetAvailableToolNames()
    {
        return _tools.Values
            .Where(t => !t.IsDynamic || _session.Capabilities.IsToolEnabled(t.Name))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();
    }

    public int TotalToolCount => _tools.Count;

    /// <summary>
    /// Returns all registered tools with their name, category, description, and enabled state.
    /// </summary>
    public IReadOnlyList<(string Name, string Category, string Description, bool IsEnabled)> GetAllToolInfo()
    {
        return _tools.Values
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Name)
            .Select(t => (t.Name, t.Category, t.Description, true))
            .ToList();
    }
}
