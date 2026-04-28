using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class MetaTools
{
    [McpServerTool(Name = "say_hello"), Description("Test MCP connection to RevitCortex. Displays a greeting in Revit.")]
    public static async Task<string> SayHello(RevitConnectionManager revit, CancellationToken ct)
    {
        var result = await revit.ExecuteAsync("say_hello", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_project_info"), Description("Get project name, address, levels, phases, worksets, and links from the active Revit document.")]
    public static async Task<string> GetProjectInfo(
        RevitConnectionManager revit,
        [Description("Include levels in the response")] bool includeLevels = true,
        [Description("Include phases in the response")] bool includePhases = true,
        [Description("Include worksets in the response")] bool includeWorksets = false,
        [Description("Include linked models in the response")] bool includeLinks = false,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["includeLevels"] = includeLevels,
            ["includePhases"] = includePhases,
            ["includeWorksets"] = includeWorksets,
            ["includeLinks"] = includeLinks,
        };
        var result = await revit.ExecuteAsync("get_project_info", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_cache_stats"), Description("Return diagnostic hit/miss telemetry from the plugin-side tool-result cache.")]
    public static async Task<string> GetCacheStats(RevitConnectionManager revit, CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_cache_stats", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "clear_cache"), Description("Clear every entry from the plugin-side tool-result cache.")]
    public static async Task<string> ClearCache(RevitConnectionManager revit, CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("clear_cache", new JObject(), ct);
        return result.ToString();
    }
}
