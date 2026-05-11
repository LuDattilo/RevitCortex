using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using RevitCortex.Plugin.PowerBiLive;
using Xunit;

namespace RevitCortex.Tests.PowerBiLive;

/// <summary>
/// Tests for PbiSelectHttpListener. Spins up a real HttpListener on port 27099.
/// No Revit API types are used — the callback is a pure lambda with no Revit dependency.
/// </summary>
public class PbiSelectHttpListenerTests : IDisposable
{
    private readonly List<(IList<long> ids, string action)> _received = new();
    private PbiSelectHttpListener? _listener;
    private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>
    /// Creates and starts a listener on port 27099.
    /// hasDocument: when true, the callback returns "" (success).
    ///              when false, the callback returns null (no active document).
    /// </summary>
    private PbiSelectHttpListener MakeListener(bool hasDocument = true)
    {
        _listener = new PbiSelectHttpListener(
            handleSelection: (ids, action) =>
            {
                if (!hasDocument) return null;      // signal "no document"
                _received.Add((ids, action));
                return "";                           // signal "success"
            },
            port: 27099);
        _listener.Start();
        return _listener;
    }

    [Fact]
    public async Task Post_EmptyArray_Returns200WithWarning()
    {
        MakeListener();
        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-select",
            new StringContent("{\"elementIds\":[]}", Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)resp.StatusCode);
        Assert.Contains("\"success\":true", body);
        Assert.Contains("warning", body);
    }

    [Fact]
    public async Task Post_InvalidJson_Returns400()
    {
        MakeListener();
        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-select",
            new StringContent("not-json", Encoding.UTF8, "application/json"));
        Assert.Equal(400, (int)resp.StatusCode);
    }

    [Fact]
    public async Task Post_NoActiveDocument_Returns200WithError()
    {
        MakeListener(hasDocument: false);
        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-select",
            new StringContent("{\"elementIds\":[1,2,3]}", Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)resp.StatusCode);
        Assert.Contains("\"success\":false", body);
        Assert.Contains("No active Revit document", body);
    }

    [Fact]
    public async Task Options_Preflight_Returns200WithCorsHeaders()
    {
        MakeListener();
        var req = new HttpRequestMessage(HttpMethod.Options, "http://localhost:27099/pbi-select");
        var resp = await _http.SendAsync(req);
        Assert.Equal(200, (int)resp.StatusCode);
        Assert.True(resp.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task NonPost_Returns405()
    {
        MakeListener();
        var resp = await _http.GetAsync("http://localhost:27099/pbi-select");
        Assert.Equal(405, (int)resp.StatusCode);
    }

    [Fact]
    public void PortAlreadyInUse_StartDoesNotThrow()
    {
        // First listener occupies port 27099
        MakeListener();
        // Second listener on same port should log warning and skip — not throw
        var second = new PbiSelectHttpListener(
            handleSelection: (_, _) => "",
            port: 27099);
        var ex = Record.Exception(() => second.Start());
        Assert.Null(ex);
        Assert.False(second.IsRunning);
        second.Dispose();
    }

    [Fact]
    public void IsRunning_TrueAfterStart_FalseAfterStop()
    {
        var l = MakeListener();
        Assert.True(l.IsRunning);
        l.Stop();
        Assert.False(l.IsRunning);
    }

    [Fact]
    public async Task Post_ValidIds_CallbackReceivesIds()
    {
        MakeListener(hasDocument: true);
        await _http.PostAsync(
            "http://localhost:27099/pbi-select",
            new StringContent("{\"elementIds\":[111,222,333],\"action\":\"select\"}", Encoding.UTF8, "application/json"));
        Assert.Single(_received);
        Assert.Equal(3, _received[0].ids.Count);
        Assert.Equal("select", _received[0].action);
    }

    [Fact]
    public async Task Post_IsolateAction_CallbackReceivesIsolate()
    {
        MakeListener(hasDocument: true);
        await _http.PostAsync(
            "http://localhost:27099/pbi-select",
            new StringContent("{\"elementIds\":[42],\"action\":\"isolate\"}", Encoding.UTF8, "application/json"));
        Assert.Single(_received);
        Assert.Equal("isolate", _received[0].action);
    }

    [Fact]
    public async Task Post_SuccessResponse_IncludesValidatedField()
    {
        _listener = new PbiSelectHttpListener(
            handleSelection: (_, _) => "queued",
            port: 27099);
        _listener.Start();

        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-select",
            new StringContent("{\"elementIds\":[1,2]}", Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)resp.StatusCode);
        Assert.Contains("\"success\":true", body);
        Assert.Contains("\"validated\":\"queued\"", body);
    }

    // ─── New endpoint tests (color, reset, create-view) ────────────────────

    [Fact]
    public async Task UnknownEndpoint_Returns404()
    {
        MakeListener();
        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-bogus",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(404, (int)resp.StatusCode);
    }

    [Fact]
    public async Task ColorEndpoint_WithoutCallback_Returns501()
    {
        // Listener created via legacy ctor → only selection callback wired
        MakeListener();
        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-color",
            new StringContent("{\"items\":[{\"id\":1,\"hex\":\"#FF0000\"}]}", Encoding.UTF8, "application/json"));
        Assert.Equal(501, (int)resp.StatusCode);
    }

    [Fact]
    public async Task ColorEndpoint_WithCallback_PassesItems()
    {
        var receivedItems = new List<PbiSelectHttpListener.ColorOverride>();
        _listener = new PbiSelectHttpListener(
            new PbiSelectHttpListener.Callbacks(
                selection: (_, _) => "",
                color: items => { foreach (var it in items) receivedItems.Add(it); return "ok"; }),
            port: 27099);
        _listener.Start();

        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-color",
            new StringContent("{\"items\":[{\"id\":111,\"hex\":\"#E53935\"},{\"id\":222,\"hex\":\"#1E88E5\"}]}",
                Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)resp.StatusCode);
        Assert.Contains("\"success\":true", body);
        Assert.Equal(2, receivedItems.Count);
        Assert.Equal(111, receivedItems[0].Id);
        Assert.Equal("#E53935", receivedItems[0].Hex);
    }

    [Fact]
    public async Task ResetEndpoint_WithCallback_IsInvoked()
    {
        var called = false;
        _listener = new PbiSelectHttpListener(
            new PbiSelectHttpListener.Callbacks(
                selection: (_, _) => "",
                resetOverrides: () => { called = true; return "42"; }),
            port: 27099);
        _listener.Start();

        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-reset-overrides",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)resp.StatusCode);
        Assert.Contains("\"success\":true", body);
        Assert.Contains("\"validated\":\"42\"", body);
        Assert.True(called);
    }

    [Fact]
    public async Task CreateViewEndpoint_WithCallback_PassesIdsAndName()
    {
        IList<long>? receivedIds = null;
        string? receivedName = null;
        _listener = new PbiSelectHttpListener(
            new PbiSelectHttpListener.Callbacks(
                selection: (_, _) => "",
                createView: (ids, name) => { receivedIds = ids; receivedName = name; return "View 01"; }),
            port: 27099);
        _listener.Start();

        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-create-view",
            new StringContent("{\"elementIds\":[1,2,3],\"viewName\":\"My View\"}",
                Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)resp.StatusCode);
        Assert.Contains("\"validated\":\"View 01\"", body);
        Assert.NotNull(receivedIds);
        Assert.Equal(3, receivedIds!.Count);
        Assert.Equal("My View", receivedName);
    }

    [Fact]
    public async Task CreateViewEndpoint_EmptyIds_Returns400()
    {
        _listener = new PbiSelectHttpListener(
            new PbiSelectHttpListener.Callbacks(
                selection: (_, _) => "",
                createView: (_, _) => "View"),
            port: 27099);
        _listener.Start();

        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-create-view",
            new StringContent("{\"elementIds\":[]}", Encoding.UTF8, "application/json"));
        Assert.Equal(400, (int)resp.StatusCode);
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _http.Dispose();
    }
}
