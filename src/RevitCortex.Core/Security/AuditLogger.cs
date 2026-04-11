using System;
using System.IO;
using Newtonsoft.Json;
using RevitCortex.Core.Results;

namespace RevitCortex.Core.Security;

/// <summary>
/// Append-only audit logger for tool operations.
/// Writes structured JSON lines to ~/.revitcortex/audit.jsonl.
/// Designed for ISO 19650 accountability: who did what, when, on which elements.
/// </summary>
public class AuditLogger
{
    private readonly string _logPath;
    private readonly object _lock = new object();

    public AuditLogger(string? logPath = null)
    {
        _logPath = logPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".revitcortex", "audit.jsonl");
    }

    /// <summary>
    /// Log a tool execution to the audit trail.
    /// </summary>
    /// <param name="toolName">Name of the tool invoked.</param>
    /// <param name="inputSummary">Brief summary of input (no sensitive data).</param>
    /// <param name="success">Whether the tool succeeded.</param>
    /// <param name="errorCode">Error code if failed, null if success.</param>
    /// <param name="elementsAffected">Number of elements read or modified.</param>
    public void Log(string toolName, string inputSummary, bool success,
        CortexErrorCode? errorCode = null, int elementsAffected = 0)
    {
        var entry = new AuditEntry
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            Tool = toolName,
            InputSummary = Truncate(inputSummary, 200),
            Result = success ? "ok" : "fail",
            ErrorCode = errorCode?.ToString(),
            ElementsAffected = elementsAffected
        };

        var json = JsonConvert.SerializeObject(entry, Formatting.None);

        try
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(_logPath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(_logPath, json + Environment.NewLine);
            }
        }
        catch
        {
            // Audit logging must never crash the application.
            // If we can't write, we silently skip.
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Failed to write audit log entry for tool '{toolName}'");
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
}
