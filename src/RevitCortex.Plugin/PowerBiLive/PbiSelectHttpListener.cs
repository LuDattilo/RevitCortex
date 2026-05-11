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
/// POST /pbi-select requests from the Power BI Desktop custom visual.
///
/// Responsibilities:
///   - Parse the JSON body (elementIds + action)
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
    /// Called when a valid pbi-select POST arrives.
    /// Arguments: raw element ID longs, action string ("select" or "isolate").
    /// RevitCortexApp is responsible for marshalling to the Revit main thread.
    /// Returns null when Revit is not active (no open document), non-null on success
    /// or to forward an error message.
    /// </summary>
    private readonly Func<IList<long>, string, string?> _handleSelection;
    private readonly int _port;

    private HttpListener? _httpListener;
    private Thread? _thread;
    private volatile bool _running;

    public bool IsRunning => _running;

    /// <summary>
    /// Creates a new listener.
    /// </summary>
    /// <param name="handleSelection">
    ///   Called with (rawIds, action) when a valid POST arrives.
    ///   Return null to indicate "no active document" (listener replies with
    ///   success=false). Return any string (including empty) to indicate success.
    /// </param>
    /// <param name="port">TCP port, default 27016.</param>
    public PbiSelectHttpListener(
        Func<IList<long>, string, string?> handleSelection,
        int port = 27016)
    {
        _handleSelection = handleSelection ?? throw new ArgumentNullException(nameof(handleSelection));
        _port = port;
    }

    /// <summary>
    /// Starts the listener on a background thread.
    /// If the port is already occupied, logs a warning and returns without throwing.
    /// </summary>
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

        // Parse JSON
        JObject body;
        try
        {
            body = JObject.Parse(bodyText);
        }
        catch
        {
            WriteJson(resp, 400, new { success = false, error = "Invalid JSON body." });
            return;
        }

        var rawIds = body["elementIds"] as JArray;
        var action = body["action"]?.Value<string>() ?? "select";
        action = action.ToLowerInvariant();

        // Empty array
        if (rawIds == null || rawIds.Count == 0)
        {
            WriteJson(resp, 200, new { success = true, elementCount = 0, warning = "No ElementIds provided." });
            return;
        }

        // Parse ElementId longs
        var ids = new List<long>();
        foreach (var token in rawIds)
        {
            try { ids.Add(token.Value<long>()); }
            catch { /* skip unparseable tokens */ }
        }

        System.Diagnostics.Trace.WriteLine(
            $"[PbiSelectHttpListener] POST received: ids={ids.Count}, action={action}");

        // Dispatch to callback — returns null if no active document
        var result = _handleSelection(ids, action);
        if (result == null)
        {
            System.Diagnostics.Trace.WriteLine(
                "[PbiSelectHttpListener] Callback returned null (no active document).");
            WriteJson(resp, 200, new { success = false, error = "No active Revit document." });
            return;
        }

        System.Diagnostics.Trace.WriteLine(
            $"[PbiSelectHttpListener] Callback ok: result={result}");
        WriteJson(resp, 200, new { success = true, elementCount = ids.Count, action, validated = result });
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
            // The client may have disconnected before we finished writing.
            // Log so we can distinguish "client gone" from "logic broken".
            System.Diagnostics.Trace.WriteLine(
                $"[PbiSelectHttpListener] WriteJson failed (status={statusCode}): {ex.Message}");
        }
    }
}
