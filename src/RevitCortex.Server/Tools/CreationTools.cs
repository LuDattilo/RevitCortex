using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class CreationTools
{
    [McpServerTool(Name = "create_surface_based_element"), Description("Create surface-based elements (floors, ceilings). Pass a JSON array of creation specs: [{category, boundaryPoints:[{x,y,z}], typeName, levelId|levelName, ...}].")]
    public static async Task<string> CreateSurfaceBasedElement(
        RevitConnectionManager revit,
        [Description("JSON array of creation specs: [{category, boundaryPoints, typeName, levelId|levelName, ...}]")] string specs,
        CancellationToken ct = default)
    {
        var p = new JObject { ["data"] = JArray.Parse(specs) };
        var result = await revit.ExecuteAsync("create_surface_based_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_line_based_element"), Description("Create line-based elements (walls). Pass a JSON array of creation specs: [{category, startPoint:{x,y,z}, endPoint:{x,y,z}, typeName, levelId|levelName, heightMm?, ...}].")]
    public static async Task<string> CreateLineBasedElement(
        RevitConnectionManager revit,
        [Description("JSON array of creation specs: [{category, startPoint, endPoint, typeName, levelId|levelName, heightMm?, ...}]")] string specs,
        CancellationToken ct = default)
    {
        var p = new JObject { ["data"] = JArray.Parse(specs) };
        var result = await revit.ExecuteAsync("create_line_based_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_point_based_element"), Description("Create point-based elements (columns, furniture). Pass a JSON array of creation specs: [{category, location:{x,y,z}, typeName, levelId|levelName, rotation?, ...}].")]
    public static async Task<string> CreatePointBasedElement(
        RevitConnectionManager revit,
        [Description("JSON array of creation specs: [{category, location, typeName, levelId|levelName, rotation?, ...}]")] string specs,
        CancellationToken ct = default)
    {
        var p = new JObject { ["data"] = JArray.Parse(specs) };
        var result = await revit.ExecuteAsync("create_point_based_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_floor"), Description("Create a floor element from boundary points, type name, and level.")]
    public static async Task<string> CreateFloor(
        RevitConnectionManager revit,
        [Description("Level element ID")] int levelId,
        [Description("JSON array of boundary points [{x, y, z}]")] string boundaryPoints,
        [Description("Floor type name")] string? typeName = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["levelId"] = levelId,
            ["boundaryPoints"] = JArray.Parse(boundaryPoints),
        };
        if (typeName != null) p["typeName"] = typeName;
        var result = await revit.ExecuteAsync("create_floor", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "change_element_type"), Description("Change the type of one or more elements to a target type specified by ID or name.")]
    public static async Task<string> ChangeElementType(
        RevitConnectionManager revit,
        [Description("Element IDs to change")] int[] elementIds,
        [Description("Target type element ID")] int? targetTypeId = null,
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

    [McpServerTool(Name = "create_grid"), Description("Create a grid line between two points with an optional label.")]
    public static async Task<string> CreateGrid(
        RevitConnectionManager revit,
        [Description("Start X coordinate")] double startX,
        [Description("Start Y coordinate")] double startY,
        [Description("End X coordinate")] double endX,
        [Description("End Y coordinate")] double endY,
        [Description("Grid label")] string? label = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["startX"] = startX,
            ["startY"] = startY,
            ["endX"] = endX,
            ["endY"] = endY,
        };
        if (label != null) p["label"] = label;
        var result = await revit.ExecuteAsync("create_grid", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_dimensions"), Description("Create dimension annotations in the active view. Pass a JSON array of dimension specs: [{viewId, referenceIds:[...], linePoint:{x,y,z}, dimensionTypeName?}].")]
    public static async Task<string> CreateDimensions(
        RevitConnectionManager revit,
        [Description("JSON array of dimension specs: [{viewId, referenceIds, linePoint, dimensionTypeName?}]")] string dimensions,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dimensions"] = JArray.Parse(dimensions) };
        var result = await revit.ExecuteAsync("create_dimensions", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "color_elements"), Description("Apply a color override to elements of a given category in a view.")]
    public static async Task<string> ColorElements(
        RevitConnectionManager revit,
        [Description("Category name (e.g. Walls, Doors)")] string category,
        [Description("Color as hex string (e.g. #FF0000)")] string color,
        [Description("View element ID. Uses active view if not specified")] int? viewId = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["category"] = category,
            ["color"] = color,
        };
        if (viewId != null) p["viewId"] = viewId;
        var result = await revit.ExecuteAsync("color_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "export_to_excel"), Description("Export element data from a Revit category to an Excel file.")]
    public static async Task<string> ExportToExcel(
        RevitConnectionManager revit,
        [Description("Category to export (e.g. Walls, Rooms)")] string? category = null,
        [Description("Output file path for the Excel file")] string? outputPath = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (category != null) p["category"] = category;
        if (outputPath != null) p["outputPath"] = outputPath;
        var result = await revit.ExecuteAsync("export_to_excel", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "export_room_data"), Description("Export room data including area, perimeter, level, and bounding elements.")]
    public static async Task<string> ExportRoomData(
        RevitConnectionManager revit,
        [Description("Maximum number of rooms to return. Default: 20")] int? maxResults = 20,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (maxResults != null) p["maxResults"] = maxResults;
        var result = await revit.ExecuteAsync("export_room_data", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_array"), Description("Create linear or radial arrays of elements. arrayType=linear uses spacingX/Y/Z; arrayType=radial uses centerX/Y and totalAngle.")]
    public static async Task<string> CreateArray(
        RevitConnectionManager revit,
        [Description("Element IDs to array")] long[] elementIds,
        [Description("Array type: linear | radial. Default: linear")] string? arrayType = null,
        [Description("Number of copies. Default: 1")] int? count = null,
        [Description("Linear spacing X in project units")] double? spacingX = null,
        [Description("Linear spacing Y in project units")] double? spacingY = null,
        [Description("Linear spacing Z in project units")] double? spacingZ = null,
        [Description("Radial center X")] double? centerX = null,
        [Description("Radial center Y")] double? centerY = null,
        [Description("Total sweep angle in degrees (radial). Default: 360")] double? totalAngle = null,
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
        var result = await revit.ExecuteAsync("create_array", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_color_legend"), Description("Color elements by parameter value and optionally create a legend view.")]
    public static async Task<string> CreateColorLegend(
        RevitConnectionManager revit,
        [Description("Parameter name to color by")] string parameterName,
        [Description("Categories to include (e.g. Rooms, Walls)")] string[]? categories = null,
        [Description("Color scheme: auto | rainbow | sequential | custom. Default: auto")] string? colorScheme = null,
        [Description("Custom colors as JSON array of hex strings (when colorScheme=custom)")] string? customColors = null,
        [Description("Create a legend view for the scheme. Default: true")] bool? createLegendView = null,
        [Description("Legend title. Default: 'Color Legend'")] string? legendTitle = null,
        [Description("Target view ID (optional; uses active view when omitted)")] long? targetViewId = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["parameterName"] = parameterName };
        if (categories != null) p["categories"] = new JArray(categories);
        if (colorScheme != null) p["colorScheme"] = colorScheme;
        if (customColors != null) p["customColors"] = JArray.Parse(customColors);
        if (createLegendView != null) p["createLegendView"] = createLegendView;
        if (legendTitle != null) p["legendTitle"] = legendTitle;
        if (targetViewId != null) p["targetViewId"] = targetViewId;
        var result = await revit.ExecuteAsync("create_color_legend", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_filled_region"), Description("Create a filled region in a view from a closed boundary.")]
    public static async Task<string> CreateFilledRegion(
        RevitConnectionManager revit,
        [Description("Boundary points as JSON array [{x,y,z}, ...] (closed loop)")] string boundaryPoints,
        [Description("View ID to host the region (optional; uses active view when -1)")] long? viewId = null,
        [Description("Filled region type name")] string? filledRegionTypeName = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["boundaryPoints"] = JArray.Parse(boundaryPoints) };
        if (viewId != null) p["viewId"] = viewId;
        if (filledRegionTypeName != null) p["filledRegionTypeName"] = filledRegionTypeName;
        var result = await revit.ExecuteAsync("create_filled_region", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_structural_framing_system"), Description("Create a beam system on a level covering a rectangular area with uniform spacing.")]
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
        var result = await revit.ExecuteAsync("create_structural_framing_system", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_text_note"), Description("Create text notes in a view. Pass a JSON array: [{viewId, text, x, y, textTypeName?, textSize?}].")]
    public static async Task<string> CreateTextNote(
        RevitConnectionManager revit,
        [Description("JSON array of text note specs: [{viewId, text, x, y, ...}]")] string textNotes,
        CancellationToken ct = default)
    {
        var p = new JObject { ["textNotes"] = JArray.Parse(textNotes) };
        var result = await revit.ExecuteAsync("create_text_note", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_revision"), Description("List, create, or assign revisions to sheets. action=list|create|assign.")]
    public static async Task<string> CreateRevision(
        RevitConnectionManager revit,
        [Description("Action: list | create | assign. Default: list")] string? action = null,
        [Description("Revision date (for create)")] string? date = null,
        [Description("Revision description (for create)")] string? description = null,
        [Description("Issued by (for create)")] string? issuedBy = null,
        [Description("Issued to (for create)")] string? issuedTo = null,
        [Description("Sheet element IDs (for assign)")] long[]? sheetIds = null,
        [Description("Revision element ID (for assign)")] long? revisionId = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (date != null) p["date"] = date;
        if (description != null) p["description"] = description;
        if (issuedBy != null) p["issuedBy"] = issuedBy;
        if (issuedTo != null) p["issuedTo"] = issuedTo;
        if (sheetIds != null) p["sheetIds"] = new JArray(sheetIds.Cast<object>().ToArray());
        if (revisionId != null) p["revisionId"] = revisionId;
        var result = await revit.ExecuteAsync("create_revision", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "import_from_excel"), Description("Import parameter values from an Excel file into Revit elements.")]
    public static async Task<string> ImportFromExcel(
        RevitConnectionManager revit,
        [Description("Path to the .xlsx file")] string filePath,
        [Description("Sheet name (optional; defaults to first sheet)")] string? sheetName = null,
        [Description("Preview changes without writing. Default: true")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["filePath"] = filePath };
        if (sheetName != null) p["sheetName"] = sheetName;
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("import_from_excel", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "export_elements_data"), Description("Export element data by category as JSON or CSV. Supports parameter filtering and an optional parameter-based include filter.")]
    public static async Task<string> ExportElementsData(
        RevitConnectionManager revit,
        [Description("Categories to include (e.g. Walls, Doors)")] string[]? categories = null,
        [Description("Parameter names to extract (all writable when omitted)")] string[]? parameterNames = null,
        [Description("Include type-level parameters. Default: false")] bool? includeTypeParameters = null,
        [Description("Include element IDs in output. Default: true")] bool? includeElementId = null,
        [Description("Output format: json | csv. Default: json")] string? outputFormat = null,
        [Description("Max elements. Default: 100")] int? maxElements = null,
        [Description("Include only elements where this parameter matches filterValue")] string? filterParameterName = null,
        [Description("Value to match for filterParameterName")] string? filterValue = null,
        [Description("Filter operator: equals | contains | startsWith | endsWith | is_empty | is_not_empty. Default: equals")] string? filterOperator = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (categories != null) p["categories"] = new JArray(categories);
        if (parameterNames != null) p["parameterNames"] = new JArray(parameterNames);
        if (includeTypeParameters != null) p["includeTypeParameters"] = includeTypeParameters;
        if (includeElementId != null) p["includeElementId"] = includeElementId;
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
        [Description("Categories to restrict the export")] string[]? categories = null,
        [Description("Create one subfolder per category. Default: true")] bool? groupByCategory = null,
        [Description("Overwrite existing files. Default: false")] bool? overwrite = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["outputDirectory"] = outputDirectory };
        if (categories != null) p["categories"] = new JArray(categories);
        if (groupByCategory != null) p["groupByCategory"] = groupByCategory;
        if (overwrite != null) p["overwrite"] = overwrite;
        var result = await revit.ExecuteAsync("export_families", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "export_schedule"), Description("Export schedule to CSV/TSV or JSON")]
    public static async Task<string> ExportSchedule(
        RevitConnectionManager revit,
        [Description("Schedule element ID")] int scheduleId,
        [Description("Export format (csv, tsv, json). Default: csv")] string? format = "csv",
        CancellationToken ct = default)
    {
        var p = new JObject { ["scheduleId"] = scheduleId };
        if (format != null) p["format"] = format;
        var result = await revit.ExecuteAsync("export_schedule", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "batch_export"), Description("Export views/sheets to DWG, DXF, DGN, or image formats.")]
    public static async Task<string> BatchExport(
        RevitConnectionManager revit,
        [Description("Output directory")] string outputDirectory,
        [Description("Export format: DWG | DXF | DGN | IMAGE. Default: DWG")] string? format = null,
        [Description("Sheet IDs to export")] long[]? sheetIds = null,
        [Description("View IDs to export")] long[]? viewIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["outputDirectory"] = outputDirectory };
        if (format != null) p["format"] = format;
        if (sheetIds != null) p["sheetIds"] = new JArray(sheetIds.Cast<object>().ToArray());
        if (viewIds != null) p["viewIds"] = new JArray(viewIds.Cast<object>().ToArray());
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
        [Description("Treat first row as header. Default: true")] bool? includeHeaders = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["filePath"] = filePath };
        if (delimiter != null) p["delimiter"] = delimiter;
        if (viewType != null) p["viewType"] = viewType;
        if (viewName != null) p["viewName"] = viewName;
        if (textSize != null) p["textSize"] = textSize;
        if (includeHeaders != null) p["includeHeaders"] = includeHeaders;
        var result = await revit.ExecuteAsync("import_table", p, ct);
        return result.ToString();
    }
}
