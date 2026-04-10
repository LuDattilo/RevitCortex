using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Plugin.Communication;

public class SocketService
{
    private TcpListener? _listener;
    private volatile bool _isRunning;
    private Thread? _listenerThread;
    private readonly CortexRouter _router;
    private readonly int _port;

    public bool IsRunning => _isRunning;

    public SocketService(CortexRouter router, int port = 8080)
    {
        _router = router;
        _port = port;
    }

    public void Start()
    {
        if (_isRunning) return;
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        _isRunning = true;
        _listenerThread = new Thread(ListenForClients) { IsBackground = true };
        _listenerThread.Start();
    }

    public void Stop()
    {
        _isRunning = false;
        _listener?.Stop();
    }

    private void ListenForClients()
    {
        while (_isRunning)
        {
            try
            {
                var client = _listener!.AcceptTcpClient();
                var thread = new Thread(HandleClient) { IsBackground = true };
                thread.Start(client);
            }
            catch (SocketException) when (!_isRunning)
            {
                break;
            }
        }
    }

    private void HandleClient(object? state)
    {
        var client = (TcpClient)state!;
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var response = ProcessRequest(line);
                writer.WriteLine(response);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] Client error: {ex.Message}");
        }
        finally
        {
            client.Close();
        }
    }

    private string ProcessRequest(string requestJson)
    {
        JsonRpcRequest? request;
        try
        {
            request = JsonConvert.DeserializeObject<JsonRpcRequest>(requestJson);
        }
        catch
        {
            return JsonConvert.SerializeObject(
                JsonRpcResponse.Fail(null, -32700, "Parse error"));
        }

        if (request == null || string.IsNullOrEmpty(request.Method))
        {
            return JsonConvert.SerializeObject(
                JsonRpcResponse.Fail(request?.Id, -32600, "Invalid request"));
        }

        try
        {
            var result = _router.Route(request.Method, request.Params ?? new JObject());

            if (result.Success)
            {
                return JsonConvert.SerializeObject(
                    JsonRpcResponse.Success(request.Id, result.Data!));
            }
            else
            {
                return JsonConvert.SerializeObject(
                    JsonRpcResponse.Fail(request.Id,
                        (int)result.Error!.Code,
                        JsonConvert.SerializeObject(result.Error)));
            }
        }
        catch (Exception ex)
        {
            return JsonConvert.SerializeObject(
                JsonRpcResponse.Fail(request.Id, -32603, ex.Message));
        }
    }
}
