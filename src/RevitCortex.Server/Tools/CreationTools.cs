using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class CreationTools
{
    [McpServerTool(Name = "create_surface_based_element"), Description("Create surface-based elements such as floors or ceilings from boundary definitions and type specifications.")]
    public static async Task<string> CreateSurfaceBasedElement(
        RevitConnectionManager revit,
        [Description("JSON array of creation specs (boundary points, type, level, etc.)")] string data,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["data"] = JArray.Parse(data),
        };
        var result = await revit.ExecuteAsync("create_surface_based_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_line_based_element"), Description("Create line-based elements such as walls from start/end points and type specifications.")]
    public static async Task<string> CreateLineBasedElement(
        RevitConnectionManager revit,
        [Description("JSON array of creation specs (start/end points, type, level, height, etc.)")] string data,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["data"] = JArray.Parse(data),
        };
        var result = await revit.ExecuteAsync("create_line_based_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_point_based_element"), Description("Create point-based elements such as columns or furniture at specified locations.")]
    public static async Task<string> CreatePointBasedElement(
        RevitConnectionManager revit,
        [Description("JSON array of creation specs (location, type, level, etc.)")] string data,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["data"] = JArray.Parse(data),
        };
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

    [McpServerTool(Name = "create_dimensions"), Description("Create dimension annotations in the active view from a specification of references and positions.")]
    public static async Task<string> CreateDimensions(
        RevitConnectionManager revit,
        [Description("JSON object with dimension specifications (references, line position, type, etc.)")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
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

    [McpServerTool(Name = "create_array"), Description("Create linear or radial arrays of elements")]
    public static async Task<string> CreateArray(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("create_array", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_color_legend"), Description("Color elements by parameter value with legend view")]
    public static async Task<string> CreateColorLegend(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("create_color_legend", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_filled_region"), Description("Create a filled region from boundary points")]
    public static async Task<string> CreateFilledRegion(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("create_filled_region", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_structural_framing_system"), Description("Create a beam system on a level with spacing")]
    public static async Task<string> CreateStructuralFramingSystem(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("create_structural_framing_system", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_text_note"), Description("Create text notes in a view")]
    public static async Task<string> CreateTextNote(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("create_text_note", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_revision"), Description("List, create or assign revisions to sheets")]
    public static async Task<string> CreateRevision(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("create_revision", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "import_from_excel"), Description("Import data from Excel into Revit element parameters")]
    public static async Task<string> ImportFromExcel(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("import_from_excel", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "export_elements_data"), Description("Export element data by category as JSON or CSV")]
    public static async Task<string> ExportElementsData(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("export_elements_data", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "export_families"), Description("Export loaded families as .rfa files")]
    public static async Task<string> ExportFamilies(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("export_families", JObject.Parse(data), ct);
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
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("batch_export", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "import_table"), Description("Import CSV/TSV file as a formatted table in a drafting or legend view.")]
    public static async Task<string> ImportTable(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("import_table", JObject.Parse(data), ct);
        return result.ToString();
    }
}
