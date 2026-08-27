using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RiveTT.Server.Connection;

/// <summary>
/// Discovers the currently running RiveTT Revit process and speaks the local
/// JSON-RPC protocol through a Windows named pipe. No TCP port is opened.
/// </summary>
public sealed class RevitPipeBridge : IDisposable
{
    private readonly TimeSpan _commandTimeout;
    private int _requestCounter;

    public RevitPipeBridge(int commandTimeoutSeconds = 300)
    {
        _commandTimeout = TimeSpan.FromSeconds(commandTimeoutSeconds);
    }

    public async Task<JToken> SendCommandAsync(string method, JObject parameters, CancellationToken cancellationToken,
        string? publicToolName = null)
    {
        var pipeName = RevitSessionDiscovery.FindPreferredPipe();
        if (pipeName == null)
        {
            throw new InvalidOperationException(
                "No RiveTT Revit session is available. Open Revit (2026.5+ or 2027) and wait for the project window to appear.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_commandTimeout);
        await using var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out connecting to the Revit pipe '{pipeName}'.");
        }

        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = true
        };

        var id = Interlocked.Increment(ref _requestCounter).ToString();
        var request = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters,
            ["id"] = id
        };
        if (!string.IsNullOrEmpty(publicToolName)) request["publicTool"] = publicToolName;
        await writer.WriteLineAsync(request.ToString(Formatting.None)).ConfigureAwait(false);
        var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        if (line == null) throw new IOException("The Revit pipe closed before returning a result.");

        var response = JObject.Parse(line);
        if (response["id"]?.ToString() != id)
            throw new IOException("The Revit pipe returned a response for a different request.");
        if (response["error"] is JObject error)
        {
            var data = error["data"] as JObject;
            return new JObject
            {
                ["success"] = false,
                ["error"] = data?.DeepClone() ?? new JObject
                {
                    ["message"] = error["message"]?.ToString() ?? "Unknown Revit error"
                }
            };
        }
        return response["result"] ?? JValue.CreateNull();
    }

    public void Dispose() { }
}

internal sealed class RevitSessionRecord
{
    public int ProcessId { get; set; }
    public string PipeName { get; set; } = "";
    public string StartedAtUtc { get; set; } = "";
}

internal static class RevitSessionDiscovery
{
    private static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RiveTT", "sessions");

    public static string? FindPreferredPipe()
    {
        if (!Directory.Exists(DirectoryPath)) return null;
        var sessions = Directory.EnumerateFiles(DirectoryPath, "*.json")
            .Select(Read)
            .Where(record => record != null && IsRunning(record!.ProcessId))
            .Cast<RevitSessionRecord>()
            .OrderByDescending(record => record.StartedAtUtc, StringComparer.Ordinal)
            .ToList();

        foreach (var stale in Directory.EnumerateFiles(DirectoryPath, "*.json")
                     .Where(path => sessions.All(record => !path.EndsWith(record.ProcessId + ".json", StringComparison.OrdinalIgnoreCase))))
        {
            try { File.Delete(stale); } catch { }
        }
        return sessions.FirstOrDefault()?.PipeName;
    }

    private static RevitSessionRecord? Read(string path)
    {
        try { return JsonConvert.DeserializeObject<RevitSessionRecord>(File.ReadAllText(path)); }
        catch { return null; }
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited && process.ProcessName.StartsWith("Revit", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

/// <summary>Serializes calls because Revit ExternalEvent executes one request at a time.</summary>
public sealed class RevitConnectionManager
{
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public async Task<JToken> ExecuteAsync(string method, JObject parameters, CancellationToken cancellationToken = default,
        string? publicToolName = null)
        => await ExecuteAsync(method, parameters, 300, cancellationToken, publicToolName).ConfigureAwait(false);

    public async Task<JToken> ExecuteAsync(string method, JObject parameters, int commandTimeoutSeconds,
        CancellationToken cancellationToken = default, string? publicToolName = null)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var bridge = new RevitPipeBridge(commandTimeoutSeconds);
            return await bridge.SendCommandAsync(method, parameters, cancellationToken, publicToolName).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled: let the host see a cancellation, not a result.
            throw;
        }
        catch (Exception exception)
        {
            // Single choke point for every transport failure. Letting these escape
            // surfaced as the MCP host's generic "An error occurred invoking '<tool>'",
            // which reads as "the tool is broken" and sent past sessions hunting
            // through paths, caches and document state for a problem that was a dead
            // pipe or a timeout.
            return TransportError.Describe(method, exception, commandTimeoutSeconds);
        }
        finally
        {
            _mutex.Release();
        }
    }
}

/// <summary>
/// Turns a transport-layer exception into a structured error payload.
///
/// Separate and public so it can be unit-tested without a Revit session: the
/// behavior being locked is that a dead pipe, a timeout or a missing session
/// arrives as DATA the caller can read, never as the MCP host's generic
/// "An error occurred invoking '&lt;tool&gt;'".
/// </summary>
public static class TransportError
{
    public static JObject Describe(string method, Exception exception, int timeoutSeconds)
    {
        var (code, suggestion) = exception switch
        {
            InvalidOperationException => ("NoRevitSession",
                "Start Revit (2026.5+ or 2027), open a project, and wait for its session to be published."),
            TimeoutException => ("Timeout",
                $"The Revit session did not answer within {timeoutSeconds}s. Revit may be busy, showing a " +
                "modal dialog, or executing a long operation — check the Revit window, then retry with a " +
                "narrower request."),
            IOException => ("PipeClosed",
                "The Revit pipe closed mid-request (Revit closed, crashed, or the add-in was reloaded). " +
                "Re-check the session with get_project_info before retrying a write."),
            UnauthorizedAccessException => ("PipeAccessDenied",
                "The named pipe is restricted to the current user; Revit must run under the same account."),
            _ => ("TransportFailure",
                "Inspect the local audit log at %LOCALAPPDATA%\\RiveTT\\audit.jsonl.")
        };

        return new JObject
        {
            ["success"] = false,
            ["error"] = new JObject
            {
                ["code"] = code,
                ["tool"] = method,
                ["message"] = exception.Message,
                ["suggestion"] = suggestion,
                // Says where the failure happened: nothing reached Revit, so no
                // transaction ran and the model is untouched.
                ["stage"] = "transport",
                ["modelChanged"] = false
            }
        };
    }
}
