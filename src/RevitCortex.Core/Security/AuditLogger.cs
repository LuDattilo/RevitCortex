using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using RevitCortex.Core.Results;

namespace RevitCortex.Core.Security;

/// <summary>
/// Append-only audit logger for tool operations.
/// Writes structured JSON lines (schema v2) to ~/.revitcortex/audit.jsonl.
/// Designed for ISO 19650 accountability: who did what, when, on which
/// elements — plus duration / response size for performance triage.
///
/// Writes are queued and drained on a dedicated background thread so the
/// caller (the Revit UI thread) never blocks on disk I/O. The file
/// stream is opened once and kept open for the lifetime of the logger;
/// entries flush to disk whenever the queue drains so external readers
/// (`Get-Content -Wait`) see them promptly.
///
/// Schema v2 fields (added 2026-04 — NavisCortex parity):
///   v               — schema version (2)
///   duration_ms     — wall-clock time the tool spent inside Execute,
///                     measured around <see cref="Plugin.CortexRouter"/>'s
///                     dispatch. Excludes IPC and JSON-RPC overhead.
///   response_bytes  — UTF-8 byte count of the serialized response data.
///                     0 on failure.
/// </summary>
public class AuditLogger : IDisposable
{
    private const int DefaultQueueCapacity = 1024;

    private readonly string _logPath;
    private readonly BlockingCollection<string> _queue;
    private readonly Thread _writerThread;
    private volatile bool _disposed;

    public AuditLogger(string? logPath = null, int queueCapacity = DefaultQueueCapacity)
    {
        _logPath = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".revitcortex", "audit.jsonl");

        _queue = new BlockingCollection<string>(queueCapacity);
        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "RevitCortex-AuditWriter"
        };
        _writerThread.Start();
    }

    /// <summary>
    /// Log a tool execution. Returns immediately — the entry is enqueued
    /// for the background writer thread. If the queue is full (writer can't
    /// keep up with audit volume) the entry is dropped and a Trace warning
    /// is emitted; we choose dropping over hanging the caller because audit
    /// loss is recoverable but a UI-thread hang is not.
    /// </summary>
    public void Log(string toolName, string inputSummary, bool success,
        CortexErrorCode? errorCode = null, int elementsAffected = 0,
        long durationMs = 0, int responseBytes = 0)
    {
        if (_disposed) return;

        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            Version = 2,
            Tool = toolName,
            InputSummary = Truncate(inputSummary, 200),
            Result = success ? "ok" : "fail",
            ErrorCode = errorCode?.ToString(),
            ElementsAffected = elementsAffected,
            DurationMs = durationMs,
            ResponseBytes = responseBytes
        };

        string json;
        try { json = JsonConvert.SerializeObject(entry, Formatting.None); }
        catch
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Audit entry serialization failed for tool '{toolName}'");
            return;
        }

        if (!_queue.TryAdd(json))
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Audit queue full (cap={_queue.BoundedCapacity}) — dropping entry for tool '{toolName}'");
        }
    }

    /// <summary>
    /// Block until pending entries are written and flushed to disk.
    /// Production code never needs to call this — the writer drains
    /// continuously. Used by tests and by graceful shutdown.
    /// </summary>
    public void Flush(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(5));
        while (_queue.Count > 0 && DateTime.UtcNow < deadline)
            Thread.Sleep(5);
        // Writer flushes when the queue empties; give it a beat to run
        // through the last item and call StreamWriter.Flush.
        Thread.Sleep(20);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _queue.CompleteAdding(); } catch { }
        try { _writerThread.Join(TimeSpan.FromSeconds(5)); } catch { }
        try { _queue.Dispose(); } catch { }
    }

    private void WriterLoop()
    {
        StreamWriter? writer = null;
        try
        {
            try
            {
                var dir = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir!);
            }
            catch
            {
                System.Diagnostics.Trace.WriteLine(
                    "[RevitCortex] Audit writer could not create log directory");
            }

            try
            {
                var fs = new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                writer = new StreamWriter(fs, new UTF8Encoding(false));
                // Manual flush — we batch while the queue has work, flush
                // when it drains. Trades a tiny durability window for
                // significantly less disk syscalls under bursty load.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Audit writer could not open log file: {ex.Message}");
                return;
            }

            foreach (var line in _queue.GetConsumingEnumerable())
            {
                try
                {
                    writer.WriteLine(line);
                    if (_queue.Count == 0) writer.Flush();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[RevitCortex] Audit writer append failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Audit writer crashed: {ex.Message}");
        }
        finally
        {
            try { writer?.Flush(); } catch { }
            try { writer?.Dispose(); } catch { }
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
    }

    private class AuditEntry
    {
        [JsonProperty("ts")] public string Timestamp { get; set; } = "";
        [JsonProperty("v")] public int Version { get; set; }
        [JsonProperty("tool")] public string Tool { get; set; } = "";
        [JsonProperty("input_summary")] public string InputSummary { get; set; } = "";
        [JsonProperty("result")] public string Result { get; set; } = "";

        [JsonProperty("error_code", NullValueHandling = NullValueHandling.Ignore)]
        public string? ErrorCode { get; set; }

        [JsonProperty("elements_affected")] public int ElementsAffected { get; set; }
        [JsonProperty("duration_ms")] public long DurationMs { get; set; }
        [JsonProperty("response_bytes")] public int ResponseBytes { get; set; }
    }
}
