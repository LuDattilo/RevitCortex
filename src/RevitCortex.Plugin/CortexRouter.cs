using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Caching;
using RevitCortex.Core.Discovery;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin.Threading;

namespace RevitCortex.Plugin;

public class CortexRouter
{
    private readonly Dictionary<string, ICortexTool> _tools = new();
    private readonly CortexSession _session;
    private readonly IDocumentAnalyzer _analyzer;
    private readonly AuditLogger _auditLogger;
    // volatile: set once from OnStartup (UI thread) but read from the socket
    // worker thread inside Route. The cheap guarantee is a full acquire/release
    // barrier so the worker never sees a partially-initialised dispatcher.
    private volatile RevitThreadDispatcher? _dispatcher;

    // UI-thread id, captured when the dispatcher is wired in OnStartup.
    // Used to detect callers that are ALREADY on the UI thread (e.g. WPF
    // button handlers like the Power BI Export panel) so we can run the
    // tool inline instead of dispatching via ExternalEvent — which would
    // deadlock because Revit's external-event machinery can only fire when
    // the UI thread is idle, and a UI-thread caller blocked in
    // WaitForCompletion holds it busy until timeout.
    private int _uiThreadId;
    // H27: read on socket worker threads (Route) and written from the WPF UI thread
    // (SetDisabledTools). A mutated HashSet races (Clear/Add visible mid-read). System
    // .Collections.Immutable is unavailable on net48 (R23/R24) without an extra NuGet, so
    // we use copy-on-write: writers build a brand-new HashSet and swap the volatile
    // reference atomically; readers only ever see a fully-built, never-mutated instance.
    private volatile HashSet<string> _disabledTools = new();
    private bool _readOnlyMode;

    /// <summary>
    /// Prefixes that identify read-only (query-only) tools.
    /// Tools matching these prefixes are allowed in read-only mode.
    /// </summary>
    private static readonly string[] ReadOnlyPrefixes = new[]
    {
        "get_", "list_", "find_", "analyze_", "check_",
        "measure_", "audit_", "export_", "say_hello",
        "clash_detection", "lines_per_view_count",
        "ifc_get_", "ifc_list_", "ifc_export_", "ifc_validate_",
        "ifc_analyze_", "ifc_compare_"
    };

    /// <summary>
    /// Write-named tools vetted for inline UI-thread execution: they open no
    /// Transaction and show no Revit UI (verified at adoption time). The inline
    /// path runs OUTSIDE a Revit API context (modeless WPF handlers), where a
    /// Transaction would throw — keep this list minimal and audited.
    /// </summary>
    private static readonly HashSet<string> InlineUiThreadAllowedTools =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "push_to_powerbi",
    };

    public CortexRouter(CortexSession session, IDocumentAnalyzer analyzer, AuditLogger? auditLogger = null)
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
                _tools[tool.Name] = tool;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Failed to register tool {type.Name}: {ex.Message}");
            }
        }
    }

    public CortexResult<object> Route(string toolName, JObject input)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{toolName}' not found",
                suggestion: $"Available tools: {string.Join(", ", GetAvailableToolNames())}");

        if (_disabledTools.Contains(toolName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{toolName}' is disabled",
                suggestion: "Enable it in RevitCortex Settings > Tools");

        if (tool.RequiresDocument && _session.Store.Get<object>("activeDocument") == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No document open in Revit",
                suggestion: "Open a Revit document before using this tool");

        if (tool.IsDynamic && !_session.Capabilities.IsToolEnabled(toolName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{toolName}' is not available for this document",
                suggestion: "This tool requires specific document features (e.g., worksets, phases)");

        // Read-only mode: block write tools
        if (_readOnlyMode && !IsReadOnlyTool(toolName))
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                $"Tool '{toolName}' is blocked in read-only mode",
                suggestion: "Disable read-only mode in Settings to allow write operations");

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
                    _session.DocumentVersion, out var cached, out var cachedBytes))
            {
                stopwatch.Stop();
                _auditLogger.LogWithPerf(toolName, BuildInputSummary(toolName, input),
                    cached.Success, cached.Error?.Code, elementsAffected: 0,
                    durationMs: stopwatch.ElapsedMilliseconds,
                    // The entry's stored estimate: re-serializing the result on every
                    // hit would defeat the point of caching it.
                    responseBytes: cachedBytes,
                    errorMessage: cached.Error?.Message);
                return cached;
            }
        }

        try
        {
            // Dispatch path:
            //  - From a background thread (socket worker, listener, etc.):
            //    go through ExternalEvent so the tool runs on Revit's UI
            //    thread (Revit API requirement).
            //  - From the UI thread itself (e.g. PowerBiExportWindow's
            //    "Esporta" button handler): run inline. Going through
            //    ExternalEvent here would deadlock — see _uiThreadId comment.
            bool onUiThread = _dispatcher != null
                && System.Threading.Thread.CurrentThread.ManagedThreadId == _uiThreadId;

            if (_dispatcher != null && !onUiThread)
            {
                var timeoutSeconds = (tool as ICommandTimeoutTool)?.CommandTimeoutSeconds ?? 120;
                result = _dispatcher.Execute(tool, input, _session, timeoutSeconds * 1000);
            }
            else if (onUiThread && !IsReadOnlyTool(toolName)
                     && !InlineUiThreadAllowedTools.Contains(toolName))
            {
                // The inline path runs on the UI thread but outside a Revit API
                // context: a tool opening a Transaction here would throw inside
                // Revit. Only read-only tools and the vetted allowlist may pass.
                result = CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                    $"Tool '{toolName}' cannot run inline on the UI thread outside a Revit API context",
                    suggestion: "Call the tool through the MCP/TCP bridge so it is dispatched via ExternalEvent.");
            }
            else
            {
                result = tool.Execute(input, _session);
            }
        }
        finally
        {
            // Reset only the per-batch "Yes to All" flag after each tool. AutoMode
            // ("Auto") must persist across tool calls until the user clicks Stop Auto
            // or the document is reinitialized — calling ResetApproveAll() here would
            // clear AutoMode too and re-prompt on every subsequent destructive op.
            _session.ApproveAll = false;
        }

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
            result.Error?.Code, elementsAffected: 0,
            durationMs: stopwatch.ElapsedMilliseconds,
            responseBytes: responseBytes,
            codeSnippet: codeSnippet,
            codeHash: codeHash,
            errorMessage: result.Error?.Message);

        return result;
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

    public bool ReadOnlyMode
    {
        get => _readOnlyMode;
        set => _readOnlyMode = value;
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
        // SetDispatcher is called from OnStartup on the Revit UI thread, so
        // capturing here gives us the right id to compare against in Route.
        _uiThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
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
            .Where(t => !_disabledTools.Contains(t.Name))
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
            .Select(t => (t.Name, t.Category, t.Description, !_disabledTools.Contains(t.Name)))
            .ToList();
    }

    public void SetDisabledTools(IEnumerable<string> toolNames)
    {
        // H27: build a new set, then swap the reference atomically — readers never observe
        // a Clear/Add window.
        _disabledTools = new HashSet<string>(toolNames);
    }

    public IReadOnlyCollection<string> DisabledTools => _disabledTools;
}
