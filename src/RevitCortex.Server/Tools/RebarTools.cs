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

    // ── Module 2: creation & mutation ────────────────────────────────────────
    [McpServerTool(Name = "create_rebar_from_shape"), Description("Create a shape-driven rebar in a host from a rebar shape. origin/xVec/yVec are JSON {x,y,z} in mm. Optional layout JSON.")]
    public static async Task<string> CreateRebarFromShape(
        RevitConnectionManager revit,
        [Description("Host element id")] long hostId,
        [Description("Origin point JSON {x,y,z} in mm")] string origin,
        [Description("Local X direction JSON {x,y,z}")] string xVec,
        [Description("Local Y direction JSON {x,y,z}")] string yVec,
        [Description("Rebar shape id")] long? shapeId = null,
        [Description("Rebar shape name (used if shapeId omitted)")] string? shapeName = null,
        [Description("Rebar bar type id")] long? barTypeId = null,
        [Description("Rebar bar type name (used if barTypeId omitted)")] string? barTypeName = null,
        [Description("Layout JSON {rule, number?, arrayLengthMm?, spacingMm?, barsOnNormalSide?, includeFirstBar?, includeLastBar?}")] string? layout = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["hostId"] = hostId,
            ["origin"] = JObject.Parse(origin),
            ["xVec"] = JObject.Parse(xVec),
            ["yVec"] = JObject.Parse(yVec)
        };
        if (shapeId != null) p["shapeId"] = shapeId;
        if (shapeName != null) p["shapeName"] = shapeName;
        if (barTypeId != null) p["barTypeId"] = barTypeId;
        if (barTypeName != null) p["barTypeName"] = barTypeName;
        if (layout != null) p["layout"] = JObject.Parse(layout);
        return (await revit.ExecuteAsync("create_rebar_from_shape", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_rebar_from_curves"), Description("Create a rebar from explicit coplanar curves (mm) in a host. curves is a JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}}; normal is JSON {x,y,z}. Optional hooks/layout.")]
    public static async Task<string> CreateRebarFromCurves(
        RevitConnectionManager revit,
        [Description("Host element id")] long hostId,
        [Description("Curves JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm")] string curves,
        [Description("Plane normal JSON {x,y,z}")] string normal,
        [Description("Rebar style: Standard|StirrupTie")] string style = "Standard",
        [Description("Start hook type id")] long? startHookId = null,
        [Description("End hook type id")] long? endHookId = null,
        [Description("Rebar bar type id")] long? barTypeId = null,
        [Description("Rebar bar type name (used if barTypeId omitted)")] string? barTypeName = null,
        [Description("Layout JSON {rule, number?, arrayLengthMm?, spacingMm?, ...}")] string? layout = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["hostId"] = hostId,
            ["curves"] = JArray.Parse(curves),
            ["normal"] = JObject.Parse(normal),
            ["style"] = style
        };
        if (startHookId != null) p["startHookId"] = startHookId;
        if (endHookId != null) p["endHookId"] = endHookId;
        if (barTypeId != null) p["barTypeId"] = barTypeId;
        if (barTypeName != null) p["barTypeName"] = barTypeName;
        if (layout != null) p["layout"] = JObject.Parse(layout);
        return (await revit.ExecuteAsync("create_rebar_from_curves", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_free_form_rebar"), Description("Create an unconstrained free-form rebar from curve loops (mm) in a host. loops is a JSON array of loops, each a JSON array of curve specs {type, start, end, mid?}.")]
    public static async Task<string> CreateFreeFormRebar(
        RevitConnectionManager revit,
        [Description("Host element id")] long hostId,
        [Description("Loops JSON: array of loops, each an array of curve specs {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm")] string loops,
        [Description("Rebar style: Standard|StirrupTie")] string style = "Standard",
        [Description("Rebar bar type id")] long? barTypeId = null,
        [Description("Rebar bar type name (used if barTypeId omitted)")] string? barTypeName = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["hostId"] = hostId,
            ["loops"] = JArray.Parse(loops),
            ["style"] = style
        };
        if (barTypeId != null) p["barTypeId"] = barTypeId;
        if (barTypeName != null) p["barTypeName"] = barTypeName;
        return (await revit.ExecuteAsync("create_free_form_rebar", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_rebar_layout"), Description("Set the distribution layout of a shape-driven rebar. layout is JSON {rule, number?, arrayLengthMm?, spacingMm?, ...}.")]
    public static async Task<string> SetRebarLayout(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Layout JSON {rule: single|fixed_number|maximum_spacing|number_with_spacing|minimum_clear_spacing, ...}")] string layout,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId, ["layout"] = JObject.Parse(layout) };
        return (await revit.ExecuteAsync("set_rebar_layout", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_rebar_shape"), Description("Change the shape of a shape-driven rebar. Provide rebarId and shapeId or shapeName.")]
    public static async Task<string> SetRebarShape(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Rebar shape id")] long? shapeId = null,
        [Description("Rebar shape name (used if shapeId omitted)")] string? shapeName = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId };
        if (shapeId != null) p["shapeId"] = shapeId;
        if (shapeName != null) p["shapeName"] = shapeName;
        return (await revit.ExecuteAsync("set_rebar_shape", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_rebar_hooks"), Description("Set the hook type at rebar ends. Provide rebarId and startHookId and/or endHookId (pass 0 to clear an end's hook). Works on all Revit versions.")]
    public static async Task<string> SetRebarHooks(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Start hook type id (0 to clear)")] long? startHookId = null,
        [Description("End hook type id (0 to clear)")] long? endHookId = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId };
        if (startHookId != null) p["startHookId"] = startHookId;
        if (endHookId != null) p["endHookId"] = endHookId;
        return (await revit.ExecuteAsync("set_rebar_hooks", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_rebar_terminations"), Description("Set rebar end terminations (orientation/rotation). Revit 2026+ only; returns a version error on older targets. Provide rebarId, end (0|1), orientation, rotationDegrees.")]
    public static async Task<string> SetRebarTerminations(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Rebar end: 0=start, 1=end")] int? end = null,
        [Description("Termination orientation (e.g. Left|Right)")] string? orientation = null,
        [Description("Rotation angle in degrees")] double? rotationDegrees = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId };
        if (end != null) p["end"] = end;
        if (orientation != null) p["orientation"] = orientation;
        if (rotationDegrees != null) p["rotationDegrees"] = rotationDegrees;
        return (await revit.ExecuteAsync("set_rebar_terminations", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_rebar_host"), Description("Reassign a rebar to a new host. Provide rebarId and newHostId (must be a valid rebar host).")]
    public static async Task<string> SetRebarHost(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("New host element id")] long newHostId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId, ["newHostId"] = newHostId };
        return (await revit.ExecuteAsync("set_rebar_host", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_rebar_visibility"), Description("Set rebar view presentation. Provide rebarId, viewId, and unobscured (show in front of host).")]
    public static async Task<string> SetRebarVisibility(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("View id")] long viewId,
        [Description("Show unobscured (in front of host). Default true")] bool? unobscured = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId, ["viewId"] = viewId };
        if (unobscured != null) p["unobscured"] = unobscured;
        return (await revit.ExecuteAsync("set_rebar_visibility", p, ct)).ToString();
    }

    [McpServerTool(Name = "move_rebar_in_set"), Description("Move a single bar within a rebar set by a translation vector (mm). Provide rebarId, barPositionIndex, translation JSON {x,y,z}. Pass reset:true to clear a prior move.")]
    public static async Task<string> MoveRebarInSet(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Bar position index. Default 0")] int? barPositionIndex = null,
        [Description("Translation JSON {x,y,z} in mm (required unless reset:true)")] string? translation = null,
        [Description("Reset a prior move on this bar. Default false")] bool? reset = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId };
        if (barPositionIndex != null) p["barPositionIndex"] = barPositionIndex;
        if (translation != null) p["translation"] = JObject.Parse(translation);
        if (reset != null) p["reset"] = reset;
        return (await revit.ExecuteAsync("move_rebar_in_set", p, ct)).ToString();
    }

    [McpServerTool(Name = "include_exclude_rebar_bars"), Description("Show or hide a single bar of a rebar set in a view. Provide rebarId, viewId, barPositionIndex, hidden (true=hide).")]
    public static async Task<string> IncludeExcludeRebarBars(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("View id")] long viewId,
        [Description("Bar position index. Default 0")] int? barPositionIndex = null,
        [Description("Hide the bar. Default true")] bool? hidden = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId, ["viewId"] = viewId };
        if (barPositionIndex != null) p["barPositionIndex"] = barPositionIndex;
        if (hidden != null) p["hidden"] = hidden;
        return (await revit.ExecuteAsync("include_exclude_rebar_bars", p, ct)).ToString();
    }

    [McpServerTool(Name = "split_rebar"), Description("Split a shape-driven rebar set into two sets at a given bar position. Provide rebarId and splitAtPosition (1..count-1). Returns the original and new rebar ids.")]
    public static async Task<string> SplitRebar(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Bar position to split at (1..count-1)")] int splitAtPosition,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId, ["splitAtPosition"] = splitAtPosition };
        return (await revit.ExecuteAsync("split_rebar", p, ct)).ToString();
    }

    // ── Module 3: area / path reinforcement ──────────────────────────────────
    [McpServerTool(Name = "create_area_reinforcement"), Description("Create an area reinforcement system on a host (wall/floor/foundation). majorDirection is JSON {x,y,z}; optional curves is a JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm for an explicit boundary. memberCount/memberIds reflect the bars after regeneration; if memberCountNote is present the count was still 0 — re-read with get_area_reinforcement_data.")]
    public static async Task<string> CreateAreaReinforcement(
        RevitConnectionManager revit,
        [Description("Host element id")] long hostId,
        [Description("Major bar direction JSON {x,y,z}")] string majorDirection,
        [Description("Boundary curves JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm (omit to cover the host boundary)")] string? curves = null,
        [Description("Rebar bar type id")] long? barTypeId = null,
        [Description("Rebar bar type name (used if barTypeId omitted)")] string? barTypeName = null,
        [Description("Area reinforcement type id (default type used if omitted)")] long? areaTypeId = null,
        [Description("Rebar hook type id (no hook if omitted)")] long? hookTypeId = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["hostId"] = hostId, ["majorDirection"] = JObject.Parse(majorDirection) };
        if (curves != null) p["curves"] = JArray.Parse(curves);
        if (barTypeId != null) p["barTypeId"] = barTypeId;
        if (barTypeName != null) p["barTypeName"] = barTypeName;
        if (areaTypeId != null) p["areaTypeId"] = areaTypeId;
        if (hookTypeId != null) p["hookTypeId"] = hookTypeId;
        return (await revit.ExecuteAsync("create_area_reinforcement", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_path_reinforcement"), Description("Create a path reinforcement system on a host. curves is a JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm (required). Optional flip, pathTypeId, startHookId, endHookId. memberCount/memberIds reflect the bars after regeneration; if memberCountNote is present the count was still 0 — re-read with get_path_reinforcement_data.")]
    public static async Task<string> CreatePathReinforcement(
        RevitConnectionManager revit,
        [Description("Host element id")] long hostId,
        [Description("Path curves JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm")] string curves,
        [Description("Flip the reinforcement side. Default false")] bool? flip = null,
        [Description("Rebar bar type id")] long? barTypeId = null,
        [Description("Rebar bar type name (used if barTypeId omitted)")] string? barTypeName = null,
        [Description("Path reinforcement type id (default type used if omitted)")] long? pathTypeId = null,
        [Description("Start hook type id")] long? startHookId = null,
        [Description("End hook type id")] long? endHookId = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["hostId"] = hostId, ["curves"] = JArray.Parse(curves) };
        if (flip != null) p["flip"] = flip;
        if (barTypeId != null) p["barTypeId"] = barTypeId;
        if (barTypeName != null) p["barTypeName"] = barTypeName;
        if (pathTypeId != null) p["pathTypeId"] = pathTypeId;
        if (startHookId != null) p["startHookId"] = startHookId;
        if (endHookId != null) p["endHookId"] = endHookId;
        return (await revit.ExecuteAsync("create_path_reinforcement", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_area_reinforcement_layers"), Description("Activate or deactivate a layer of an area reinforcement system. Provide areaReinforcementId, layer (top_major|top_minor|bottom_major|bottom_minor) and active.")]
    public static async Task<string> SetAreaReinforcementLayers(
        RevitConnectionManager revit,
        [Description("Area reinforcement element id")] long areaReinforcementId,
        [Description("Layer: top_major|top_minor|bottom_major|bottom_minor")] string layer,
        [Description("Activate (true) or deactivate (false) the layer")] bool active,
        CancellationToken ct = default)
    {
        var p = new JObject { ["areaReinforcementId"] = areaReinforcementId, ["layer"] = layer, ["active"] = active };
        return (await revit.ExecuteAsync("set_area_reinforcement_layers", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_path_reinforcement_options"), Description("Set options on a path reinforcement system. Provide pathReinforcementId and any of additionalOffsetMm, primaryBarOrientation (TopOrExterior|BottomOrInterior|NearSide|FarSide), alternatingBarOrientation. Unsupported keys are reported in warnings.")]
    public static async Task<string> SetPathReinforcementOptions(
        RevitConnectionManager revit,
        [Description("Path reinforcement element id")] long pathReinforcementId,
        [Description("Additional offset in mm")] double? additionalOffsetMm = null,
        [Description("Primary bar orientation: TopOrExterior|BottomOrInterior|NearSide|FarSide")] string? primaryBarOrientation = null,
        [Description("Alternating bar orientation: TopOrExterior|BottomOrInterior|NearSide|FarSide")] string? alternatingBarOrientation = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["pathReinforcementId"] = pathReinforcementId };
        if (additionalOffsetMm != null) p["additionalOffsetMm"] = additionalOffsetMm;
        if (primaryBarOrientation != null) p["primaryBarOrientation"] = primaryBarOrientation;
        if (alternatingBarOrientation != null) p["alternatingBarOrientation"] = alternatingBarOrientation;
        return (await revit.ExecuteAsync("set_path_reinforcement_options", p, ct)).ToString();
    }

    [McpServerTool(Name = "convert_rebar_system_to_rebars"), Description("Convert an area or path reinforcement system into standalone rebars (destructive). Provide systemId. Returns the resulting standalone rebar ids.")]
    public static async Task<string> ConvertRebarSystemToRebars(
        RevitConnectionManager revit,
        [Description("Area or path reinforcement element id")] long systemId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["systemId"] = systemId };
        return (await revit.ExecuteAsync("convert_rebar_system_to_rebars", p, ct)).ToString();
    }

    [McpServerTool(Name = "remove_rebar_system"), Description("Remove an area or path reinforcement system (destructive). Provide systemId.")]
    public static async Task<string> RemoveRebarSystem(
        RevitConnectionManager revit,
        [Description("Area or path reinforcement element id")] long systemId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["systemId"] = systemId };
        return (await revit.ExecuteAsync("remove_rebar_system", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_area_reinforcement_data"), Description("Read an area reinforcement system: major direction (mm vector), type id/name, host id, member rebar ids, boundary curve ids, member count.")]
    public static async Task<string> GetAreaReinforcementData(
        RevitConnectionManager revit,
        [Description("Area reinforcement element id")] long areaReinforcementId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_area_reinforcement_data", new JObject { ["areaReinforcementId"] = areaReinforcementId }, ct)).ToString();

    [McpServerTool(Name = "get_path_reinforcement_data"), Description("Read a path reinforcement system: type id/name, host id, member rebar ids, curve element ids, additional offset (mm), primary bar orientation.")]
    public static async Task<string> GetPathReinforcementData(
        RevitConnectionManager revit,
        [Description("Path reinforcement element id")] long pathReinforcementId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_path_reinforcement_data", new JObject { ["pathReinforcementId"] = pathReinforcementId }, ct)).ToString();

    // ── Module 4: fabric reinforcement ───────────────────────────────────────
    [McpServerTool(Name = "create_fabric_area"), Description("Create a fabric area system on a host (wall/floor/foundation). majorDirection is JSON {x,y,z}; optional curves is a JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm forming a closed loop.")]
    public static async Task<string> CreateFabricArea(
        RevitConnectionManager revit,
        [Description("Host element id")] long hostId,
        [Description("Major direction JSON {x,y,z}")] string majorDirection,
        [Description("Boundary curves JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm forming a closed loop (omit to cover the host boundary)")] string? curves = null,
        [Description("Fabric sheet type id")] long? fabricSheetTypeId = null,
        [Description("Fabric sheet type name (used if fabricSheetTypeId omitted)")] string? fabricSheetTypeName = null,
        [Description("Fabric area type id (default type used if omitted)")] long? fabricAreaTypeId = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["hostId"] = hostId, ["majorDirection"] = JObject.Parse(majorDirection) };
        if (curves != null) p["curves"] = JArray.Parse(curves);
        if (fabricSheetTypeId != null) p["fabricSheetTypeId"] = fabricSheetTypeId;
        if (fabricSheetTypeName != null) p["fabricSheetTypeName"] = fabricSheetTypeName;
        if (fabricAreaTypeId != null) p["fabricAreaTypeId"] = fabricAreaTypeId;
        return (await revit.ExecuteAsync("create_fabric_area", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_fabric_sheet"), Description("Create a single fabric sheet in a host. Provide hostId and fabricSheetTypeId or fabricSheetTypeName; optional bendProfile is a JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm forming a closed loop (makes the sheet bent).")]
    public static async Task<string> CreateFabricSheet(
        RevitConnectionManager revit,
        [Description("Host element id")] long hostId,
        [Description("Fabric sheet type id")] long? fabricSheetTypeId = null,
        [Description("Fabric sheet type name (used if fabricSheetTypeId omitted)")] string? fabricSheetTypeName = null,
        [Description("Bend profile JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm forming a closed loop (omit for a flat sheet)")] string? bendProfile = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["hostId"] = hostId };
        if (fabricSheetTypeId != null) p["fabricSheetTypeId"] = fabricSheetTypeId;
        if (fabricSheetTypeName != null) p["fabricSheetTypeName"] = fabricSheetTypeName;
        if (bendProfile != null) p["bendProfile"] = JArray.Parse(bendProfile);
        return (await revit.ExecuteAsync("create_fabric_sheet", p, ct)).ToString();
    }

    [McpServerTool(Name = "place_fabric_sheet"), Description("Place an existing fabric sheet into a host. Provide fabricSheetId and hostId; optional transform is JSON {translation:{x,y,z}} in mm (default identity).")]
    public static async Task<string> PlaceFabricSheet(
        RevitConnectionManager revit,
        [Description("Fabric sheet element id")] long fabricSheetId,
        [Description("Host element id")] long hostId,
        [Description("Transform JSON {translation:{x,y,z}} in mm (default identity)")] string? transform = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["fabricSheetId"] = fabricSheetId, ["hostId"] = hostId };
        if (transform != null) p["transform"] = JObject.Parse(transform);
        return (await revit.ExecuteAsync("place_fabric_sheet", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_fabric_sheet_bend_profile"), Description("Set the bend profile of a bent fabric sheet. Provide fabricSheetId and bendProfile (a JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm forming a closed loop). Only valid when the sheet is bent.")]
    public static async Task<string> SetFabricSheetBendProfile(
        RevitConnectionManager revit,
        [Description("Fabric sheet element id")] long fabricSheetId,
        [Description("Bend profile JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm forming a closed loop")] string bendProfile,
        CancellationToken ct = default)
    {
        var p = new JObject { ["fabricSheetId"] = fabricSheetId, ["bendProfile"] = JArray.Parse(bendProfile) };
        return (await revit.ExecuteAsync("set_fabric_sheet_bend_profile", p, ct)).ToString();
    }

    [McpServerTool(Name = "remove_fabric_reinforcement_system"), Description("Remove a fabric area reinforcement system (destructive). Provide fabricAreaId.")]
    public static async Task<string> RemoveFabricReinforcementSystem(
        RevitConnectionManager revit,
        [Description("Fabric area element id")] long fabricAreaId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("remove_fabric_reinforcement_system", new JObject { ["fabricAreaId"] = fabricAreaId }, ct)).ToString();

    [McpServerTool(Name = "get_fabric_area_data"), Description("Read a fabric area system: type id/name, host id, sheet ids, sheet count, major direction (mm vector).")]
    public static async Task<string> GetFabricAreaData(
        RevitConnectionManager revit,
        [Description("Fabric area element id")] long fabricAreaId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_fabric_area_data", new JObject { ["fabricAreaId"] = fabricAreaId }, ct)).ToString();

    [McpServerTool(Name = "get_fabric_sheet_data"), Description("Read a fabric sheet: type id/name, isBent, fabricNumber, cut overall length and width (mm).")]
    public static async Task<string> GetFabricSheetData(
        RevitConnectionManager revit,
        [Description("Fabric sheet element id")] long fabricSheetId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_fabric_sheet_data", new JObject { ["fabricSheetId"] = fabricSheetId }, ct)).ToString();

    [McpServerTool(Name = "get_fabric_wire_data"), Description("Read the wire items of a fabric sheet in one direction. Provide fabricSheetId and direction (major|minor); optional maxWires (default 200). Returns per-wire diameter (mm), distance (mm), wire length (mm).")]
    public static async Task<string> GetFabricWireData(
        RevitConnectionManager revit,
        [Description("Fabric sheet element id")] long fabricSheetId,
        [Description("Wire direction: major|minor")] string direction,
        [Description("Maximum wires to return. Default 200")] int? maxWires = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["fabricSheetId"] = fabricSheetId, ["direction"] = direction };
        if (maxWires != null) p["maxWires"] = maxWires;
        return (await revit.ExecuteAsync("get_fabric_wire_data", p, ct)).ToString();
    }

    // ── Module 5: couplers, constraints, propagation, annotations, splices ───
    [McpServerTool(Name = "create_rebar_coupler"), Description("Create a rebar coupler connecting two bar ends, or cap one. Provide couplerTypeId or couplerTypeName (a Coupler-category family type), end1 (JSON {rebarId,end}) and optional end2 (JSON {rebarId,end}). 'end' is 0 or 1.")]
    public static async Task<string> CreateRebarCoupler(
        RevitConnectionManager revit,
        [Description("end1 JSON descriptor {rebarId,end} (end is 0 or 1)")] string end1,
        [Description("Coupler type id (a FamilySymbol of category OST_Coupler)")] long? couplerTypeId = null,
        [Description("Coupler type name (used if couplerTypeId omitted)")] string? couplerTypeName = null,
        [Description("end2 JSON descriptor {rebarId,end} (omit to cap a single bar)")] string? end2 = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["end1"] = JObject.Parse(end1) };
        if (couplerTypeId != null) p["couplerTypeId"] = couplerTypeId;
        if (couplerTypeName != null) p["couplerTypeName"] = couplerTypeName;
        if (end2 != null) p["end2"] = JObject.Parse(end2);
        return (await revit.ExecuteAsync("create_rebar_coupler", p, ct)).ToString();
    }

    [McpServerTool(Name = "set_rebar_coupler_visibility"), Description("Set a coupler unobscured (solid) or obscured in a view. Provide couplerId, viewId, unobscured (bool).")]
    public static async Task<string> SetRebarCouplerVisibility(
        RevitConnectionManager revit,
        [Description("Coupler element id")] long couplerId,
        [Description("View element id")] long viewId,
        [Description("True = unobscured (solid); false = obscured")] bool unobscured,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("set_rebar_coupler_visibility",
            new JObject { ["couplerId"] = couplerId, ["viewId"] = viewId, ["unobscured"] = unobscured }, ct)).ToString();

    [McpServerTool(Name = "manage_rebar_constraints"), Description("Inspect/edit rebar constraints. Provide rebarId and action: list_handles | list_candidates (with handleIndex) | set_preferred (with handleIndex, candidateIndex) | remove_preferred (with handleIndex) | recompute (2025+). list_* actions are read-only.")]
    public static async Task<string> ManageRebarConstraints(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Action: list_handles | list_candidates | set_preferred | remove_preferred | recompute")] string action,
        [Description("Handle index from action=list_handles (required for list_candidates/set_preferred/remove_preferred)")] int? handleIndex = null,
        [Description("Candidate index from action=list_candidates (required for set_preferred)")] int? candidateIndex = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId, ["action"] = action };
        if (handleIndex != null) p["handleIndex"] = handleIndex;
        if (candidateIndex != null) p["candidateIndex"] = candidateIndex;
        return (await revit.ExecuteAsync("manage_rebar_constraints", p, ct)).ToString();
    }

    [McpServerTool(Name = "propagate_rebar"), Description("Propagate rebar to similar hosts. NOTE: the Revit API exposes no rebar-propagation method on any supported version; this returns a structured 'unsupported' result. Provide rebarId, optional targetHostIds JSON array.")]
    public static async Task<string> PropagateRebar(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Optional target host ids JSON array (e.g. [123,456])")] string? targetHostIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId };
        if (targetHostIds != null) p["targetHostIds"] = JArray.Parse(targetHostIds);
        return (await revit.ExecuteAsync("propagate_rebar", p, ct)).ToString();
    }

    [McpServerTool(Name = "unify_rebars"), Description("Unify compatible standalone bars into one (Revit 2025+). Provide rebarIds (JSON array of >=2 ids); bars are unified pairwise into a single rebar. Returns a version error on older targets.")]
    public static async Task<string> UnifyRebars(
        RevitConnectionManager revit,
        [Description("Rebar ids JSON array (>=2 ids, e.g. [123,456])")] string rebarIds,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("unify_rebars", new JObject { ["rebarIds"] = JArray.Parse(rebarIds) }, ct)).ToString();

    [McpServerTool(Name = "transfer_rebar_annotations"), Description("Transfer rebar annotations between views by recreating MultiReferenceAnnotations over the rebars visible in the source view. Provide sourceViewId, targetViewId. Per-element issues are reported in warnings[].")]
    public static async Task<string> TransferRebarAnnotations(
        RevitConnectionManager revit,
        [Description("Source view element id (views from which rebar are read)")] long sourceViewId,
        [Description("Target view element id (where annotations are created)")] long targetViewId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("transfer_rebar_annotations",
            new JObject { ["sourceViewId"] = sourceViewId, ["targetViewId"] = targetViewId }, ct)).ToString();

    [McpServerTool(Name = "get_rebar_coupler_data"), Description("Read a rebar coupler: couplerMark, quantity, type id/name, and each linked reinforcement descriptor {rebarId, end}. Provide couplerId.")]
    public static async Task<string> GetRebarCouplerData(
        RevitConnectionManager revit,
        [Description("Coupler element id")] long couplerId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_rebar_coupler_data", new JObject { ["couplerId"] = couplerId }, ct)).ToString();

    [McpServerTool(Name = "get_rebar_constraint_candidates"), Description("List the constraint candidates for one rebar handle. Provide rebarId and handleIndex (from manage_rebar_constraints action=list_handles). Read-only.")]
    public static async Task<string> GetRebarConstraintCandidates(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Handle index from manage_rebar_constraints action=list_handles")] int handleIndex,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_rebar_constraint_candidates",
            new JObject { ["rebarId"] = rebarId, ["handleIndex"] = handleIndex }, ct)).ToString();

    [McpServerTool(Name = "splice_rebar"), Description("Splice a rebar by rules at a position (Revit 2025+). Provide rebarId, optional spliceTypeId, position (End1|Middle|End2). Returns the resulting rebar ids, or a version error on older targets.")]
    public static async Task<string> SpliceRebar(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Splice type id (optional; resolved/created if omitted)")] long? spliceTypeId = null,
        [Description("Splice position: End1 | Middle | End2 (default Middle)")] string? position = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId };
        if (spliceTypeId != null) p["spliceTypeId"] = spliceTypeId;
        if (position != null) p["position"] = position;
        return (await revit.ExecuteAsync("splice_rebar", p, ct)).ToString();
    }

    [McpServerTool(Name = "remove_rebar_splice"), Description("Remove a rebar splice at a bar end (Revit 2025+). Provide rebarId and optional barEnd (0 or 1; default 0). Returns a version error on older targets.")]
    public static async Task<string> RemoveRebarSplice(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Bar end: 0 or 1 (default 0)")] int? barEnd = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId };
        if (barEnd != null) p["barEnd"] = barEnd;
        return (await revit.ExecuteAsync("remove_rebar_splice", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_rebar_splice_data"), Description("Read rebar splice data (Revit 2025+): for each bar end, lap length (mm), stagger (mm), splice position, connected rebar id/end, plus the splice chain. Provide rebarId. Returns a version error on older targets.")]
    public static async Task<string> GetRebarSpliceData(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_rebar_splice_data", new JObject { ["rebarId"] = rebarId }, ct)).ToString();

    [McpServerTool(Name = "get_rebar_splice_candidates"), Description("Report candidate splice geometries for a rebar by rules (Revit 2025+, read-only). Provide rebarId, optional spliceTypeId, position (End1|Middle|End2). Returns a version error on older targets.")]
    public static async Task<string> GetRebarSpliceCandidates(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("Splice type id (optional; first existing type used if omitted)")] long? spliceTypeId = null,
        [Description("Splice position: End1 | Middle | End2 (default Middle)")] string? position = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId };
        if (spliceTypeId != null) p["spliceTypeId"] = spliceTypeId;
        if (position != null) p["position"] = position;
        return (await revit.ExecuteAsync("get_rebar_splice_candidates", p, ct)).ToString();
    }

    // ── Module 6: settings, rounding, numbering, bending details ─────────────
    [McpServerTool(Name = "set_reinforcement_settings"), Description("Set document-level reinforcement settings. Provide any of hostStructuralRebar, rebarShapeDefinesHooks, rebarShapeDefinesEndTreatments (bools). Some toggles only apply when the document has no reinforcement; blocked changes are reported in warnings.")]
    public static async Task<string> SetReinforcementSettings(
        RevitConnectionManager revit,
        [Description("Host structural rebar (bool)")] bool? hostStructuralRebar = null,
        [Description("Rebar shape defines hooks (bool)")] bool? rebarShapeDefinesHooks = null,
        [Description("Rebar shape defines end treatments (bool)")] bool? rebarShapeDefinesEndTreatments = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (hostStructuralRebar != null) p["hostStructuralRebar"] = hostStructuralRebar;
        if (rebarShapeDefinesHooks != null) p["rebarShapeDefinesHooks"] = rebarShapeDefinesHooks;
        if (rebarShapeDefinesEndTreatments != null) p["rebarShapeDefinesEndTreatments"] = rebarShapeDefinesEndTreatments;
        return (await revit.ExecuteAsync("set_reinforcement_settings", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_rebar_rounding"), Description("Read rebar length-rounding rules. Without rebarId returns the document default; with rebarId returns that bar's effective rounding. Method is Nearest|Up|Down. There is no volume rounding in the Revit API.")]
    public static async Task<string> GetRebarRounding(
        RevitConnectionManager revit,
        [Description("Rebar element id (optional; document default if omitted)")] long? rebarId = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (rebarId != null) p["rebarId"] = rebarId;
        return (await revit.ExecuteAsync("get_rebar_rounding", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_fabric_rounding"), Description("Read the document fabric length-rounding rules (apply flag, segment/total length rounding in mm and method Nearest|Up|Down).")]
    public static async Task<string> GetFabricRounding(RevitConnectionManager revit, CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_fabric_rounding", new JObject(), ct)).ToString();

    [McpServerTool(Name = "manage_rebar_rounding"), Description("Set rebar length-rounding rules. Without rebarId edits the document default; with rebarId edits that bar. Fields: applyRules (bool), lengthRoundingMm (double), lengthRoundingMethod (Nearest|Up|Down). volumeRounding is unsupported by the API and reported in warnings.")]
    public static async Task<string> ManageRebarRounding(
        RevitConnectionManager revit,
        [Description("Rebar element id (optional; document default if omitted)")] long? rebarId = null,
        [Description("Apply rounding rules (bool)")] bool? applyRules = null,
        [Description("Segment (cut) length rounding in mm")] double? lengthRoundingMm = null,
        [Description("Length rounding method: Nearest|Up|Down")] string? lengthRoundingMethod = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (rebarId != null) p["rebarId"] = rebarId;
        if (applyRules != null) p["applyRules"] = applyRules;
        if (lengthRoundingMm != null) p["lengthRoundingMm"] = lengthRoundingMm;
        if (lengthRoundingMethod != null) p["lengthRoundingMethod"] = lengthRoundingMethod;
        return (await revit.ExecuteAsync("manage_rebar_rounding", p, ct)).ToString();
    }

    [McpServerTool(Name = "manage_fabric_rounding"), Description("Set the document fabric length-rounding rules. Fields: applyRules (bool), lengthRoundingMm (double), lengthRoundingMethod (Nearest|Up|Down). volumeRounding is unsupported by the API and reported in warnings.")]
    public static async Task<string> ManageFabricRounding(
        RevitConnectionManager revit,
        [Description("Apply rounding rules (bool)")] bool? applyRules = null,
        [Description("Segment (cut) length rounding in mm")] double? lengthRoundingMm = null,
        [Description("Length rounding method: Nearest|Up|Down")] string? lengthRoundingMethod = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (applyRules != null) p["applyRules"] = applyRules;
        if (lengthRoundingMm != null) p["lengthRoundingMm"] = lengthRoundingMm;
        if (lengthRoundingMethod != null) p["lengthRoundingMethod"] = lengthRoundingMethod;
        return (await revit.ExecuteAsync("manage_fabric_rounding", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_rebar_numbering"), Description("Read rebar numbering. With rebarId returns that bar's schedule mark; without it returns every rebar's schedule mark plus the count of blank marks (a proxy for numbering gaps). The document-wide list is capped by maxResults (truncated/returnedCount flag a cut) and summaryOnly returns only the counts.")]
    public static async Task<string> GetRebarNumbering(
        RevitConnectionManager revit,
        [Description("Rebar element id (optional; category-wide if omitted)")] long? rebarId = null,
        [Description("Max rebars to return in the document-wide list. Default 100")] int? maxResults = null,
        [Description("Return only count + blankMarkCount, no per-rebar list. Default false")] bool? summaryOnly = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (rebarId != null) p["rebarId"] = rebarId;
        if (maxResults != null) p["maxResults"] = maxResults;
        if (summaryOnly != null) p["summaryOnly"] = summaryOnly;
        return (await revit.ExecuteAsync("get_rebar_numbering", p, ct)).ToString();
    }

    [McpServerTool(Name = "manage_rebar_numbering"), Description("Manage rebar numbering. action=set_number writes a single bar's schedule mark (needs rebarId + newNumber). action=renumber|remove_gaps are not exposed by the Revit API and return a structured 'unsupported' result. A read-only target surfaces the issue in warnings rather than failing.")]
    public static async Task<string> ManageRebarNumbering(
        RevitConnectionManager revit,
        [Description("Action: set_number | renumber | remove_gaps")] string action,
        [Description("Rebar element id (required for set_number)")] long? rebarId = null,
        [Description("New schedule mark/number (required for set_number)")] string? newNumber = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (rebarId != null) p["rebarId"] = rebarId;
        if (newNumber != null) p["newNumber"] = newNumber;
        return (await revit.ExecuteAsync("manage_rebar_numbering", p, ct)).ToString();
    }

    [McpServerTool(Name = "create_rebar_bending_detail"), Description("Create a rebar bending detail for a rebar in a view (Revit 2024+). Provide rebarId and viewId (a drafting/detail view); optional bendingDetailTypeId, position JSON {x,y,z} in mm, rotationDegrees. Returns a version error on Revit 2023.")]
    public static async Task<string> CreateRebarBendingDetail(
        RevitConnectionManager revit,
        [Description("Rebar element id")] long rebarId,
        [Description("View id (drafting/detail view) to host the bending detail")] long viewId,
        [Description("Bending detail type id (optional; first existing type used if omitted)")] long? bendingDetailTypeId = null,
        [Description("Position JSON {x,y,z} in mm (optional, default origin)")] string? position = null,
        [Description("Rotation in degrees (optional, default 0)")] double? rotationDegrees = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["rebarId"] = rebarId, ["viewId"] = viewId };
        if (bendingDetailTypeId != null) p["bendingDetailTypeId"] = bendingDetailTypeId;
        if (position != null) p["position"] = JObject.Parse(position);
        if (rotationDegrees != null) p["rotationDegrees"] = rotationDegrees;
        return (await revit.ExecuteAsync("create_rebar_bending_detail", p, ct)).ToString();
    }

    [McpServerTool(Name = "modify_rebar_bending_detail"), Description("Modify a rebar bending detail (Revit 2024+). Provide bendingDetailId and any of position JSON {x,y,z} in mm, rotationDegrees. Returns a version error on Revit 2023.")]
    public static async Task<string> ModifyRebarBendingDetail(
        RevitConnectionManager revit,
        [Description("Bending detail element id")] long bendingDetailId,
        [Description("Position JSON {x,y,z} in mm (optional)")] string? position = null,
        [Description("Rotation in degrees (optional)")] double? rotationDegrees = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["bendingDetailId"] = bendingDetailId };
        if (position != null) p["position"] = JObject.Parse(position);
        if (rotationDegrees != null) p["rotationDegrees"] = rotationDegrees;
        return (await revit.ExecuteAsync("modify_rebar_bending_detail", p, ct)).ToString();
    }

    [McpServerTool(Name = "get_rebar_bending_detail_data"), Description("Read a rebar bending detail (Revit 2024+): host rebar id, owner view id, position (mm) and rotation (degrees). Provide bendingDetailId. Returns a version error on Revit 2023.")]
    public static async Task<string> GetRebarBendingDetailData(
        RevitConnectionManager revit,
        [Description("Bending detail element id")] long bendingDetailId,
        CancellationToken ct = default)
        => (await revit.ExecuteAsync("get_rebar_bending_detail_data", new JObject { ["bendingDetailId"] = bendingDetailId }, ct)).ToString();
}
