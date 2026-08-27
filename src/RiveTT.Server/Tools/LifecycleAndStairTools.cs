using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

/// <summary>
/// Document lifecycle, stairs and group members — the operations previously
/// declared impossible. Two of those declarations were wrong: an ExternalEvent
/// handler MAY switch the active document (it is the API event handlers that may
/// not), and a component stair is built through the non-modal StairsEditScope.
/// </summary>
[McpServerToolType]
public static class LifecycleAndStairTools
{
    [McpServerTool(Name = "create_document"), Description(
        "Create a NEW EMPTY project from a Revit template (.rte) and save it to targetPath. This is the real " +
        "'new project': save_as_document duplicates the open model with all its history, this does not. Omit " +
        "templatePath to use Revit's default project template. The file is created and saved but NOT opened " +
        "unless activate=true. Preview with dryRun (default true) to check the template, the target and the " +
        "blockers first.")]
    public static async Task<string> CreateDocument(
        RevitConnectionManager revit,
        [Description("Absolute output .rvt path")] string targetPath,
        [Description("Absolute path of the .rte template. Omit for Revit's default project template")] string? templatePath = null,
        [Description("Replace an existing target file. Default false")] bool overwrite = false,
        [Description("Open and activate the new project in Revit once saved. Default false")] bool activate = false,
        [Description("Preview without creating. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["targetPath"] = targetPath, ["dryRun"] = dryRun };
        if (templatePath != null) p["templatePath"] = templatePath;
        p["overwrite"] = overwrite;
        p["activate"] = activate;
        return (await revit.ExecuteAsync("create_document", p, ct)).ToString();
    }

    [McpServerTool(Name = "open_document"), Description(
        "Open a .rvt file and make it the ACTIVE document in Revit. Every later tool call targets that " +
        "document and all caches are flushed. Save the current document first if it has unsaved changes — " +
        "switching does not save it. Use detachFromCentral=true for a workshared central model.")]
    public static async Task<string> OpenDocument(
        RevitConnectionManager revit,
        [Description("Absolute .rvt path to open")] string filePath,
        [Description("Detach from central and preserve worksets (workshared models). Default false")] bool detachFromCentral = false,
        [Description("Preview without opening. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["filePath"] = filePath, ["dryRun"] = dryRun };
        p["detachFromCentral"] = detachFromCentral;
        return (await revit.ExecuteAsync("open_document", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_stair"), Description(
        "Create a native component stair between two levels. runs is a JSON array [{p0:{x,y}, p1:{x,y}}, ...] " +
        "in mm plan coordinates — the levels drive the elevation, not z. Consecutive runs get an automatic " +
        "landing. The response reports actualRiserCount against desiredRiserCount and reachesTopLevel: a run " +
        "too short produces a stair that stops below the top level. Get stairsTypeId and railingTypeId from " +
        "list_system_types (OST_Stairs, OST_StairsRailing).")]
    public static async Task<string> CreateStair(
        RevitConnectionManager revit,
        [Description("Base level element ID")] long baseLevelId,
        [Description("Top level element ID (must be above the base level)")] long topLevelId,
        [Description("Runs JSON: [{p0:{x,y}, p1:{x,y}}, ...] in mm")] string runs,
        [Description("StairsType element ID. Omit for the document default")] long? stairsTypeId = null,
        [Description("Run width in mm. Omit for the type default")] double? widthMm = null,
        [Description("Railing type ID to place on the treads (creates one railing per side)")] long? railingTypeId = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["baseLevelId"] = baseLevelId,
            ["topLevelId"] = topLevelId,
            ["runs"] = JArray.Parse(runs),
            ["dryRun"] = dryRun
        };
        if (stairsTypeId != null) p["stairsTypeId"] = stairsTypeId;
        if (widthMm != null) p["widthMm"] = widthMm;
        if (railingTypeId != null) p["railingTypeId"] = railingTypeId;
        return (await revit.ExecuteAsync("create_stair", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_ramp"), Description(
        "Create a native component ramp between two levels (accessibility/PMR). runs is a JSON array " +
        "[{p0:{x,y}, p1:{x,y}}, ...] in mm plan coordinates — the levels drive the elevation. Revit has no " +
        "separate ramp API: this uses the same StairsEditScope mechanism as create_stair with an OST_Ramps " +
        "type applied — rampTypeId is REQUIRED and must come from list_system_types(category: \"OST_Ramps\"); " +
        "a stair type there produces a stair, not a ramp. The response reports the run slope against the " +
        "common 1:12 (8.3%) PMR/code limit.")]
    public static async Task<string> CreateRamp(
        RevitConnectionManager revit,
        [Description("Base level element ID")] long baseLevelId,
        [Description("Top level element ID (must be above the base level)")] long topLevelId,
        [Description("Runs JSON: [{p0:{x,y}, p1:{x,y}}, ...] in mm")] string runs,
        [Description("OST_Ramps type element ID (required) — from list_system_types(category: \"OST_Ramps\")")] long rampTypeId,
        [Description("Run width in mm. Omit for the type default")] double? widthMm = null,
        [Description("Railing type ID to place on the treads (creates one railing per side)")] long? railingTypeId = null,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["baseLevelId"] = baseLevelId,
            ["topLevelId"] = topLevelId,
            ["runs"] = JArray.Parse(runs),
            ["rampTypeId"] = rampTypeId,
            ["dryRun"] = dryRun
        };
        if (widthMm != null) p["widthMm"] = widthMm;
        if (railingTypeId != null) p["railingTypeId"] = railingTypeId;
        return (await revit.ExecuteAsync("create_ramp", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_curtain_grid_info"), Description(
        "Reads an existing curtain wall/system grid: U/V grid line ids, panel ids, mullion ids. " +
        "hostElementId is the curtain wall or curtain system element.")]
    public static async Task<string> GetCurtainGridInfo(
        RevitConnectionManager revit,
        [Description("Curtain wall or curtain system element ID")] long hostElementId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["hostElementId"] = hostElementId };
        return (await revit.ExecuteAsync("get_curtain_grid_info", p, ct)).ToString();
    }

    [McpServerTool(Name = "add_curtain_grid_line"), Description(
        "Adds a grid line to an existing curtain wall/system's grid (create the wall itself with " +
        "create_line_based_element and a curtain wall type). hostElementId, direction (u|v), and " +
        "offsetMm (distance along the host's own axis, in mm) are required.")]
    public static async Task<string> AddCurtainGridLine(
        RevitConnectionManager revit,
        [Description("Curtain wall or curtain system element ID")] long hostElementId,
        [Description("Grid line direction: u | v")] string direction,
        [Description("Offset in mm along the host's own axis for the new grid line")] double offsetMm,
        CancellationToken ct = default)
    {
        var p = new JObject { ["hostElementId"] = hostElementId, ["direction"] = direction, ["offsetMm"] = offsetMm };
        return (await revit.ExecuteAsync("add_curtain_grid_line", p, ct)).ToString();
    }

    [McpServerTool(Name = "add_curtain_mullions"), Description(
        "Adds mullions to an existing curtain wall/system's grid lines. hostElementId and mullionTypeId " +
        "are required; applies to every ungridded segment unless gridLineIds narrows it.")]
    public static async Task<string> AddCurtainMullions(
        RevitConnectionManager revit,
        [Description("Curtain wall or curtain system element ID")] long hostElementId,
        [Description("MullionType element ID — from list_system_types(category: \"OST_CurtainWallMullions\")")] long mullionTypeId,
        [Description("Grid line element IDs to restrict this to, as a JSON array of numbers. Omit to cover every ungridded segment")] System.Text.Json.JsonElement? gridLineIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["hostElementId"] = hostElementId, ["mullionTypeId"] = mullionTypeId };
        if (gridLineIds != null)
        {
            if (!JsonArrayParam.TryParse(gridLineIds, out var gridLineIdsArray))
                return JsonArrayParam.InvalidArrayResult("add_curtain_mullions", "gridLineIds", gridLineIds);
            p["gridLineIds"] = gridLineIdsArray;
        }
        return (await revit.ExecuteAsync("add_curtain_mullions", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_toposolid"), Description(
        "Creates a Toposolid (site/ground surface) from a closed boundary loop (Toposolid.Create). " +
        "toposolidTypeId and levelId are required — list types with list_system_types(category: \"OST_Toposolid\").")]
    public static async Task<string> CreateToposolid(
        RevitConnectionManager revit,
        [Description("Curve specs forming a closed loop, JSON array: [{type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}}] in mm")] string curves,
        [Description("Toposolid type element ID")] long toposolidTypeId,
        [Description("Level element ID")] long levelId,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["curves"] = JArray.Parse(curves),
            ["toposolidTypeId"] = toposolidTypeId,
            ["levelId"] = levelId
        };
        return (await revit.ExecuteAsync("create_toposolid", p, ct)).ToString();
    }

    [McpServerTool(Name = "edit_group_members"), Description(
        "Add or remove members of a model group. The Revit API cannot edit group members in place, so this " +
        "ungroups the instance, changes the member set and creates a NEW group type: the type id changes and " +
        "other instances of the original type keep the old definition. A type with several instances is " +
        "refused unless allowMultiInstance=true. Preview with dryRun.")]
    public static async Task<string> EditGroupMembers(
        RevitConnectionManager revit,
        [Description("Group INSTANCE element ID")] long groupId,
        [Description("Element IDs to add to the group. JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? addElementIds = null,
        [Description("Element IDs to remove from the group. JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? removeElementIds = null,
        [Description("Name for the resulting group type. Defaults to the original name")] string? newTypeName = null,
        [Description("Accept that other instances of the type keep the old definition. Default false")] bool allowMultiInstance = false,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["groupId"] = groupId, ["dryRun"] = dryRun };
        if (addElementIds != null)
        {
            if (!JsonArrayParam.TryParse(addElementIds, out var addElementIdsArray))
                return JsonArrayParam.InvalidArrayResult("edit_group_members", "addElementIds", addElementIds);
            p["addElementIds"] = addElementIdsArray;
        }
        if (removeElementIds != null)
        {
            if (!JsonArrayParam.TryParse(removeElementIds, out var removeElementIdsArray))
                return JsonArrayParam.InvalidArrayResult("edit_group_members", "removeElementIds", removeElementIds);
            p["removeElementIds"] = removeElementIdsArray;
        }
        if (newTypeName != null) p["newTypeName"] = newTypeName;
        p["allowMultiInstance"] = allowMultiInstance;
        return (await revit.ExecuteAsync("edit_group_members", p, ct)).ToString();
    }
}
