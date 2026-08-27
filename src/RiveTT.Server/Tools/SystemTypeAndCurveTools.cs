using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

/// <summary>
/// Discovery of system types, and creation of pure curve elements. These close the
/// two gaps that had no workaround: a system type (wall, floor, railing, stair,
/// title block) could not be enumerated at all, and no tool could draw a line —
/// detail, model, or room separation — because every creation path required a
/// family type.
/// </summary>
[McpServerToolType]
public static class SystemTypeAndCurveTools
{
    [McpServerTool(Name = "list_system_types"), Description(
        "List the system types of a category: walls, floors, ceilings, roofs, railings, stairs, ramps, " +
        "viewports, text, dimensions, sheets, title blocks. System types are NOT loadable families, so " +
        "duplicate_family_type does not apply to them — use duplicate_system_type. Omit the category to get " +
        "the per-category inventory with its language-independent OST codes. The returned typeId feeds " +
        "create_wall, create_railing, create_floor, create_sheet and duplicate_system_type.")]
    public static async Task<string> ListSystemTypes(
        RevitConnectionManager revit,
        [Description("Category: OST_* code, English name, or localized label. Omit for the inventory")] string? category = null,
        [Description("Case- and accent-insensitive substring filter on family or type name")] string? nameFilter = null,
        [Description("Also include loadable family types. Default: false")] bool includeLoadable = false,
        [Description("Max types to return. Default: 200")] int? limit = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (category != null) p["category"] = category;
        if (nameFilter != null) p["nameFilter"] = nameFilter;
        p["includeLoadable"] = includeLoadable;
        if (limit != null) p["limit"] = limit;
        return (await revit.ExecuteAsync("list_system_types", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_detail_line"), Description(
        "Draw 2D detail lines in a view (view-owned, not visible in other views). path is a JSON array " +
        "[{x,y,z}, ...] in mm; consecutive points become segments. Not available in 3D views — use " +
        "create_model_line there.")]
    public static async Task<string> CreateDetailLine(
        RevitConnectionManager revit,
        [Description("Path JSON: [{x,y,z}, ...] in mm")] string path,
        [Description("View element ID. Defaults to the active view")] long? viewId = null,
        [Description("Line style name (e.g. Lignes fines). Optional")] string? lineStyleName = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["path"] = JArray.Parse(path), ["dryRun"] = dryRun };
        if (viewId != null) p["viewId"] = viewId;
        if (lineStyleName != null) p["lineStyleName"] = lineStyleName;
        return (await revit.ExecuteAsync("create_detail_line", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_model_line"), Description(
        "Draw 3D model lines on a horizontal sketch plane. path is a JSON array [{x,y,z}, ...] in mm; all " +
        "points must share the same z, which sets the plane elevation. Model lines are visible in every view.")]
    public static async Task<string> CreateModelLine(
        RevitConnectionManager revit,
        [Description("Path JSON: [{x,y,z}, ...] in mm, all at the same z")] string path,
        [Description("Line style name. Optional")] string? lineStyleName = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["path"] = JArray.Parse(path), ["dryRun"] = dryRun };
        if (lineStyleName != null) p["lineStyleName"] = lineStyleName;
        return (await revit.ExecuteAsync("create_model_line", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_room_separation_line"), Description(
        "Draw room separation lines in a plan view to split or bound a room without building a physical " +
        "wall. path is a JSON array [{x,y,z}, ...] in mm. This is the correct tool for cutting a room in " +
        "two when no wall is wanted; a low wall would be wrong in schedules and exports.")]
    public static async Task<string> CreateRoomSeparationLine(
        RevitConnectionManager revit,
        [Description("Path JSON: [{x,y,z}, ...] in mm")] string path,
        [Description("Plan view element ID. Defaults to the active view, which must be a plan")] long? viewId = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["path"] = JArray.Parse(path), ["dryRun"] = dryRun };
        if (viewId != null) p["viewId"] = viewId;
        return (await revit.ExecuteAsync("create_room_separation_line", p, ct)).ToString();
    }

    [McpServerTool(Name = "place_title_block"), Description(
        "Place a title block instance on an existing sheet. Use it to repair a sheet that has no frame. " +
        "Call it without titleBlockId to get the list of title blocks loaded in the document.")]
    public static async Task<string> PlaceTitleBlock(
        RevitConnectionManager revit,
        [Description("Sheet (ViewSheet) element ID")] long sheetId,
        [Description("Title block family TYPE id (OST_TitleBlocks). Omit to list the available ones")] long? titleBlockId = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["sheetId"] = sheetId, ["dryRun"] = dryRun };
        if (titleBlockId != null) p["titleBlockId"] = titleBlockId;
        return (await revit.ExecuteAsync("place_title_block", p, ct)).ToString();
    }
}
