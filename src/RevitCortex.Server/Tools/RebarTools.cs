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
    [McpServerTool(Name = "create_area_reinforcement"), Description("Create an area reinforcement system on a host (wall/floor/foundation). majorDirection is JSON {x,y,z}; optional curves is a JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm for an explicit boundary.")]
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

    [McpServerTool(Name = "create_path_reinforcement"), Description("Create a path reinforcement system on a host. curves is a JSON array of {type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}} in mm (required). Optional flip, pathTypeId, startHookId, endHookId.")]
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
}
