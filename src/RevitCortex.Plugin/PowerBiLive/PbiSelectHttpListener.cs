using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// Lightweight HTTP listener on port 27016 (configurable) that accepts
/// requests from the Power BI Desktop custom visual.
///
/// Endpoints (all POST, with OPTIONS preflight + CORS):
///   POST /pbi-select          { elementIds, action: "select"|"isolate" }
///   POST /pbi-color           { items: [{ id, hex }, ...] }
///   POST /pbi-reset-overrides { }
///   POST /pbi-create-view     { elementIds, viewName? }
///
/// Responsibilities:
///   - Parse the JSON body
///   - Validate that a callback handler is registered (i.e. Revit is active)
///   - Invoke the callback on the background thread; RevitCortexApp is responsible
///     for marshalling to the Revit main thread via ExternalEventHandler
///
/// The listener has no direct dependency on Revit API types, making it
/// fully testable without RevitAPI.dll.
///
/// Threading: listener loop runs on a dedicated background thread.
///
/// Port conflict: if the port is already in use (second Revit instance),
/// Start() logs a warning and returns without throwing.
/// </summary>
public class PbiSelectHttpListener : IDisposable
{
    /// <summary>
    /// Callbacks for the supported request types. Each one runs on the
    /// listener background thread and is expected to validate state and
    /// raise an ExternalEvent — Revit API calls are illegal from here.
    /// Return value: null → "no active document" (success=false in response).
    ///               non-null → the response will include validated=&lt;value&gt;.
    /// </summary>
    public sealed class Callbacks
    {
        public Func<IList<long>, string, string?> Selection { get; }
        public Func<IList<ColorOverride>, string?>? Color { get; }
        public Func<string?>? ResetOverrides { get; }
        public Func<IList<long>, string?, string?>? CreateView { get; }

        public Callbacks(
            Func<IList<long>, string, string?> selection,
            Func<IList<ColorOverride>, string?>? color = null,
            Func<string?>? resetOverrides = null,
            Func<IList<long>, string?, string?>? createView = null)
        {
            Selection = selection ?? throw new ArgumentNullException(nameof(selection));
            Color = color;
            ResetOverrides = resetOverrides;
            CreateView = createView;
        }
    }

    /// <summary>
    /// Wire-level color override entry: raw element id + hex color (e.g. "#E53935").
    /// No Revit API types — kept simple so tests don't need RevitAPI.dll.
    /// </summary>
    public sealed class ColorOverride
    {
        public long Id { get; }
        public string Hex { get; }
        public ColorOverride(long id, string hex)
        {
            Id = id;
            Hex = hex ?? "";
        }
    }

    private readonly Callbacks _callbacks;
    private readonly int _port;

    private HttpListener? _httpListener;
    private Thread? _thread;
    private volatile bool _running;

    public bool IsRunning => _running;

