using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Server.Connection;

/// <summary>
/// TCP bridge to the RevitCortex plugin running inside Revit.
/// Sends JSON-RPC requests and reads line-delimited responses.
/// </summary>
public sealed class RevitBridge : IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _commandTimeout;
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private int _requestCounter;

    public RevitBridge(string host = "127.0.0.1", int port = 8080, int commandTimeoutSeconds = 300)
    {
        _host = host;
        _port = port;
        _commandTimeout = TimeSpan.FromSeconds(commandTimeoutSeconds);
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (_client is { Connected: true }) return;

        Disconnect();
        _client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        await _client.ConnectAsync(_host, _port, cts.Token);
        var stream = _client.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    /// <summary>
    /// Send a JSON-RPC command to the Revit plugin and return the result.
    /// Opens a TCP connection per call (same pattern as the TS server).
    /// </summary>
    public async Task<JToken> SendCommandAsync(string method, JObject parameters, CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);

        var id = Interlocked.Increment(ref _requestCounter).ToString();
        var request = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters,
            ["id"] = id
        };

        await _writer!.WriteLineAsync(request.ToString(Formatting.None));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_commandTimeout);

        while (!cts.Token.IsCancellationRequested)
        {
            var line = await _reader!.ReadLineAsync(cts.Token);
            if (line == null) throw new IOException("Connection closed by Revit plugin");

            var response = JObject.Parse(line);
            if (response["id"]?.ToString() != id) continue;

            if (response["error"] != null)
            {
                var errMsg = response["error"]!["message"]?.ToString() ?? "Unknown Revit error";
                throw new InvalidOperationException(errMsg);
            }

            return response["result"]!;
        }

        throw new TimeoutException($"Command '{method}' timed out after {_commandTimeout.TotalSeconds}s");
    }

    private void Disconnect()
    {
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _reader = null;
        _writer = null;
        _client = null;
    }

    public void Dispose() => Disconnect();
}

/// <summary>
/// Manages a per-request TCP connection to the Revit plugin.
/// Serializes access (only one command at a time, like the TS mutex).
/// </summary>
public sealed class RevitConnectionManager
{
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly int _port;

    public RevitConnectionManager(int port = 8080)
    {
        _port = port;
    }

    public async Task<JToken> ExecuteAsync(string method, JObject parameters, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            using var bridge = new RevitBridge(port: _port);
            return await bridge.SendCommandAsync(method, parameters, ct);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// Reads the port from settings or environment, same logic as the TS server.
    /// </summary>
    public static int ResolvePort()
    {
        var envPort = Environment.GetEnvironmentVariable("REVITCORTEX_PORT");
        if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var ep) && ep > 0 && ep <= 65535)
            return ep;

        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".revitcortex", "settings.json");
            if (File.Exists(settingsPath))
            {
                var json = JObject.Parse(File.ReadAllText(settingsPath));
                var port = json["Port"]?.Value<int>();
                if (port is > 0 and <= 65535) return port.Value;
            }
        }
        catch { }

        return 8080;
    }
}
