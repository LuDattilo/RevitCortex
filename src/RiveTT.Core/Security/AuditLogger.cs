using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using RiveTT.Core.Hosting;
using RiveTT.Core.Results;

namespace RiveTT.Core.Security;

/// <summary>
/// Append-only audit logger for tool operations.
/// Writes structured JSON lines to %LOCALAPPDATA%\RiveTT\audit.jsonl.
/// Designed for ISO 19650 accountability: who did what, when, on which elements.
/// </summary>
public class AuditLogger
{
    private readonly string _logPath;
    private readonly object _lock = new object();
    private long _writeFailureCount;

    public AuditLogger(string? logPath = null)
    {
        _logPath = logPath ?? CortexEnvironment.Current.AuditLogPath;
    }

    /// <summary>
    /// Log a tool execution to the audit trail (legacy overload, schema v1).
    /// </summary>
    public void Log(string toolName, string inputSummary, bool success,
        CortexErrorCode? errorCode = null, int elementsAffected = 0)
    {
        WriteEntry(new AuditEntry
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            Tool = toolName,
            InputSummary = Truncate(inputSummary, 500),
            Result = success ? "ok" : "fail",
            ErrorCode = errorCode?.ToString(),
            ElementsAffected = elementsAffected
        }, toolName);
    }

    /// <summary>
    /// Log a tool execution with performance data and optional send_code_to_revit
    /// snippet/hash (schema v2). Used by CortexRouter so rclog can diagnose
    /// perf bottlenecks and token-heavy tools.
    /// errorMessage is the human-readable failure detail (truncated to 200 chars)
    /// and lets triage distinguish e.g. "Unhandled exception: NRE" from
    /// "No result from tool execution" when both surface as Unknown.
    /// </summary>
    public void LogWithPerf(string toolName, string inputSummary, bool success,
        CortexErrorCode? errorCode = null, int elementsAffected = 0,
        long? durationMs = null, long? responseBytes = null,
        string? codeSnippet = null, string? codeHash = null,
        string? errorMessage = null, string? outputSummary = null)
    {
        WriteEntry(new AuditEntryV2
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            SchemaVersion = 2,
            Tool = toolName,
            InputSummary = Truncate(inputSummary, 500),
            Result = success ? "ok" : "fail",
            ErrorCode = errorCode?.ToString(),
            ErrorMessage = success ? null : Truncate(errorMessage ?? "", 200),
            ElementsAffected = elementsAffected,
            DurationMs = durationMs,
            ResponseBytes = responseBytes,
            OutputSummary = Truncate(outputSummary ?? "", 500),
            CodeSnippet = codeSnippet,
            CodeHash = codeHash
        }, toolName);
    }

    private void WriteEntry(object entry, string toolName)
    {
        var line = JsonConvert.SerializeObject(entry, Formatting.None) + Environment.NewLine;

        // Two RiveTT.Server/Revit sessions can share one audit.jsonl (P0.3 in
        // PLAN_CORRECTION.md notes two server processes running at once): a
        // concurrent File.AppendAllText can throw a sharing violation. Retry
        // briefly before treating it as a real failure.
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                lock (_lock)
                {
                    var dir = Path.GetDirectoryName(_logPath);
                    if (dir != null && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    File.AppendAllText(_logPath, line);
                }
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(25 * attempt);
            }
            catch (Exception exception)
            {
                RecordFailure(toolName, exception);
                return;
            }
        }

        RecordFailure(toolName, new IOException($"Gave up writing the audit entry after {maxAttempts} attempts"));
    }

    /// <summary>
    /// Total audit writes lost to an exception since this instance was created —
    /// exposed so a caller can tell "the audit log is empty" from "audit writes
    /// are failing" instead of trusting a promise nothing checks.
    /// </summary>
    public long WriteFailureCount => Interlocked.Read(ref _writeFailureCount);

    private void RecordFailure(string toolName, Exception exception)
    {
        Interlocked.Increment(ref _writeFailureCount);

        // Trace.WriteLine goes to OutputDebugString, which is invisible inside
        // Revit.exe without a debugger attached — this silence is exactly what
        // let audit.jsonl stop growing unnoticed during the 2026-08-26 campaign
        // (P0.2). A plain sibling file is readable without one.
        try
        {
            File.AppendAllText(_logPath + ".errors.log",
                $"{DateTime.UtcNow:o} tool={toolName} error={exception}{Environment.NewLine}");
        }
        catch
        {
            // Nothing left to fall back to.
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
        [JsonProperty("tool")] public string Tool { get; set; } = "";
        [JsonProperty("input_summary")] public string InputSummary { get; set; } = "";
        [JsonProperty("result")] public string Result { get; set; } = "";
        [JsonProperty("error_code", NullValueHandling = NullValueHandling.Ignore)]
        public string? ErrorCode { get; set; }
        [JsonProperty("elements_affected")] public int ElementsAffected { get; set; }
    }

    private class AuditEntryV2
    {
        [JsonProperty("ts")] public string Timestamp { get; set; } = "";
        [JsonProperty("v")] public int SchemaVersion { get; set; }
        [JsonProperty("tool")] public string Tool { get; set; } = "";
        [JsonProperty("input_summary")] public string InputSummary { get; set; } = "";
        [JsonProperty("result")] public string Result { get; set; } = "";
        [JsonProperty("error_code", NullValueHandling = NullValueHandling.Ignore)]
        public string? ErrorCode { get; set; }
        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string? ErrorMessage { get; set; }
        [JsonProperty("elements_affected")] public int ElementsAffected { get; set; }
        [JsonProperty("duration_ms", NullValueHandling = NullValueHandling.Ignore)]
        public long? DurationMs { get; set; }
        [JsonProperty("response_bytes", NullValueHandling = NullValueHandling.Ignore)]
        public long? ResponseBytes { get; set; }
        [JsonProperty("output_summary", NullValueHandling = NullValueHandling.Ignore)]
        public string? OutputSummary { get; set; }
        [JsonProperty("code_snippet", NullValueHandling = NullValueHandling.Ignore)]
        public string? CodeSnippet { get; set; }
        [JsonProperty("code_hash", NullValueHandling = NullValueHandling.Ignore)]
        public string? CodeHash { get; set; }
    }
}
