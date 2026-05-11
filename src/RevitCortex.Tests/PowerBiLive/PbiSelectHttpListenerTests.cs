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

    public void Dispose()
    {
        _listener?.Dispose();
        _http.Dispose();
    }
}
