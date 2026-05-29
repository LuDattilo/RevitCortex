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

    [McpServerTool(Name = "set_project_info"), Description("Set editable Project Information fields. Only the fields you pass are changed; others are left untouched.")]
    public static async Task<string> SetProjectInfo(
        RevitConnectionManager revit,
        [Description("Project name")] string? projectName = null,
        [Description("Project number")] string? projectNumber = null,
        [Description("Project address")] string? projectAddress = null,
        [Description("Building name")] string? buildingName = null,
        [Description("Author")] string? author = null,
        [Description("Organization name")] string? organizationName = null,
        [Description("Organization description")] string? organizationDescription = null,
        [Description("Issue date")] string? issueDate = null,
        [Description("Project status")] string? status = null,
        [Description("Client name (Owner)")] string? clientName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (projectName != null) p["projectName"] = projectName;
        if (projectNumber != null) p["projectNumber"] = projectNumber;
        if (projectAddress != null) p["projectAddress"] = projectAddress;
        if (buildingName != null) p["buildingName"] = buildingName;
        if (author != null) p["author"] = author;
        if (organizationName != null) p["organizationName"] = organizationName;
        if (organizationDescription != null) p["organizationDescription"] = organizationDescription;
        if (issueDate != null) p["issueDate"] = issueDate;
        if (status != null) p["status"] = status;
        if (clientName != null) p["clientName"] = clientName;
        var result = await revit.ExecuteAsync("set_project_info", p, ct);
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
