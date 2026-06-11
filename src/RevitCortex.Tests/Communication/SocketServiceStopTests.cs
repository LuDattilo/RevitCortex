using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using RevitCortex.Core.Session;
using RevitCortex.Plugin;
using RevitCortex.Plugin.Communication;
using Xunit;

namespace RevitCortex.Tests.Communication;

/// <summary>
/// Real-TCP tests for the SocketService lifecycle. Audit 2026-06-11: Stop() only
/// stopped the listener — connections accepted earlier kept serving requests,
/// bypassing the OnDocumentClosing protection ("prevent stale commands reaching
/// a different document") entirely.
/// </summary>
public class SocketServiceStopTests
{
    private static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public async Task Stop_ClosesActiveClientConnections()
    {
        var session = new CortexSession(new SessionStore());
        var router = new CortexRouter(session, new Router.FakeAnalyzer());
        var port = GetFreePort();
        var service = new SocketService(router, port);
        service.Start();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8);

            // Prove the per-client loop is alive: any JSON-RPC line gets a response
            // (an error response for an unknown tool is fine).
            await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"say_hello\",\"params\":{}}");
            var first = await reader.ReadLineAsync();
            Assert.NotNull(first);

            service.Stop();

            // After Stop the server must close the connection: the next read completes
            // quickly with EOF (null) or a reset, instead of blocking on a live socket.
            var readTask = reader.ReadLineAsync();
            var winner = await Task.WhenAny(readTask, Task.Delay(3000));
            Assert.True(winner == readTask,
                "Stop() left the client connection open: the read did not complete");
            if (readTask.Status == TaskStatus.RanToCompletion)
                Assert.Null(readTask.Result);
        }
        finally
        {
            service.Stop();
        }
    }
}