    /// <summary>
    /// Creates a new listener with full callback set.
    /// </summary>
    public PbiSelectHttpListener(Callbacks callbacks, int port = 27016)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        _port = port;
    }

    /// <summary>
    /// Backwards-compatible constructor that only wires the /pbi-select callback.
    /// Used by older tests and code paths.
    /// </summary>
    public PbiSelectHttpListener(
        Func<IList<long>, string, string?> handleSelection,
        int port = 27016)
        : this(new Callbacks(handleSelection), port)
    {
    }

    public void Start()
    {
        if (_running) return;

        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://localhost:{_port}/");
            _httpListener.Start();
        }
        catch (HttpListenerException ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[PbiSelectHttpListener] Port {_port} already in use — listener skipped. ({ex.ErrorCode})");
            _httpListener = null;
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[PbiSelectHttpListener] Failed to start: {ex.Message}");
            _httpListener = null;
            return;
        }

        _running = true;
        _thread = new Thread(ListenLoop) { IsBackground = true, Name = "PbiSelectHttpListener" };
        _thread.Start();

        System.Diagnostics.Trace.WriteLine(
            $"[PbiSelectHttpListener] Listening on port {_port}.");
    }

    /// <summary>Stops the listener and releases the port.</summary>
    public void Stop()
    {
        _running = false;
        try { _httpListener?.Stop(); } catch { }
        _httpListener = null;

        // Wait briefly for the background thread to exit GetContext.
        // Without this, Revit can unload the assembly while the thread is
        // still in a blocking call → AppDomainUnloadedException on shutdown.
        try { _thread?.Join(500); } catch { }
        _thread = null;
    }

    public void Dispose() => Stop();

    // ─── Background listener loop ──────────────────────────────────────────

    private void ListenLoop()
    {
        while (_running)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = _httpListener?.GetContext();
            }
            catch (HttpListenerException)
            {
                break; // Listener stopped — exit loop normally
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[PbiSelectHttpListener] GetContext error: {ex.Message}");
                break;
            }

            if (ctx == null) break;

            try
            {
                HandleRequest(ctx);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[PbiSelectHttpListener] HandleRequest error: {ex.Message}");
                try { ctx.Response.Abort(); } catch { }
            }
        }

        _running = false;
        System.Diagnostics.Trace.WriteLine("[PbiSelectHttpListener] Loop exited.");
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        var req  = ctx.Request;
        var resp = ctx.Response;

        // CORS headers on every response (required for Power BI Desktop WebView preflight)
        resp.AddHeader("Access-Control-Allow-Origin", "*");
        resp.AddHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
        resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        // OPTIONS preflight — respond 200 with CORS headers only
        if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            resp.StatusCode = 200;
            resp.Close();
            return;
        }

        // Only POST accepted
        if (!req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            resp.StatusCode = 405;
            resp.Close();
            return;
        }

        // Read body
        string bodyText;
        try
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            bodyText = reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            WriteJson(resp, 400, new { success = false, error = $"Failed to read body: {ex.Message}" });
            return;
        }

        var path = (req.Url?.AbsolutePath ?? "/pbi-select").TrimEnd('/').ToLowerInvariant();

        switch (path)
        {
            case "/pbi-select":
                HandleSelect(resp, bodyText);
                return;
            case "/pbi-color":
                HandleColor(resp, bodyText);
                return;
            case "/pbi-reset-overrides":
                HandleResetOverrides(resp);
                return;
            case "/pbi-create-view":
                HandleCreateView(resp, bodyText);
                return;
            default:
                WriteJson(resp, 404, new { success = false, error = $"Unknown endpoint: {path}" });
                return;
        }
    }

    // ─── /pbi-select ───────────────────────────────────────────────────────

    private void HandleSelect(HttpListenerResponse resp, string bodyText)
    {
        if (!TryParseBody(resp, bodyText, out var body)) return;

        var rawIds = body["elementIds"] as JArray;
        var action = (body["action"]?.Value<string>() ?? "select").ToLowerInvariant();

        if (rawIds == null || rawIds.Count == 0)
        {
            WriteJson(resp, 200, new { success = true, elementCount = 0, warning = "No ElementIds provided." });
            return;
        }

        var ids = ParseLongArray(rawIds);
        System.Diagnostics.Trace.WriteLine(
            $"[PbiSelectHttpListener] /pbi-select: ids={ids.Count}, action={action}");

        var result = _callbacks.Selection(ids, action);
        if (result == null)
        {
            WriteJson(resp, 200, new { success = false, error = "No active Revit document." });
            return;
        }
        WriteJson(resp, 200, new { success = true, elementCount = ids.Count, action, validated = result });
    }

    // ─── /pbi-color ────────────────────────────────────────────────────────

    private void HandleColor(HttpListenerResponse resp, string bodyText)
    {
        if (_callbacks.Color == null)
        {
            WriteJson(resp, 501, new { success = false, error = "Color callback not registered." });
            return;
        }

        if (!TryParseBody(resp, bodyText, out var body)) return;

        var rawItems = body["items"] as JArray;
        if (rawItems == null || rawItems.Count == 0)
        {
            WriteJson(resp, 200, new { success = true, count = 0, warning = "No items provided." });
            return;
        }

        var items = new List<ColorOverride>(rawItems.Count);
        foreach (var token in rawItems)
        {
            try
            {
                var id = token["id"]?.Value<long>() ?? 0;
                var hex = token["hex"]?.Value<string>() ?? "";
                if (id != 0 && !string.IsNullOrEmpty(hex))
                    items.Add(new ColorOverride(id, hex));
            }
            catch { /* skip unparseable */ }
        }

        System.Diagnostics.Trace.WriteLine(
            $"[PbiSelectHttpListener] /pbi-color: items={items.Count}");

        var result = _callbacks.Color(items);
        if (result == null)
        {
            WriteJson(resp, 200, new { success = false, error = "No active Revit document or cancelled." });
            return;
        }
        WriteJson(resp, 200, new { success = true, count = items.Count, validated = result });
    }

    // ─── /pbi-reset-overrides ──────────────────────────────────────────────

    private void HandleResetOverrides(HttpListenerResponse resp)
    {
        if (_callbacks.ResetOverrides == null)
        {
            WriteJson(resp, 501, new { success = false, error = "Reset callback not registered." });
            return;
        }

        System.Diagnostics.Trace.WriteLine("[PbiSelectHttpListener] /pbi-reset-overrides");

        var result = _callbacks.ResetOverrides();
        if (result == null)
        {
            WriteJson(resp, 200, new { success = false, error = "No active Revit document or cancelled." });
            return;
        }
        WriteJson(resp, 200, new { success = true, validated = result });
    }

    // ─── /pbi-create-view ──────────────────────────────────────────────────

    private void HandleCreateView(HttpListenerResponse resp, string bodyText)
    {
        if (_callbacks.CreateView == null)
        {
            WriteJson(resp, 501, new { success = false, error = "CreateView callback not registered." });
            return;
        }

        if (!TryParseBody(resp, bodyText, out var body)) return;

        var rawIds = body["elementIds"] as JArray;
        var viewName = body["viewName"]?.Value<string>();

        if (rawIds == null || rawIds.Count == 0)
        {
            WriteJson(resp, 400, new { success = false, error = "No ElementIds provided." });
            return;
        }

        var ids = ParseLongArray(rawIds);
        System.Diagnostics.Trace.WriteLine(
            $"[PbiSelectHttpListener] /pbi-create-view: ids={ids.Count}, viewName={viewName ?? "(auto)"}");

        var result = _callbacks.CreateView(ids, viewName);
        if (result == null)
        {
            WriteJson(resp, 200, new { success = false, error = "No active Revit document or cancelled." });
            return;
        }
        WriteJson(resp, 200, new { success = true, elementCount = ids.Count, validated = result });
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static bool TryParseBody(HttpListenerResponse resp, string bodyText, out JObject body)
    {
        try
        {
            body = JObject.Parse(bodyText);
            return true;
        }
        catch
        {
            body = new JObject();
            WriteJson(resp, 400, new { success = false, error = "Invalid JSON body." });
            return false;
        }
    }

    private static IList<long> ParseLongArray(JArray arr)
    {
        var ids = new List<long>();
        foreach (var token in arr)
        {
            try { ids.Add(token.Value<long>()); }
            catch { /* skip unparseable */ }
        }
        return ids;
    }

    private static void WriteJson(HttpListenerResponse resp, int statusCode, object payload)
    {
        resp.StatusCode = statusCode;
        resp.ContentType = "application/json";
        var json = JsonConvert.SerializeObject(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.ContentLength64 = bytes.Length;
        try
        {
            resp.OutputStream.Write(bytes, 0, bytes.Length);
            resp.OutputStream.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[PbiSelectHttpListener] WriteJson failed (status={statusCode}): {ex.Message}");
        }
    }
}
