using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Plugin.Tracking;

public static class UsageTracker
{
    private static readonly string UsageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".revitcortex");
    private static readonly string UsagePath = Path.Combine(UsageDir, "usage.jsonl");
    private static string? _sessionId;

    public static string SessionId
    {
        get
        {
            if (_sessionId == null)
            {
                var rng = new Random();
                _sessionId = $"{DateTime.Now:yyyyMMdd}-{rng.Next(0x10000):x4}";
            }
            return _sessionId;
        }
    }

    public static void Record(
        string model,
        int inputTokens,
        int outputTokens,
        int thinkingTokens,
        List<string> toolCalls,
        int durationMs)
    {
        try
        {
            Directory.CreateDirectory(UsageDir);

            var entry = new JObject
            {
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["session_id"] = SessionId,
                ["model"] = model,
                ["input_tokens"] = inputTokens,
                ["output_tokens"] = outputTokens,
                ["thinking_tokens"] = thinkingTokens,
                ["tool_calls"] = new JArray(toolCalls.ToArray()),
                ["source"] = "actual",
                ["duration_ms"] = durationMs,
            };

            File.AppendAllText(UsagePath, entry.ToString(Formatting.None) + "\n");
        }
        catch
        {
            // Silent fail — tracking must never break the chat
        }
    }
}
