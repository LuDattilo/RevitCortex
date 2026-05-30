using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class RebarTools
{
    // ── Module 1: discovery ──────────────────────────────────────────────────
    [McpServerTool(Name = "list_rebar_bar_types"), Description("List all rebar bar types (id, name, model and nominal diameter in mm).")]
    public static async Task<string> ListRebarBarTypes(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_rebar_bar_types", new JObject(), ct)).ToString();

    [McpServerTool(Name = "list_rebar_hook_types"), Description("List all rebar hook types (id, name, hook angle in degrees).")]
    public static async Task<string> ListRebarHookTypes(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_rebar_hook_types", new JObject(), ct)).ToString();

    [McpServerTool(Name = "list_rebar_shapes"), Description("List all rebar shapes (id, name).")]
    public static async Task<string> ListRebarShapes(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_rebar_shapes", new JObject(), ct)).ToString();

    [McpServerTool(Name = "list_rebar_cover_types"), Description("List all rebar cover types (id, name, cover distance in mm).")]
    public static async Task<string> ListRebarCoverTypes(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_rebar_cover_types", new JObject(), ct)).ToString();

    [McpServerTool(Name = "list_rebar_splice_types"), Description("List rebar splice types (Revit 2025+; returns a version error on older targets).")]
    public static async Task<string> ListRebarSpliceTypes(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_rebar_splice_types", new JObject(), ct)).ToString();

    [McpServerTool(Name = "list_rebar_fabric_types"), Description("List fabric reinforcement types (fabric sheet types and fabric area types).")]
    public static async Task<string> ListRebarFabricTypes(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("list_rebar_fabric_types", new JObject(), ct)).ToString();

    [McpServerTool(Name = "get_rebar_host_data"), Description("Report reinforcement hosted by an element: validity and the rebar/area/path/fabric it contains, plus common cover.")]
    public static async Task<string> GetRebarHostData(
        RevitConnectionManager revit,
        [Description("Host element id (beam/column/wall/floor/foundation)")] long hostId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_rebar_host_data", new JObject { ["hostId"] = hostId }, ct)).ToString();

    [McpServerTool(Name = "get_rebar_element_data"), Description("Read a single rebar's core data: bar type, host, shape, layout rule, bar count, total length (mm), volume.")]
    public static async Task<string> GetRebarElementData(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_rebar_element_data", new JObject { ["rebarId"] = rebarId }, ct)).ToString();

    [McpServerTool(Name = "get_rebar_geometry"), Description("Return the centerline curves (mm) of a rebar at a bar position index (default 0). Optionally suppress hooks/bend radius.")]
    public static async Task<string> GetRebarGeometry(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Bar position index. Default 0")] int? barPositionIndex = null,
        [Description("Suppress hook curves. Default false")] bool? suppressHooks = null,
        [Description("Suppress bend radius. Default false")] bool? suppressBendRadius = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId };
        if (barPositionIndex != null) p["barPositionIndex"] = barPositionIndex;
        if (suppressHooks != null) p["suppressHooks"] = suppressHooks;
        if (suppressBendRadius != null) p["suppressBendRadius"] = suppressBendRadius;
        return (await revit.ExecuteAsync("get_rebar_geometry", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_rebar_constraints"), Description("List the constrained handles of a rebar and whether its constraints can be edited.")]
    public static async Task<string> GetRebarConstraints(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_rebar_constraints", new JObject { ["rebarId"] = rebarId }, ct)).ToString();

    [McpServerTool(Name = "get_reinforcement_settings"), Description("Read document-level reinforcement settings.")]
    public static async Task<string> GetReinforcementSettings(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_reinforcement_settings", new JObject(), ct)).ToString();

    [McpServerTool(Name = "get_rebar_api_capabilities"), Description("Report which version-gated reinforcement features the running Revit supports.")]
    public static async Task<string> GetRebarApiCapabilities(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_rebar_api_capabilities", new JObject(), ct)).ToString();
}
