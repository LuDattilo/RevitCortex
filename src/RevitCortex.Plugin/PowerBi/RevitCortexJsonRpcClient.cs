using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// Line-delimited JSON-RPC client for the local RevitCortex socket. Used by
/// the WPF wizard so the export goes through the same pipeline (audit log,
/// read-only mode, confirmation) as a remote MCP call.
/// </summary>
public static class RevitCortexJsonRpcClient
{
    public static async Task<JObject> InvokeAsync(int port, string toolName, JObject input)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

        var request = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Guid.NewGuid().ToString("N"),
            ["method"] = toolName,
            ["params"] = input
        };

        await writer.WriteLineAsync(request.ToString(Newtonsoft.Json.Formatting.None));
        var responseLine = await reader.ReadLineAsync();
        if (string.IsNullOrEmpty(responseLine))
            throw new InvalidOperationException("Empty response from RevitCortex socket.");

        return JObject.Parse(responseLine);
    }
}
