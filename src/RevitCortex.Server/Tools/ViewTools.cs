using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class ViewTools
{
    [McpServerTool(Name = "create_view"), Description("Create a new view in Revit: floor plan, section, 3D view, or elevation.")]
    public static async Task<string> CreateView(
        RevitConnectionManager revit,
        [Description("Type of view to create: FloorPlan, Section, ThreeD, Elevation")] string viewType,
        [Description("Level element ID (required for floor plans)")] int? levelId = null,
        [Description("Name for the new view")] string? name = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["viewType"] = viewType };
        if (levelId != null) p["levelId"] = levelId;
        if (name != null) p["name"] = name;
        var result = await revit.ExecuteAsync("create_view", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "duplicate_view"), Description("Duplicate an existing view in Revit.")]
    public static async Task<string> DuplicateView(
        RevitConnectionManager revit,
        [Description("Element ID of the view to duplicate")] int viewId,
        [Description("Duplicate option: Duplicate, AsDependent, WithDetailing")] string? duplicateOption = "Duplicate",
        CancellationToken ct = default)
    {
        var p = new JObject { ["viewId"] = viewId };
        if (duplicateOption != null) p["duplicateOption"] = duplicateOption;
        var result = await revit.ExecuteAsync("duplicate_view", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_current_view_info"), Description("Get information about the currently active view in Revit.")]
    public static async Task<string> GetCurrentViewInfo(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_current_view_info", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_current_view_elements"), Description("List elements visible in the currently active view.")]
    public static async Task<string> GetCurrentViewElements(
        RevitConnectionManager revit,
        [Description("Maximum number of elements to return")] int? limit = 50,
        [Description("Filter by category name (e.g. Walls, Doors)")] string? categoryFilter = null,
        [Description("Specific fields to include in the response")] string[]? fields = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (limit != null) p["limit"] = limit;
        if (categoryFilter != null) p["categoryFilter"] = categoryFilter;
        if (fields != null) p["fields"] = new JArray(fields);
        var result = await revit.ExecuteAsync("get_current_view_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_view_filter"), Description("Create a parameter-based view filter and apply it to categories.")]
    public static async Task<string> CreateViewFilter(
        RevitConnectionManager revit,
        [Description("Filter name")] string name,
        [Description("Categories to apply the filter to (e.g. [\"Walls\", \"Floors\"])")] string[] categories,
        [Description("Parameter name to filter on")] string parameterName,
        [Description("Filter rule: equals, notEquals, greater, less, contains, startsWith, endsWith")] string rule,
        [Description("Value to compare against")] string? value = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["name"] = name,
            ["categories"] = new JArray(categories),
            ["parameterName"] = parameterName,
            ["rule"] = rule,
        };
        if (value != null) p["value"] = value;
        var result = await revit.ExecuteAsync("create_view_filter", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "override_graphics"), Description("Override element graphics in a view (colors, line weights, patterns, transparency). Pass a JSON string with override parameters.")]
    public static async Task<string> OverrideGraphics(
        RevitConnectionManager revit,
        [Description("JSON string with override parameters (e.g. {\"viewId\":123, \"elementIds\":[1,2], \"color\":\"#FF0000\", \"transparency\":50})")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
        var result = await revit.ExecuteAsync("override_graphics", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_sheet"), Description("Create a new sheet in the Revit project.")]
    public static async Task<string> CreateSheet(
        RevitConnectionManager revit,
        [Description("Sheet number (e.g. A101)")] string sheetNumber,
        [Description("Sheet name")] string sheetName,
        [Description("Title block type element ID")] int? titleBlockId = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["sheetNumber"] = sheetNumber,
            ["sheetName"] = sheetName,
        };
        if (titleBlockId != null) p["titleBlockId"] = titleBlockId;
        var result = await revit.ExecuteAsync("create_sheet", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "place_viewport"), Description("Place a view on a sheet as a viewport.")]
    public static async Task<string> PlaceViewport(
        RevitConnectionManager revit,
        [Description("Sheet element ID")] int sheetId,
        [Description("View element ID to place")] int viewId,
        [Description("X coordinate for viewport center")] double? x = null,
        [Description("Y coordinate for viewport center")] double? y = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["sheetId"] = sheetId,
            ["viewId"] = viewId,
        };
        if (x != null) p["x"] = x;
        if (y != null) p["y"] = y;
        var result = await revit.ExecuteAsync("place_viewport", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_schedule"), Description("Create a new schedule view in Revit.")]
    public static async Task<string> CreateSchedule(
        RevitConnectionManager revit,
        [Description("Schedule name")] string name,
        [Description("Category to schedule (e.g. Walls, Doors, Rooms)")] string category,
        [Description("Parameter fields to include in the schedule")] string[]? fields = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["name"] = name,
            ["category"] = category,
        };
        if (fields != null) p["fields"] = new JArray(fields);
        var result = await revit.ExecuteAsync("create_schedule", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_schedule_data"), Description("Export schedule data as JSON from an existing schedule view.")]
    public static async Task<string> GetScheduleData(
        RevitConnectionManager revit,
        [Description("Schedule view element ID")] int scheduleId,
        CancellationToken ct = default)
    {
        var p = new JObject { ["scheduleId"] = scheduleId };
        var result = await revit.ExecuteAsync("get_schedule_data", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_preset_schedule"), Description("Create a schedule from a predefined template (e.g. RoomFinish, DoorHardware, WallQuantities, WindowSchedule).")]
    public static async Task<string> CreatePresetSchedule(
        RevitConnectionManager revit,
        [Description("Preset template name: RoomFinish, DoorHardware, WallQuantities, WindowSchedule")] string preset,
        [Description("Custom name for the schedule")] string? name = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["preset"] = preset };
        if (name != null) p["name"] = name;
        var result = await revit.ExecuteAsync("create_preset_schedule", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "rename_views"), Description("Batch rename views using find/replace, prefix, or suffix operations.")]
    public static async Task<string> RenameViews(
        RevitConnectionManager revit,
        [Description("Rename operation: addPrefix, addSuffix, findReplace")] string operation,
        [Description("Prefix to add (for addPrefix operation)")] string? prefix = null,
        [Description("Suffix to add (for addSuffix operation)")] string? suffix = null,
        [Description("Text to find (for findReplace operation)")] string? findText = null,
        [Description("Replacement text (for findReplace operation)")] string? replaceText = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["operation"] = operation };
        if (prefix != null) p["prefix"] = prefix;
        if (suffix != null) p["suffix"] = suffix;
        if (findText != null) p["findText"] = findText;
        if (replaceText != null) p["replaceText"] = replaceText;
        var result = await revit.ExecuteAsync("rename_views", p, ct);
        return result.ToString();
    }

    // ── Viewport & View Template tools ──────────────────────────────────

    [McpServerTool(Name = "align_viewports"), Description("Align viewports across sheets by position")]
    public static async Task<string> AlignViewports(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("align_viewports", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "apply_view_template"), Description("List, apply, or remove view templates from views")]
    public static async Task<string> ApplyViewTemplate(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("apply_view_template", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "batch_modify_view_range"), Description("Modify view range offsets for multiple views")]
    public static async Task<string> BatchModifyViewRange(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("batch_modify_view_range", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_views_from_rooms"), Description("Create callout, section, or elevation views from rooms")]
    public static async Task<string> CreateViewsFromRooms(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("create_views_from_rooms", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "manage_unplaced_views"), Description("List or delete views that are not placed on any sheet")]
    public static async Task<string> ManageUnplacedViews(
        RevitConnectionManager revit,
        [Description("Action to perform: list or delete")] string action = "list",
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        var result = await revit.ExecuteAsync("manage_unplaced_views", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "manage_view_templates"), Description("List, duplicate, delete, or rename view templates")]
    public static async Task<string> ManageViewTemplates(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("manage_view_templates", JObject.Parse(data), ct);
        return result.ToString();
    }

    // ── Sheet tools ─────────────────────────────────────────────────────

    [McpServerTool(Name = "batch_create_sheets"), Description("Create multiple sheets with title blocks and optional view placement")]
    public static async Task<string> BatchCreateSheets(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("batch_create_sheets", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_placeholder_sheets"), Description("Create, list, convert, or delete placeholder sheets")]
    public static async Task<string> CreatePlaceholderSheets(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("create_placeholder_sheets", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "duplicate_sheet_with_content"), Description("Duplicate a sheet including annotations and detail items")]
    public static async Task<string> DuplicateSheetWithContent(
        RevitConnectionManager revit,
        [Description("Element ID of the sheet to duplicate")] int sheetId,
        [Description("New sheet number")] string? newNumber = null,
        [Description("New sheet name")] string? newName = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["sheetId"] = sheetId };
        if (newNumber != null) p["newNumber"] = newNumber;
        if (newName != null) p["newName"] = newName;
        var result = await revit.ExecuteAsync("duplicate_sheet_with_content", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "duplicate_sheet_with_views"), Description("Duplicate a sheet with configurable view duplication options")]
    public static async Task<string> DuplicateSheetWithViews(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("duplicate_sheet_with_views", JObject.Parse(data), ct);
        return result.ToString();
    }

    // ── Schedule tools ──────────────────────────────────────────────────

    [McpServerTool(Name = "delete_schedule"), Description("Delete a schedule by ID or name")]
    public static async Task<string> DeleteSchedule(
        RevitConnectionManager revit,
        [Description("Schedule element ID")] int? scheduleId = null,
        [Description("Schedule name")] string? scheduleName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (scheduleId != null) p["scheduleId"] = scheduleId;
        if (scheduleName != null) p["scheduleName"] = scheduleName;
        var result = await revit.ExecuteAsync("delete_schedule", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "duplicate_schedule"), Description("Duplicate a schedule with a new name")]
    public static async Task<string> DuplicateSchedule(
        RevitConnectionManager revit,
        [Description("Schedule element ID to duplicate")] int scheduleId,
        [Description("Name for the duplicated schedule")] string newName,
        CancellationToken ct = default)
    {
        var p = new JObject { ["scheduleId"] = scheduleId, ["newName"] = newName };
        var result = await revit.ExecuteAsync("duplicate_schedule", p, ct);
        return result.ToString();
    }
}
