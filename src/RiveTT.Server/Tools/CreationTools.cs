using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

[McpServerToolType]
public static class CreationTools
{
    [McpServerTool(Name = "create_surface_based_element"), Description("Create surface-based elements: floors, ceilings, or roofs (OST_Floors, OST_Ceilings, OST_Roofs — a roof is a real FootPrintRoof, Document.Create.NewFootPrintRoof). Pass [{category, boundary:{outerLoop:[{p0,p1}, ...]}, typeId?, baseLevel?, baseOffset?, roofSlopeDegrees?}]. roofSlopeDegrees (OST_Roofs only) applies the same pitch to every footprint edge, producing a hip roof; omit for a flat roof.")]
    public static async Task<string> CreateSurfaceBasedElement(
        RevitConnectionManager revit,
        [Description("JSON array of creation specs: [{category, boundary:{outerLoop:[{p0:{x,y,z},p1:{x,y,z}}, ...]}, typeId?, baseLevel?, baseOffset?, roofSlopeDegrees?}]. roofSlopeDegrees applies to OST_Roofs only.")] string specs,
        CancellationToken ct = default)
    {
        var p = new JObject { ["data"] = JArray.Parse(specs) };
        var result = await revit.ExecuteAsync("create_surface_based_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "manage_area_plans"), Description("Builds regulatory area surfaces (SHAB/SU/SDP): area schemes, area plan views, area boundary lines, and Area elements. action=list_schemes|duplicate_scheme|create_plan|create_boundary|create_area. AreaScheme creation from scratch is confirmed unsupported by the public Revit API — duplicate_scheme copies an existing one instead (every template ships 'Gross Building').")]
    public static async Task<string> ManageAreaPlans(
        RevitConnectionManager revit,
        [Description("Action: list_schemes | duplicate_scheme | create_plan | create_boundary | create_area. Default: list_schemes")] string action = "list_schemes",
        [Description("AreaScheme element ID to duplicate (duplicate_scheme)")] long? sourceSchemeId = null,
        [Description("New area scheme name (duplicate_scheme)")] string? newName = null,
        [Description("AreaScheme element ID (create_plan)")] long? areaSchemeId = null,
        [Description("Level element ID (create_plan)")] long? levelId = null,
        [Description("Area plan view element ID (create_boundary, create_area)")] long? viewId = null,
        [Description("JSON array of curve specs forming a closed loop (create_boundary): [{type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}}] in mm")] System.Text.Json.JsonElement? curves = null,
        [Description("Point inside a closed area boundary, JSON {x,y} in mm (create_area)")] string? point = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (sourceSchemeId != null) p["sourceSchemeId"] = sourceSchemeId;
        if (newName != null) p["newName"] = newName;
        if (areaSchemeId != null) p["areaSchemeId"] = areaSchemeId;
        if (levelId != null) p["levelId"] = levelId;
        if (viewId != null) p["viewId"] = viewId;
        if (curves != null)
        {
            if (!JsonArrayParam.TryParse(curves, out var curvesArray))
                return JsonArrayParam.InvalidArrayResult("manage_area_plans", "curves", curves);
            p["curves"] = curvesArray;
        }
        if (point != null) p["point"] = JObject.Parse(point);
        var result = await revit.ExecuteAsync("manage_area_plans", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_opening"), Description("Cuts an opening or a vertical shaft. openingType=shaft|host|wall. shaft: baseLevelId+topLevelId+curves (closed loop, mm) — a vertical shaft through every floor/roof between the two levels. host: hostElementId (a floor or roof)+curves (closed loop, mm) — cutIsVoid defaults to true. wall: hostElementId (a wall)+point1+point2 ({x,y,z} mm).")]
    public static async Task<string> CreateOpening(
        RevitConnectionManager revit,
        [Description("shaft | host | wall")] string openingType,
        [Description("Base level element ID (shaft)")] long? baseLevelId = null,
        [Description("Top level element ID (shaft)")] long? topLevelId = null,
        [Description("Host floor/roof/wall element ID (host, wall)")] long? hostElementId = null,
        [Description("JSON array of curve specs forming a closed loop (shaft, host): [{type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}}] in mm")] System.Text.Json.JsonElement? curves = null,
        [Description("Whether the cut is a void vs. solid addition (host). Default: true")] bool cutIsVoid = true,
        [Description("First corner point, JSON {x,y,z} in mm (wall)")] string? point1 = null,
        [Description("Second (opposite) corner point, JSON {x,y,z} in mm (wall)")] string? point2 = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["openingType"] = openingType, ["cutIsVoid"] = cutIsVoid };
        if (baseLevelId != null) p["baseLevelId"] = baseLevelId;
        if (topLevelId != null) p["topLevelId"] = topLevelId;
        if (hostElementId != null) p["hostElementId"] = hostElementId;
        if (curves != null)
        {
            if (!JsonArrayParam.TryParse(curves, out var curvesArray))
                return JsonArrayParam.InvalidArrayResult("create_opening", "curves", curves);
            p["curves"] = curvesArray;
        }
        if (point1 != null) p["point1"] = JObject.Parse(point1);
        if (point2 != null) p["point2"] = JObject.Parse(point2);
        var result = await revit.ExecuteAsync("create_opening", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_line_based_element"), Description("Create line-based elements (walls, beams). Pass a JSON array of specs: [{category, locationLine:{p0:{x,y,z}, p1:{x,y,z}, pMid?:{x,y,z}}, typeId?, height?, baseLevel?, baseOffset?}]. Add pMid to make a curved (arc) wall/beam. Coordinates in mm.")]
    public static async Task<string> CreateLineBasedElement(
        RevitConnectionManager revit,
        [Description("JSON array of specs: [{category, locationLine:{p0, p1, pMid?}, typeId?, height?, baseLevel?, baseOffset?}]")] string specs,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["data"] = JArray.Parse(specs), ["dryRun"] = dryRun };
        var result = await revit.ExecuteAsync("create_line_based_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_point_based_element"), Description("Create point-based elements. Pass [{category, locationPoint:{x,y,z}, typeId?, levelId?, baseLevel?, hostWallId?, facingFlipped?, handFlipped?, rotation?}]. Use create_door or create_window for hosted openings.")]
    public static async Task<string> CreatePointBasedElement(
        RevitConnectionManager revit,
        [Description("JSON array of creation specs: [{category, locationPoint, typeId?, levelId?, baseLevel?, hostWallId?, facingFlipped?, handFlipped?, rotation?}]")] string specs,
        [Description("Preview without changing the model. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["data"] = JArray.Parse(specs), ["dryRun"] = dryRun };
        var result = await revit.ExecuteAsync("create_point_based_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_floor"), Description("Create an architectural floor from a boundary (or a room), optionally with holes. Provide boundaryPoints OR roomId. Previews by default: the dry run reports the floor type and level it resolved to — both come from fallbacks the caller usually does not state — plus the boundary area. Set dryRun=false to create.")]
    public static async Task<string> CreateFloor(
        RevitConnectionManager revit,
        [Description("JSON array of boundary points [{x, y}] in mm (outer loop). Omit if using roomId")] System.Text.Json.JsonElement? boundaryPoints = null,
        [Description("Room element id to take the boundary from (alternative to boundaryPoints)")] long? roomId = null,
        [Description("Floor type name. Defaults to first architectural floor type")] string? floorTypeName = null,
        [Description("Target level elevation in mm (picks the nearest level). Ignored when roomId is given")] double? levelElevation = null,
        [Description("JSON array of holes, each a [{x,y}] inner loop, e.g. [[{x,y},{x,y},{x,y}]]")] System.Text.Json.JsonElement? holes = null,
        [Description("Preview without creating the floor. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dryRun"] = dryRun };
        if (boundaryPoints != null)
        {
            if (!JsonArrayParam.TryParse(boundaryPoints, out var boundaryPointsArray))
                return JsonArrayParam.InvalidArrayResult("create_floor", "boundaryPoints", boundaryPoints);
            p["boundaryPoints"] = boundaryPointsArray;
        }
        if (roomId != null) p["roomId"] = roomId;
        if (floorTypeName != null) p["floorTypeName"] = floorTypeName;
        if (levelElevation != null) p["levelElevation"] = levelElevation;
        if (holes != null)
        {
            if (!JsonArrayParam.TryParse(holes, out var holesArray))
                return JsonArrayParam.InvalidArrayResult("create_floor", "holes", holes);
            p["holes"] = holesArray;
        }
        var result = await revit.ExecuteAsync("create_floor", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "change_element_type"), Description("Change the type of one or more elements to a target type specified by ID or name.")]
    public static async Task<string> ChangeElementType(
        RevitConnectionManager revit,
        [Description("Element IDs to change")] long[] elementIds,
        [Description("Target type element ID")] long? targetTypeId = null,
        [Description("Target type name")] string? targetTypeName = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Select(id => (object)id).ToArray()),
        };
        if (targetTypeId != null) p["targetTypeId"] = targetTypeId;
        if (targetTypeName != null) p["targetTypeName"] = targetTypeName;
        var result = await revit.ExecuteAsync("change_element_type", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_grid"), Description("Create a grid system (X and/or Y grids by count + spacing), or rename/delete an existing grid. action=create|rename|delete. Spacing/extent values are in mm.")]
    public static async Task<string> CreateGrid(
        RevitConnectionManager revit,
        [Description("Action: create | rename | delete. Default: create")] string? action = null,
        [Description("Number of X grids (vertical lines) to create")] int? xCount = null,
        [Description("Number of Y grids (horizontal lines) to create")] int? yCount = null,
        [Description("Spacing between X grids in mm. Default: 5000")] double? xSpacing = null,
        [Description("Spacing between Y grids in mm. Default: 5000")] double? ySpacing = null,
        [Description("First X grid label. Default: A")] string? xStartLabel = null,
        [Description("First Y grid label. Default: 1")] string? yStartLabel = null,
        [Description("Grid element id (for rename/delete)")] long? gridId = null,
        [Description("Grid name (identifies the target for rename/delete when gridId is omitted)")] string? name = null,
        [Description("New name (for rename)")] string? newName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (xCount != null) p["xCount"] = xCount;
        if (yCount != null) p["yCount"] = yCount;
        if (xSpacing != null) p["xSpacing"] = xSpacing;
        if (ySpacing != null) p["ySpacing"] = ySpacing;
        if (xStartLabel != null) p["xStartLabel"] = xStartLabel;
        if (yStartLabel != null) p["yStartLabel"] = yStartLabel;
        if (gridId != null) p["gridId"] = gridId;
        if (name != null) p["name"] = name;
        if (newName != null) p["newName"] = newName;
        var result = await revit.ExecuteAsync("create_grid", p, ct);
        return result.ToString();
    }

    // The element-mode key is elementIds. This advertised referenceIds, which the runtime
    // never reads, so every documented call fell through to "Provide either elementIds
    // (2+) or startPoint/endPoint". Nested keys like this escape
    // ServerRuntimeParameterContractTests, which only sees top-level parameters —
    // NestedKeyContractTests now covers them.
    [McpServerTool(Name = "create_dimensions"), Description("Create dimension annotations in a view. Pass a JSON array of dimension specs. Element mode: [{viewId, elementIds:[...], linePoint:{x,y,z}, dimensionStyleId?}] — elementIds needs at least 2 elements, and the dimension is measured between the faces facing each other. Point-to-point mode: [{viewId, startPoint:{x,y,z}, endPoint:{x,y,z}, linePoint?, dimensionStyleId?}] — both points must lie in the view's plane. dimensionStyleId is honored in both modes.")]
    public static async Task<string> CreateDimensions(
        RevitConnectionManager revit,
        [Description("JSON array of dimension specs. Element mode uses elementIds; point-to-point uses startPoint+endPoint. Both accept dimensionStyleId")] string dimensions,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dimensions"] = JArray.Parse(dimensions) };
        var result = await revit.ExecuteAsync("create_dimensions", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_spot_dimension"), Description("Create a spot elevation annotation (a level/coordinate callout) at a point on an element's geometry. create_dimensions only builds linear dimensions; use this for altimetry.")]
    public static async Task<string> CreateSpotDimension(
        RevitConnectionManager revit,
        [Description("Element ID whose face/edge is tagged")] long elementId,
        [Description("Point to read the elevation at, as JSON {\"x\":mm,\"y\":mm,\"z\":mm}; must lie on or very near the element's geometry")] string point,
        [Description("Owning view element ID. Default: the active view")] long? viewId = null,
        [Description("Elbow point as JSON {x,y,z} in mm. Default: derived from the view's up direction")] string? bend = null,
        [Description("Leader end point as JSON {x,y,z} in mm. Default: derived from the view's right direction")] string? end = null,
        [Description("Show a leader line. Default: true")] bool hasLeader = true,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementId"] = elementId,
            ["point"] = JObject.Parse(point),
            ["hasLeader"] = hasLeader
        };
        if (viewId != null) p["viewId"] = viewId;
        if (bend != null) p["bend"] = JObject.Parse(bend);
        if (end != null) p["end"] = JObject.Parse(end);
        var result = await revit.ExecuteAsync("create_spot_dimension", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "color_elements"), Description("Color a view's elements of a category by grouping them on a parameter value, or reset (clear) those color overrides. action=color|reset. Pass viewId to target a specific model view; without it the active view is used. Not a sheet.")]
    public static async Task<string> ColorElements(
        RevitConnectionManager revit,
        [Description("Category name or OST_* code (e.g. OST_Walls, Doors)")] string categoryName,
        [Description("Parameter to group/color by (required for color), e.g. \"Type Name\", \"Level\"")] string? parameterName = null,
        [Description("Action: color | reset. Default: color")] string? action = null,
        [Description("Use a blue→red gradient across groups. Default: false (random colors)")] bool useGradient = false,
        [Description("Optional explicit colors as JSON array [{r,g,b}, ...], cycled across groups")] System.Text.Json.JsonElement? customColors = null,
        [Description("View to apply the overrides in. Omit to use the currently active view.")] long? viewId = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["categoryName"] = categoryName };
        if (viewId != null) p["viewId"] = viewId;
        if (parameterName != null) p["parameterName"] = parameterName;
        if (action != null) p["action"] = action;
        p["useGradient"] = useGradient;
        if (customColors != null)
        {
            if (!JsonArrayParam.TryParse(customColors, out var customColorsArray))
                return JsonArrayParam.InvalidArrayResult("color_elements", "customColors", customColors);
            p["customColors"] = customColorsArray;
        }
        var result = await revit.ExecuteAsync("color_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "export_to_excel"), Description("Export element data from a Revit category to an Excel file.")]
    public static async Task<string> ExportToExcel(
        RevitConnectionManager revit,
        [Description("Categories to export (OST_* codes or display names). JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? categories = null,
        [Description("Legacy single category alias; used only when categories is omitted")] string? category = null,
        [Description("Specific parameter names to export. JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? parameterNames = null,
        [Description("Include type parameters. Default: false")] bool includeTypeParameters = false,
        [Description("Include element id column. Default: true")] bool includeElementId = true,
        [Description("Output file path for the Excel file")] string? filePath = null,
        [Description("Legacy output path alias; used only when filePath is omitted")] string? outputPath = null,
        [Description("Worksheet name. Default: Export")] string? sheetName = null,
        [Description("Maximum elements to export. Default: 10000")] int? maxElements = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (categories != null)
        {
            if (!JsonArrayParam.TryParse(categories, out var categoriesArray))
                return JsonArrayParam.InvalidArrayResult("export_to_excel", "categories", categories);
            p["categories"] = categoriesArray;
        }
        else if (category != null) p["categories"] = new JArray(category);
        if (parameterNames != null)
        {
            if (!JsonArrayParam.TryParse(parameterNames, out var parameterNamesArray))
                return JsonArrayParam.InvalidArrayResult("export_to_excel", "parameterNames", parameterNames);
            p["parameterNames"] = parameterNamesArray;
        }
        p["includeTypeParameters"] = includeTypeParameters;
        p["includeElementId"] = includeElementId;
        if (filePath != null) p["filePath"] = filePath;
        else if (outputPath != null) p["filePath"] = outputPath;
        if (sheetName != null) p["sheetName"] = sheetName;
        if (maxElements != null) p["maxElements"] = maxElements;
        var result = await revit.ExecuteAsync("export_to_excel", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "export_room_data"), Description("Export room data (area in m2, perimeter, level, department). Filter inside Revit with levelName/levelId and nameFilter instead of returning every room of the model: matchedCount reports how many matched before truncation.")]
    public static async Task<string> ExportRoomData(
        RevitConnectionManager revit,
        [Description("Maximum number of rooms to return. Default: 20")] int? maxResults = 20,
        [Description("Keep only the rooms of this level (accent- and case-insensitive name match)")] string? levelName = null,
        [Description("Keep only the rooms of this level id")] long? levelId = null,
        [Description("Substring filter on room name or number")] string? nameFilter = null,
        [Description("Include unplaced rooms (area 0). Default: false")] bool includeUnplacedRooms = false,
        [Description("Include rooms that are not enclosed. Default: false")] bool includeNotEnclosedRooms = false,
        [Description("Strip department/perimeterMm. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (maxResults != null) p["maxResults"] = maxResults;
        if (levelName != null) p["levelName"] = levelName;
        if (levelId != null) p["levelId"] = levelId;
        if (nameFilter != null) p["nameFilter"] = nameFilter;
        p["includeUnplacedRooms"] = includeUnplacedRooms;
        p["includeNotEnclosedRooms"] = includeNotEnclosedRooms;
        var result = await revit.ExecuteAsync("export_room_data", p, ct);
        return ToolResponseShaper.Shape("export_room_data", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "create_array"), Description("Create a linear or radial array. Default builds a real associative Revit ArrayElement (editable count); set associative=false for loose copies. linear uses spacingX/Y/Z (mm); radial uses centerX/Y (mm) and totalAngle.")]
    public static async Task<string> CreateArray(
        RevitConnectionManager revit,
        [Description("Element IDs to array")] long[] elementIds,
        [Description("Array type: linear | radial. Default: linear")] string? arrayType = null,
        [Description("Number of members (including original). Default: 1")] int? count = null,
        [Description("Linear spacing X in mm")] double? spacingX = null,
        [Description("Linear spacing Y in mm")] double? spacingY = null,
        [Description("Linear spacing Z in mm")] double? spacingZ = null,
        [Description("Radial center X in mm")] double? centerX = null,
        [Description("Radial center Y in mm")] double? centerY = null,
        [Description("Total sweep angle in degrees (radial). Default: 360")] double? totalAngle = null,
        [Description("Build a real associative ArrayElement. Default: true. false = loose copies")] bool associative = true,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()),
        };
        if (arrayType != null) p["arrayType"] = arrayType;
        if (count != null) p["count"] = count;
        if (spacingX != null) p["spacingX"] = spacingX;
        if (spacingY != null) p["spacingY"] = spacingY;
        if (spacingZ != null) p["spacingZ"] = spacingZ;
        if (centerX != null) p["centerX"] = centerX;
        if (centerY != null) p["centerY"] = centerY;
        if (totalAngle != null) p["totalAngle"] = totalAngle;
        p["associative"] = associative;
        var result = await revit.ExecuteAsync("create_array", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_color_legend"), Description("Color elements by parameter value and optionally create a legend view.")]
    public static async Task<string> CreateColorLegend(
        RevitConnectionManager revit,
        [Description("Parameter name to color by")] string parameterName,
        [Description("Categories to include (e.g. Rooms, Walls). JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? categories = null,
        [Description("Color scheme: auto | rainbow | sequential | custom. Default: auto")] string? colorScheme = null,
        [Description("Custom colors as JSON array of hex strings (when colorScheme=custom)")] System.Text.Json.JsonElement? customColors = null,
        [Description("Create a legend view for the scheme. Default: true")] bool createLegendView = true,
        [Description("Legend title. Default: 'Color Legend'")] string? legendTitle = null,
        [Description("Target view ID (optional; uses active view when omitted)")] long? targetViewId = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["parameterName"] = parameterName };
        if (categories != null)
        {
            if (!JsonArrayParam.TryParse(categories, out var categoriesArray))
                return JsonArrayParam.InvalidArrayResult("create_color_legend", "categories", categories);
            p["categories"] = categoriesArray;
        }
        if (colorScheme != null) p["colorScheme"] = colorScheme;
        if (customColors != null)
        {
            if (!JsonArrayParam.TryParse(customColors, out var customColorsArray))
                return JsonArrayParam.InvalidArrayResult("create_color_legend", "customColors", customColors);
            p["customColors"] = customColorsArray;
        }
        p["createLegendView"] = createLegendView;
        if (legendTitle != null) p["legendTitle"] = legendTitle;
        if (targetViewId != null) p["targetViewId"] = targetViewId;
        var result = await revit.ExecuteAsync("create_color_legend", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_filled_region"), Description("Create a filled region in a view from a closed boundary, optionally with holes (inner loops).")]
    public static async Task<string> CreateFilledRegion(
        RevitConnectionManager revit,
        [Description("Boundary points as JSON array [{x,y}, ...] (outer closed loop)")] string boundaryPoints,
        [Description("View ID to host the region (optional; uses active view when omitted)")] long? viewId = null,
        [Description("Filled region type name")] string? filledRegionTypeName = null,
        [Description("JSON array of holes, each a [{x,y}] inner loop, e.g. [[{x,y},{x,y},{x,y}]]")] System.Text.Json.JsonElement? holes = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["boundaryPoints"] = JArray.Parse(boundaryPoints) };
        if (viewId != null) p["viewId"] = viewId;
        if (filledRegionTypeName != null) p["filledRegionTypeName"] = filledRegionTypeName;
        if (holes != null)
        {
            if (!JsonArrayParam.TryParse(holes, out var holesArray))
                return JsonArrayParam.InvalidArrayResult("create_filled_region", "holes", holes);
            p["holes"] = holesArray;
        }
        var result = await revit.ExecuteAsync("create_filled_region", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_structural_framing_system"), Description("Create a beam system on a level over a rectangular area. Default builds a real associative Revit BeamSystem (editable layout); set associative=false for loose independent beams.")]
    public static async Task<string> CreateStructuralFramingSystem(
        RevitConnectionManager revit,
        [Description("Level name")] string levelName,
        [Description("Min X in mm. Default: 0")] double? xMin = null,
        [Description("Max X in mm. Default: 10000")] double? xMax = null,
        [Description("Min Y in mm. Default: 0")] double? yMin = null,
        [Description("Max Y in mm. Default: 10000")] double? yMax = null,
        [Description("Beam spacing in mm. Default: 1000")] double? spacing = null,
        [Description("Beam type name (optional)")] string? beamTypeName = null,
        [Description("Elevation offset in mm relative to level. Default: 0")] double? elevation = null,
        [Description("Build a real associative BeamSystem. Default: true. false = loose beams")] bool associative = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["levelName"] = levelName };
        if (xMin != null) p["xMin"] = xMin;
        if (xMax != null) p["xMax"] = xMax;
        if (yMin != null) p["yMin"] = yMin;
        if (yMax != null) p["yMax"] = yMax;
        if (spacing != null) p["spacing"] = spacing;
        if (beamTypeName != null) p["beamTypeName"] = beamTypeName;
        if (elevation != null) p["elevation"] = elevation;
        p["associative"] = associative;
        var result = await revit.ExecuteAsync("create_structural_framing_system", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_text_note"), Description("Create text notes in a view. Pass a JSON array: [{text, position:{x,y,z}, viewId?, textNoteTypeId?, width?, horizontalAlignment?, verticalAlignment?, rotation?, leader?}]. rotation is degrees; leader is left|right|leftArc|rightArc.")]
    public static async Task<string> CreateTextNote(
        RevitConnectionManager revit,
        [Description("JSON array of text note specs: [{text, position:{x,y,z}, viewId?, width?, horizontalAlignment?, verticalAlignment?, rotation?, leader?}]")] string textNotes,
        CancellationToken ct = default)
    {
        var p = new JObject { ["textNotes"] = JArray.Parse(textNotes) };
        var result = await revit.ExecuteAsync("create_text_note", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_revision"), Description("List, create, update, or assign revisions to sheets, and draw revision clouds. action=list|create|set|add_to_sheets|create_cloud. 'set' updates an existing revision (needs revisionId). 'create_cloud' draws the cloud that localizes a revision on a view (needs revisionId, viewId, curves) — Revit refuses this once the revision is marked Issued.")]
    public static async Task<string> CreateRevision(
        RevitConnectionManager revit,
        [Description("Action: list | create | set | add_to_sheets | create_cloud. Default: list")] string? action = null,
        [Description("Revision date (for create/set)")] string? date = null,
        [Description("Revision description (for create/set)")] string? description = null,
        [Description("Issued by (for create/set)")] string? issuedBy = null,
        [Description("Issued to (for create/set)")] string? issuedTo = null,
        [Description("Mark the revision issued (true) or not (false), for create/set Pass \"true\" or \"false\"; omit to leave unchanged.")] string? issued = null,
        [Description("Revision visibility: cloud_and_tag | tag_visible | none, for create/set")] string? visibility = null,
        [Description("Sheet element IDs (for add_to_sheets). JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? sheetIds = null,
        [Description("Revision element ID (required for set, add_to_sheets, and create_cloud)")] long? revisionId = null,
        [Description("View element ID the cloud is drawn in (required for create_cloud)")] long? viewId = null,
        [Description("JSON array of curve specs forming a closed loop (required for create_cloud): [{type:line|arc, start{x,y,z}, end{x,y,z}, mid?{x,y,z}}] in mm")] System.Text.Json.JsonElement? curves = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (date != null) p["date"] = date;
        if (description != null) p["description"] = description;
        if (issuedBy != null) p["issuedBy"] = issuedBy;
        if (issuedTo != null) p["issuedTo"] = issuedTo;
        if (issued != null)
        {
            if (!TriStateFlag.TryParse(issued, out var issuedFlag))
                return TriStateFlag.InvalidFlagResult("create_revision", "issued", issued);
            p["issued"] = issuedFlag;
        }
        if (visibility != null) p["visibility"] = visibility;
        if (sheetIds != null)
        {
            if (!JsonArrayParam.TryParse(sheetIds, out var sheetIdsArray))
                return JsonArrayParam.InvalidArrayResult("create_revision", "sheetIds", sheetIds);
            p["sheetIds"] = sheetIdsArray;
        }
        if (revisionId != null) p["revisionId"] = revisionId;
        if (viewId != null) p["viewId"] = viewId;
        if (curves != null)
        {
            if (!JsonArrayParam.TryParse(curves, out var curvesArray))
                return JsonArrayParam.InvalidArrayResult("create_revision", "curves", curves);
            p["curves"] = curvesArray;
        }
        var result = await revit.ExecuteAsync("create_revision", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "import_from_excel"), Description("Import parameter values from an Excel file into Revit elements.")]
    public static async Task<string> ImportFromExcel(
        RevitConnectionManager revit,
        [Description("Path to the .xlsx file")] string filePath,
        [Description("Sheet name (optional; defaults to first sheet)")] string? sheetName = null,
        [Description("Preview changes without writing. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["filePath"] = filePath };
        if (sheetName != null) p["sheetName"] = sheetName;
        p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("import_from_excel", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "export_elements_data"), Description("Export element data as JSON or CSV, by category and/or by explicit elementIds. Parameter names may be given in English or in the document language (Mark/Repere, Level/Niveau, Width/Largeur); names that resolve to nothing are listed in unresolvedParameterNames instead of producing a silently empty column. Filters on a Level-type parameter match the level NAME. Use countOnly=true first to size a large export.")]
    public static async Task<string> ExportElementsData(
        RevitConnectionManager revit,
        [Description("Categories to include (e.g. Walls, Doors). JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? categories = null,
        [Description("Parameter names to extract (all writable when omitted). JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? parameterNames = null,
        [Description("Include type-level parameters. Default: false")] bool includeTypeParameters = false,
        [Description("Include element IDs in output. Default: true")] bool includeElementId = true,
        [Description("Output format: json | csv. Default: json")] string? outputFormat = null,
        [Description("Max elements. Default: 100")] int? maxElements = null,
        [Description("Include only elements where this parameter matches filterValue")] string? filterParameterName = null,
        [Description("Value to match for filterParameterName")] string? filterValue = null,
        [Description("Filter operator: equals | not_equals | contains | startsWith | endsWith | is_empty | is_not_empty | greater_than | less_than. Default: equals")] string? filterOperator = null,
        [Description("Restrict the export to these element IDs. Applied before pagination. JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? elementIds = null,
        [Description("Return counts and estimated column count only, no rows. Use it to size an export first. Default: false")] bool countOnly = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (elementIds != null)
        {
            if (!JsonArrayParam.TryParse(elementIds, out var elementIdsArray))
                return JsonArrayParam.InvalidArrayResult("export_elements_data", "elementIds", elementIds);
            p["elementIds"] = elementIdsArray;
        }
        p["countOnly"] = countOnly;
        if (categories != null)
        {
            if (!JsonArrayParam.TryParse(categories, out var categoriesArray))
                return JsonArrayParam.InvalidArrayResult("export_elements_data", "categories", categories);
            p["categories"] = categoriesArray;
        }
        if (parameterNames != null)
        {
            if (!JsonArrayParam.TryParse(parameterNames, out var parameterNamesArray))
                return JsonArrayParam.InvalidArrayResult("export_elements_data", "parameterNames", parameterNames);
            p["parameterNames"] = parameterNamesArray;
        }
        p["includeTypeParameters"] = includeTypeParameters;
        p["includeElementId"] = includeElementId;
        if (outputFormat != null) p["outputFormat"] = outputFormat;
        if (maxElements != null) p["maxElements"] = maxElements;
        if (filterParameterName != null) p["filterParameterName"] = filterParameterName;
        if (filterValue != null) p["filterValue"] = filterValue;
        if (filterOperator != null) p["filterOperator"] = filterOperator;
        var result = await revit.ExecuteAsync("export_elements_data", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "export_families"), Description("Export loaded families as .rfa files into a target directory.")]
    public static async Task<string> ExportFamilies(
        RevitConnectionManager revit,
        [Description("Output directory for the .rfa files")] string outputDirectory,
        [Description("Categories to restrict the export. JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? categories = null,
        [Description("Create one subfolder per category. Default: true")] bool groupByCategory = true,
        [Description("Overwrite existing files. Default: false")] bool overwrite = false,
        CancellationToken ct = default)
    {
        var p = new JObject { ["outputDirectory"] = outputDirectory };
        if (categories != null)
        {
            if (!JsonArrayParam.TryParse(categories, out var categoriesArray))
                return JsonArrayParam.InvalidArrayResult("export_families", "categories", categories);
            p["categories"] = categoriesArray;
        }
        p["groupByCategory"] = groupByCategory;
        p["overwrite"] = overwrite;
        var result = await revit.ExecuteAsync("export_families", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "export_schedule"), Description("Export a schedule as JSON, or write it to a CSV/TSV file. Without exportPath the data comes back inline; with exportPath the file is written using delimiter (or format).")]
    public static async Task<string> ExportSchedule(
        RevitConnectionManager revit,
        [Description("Schedule element ID")] long scheduleId,
        [Description("Export format: csv | tsv | json. Default: json inline, csv when exportPath is set")] string? format = null,
        [Description("Absolute output file path. Omit to get the data inline")] string? exportPath = null,
        [Description("Field separator: Tab | Comma | Semicolon. Overrides format")] string? delimiter = null,
        [Description("Write the header row. Default: true")] bool includeHeaders = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["scheduleId"] = scheduleId };
        if (format != null) p["format"] = format;
        if (exportPath != null) p["exportPath"] = exportPath;
        if (delimiter != null) p["delimiter"] = delimiter;
        p["includeHeaders"] = includeHeaders;
        var result = await revit.ExecuteAsync("export_schedule", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "batch_export"), Description("Export views/sheets to DWG, DXF, DGN, PDF, or image (PNG) formats.")]
    public static async Task<string> BatchExport(
        RevitConnectionManager revit,
        [Description("Output directory")] string outputDirectory,
        [Description("Export format: DWG | DXF | DGN | PDF | IMAGE. Default: DWG")] string? format = null,
        [Description("Sheet IDs to export. JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? sheetIds = null,
        [Description("View IDs to export. JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? viewIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["outputDirectory"] = outputDirectory };
        if (format != null) p["format"] = format;
        if (sheetIds != null)
        {
            if (!JsonArrayParam.TryParse(sheetIds, out var sheetIdsArray))
                return JsonArrayParam.InvalidArrayResult("batch_export", "sheetIds", sheetIds);
            p["sheetIds"] = sheetIdsArray;
        }
        if (viewIds != null)
        {
            if (!JsonArrayParam.TryParse(viewIds, out var viewIdsArray))
                return JsonArrayParam.InvalidArrayResult("batch_export", "viewIds", viewIds);
            p["viewIds"] = viewIdsArray;
        }
        var result = await revit.ExecuteAsync("batch_export", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "import_table"), Description("Import a CSV/TSV file as a formatted table in a drafting or legend view.")]
    public static async Task<string> ImportTable(
        RevitConnectionManager revit,
        [Description("Path to the CSV/TSV file")] string filePath,
        [Description("Field delimiter. Default: ,")] string? delimiter = null,
        [Description("Destination view type: drafting | legend. Default: drafting")] string? viewType = null,
        [Description("View name (optional; default derived from file name)")] string? viewName = null,
        [Description("Text size in mm. Default: 2.0")] double? textSize = null,
        [Description("Treat first row as header. Default: true")] bool includeHeaders = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["filePath"] = filePath };
        if (delimiter != null) p["delimiter"] = delimiter;
        if (viewType != null) p["viewType"] = viewType;
        if (viewName != null) p["viewName"] = viewName;
        if (textSize != null) p["textSize"] = textSize;
        p["includeHeaders"] = includeHeaders;
        var result = await revit.ExecuteAsync("import_table", p, ct);
        return result.ToString();
    }
}
