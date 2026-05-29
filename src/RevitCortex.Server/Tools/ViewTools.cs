using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class ViewTools
{
    [McpServerTool(Name = "create_view"), Description("Create a new view in Revit: floor plan, ceiling plan, section, elevation, drafting, or 3D view.")]
    public static async Task<string> CreateView(
        RevitConnectionManager revit,
        [Description("Type of view to create: FloorPlan, CeilingPlan, Section, Elevation, Drafting, ThreeD")] string viewType,
        [Description("Level name (e.g. 'L1 - Block 43') — preferred for floor/ceiling plans")] string? levelName = null,
        [Description("Level element ID (alternative to levelName)")] long? levelId = null,
        [Description("Name for the new view")] string? name = null,
        [Description("View scale denominator, e.g. 100 for 1:100. Default: 100")] int? scale = null,
        [Description("Detail level: Coarse, Medium, Fine. Default: Coarse")] string? detailLevel = null,
        [Description("Origin X in mm (for Section/Elevation). Default: 0")] double? originX = null,
        [Description("Origin Y in mm (for Section/Elevation). Default: 0")] double? originY = null,
        [Description("Origin Z in mm (for Section/Elevation). Default: 0")] double? originZ = null,
        [Description("Facing direction for Section/Elevation: north | south | east | west. Default: north")] string? direction = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["viewType"] = viewType };
        if (levelName != null) p["levelName"] = levelName;
        if (levelId != null) p["levelId"] = levelId;
        if (name != null) p["name"] = name;
        if (scale != null) p["scale"] = scale;
        if (detailLevel != null) p["detailLevel"] = detailLevel;
        if (originX != null) p["originX"] = originX;
        if (originY != null) p["originY"] = originY;
        if (originZ != null) p["originZ"] = originZ;
        if (direction != null) p["direction"] = direction;
        var result = await revit.ExecuteAsync("create_view", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "duplicate_view"), Description("Duplicate an existing view in Revit.")]
    public static async Task<string> DuplicateView(
        RevitConnectionManager revit,
        [Description("Element ID of the view to duplicate")] long viewId,
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
        [Description("Model category filters (e.g. OST_Walls, OST_Doors)")] string[]? modelCategoryList = null,
        [Description("Annotation category filters (e.g. OST_Dimensions, OST_TextNotes)")] string[]? annotationCategoryList = null,
        [Description("Legacy single-category filter; mapped into modelCategoryList for backward compatibility")] string? categoryFilter = null,
        [Description("Specific fields to include in the response")] string[]? fields = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (limit != null) p["limit"] = limit;
        if (modelCategoryList != null) p["modelCategoryList"] = new JArray(modelCategoryList);
        if (annotationCategoryList != null) p["annotationCategoryList"] = new JArray(annotationCategoryList);
        if (categoryFilter != null && modelCategoryList == null) p["modelCategoryList"] = new JArray(categoryFilter);
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

    [McpServerTool(Name = "override_graphics"), Description("Override element graphics in a view (colors, transparency, halftone, line weight).")]
    public static async Task<string> OverrideGraphics(
        RevitConnectionManager revit,
        [Description("Element IDs to override")] long[] elementIds,
        [Description("Action: set | reset. Default: set")] string? action = null,
        [Description("View ID (optional; uses active view when 0)")] long? viewId = null,
        [Description("Color red channel 0-255")] int? colorR = null,
        [Description("Color green channel 0-255")] int? colorG = null,
        [Description("Color blue channel 0-255")] int? colorB = null,
        [Description("Transparency 0-100")] int? transparency = null,
        [Description("Apply halftone")] bool? isHalftone = null,
        [Description("Projection line weight 1-16")] int? projectionLineWeight = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()) };
        if (action != null) p["action"] = action;
        if (viewId != null) p["viewId"] = viewId;
        if (colorR != null) p["colorR"] = colorR;
        if (colorG != null) p["colorG"] = colorG;
        if (colorB != null) p["colorB"] = colorB;
        if (transparency != null) p["transparency"] = transparency;
        if (isHalftone != null) p["isHalftone"] = isHalftone;
        if (projectionLineWeight != null) p["projectionLineWeight"] = projectionLineWeight;
        var result = await revit.ExecuteAsync("override_graphics", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_sheet"), Description("Create a new sheet in the Revit project.")]
    public static async Task<string> CreateSheet(
        RevitConnectionManager revit,
        [Description("Sheet number (e.g. A101)")] string sheetNumber,
        [Description("Sheet name")] string sheetName,
        [Description("Title block type element ID")] long? titleBlockId = null,
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
        [Description("Sheet element ID")] long sheetId,
        [Description("View element ID to place")] long viewId,
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
        [Description("Schedule view element ID")] long scheduleId,
        [Description("Maximum number of body rows to return. Default: 500")] int? maxRows = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["scheduleId"] = scheduleId };
        if (maxRows != null) p["maxRows"] = maxRows;
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

    [McpServerTool(Name = "align_viewports"), Description("Align viewports across sheets. 'placement' matches box centers; 'model' matches the box outline min-corner so equal-scale views of the same region line up.")]
    public static async Task<string> AlignViewports(
        RevitConnectionManager revit,
        [Description("Reference viewport element ID")] long sourceViewportId,
        [Description("Viewport IDs to align to the reference")] long[] targetViewportIds,
        [Description("Alignment mode: placement | model. Default: placement")] string? alignMode = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["sourceViewportId"] = sourceViewportId,
            ["targetViewportIds"] = new JArray(targetViewportIds.Cast<object>().ToArray()),
        };
        if (alignMode != null) p["alignMode"] = alignMode;
        var result = await revit.ExecuteAsync("align_viewports", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "apply_view_template"), Description("List, apply, or remove view templates from views. action=list|apply|remove.")]
    public static async Task<string> ApplyViewTemplate(
        RevitConnectionManager revit,
        [Description("Action: list | apply | remove. Default: apply")] string? action = null,
        [Description("View IDs to apply/remove template on")] long[]? viewIds = null,
        [Description("Template element ID (for apply)")] long? templateId = null,
        [Description("Template name (alternative to templateId)")] string? templateName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (viewIds != null) p["viewIds"] = new JArray(viewIds.Cast<object>().ToArray());
        if (templateId != null) p["templateId"] = templateId;
        if (templateName != null) p["templateName"] = templateName;
        var result = await revit.ExecuteAsync("apply_view_template", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "batch_modify_view_range"), Description("Modify view range offsets (top, cut plane, bottom, view depth) for multiple views. Offsets are in mm.")]
    public static async Task<string> BatchModifyViewRange(
        RevitConnectionManager revit,
        [Description("View IDs to modify")] long[] viewIds,
        [Description("Top offset in mm")] double? topOffset = null,
        [Description("Cut plane offset in mm")] double? cutPlaneOffset = null,
        [Description("Bottom offset in mm")] double? bottomOffset = null,
        [Description("View depth offset in mm")] double? viewDepthOffset = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["viewIds"] = new JArray(viewIds.Cast<object>().ToArray()) };
        if (topOffset != null) p["topOffset"] = topOffset;
        if (cutPlaneOffset != null) p["cutPlaneOffset"] = cutPlaneOffset;
        if (bottomOffset != null) p["bottomOffset"] = bottomOffset;
        if (viewDepthOffset != null) p["viewDepthOffset"] = viewDepthOffset;
        var result = await revit.ExecuteAsync("batch_modify_view_range", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_views_from_rooms"), Description("Create callout, section, or elevation views from rooms with a naming pattern.")]
    public static async Task<string> CreateViewsFromRooms(
        RevitConnectionManager revit,
        [Description("Room element IDs")] long[] roomIds,
        [Description("View type: callout | section | elevation. Default: callout")] string? viewType = null,
        [Description("Boundary offset in mm. Default: 500")] double? offset = null,
        [Description("View scale denominator (e.g. 50 for 1:50). Default: 50")] int? scale = null,
        [Description("Naming pattern with {RoomNumber} and {RoomName} placeholders. Default: '{RoomNumber} - {RoomName}'")] string? namingPattern = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["roomIds"] = new JArray(roomIds.Cast<object>().ToArray()) };
        if (viewType != null) p["viewType"] = viewType;
        if (offset != null) p["offset"] = offset;
        if (scale != null) p["scale"] = scale;
        if (namingPattern != null) p["namingPattern"] = namingPattern;
        var result = await revit.ExecuteAsync("create_views_from_rooms", p, ct);
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

    [McpServerTool(Name = "manage_view_templates"), Description("List, duplicate, delete, or rename view templates. action=list|duplicate|delete|rename.")]
    public static async Task<string> ManageViewTemplates(
        RevitConnectionManager revit,
        [Description("Action: list | duplicate | delete | rename. Default: list")] string? action = null,
        [Description("Filter templates by view type (for list)")] string? filterViewType = null,
        [Description("Template IDs (for duplicate/delete)")] long[]? templateIds = null,
        [Description("Template ID (for rename)")] long? templateId = null,
        [Description("New name (for rename or duplicate)")] string? newName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (filterViewType != null) p["filterViewType"] = filterViewType;
        if (templateIds != null) p["templateIds"] = new JArray(templateIds.Cast<object>().ToArray());
        if (templateId != null) p["templateId"] = templateId;
        if (newName != null) p["newName"] = newName;
        var result = await revit.ExecuteAsync("manage_view_templates", p, ct);
        return result.ToString();
    }

    // ── Sheet tools ─────────────────────────────────────────────────────

    [McpServerTool(Name = "batch_create_sheets"), Description("Create multiple sheets with title blocks and optional view placement. sheets is a JSON array: [{number, name, titleBlockName?, viewIds?}].")]
    public static async Task<string> BatchCreateSheets(
        RevitConnectionManager revit,
        [Description("JSON array of sheet specs: [{number, name, titleBlockName?, viewIds?}]")] string sheets,
        [Description("Default title block family-type name used when a sheet spec omits titleBlockName")] string? defaultTitleBlockName = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["sheets"] = JArray.Parse(sheets) };
        if (defaultTitleBlockName != null) p["defaultTitleBlockName"] = defaultTitleBlockName;
        var result = await revit.ExecuteAsync("batch_create_sheets", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_placeholder_sheets"), Description("Create, list, convert, or delete placeholder sheets. action=create|list|convert|delete.")]
    public static async Task<string> CreatePlaceholderSheets(
        RevitConnectionManager revit,
        [Description("Action: create | list | convert | delete. Default: create")] string? action = null,
        [Description("JSON array of sheet specs for create: [{number, name}]")] string? sheets = null,
        [Description("Sheet IDs (for convert/delete)")] long[]? sheetIds = null,
        [Description("Title block type element ID (for convert)")] long? titleBlockId = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (sheets != null) p["sheets"] = JArray.Parse(sheets);
        if (sheetIds != null) p["sheetIds"] = new JArray(sheetIds.Cast<object>().ToArray());
        if (titleBlockId != null) p["titleBlockId"] = titleBlockId;
        var result = await revit.ExecuteAsync("create_placeholder_sheets", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "duplicate_sheet_with_content"), Description("Duplicate a sheet including annotations and detail items")]
    public static async Task<string> DuplicateSheetWithContent(
        RevitConnectionManager revit,
        [Description("Element ID of the sheet to duplicate")] long sheetId,
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

    [McpServerTool(Name = "duplicate_sheet_with_views"), Description("Duplicate a sheet N times with configurable view duplication options.")]
    public static async Task<string> DuplicateSheetWithViews(
        RevitConnectionManager revit,
        [Description("Sheet element ID to duplicate")] long sheetId,
        [Description("Number of copies. Default: 1")] int? copies = null,
        [Description("Duplicate placed views as well. Default: true")] bool? duplicateViews = null,
        [Description("Keep legends on the new sheets. Default: true")] bool? keepLegends = null,
        [Description("Keep schedules on the new sheets. Default: true")] bool? keepSchedules = null,
        [Description("Prefix applied to new sheet numbers")] string? newSheetNumberPrefix = null,
        [Description("View duplicate option: Duplicate | DuplicateWithDetailing | DuplicateAsDependent. Default: DuplicateWithDetailing")] string? viewDuplicateOption = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["sheetId"] = sheetId };
        if (copies != null) p["copies"] = copies;
        if (duplicateViews != null) p["duplicateViews"] = duplicateViews;
        if (keepLegends != null) p["keepLegends"] = keepLegends;
        if (keepSchedules != null) p["keepSchedules"] = keepSchedules;
        if (newSheetNumberPrefix != null) p["newSheetNumberPrefix"] = newSheetNumberPrefix;
        if (viewDuplicateOption != null) p["viewDuplicateOption"] = viewDuplicateOption;
        var result = await revit.ExecuteAsync("duplicate_sheet_with_views", p, ct);
        return result.ToString();
    }

    // ── Schedule tools ──────────────────────────────────────────────────

    [McpServerTool(Name = "delete_schedule"), Description("Delete a schedule by ID or name")]
    public static async Task<string> DeleteSchedule(
        RevitConnectionManager revit,
        [Description("Schedule element ID")] long? scheduleId = null,
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
        [Description("Schedule element ID to duplicate")] long scheduleId,
        [Description("Name for the duplicated schedule")] string newName,
        CancellationToken ct = default)
    {
        var p = new JObject { ["scheduleId"] = scheduleId, ["newName"] = newName };
        var result = await revit.ExecuteAsync("duplicate_schedule", p, ct);
        return result.ToString();
    }
}
