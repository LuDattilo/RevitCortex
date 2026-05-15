using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;
using Xunit;

namespace RevitCortex.Tests.Bridge;

/// <summary>
/// Regression tests for the bug where JSON-RPC `error` responses from the plugin
/// were thrown as InvalidOperationException, hiding the structured CortexError
/// (code/message/suggestion) from MCP clients. After the fix, the bridge must
/// surface those responses as a normal payload {"success": false, "error": {...}}.
/// </summary>
public class RevitBridgeErrorPropagationTests
{
    private static (TcpListener listener, int port) StartFakePlugin(string responseLine)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            _ = await reader.ReadLineAsync(); // consume request
            await writer.WriteLineAsync(responseLine);
        });
        return (listener, port);
    }

    [Fact]
    public async Task SendCommand_OnJsonRpcError_ReturnsStructuredPayload_DoesNotThrow()
    {
        // Plugin replies with a JSON-RPC error carrying the full CortexError in `data`.
        var pluginResponse =
            "{\"jsonrpc\":\"2.0\",\"id\":\"1\"," +
            "\"error\":{\"code\":200,\"message\":\"send_code_to_revit is disabled\"," +
            "\"data\":{\"code\":\"PermissionDenied\",\"message\":\"send_code_to_revit is disabled\"," +
            "\"suggestion\":\"Enable it in Settings\"}}}";

        var (listener, port) = StartFakePlugin(pluginResponse);
        try
        {
            using var bridge = new RevitBridge(port: port);
            var result = await bridge.SendCommandAsync("send_code_to_revit", new JObject());

            Assert.NotNull(result);
            var obj = Assert.IsType<JObject>(result);
            Assert.False(obj["success"]!.Value<bool>());
            var err = obj["error"] as JObject;
            Assert.NotNull(err);
            Assert.Equal("PermissionDenied", err!["code"]!.Value<string>());
            Assert.Equal("send_code_to_revit is disabled", err["message"]!.Value<string>());
            Assert.Equal("Enable it in Settings", err["suggestion"]!.Value<string>());
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task SendCommand_OnJsonRpcError_WithoutData_FallsBackToMessage()
    {
        // Edge case: plugin sends an error without the `data` field (legacy / non-Cortex error).
        var pluginResponse =
            "{\"jsonrpc\":\"2.0\",\"id\":\"1\"," +
            "\"error\":{\"code\":-32603,\"message\":\"internal plugin failure\"}}";

        var (listener, port) = StartFakePlugin(pluginResponse);
        try
        {
            using var bridge = new RevitBridge(port: port);
            var result = await bridge.SendCommandAsync("any_tool", new JObject());

            var obj = Assert.IsType<JObject>(result);
            Assert.False(obj["success"]!.Value<bool>());
            Assert.Equal("internal plugin failure", obj["error"]!["message"]!.Value<string>());
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task SendCommand_OnSuccess_ReturnsResultPayloadUnchanged()
    {
        var pluginResponse =
            "{\"jsonrpc\":\"2.0\",\"id\":\"1\"," +
            "\"result\":{\"greeting\":\"Hello\",\"count\":42}}";

        var (listener, port) = StartFakePlugin(pluginResponse);
        try
        {
            using var bridge = new RevitBridge(port: port);
            var result = await bridge.SendCommandAsync("say_hello", new JObject());

            var obj = Assert.IsType<JObject>(result);
            Assert.Equal("Hello", obj["greeting"]!.Value<string>());
            Assert.Equal(42, obj["count"]!.Value<int>());
            Assert.Null(obj["success"]); // success path must not wrap the payload
        }
        finally
        {
            listener.Stop();
        }
    }
}
