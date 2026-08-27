using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;

namespace RiveTT.Plugin.Communication;

/// <summary>
/// Per-Revit-process local bridge. Named pipes avoid network ports, firewall
/// rules, and manual server startup. CurrentUserOnly prevents cross-account use.
/// </summary>
public sealed class RevitNamedPipeService : IDisposable
{
    private readonly CortexRouter _router;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _listenerTask;

    public string PipeName { get; }
    public bool IsRunning { get; private set; }

    public RevitNamedPipeService(CortexRouter router)
    {
        _router = router;
        PipeName = $"RiveTT.Revit.{Process.GetCurrentProcess().Id}";
    }

    public void Start()
    {
        if (IsRunning) return;
        RevitSessionRegistry.Publish(PipeName, Process.GetCurrentProcess().Id);
        IsRunning = true;
        _listenerTask = Task.Run(ListenAsync);
    }

    public void Dispose()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _shutdown.Cancel();
        RevitSessionRegistry.Remove(Process.GetCurrentProcess().Id);
        try { _listenerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _shutdown.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_shutdown.Token).ConfigureAwait(false);
                await ServeClientAsync(pipe, _shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[RiveTT] Named-pipe listener error: {ex.Message}");
            }
        }
    }

    private async Task ServeClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024,
            leaveOpen: true) { AutoFlush = true };
        while (!cancellationToken.IsCancellationRequested)
        {
            var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (requestLine == null) return;
            await writer.WriteLineAsync(ProcessRequest(requestLine)).ConfigureAwait(false);
        }
    }

    private string ProcessRequest(string requestJson)
    {
        JsonRpcRequest? request;
        try { request = JsonConvert.DeserializeObject<JsonRpcRequest>(requestJson); }
        catch { return JsonConvert.SerializeObject(JsonRpcResponse.Fail(null, -32700, "Parse error")); }

        if (request == null || string.IsNullOrWhiteSpace(request.Method))
            return JsonConvert.SerializeObject(JsonRpcResponse.Fail(request?.Id, -32600, "Invalid request"));

        try
        {
            var result = _router.Route(request.Method, request.Params ?? new JObject(), request.PublicTool);
            return result.Success
                ? JsonConvert.SerializeObject(JsonRpcResponse.Success(request.Id, result.Data!))
                : JsonConvert.SerializeObject(JsonRpcResponse.Fail(
                    request.Id, (int)result.Error!.Code, result.Error.Message,
                    JToken.FromObject(result.Error)));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[RiveTT] Pipe request failure: {ex}");
            return JsonConvert.SerializeObject(JsonRpcResponse.Fail(
                request.Id, -32603, SafeErrorMessages.ForInternal(ex)));
        }
    }
}
