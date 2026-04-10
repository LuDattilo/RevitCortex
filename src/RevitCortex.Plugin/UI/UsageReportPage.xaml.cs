using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace RevitCortex.Plugin.UI;

public partial class UsageReportPage : Page
{
    private static readonly string UsageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".revitcortex");
    private static readonly string UsageJsonlPath = Path.Combine(UsageDir, "usage.jsonl");
    private static readonly string SettingsFilePath = Path.Combine(UsageDir, "settings.json");
    private static readonly string ReportsDir = Path.Combine(UsageDir, "reports");

    public UsageReportPage()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadReport();
    }

    private void Period_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) LoadReport();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => LoadReport();

    private void LoadReport()
    {
        var entries = ReadUsageEntries();
        var (start, end) = GetDateRange();
        var filtered = entries.Where(e => e.Timestamp >= start && e.Timestamp <= end).ToList();

        // Summary
        int totalCalls = filtered.Count;
        long totalTokens = filtered.Sum(e => (long)e.InputTokens + e.OutputTokens);
        var pricing = LoadPricing();
        double totalCost = filtered.Sum(e => EstimateCost(e, pricing));

        TotalCallsText.Text = totalCalls.ToString("N0");
        TotalTokensText.Text = FormatTokens(totalTokens);
        TotalCostText.Text = $"${totalCost:F2}";

        // Top tools breakdown
        var toolBreakdown = filtered
            .SelectMany(e => e.ToolCalls.Count > 0
                ? e.ToolCalls.Select(t => new { Tool = t, Entry = e })
                : new[] { new { Tool = "(direct)", Entry = e } })
            .GroupBy(x => x.Tool)
            .Select(g => new
            {
                ToolName = g.Key,
                Calls = g.Count(),
                Tokens = g.Sum(x => (long)x.Entry.InputTokens + x.Entry.OutputTokens),
                Cost = g.Sum(x => EstimateCost(x.Entry, pricing))
            })
            .OrderByDescending(x => x.Cost)
            .Take(10)
            .Select((x, i) => new ToolRow
            {
                Rank = (i + 1).ToString(),
                ToolName = x.ToolName,
                Calls = x.Calls.ToString("N0"),
                Tokens = FormatTokens(x.Tokens),
                Cost = $"${x.Cost:F3}"
            })
            .ToList();

        ToolsTable.ItemsSource = toolBreakdown;
        EmptyText.Visibility = toolBreakdown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var entries = ReadUsageEntries();
        var (start, end) = GetDateRange();
        var filtered = entries.Where(en => en.Timestamp >= start && en.Timestamp <= end).ToList();

        if (filtered.Count == 0)
        {
            MessageBox.Show("No usage data to export.", "Empty", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Directory.CreateDirectory(ReportsDir);
            var pricing = LoadPricing();
            string fileName = $"usage-{start:yyyy-MM-dd}-to-{end:yyyy-MM-dd}.csv";
            string filePath = Path.Combine(ReportsDir, fileName);

            var lines = new List<string>
            {
                "timestamp,session_id,model,input_tokens,output_tokens,thinking_tokens,tool_calls,duration_ms,cost_usd"
            };

            foreach (var entry in filtered)
            {
                double cost = EstimateCost(entry, pricing);
                string tools = string.Join(";", entry.ToolCalls);
                lines.Add($"{entry.Timestamp:o},{entry.SessionId},{entry.Model},{entry.InputTokens},{entry.OutputTokens},{entry.ThinkingTokens},{tools},{entry.DurationMs},{cost:F4}");
            }

            File.WriteAllLines(filePath, lines);
            MessageBox.Show($"Exported to:\n{filePath}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private (DateTime start, DateTime end) GetDateRange()
    {
        var now = DateTime.UtcNow;
        int index = PeriodCombo.SelectedIndex;
        return index switch
        {
            0 => (now.Date, now),
            1 => (now.AddDays(-7), now),
            2 => (now.AddDays(-30), now),
            _ => (DateTime.MinValue, now),
        };
    }

    private static List<UsageEntry> ReadUsageEntries()
    {
        var entries = new List<UsageEntry>();
        if (!File.Exists(UsageJsonlPath)) return entries;

        try
        {
            foreach (var line in File.ReadLines(UsageJsonlPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var obj = JObject.Parse(line);
                    entries.Add(new UsageEntry
                    {
                        Timestamp = obj["timestamp"]?.Value<DateTime>() ?? DateTime.MinValue,
                        SessionId = obj["session_id"]?.Value<string>() ?? "",
                        Model = obj["model"]?.Value<string>() ?? "",
                        InputTokens = obj["input_tokens"]?.Value<int>() ?? 0,
                        OutputTokens = obj["output_tokens"]?.Value<int>() ?? 0,
                        ThinkingTokens = obj["thinking_tokens"]?.Value<int>() ?? 0,
                        ToolCalls = obj["tool_calls"]?.ToObject<List<string>>() ?? new List<string>(),
                        DurationMs = obj["duration_ms"]?.Value<int>() ?? 0,
                    });
                }
                catch { /* skip malformed lines */ }
            }
        }
        catch { }

        return entries;
    }

    private static Dictionary<string, (double input, double output)> LoadPricing()
    {
        var defaults = new Dictionary<string, (double, double)>
        {
            ["claude-sonnet-4-6"] = (3.0, 15.0),
            ["claude-haiku-4-5"] = (0.80, 4.0),
            ["claude-opus-4-6"] = (15.0, 75.0),
        };

        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var settings = JObject.Parse(File.ReadAllText(SettingsFilePath));
                var pricing = settings["tokenPricing"] as JObject;
                if (pricing != null)
                {
                    foreach (var prop in pricing.Properties())
                    {
                        var m = prop.Value as JObject;
                        if (m != null)
                        {
                            double inp = m["inputPerMTok"]?.Value<double>() ?? 0;
                            double outp = m["outputPerMTok"]?.Value<double>() ?? 0;
                            defaults[prop.Name] = (inp, outp);
                        }
                    }
                }
            }
        }
        catch { }

        return defaults;
    }

    private static double EstimateCost(UsageEntry entry, Dictionary<string, (double input, double output)> pricing)
    {
        var key = entry.Model;
        if (!pricing.TryGetValue(key, out var rates))
        {
            // Try partial match
            var match = pricing.Keys.FirstOrDefault(k => key.Contains(k) || k.Contains(key));
            rates = match != null ? pricing[match] : (3.0, 15.0);
        }

        return (entry.InputTokens / 1_000_000.0) * rates.input
             + (entry.OutputTokens / 1_000_000.0) * rates.output;
    }

    private static string FormatTokens(long tokens)
    {
        if (tokens >= 1_000_000) return $"{tokens / 1_000_000.0:F1}M";
        if (tokens >= 1_000) return $"{tokens / 1_000.0:F1}K";
        return tokens.ToString("N0");
    }

    private class UsageEntry
    {
        public DateTime Timestamp { get; set; }
        public string SessionId { get; set; } = "";
        public string Model { get; set; } = "";
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int ThinkingTokens { get; set; }
        public List<string> ToolCalls { get; set; } = new();
        public int DurationMs { get; set; }
    }

    private class ToolRow
    {
        public string Rank { get; set; } = "";
        public string ToolName { get; set; } = "";
        public string Calls { get; set; } = "";
        public string Tokens { get; set; } = "";
        public string Cost { get; set; } = "";
    }
}
