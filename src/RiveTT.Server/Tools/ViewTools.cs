using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

[McpServerToolType]
public static class ViewTools
{
    [McpServerTool(Name = "create_view"), Description("Create a new view in Revit: floor plan, ceiling plan, section, elevation, drafting, callout, or 3D view.")]
    public static async Task<string> CreateView(
        RevitConnectionManager revit,
        [Description("Type of view to create: FloorPlan, CeilingPlan, Section, Elevation, Drafting, Callout, ThreeD")] string viewType,
        [Description("Level name (e.g. 'L1 - Block 43') — preferred for floor/ceiling plans")] string? levelName = null,
        [Description("Level element ID (alternative to levelName)")] long? levelId = null,
        [Description("Name for the new view")] string? name = null,
        [Description("View scale denominator, e.g. 100 for 1:100. Default: 100")] int? scale = null,
        [Description("Detail level: Coarse, Medium, Fine. Default: Coarse")] string? detailLevel = null,
        [Description("Origin X in mm (for Section/Elevation). Default: 0")] double? originX = null,
        [Description("Origin Y in mm (for Section/Elevation). Default: 0")] double? originY = null,
        [Description("Origin Z in mm (for Section/Elevation). Default: 0")] double? originZ = null,
        [Description("Facing direction for Section/Elevation: north | south | east | west. Default: north")] string? direction = null,
        [Description("View template element ID to apply on creation (optional)")] long? templateId = null,
        [Description("View template name to apply on creation (alternative to templateId)")] string? templateName = null,
        [Description("Activate the crop box. Default: unchanged Pass \"true\" or \"false\"; omit to leave unchanged.")] string? cropActive = null,
        [Description("Crop rectangle min corner as JSON {\"x\":mm,\"y\":mm} (in the view plane). Requires cropMax")] string? cropMin = null,
        [Description("Crop rectangle max corner as JSON {\"x\":mm,\"y\":mm}. Requires cropMin")] string? cropMax = null,
        [Description("REQUIRED for viewType=Callout: element ID of the parent view the callout is cut from")] long? parentViewId = null,
        [Description("REQUIRED for viewType=Callout: callout rectangle min corner as JSON {\"x\":mm,\"y\":mm} in the parent view's own coordinates (model XY for a plan). Requires calloutMax")] string? calloutMin = null,
        [Description("REQUIRED for viewType=Callout: callout rectangle max corner as JSON {\"x\":mm,\"y\":mm}. Requires calloutMin")] string? calloutMax = null,
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
        if (templateId != null) p["templateId"] = templateId;
        if (templateName != null) p["templateName"] = templateName;
        if (cropActive != null)
        {
            if (!TriStateFlag.TryParse(cropActive, out var cropActiveFlag))
                return TriStateFlag.InvalidFlagResult("create_view", "cropActive", cropActive);
            p["cropActive"] = cropActiveFlag;
        }
        if (cropMin != null) p["cropMin"] = JObject.Parse(cropMin);
        if (cropMax != null) p["cropMax"] = JObject.Parse(cropMax);
        if (parentViewId != null) p["parentViewId"] = parentViewId;
        if (calloutMin != null) p["calloutMin"] = JObject.Parse(calloutMin);
        if (calloutMax != null) p["calloutMax"] = JObject.Parse(calloutMax);
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
        var p = new JObject { ["viewIds"] = new JArray(viewId) };
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

    [McpServerTool(Name = "get_current_view_elements"), Description("List elements visible in the currently active view. categoryFilter is a single-category shortcut (OST code, English name or localized label); modelCategoryList/annotationCategoryList take several. Pages via pageSize/cursor: nextCursor in the response, passed back as cursor, reaches elements beyond the first page.")]
    public static async Task<string> GetCurrentViewElements(
        RevitConnectionManager revit,
        [Description("Maximum number of elements to return per page. Default: 200")] int? pageSize = null,
        [Description("Legacy alias for pageSize")] int? limit = null,
        [Description("Opaque cursor from a previous call's nextCursor, to fetch the next page")] string? cursor = null,
        [Description("Model category filters (e.g. OST_Walls, OST_Doors). JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? modelCategoryList = null,
        [Description("Annotation category filters (e.g. OST_Dimensions, OST_TextNotes). JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? annotationCategoryList = null,
        [Description("Legacy single-category filter; mapped into modelCategoryList for backward compatibility")] string? categoryFilter = null,
        [Description("Specific fields to include in the response. JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? fields = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (pageSize != null) p["pageSize"] = pageSize;
        else if (limit != null) p["pageSize"] = limit;
        if (cursor != null) p["cursor"] = cursor;
        if (modelCategoryList != null)
        {
            if (!JsonArrayParam.TryParse(modelCategoryList, out var modelCategoryListArray))
                return JsonArrayParam.InvalidArrayResult("get_current_view_elements", "modelCategoryList", modelCategoryList);
            p["modelCategoryList"] = modelCategoryListArray;
        }
        if (annotationCategoryList != null)
        {
            if (!JsonArrayParam.TryParse(annotationCategoryList, out var annotationCategoryListArray))
                return JsonArrayParam.InvalidArrayResult("get_current_view_elements", "annotationCategoryList", annotationCategoryList);
            p["annotationCategoryList"] = annotationCategoryListArray;
        }
        if (categoryFilter != null && modelCategoryList == null) p["modelCategoryList"] = new JArray(categoryFilter);
        if (categoryFilter != null) p["categoryFilter"] = categoryFilter;
        if (fields != null)
        {
            if (!JsonArrayParam.TryParse(fields, out var fieldsArray))
                return JsonArrayParam.InvalidArrayResult("get_current_view_elements", "fields", fields);
            p["fields"] = fieldsArray;
        }
        var result = await revit.ExecuteAsync("get_current_view_elements", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_view_filter"), Description("Create, apply, or list parameter-based view filters. action=create|apply|list. A filter carries one rule (parameterName/filterRule/filterValue) or several via the rules array combined with AND/OR (logic). For apply: filterId+viewId, optional overrideR/G/B.")]
    public static async Task<string> CreateViewFilter(
        RevitConnectionManager revit,
        [Description("Action: create | apply | list. Default: create")] string? action = null,
        [Description("Filter name (for create)")] string? filterName = null,
        [Description("Category names for create (e.g. [\"Walls\", \"Floors\"]). JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? categoryNames = null,
        [Description("Parameter name to filter on (single-rule create)")] string? parameterName = null,
        [Description("Filter rule (single-rule create): equals | not_equals | contains | begins_with | ends_with | greater_than | less_than")] string? filterRule = null,
        [Description("Value to compare against (single-rule create)")] string? filterValue = null,
        [Description("Multi-rule create: JSON array of {parameterName, rule, value}")] System.Text.Json.JsonElement? rules = null,
        [Description("Combine multiple rules with: and | or. Default: and")] string? logic = null,
        [Description("Filter id (for apply)")] long? filterId = null,
        [Description("View id (for apply)")] long? viewId = null,
        [Description("Override color R 0-255 (for apply)")] int? overrideR = null,
        [Description("Override color G 0-255 (for apply)")] int? overrideG = null,
        [Description("Override color B 0-255 (for apply)")] int? overrideB = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (filterName != null) p["filterName"] = filterName;
        if (categoryNames != null)
        {
            if (!JsonArrayParam.TryParse(categoryNames, out var categoryNamesArray))
                return JsonArrayParam.InvalidArrayResult("create_view_filter", "categoryNames", categoryNames);
            p["categoryNames"] = categoryNamesArray;
        }
        if (parameterName != null) p["parameterName"] = parameterName;
        if (filterRule != null) p["filterRule"] = filterRule;
        if (filterValue != null) p["filterValue"] = filterValue;
        if (rules != null)
        {
            if (!JsonArrayParam.TryParse(rules, out var rulesArray))
                return JsonArrayParam.InvalidArrayResult("create_view_filter", "rules", rules);
            p["rules"] = rulesArray;
        }
        if (logic != null) p["logic"] = logic;
        if (filterId != null) p["filterId"] = filterId;
        if (viewId != null) p["viewId"] = viewId;
        if (overrideR != null) p["overrideR"] = overrideR;
        if (overrideG != null) p["overrideG"] = overrideG;
        if (overrideB != null) p["overrideB"] = overrideB;
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
        [Description("Apply halftone Pass \"true\" or \"false\"; omit to leave unchanged.")] string? isHalftone = null,
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
        if (isHalftone != null)
        {
            if (!TriStateFlag.TryParse(isHalftone, out var isHalftoneFlag))
                return TriStateFlag.InvalidFlagResult("override_graphics", "isHalftone", isHalftone);
            p["isHalftone"] = isHalftoneFlag;
        }
        if (projectionLineWeight != null) p["projectionLineWeight"] = projectionLineWeight;
        var result = await revit.ExecuteAsync("override_graphics", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_sheet"), Description("Create a sheet, with a title block. Pass titleBlockId (an OST_TitleBlocks family type id, from list_system_types or list_family_types) or a family/type name. Without any of them Revit creates a bare 210x297 mm sheet with no frame. The response reports the title block actually placed; an unusable titleBlockId is an error, not a silent fallback.")]
    public static async Task<string> CreateSheet(
        RevitConnectionManager revit,
        [Description("Sheet number (e.g. A101)")] string sheetNumber,
        [Description("Sheet name")] string sheetName,
        [Description("Title block family TYPE element ID (category OST_TitleBlocks)")] long? titleBlockId = null,
        [Description("Title block family name, if you do not have the id")] string? titleBlockFamilyName = null,
        [Description("Title block type name (e.g. A1 metric)")] string? titleBlockTypeName = null,
        [Description("Preview the resolved title block without creating the sheet. Default: false")] bool dryRun = false,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["sheetNumber"] = sheetNumber,
            ["sheetName"] = sheetName,
        };
        if (titleBlockId != null) p["titleBlockId"] = titleBlockId;
        if (titleBlockFamilyName != null) p["titleBlockFamilyName"] = titleBlockFamilyName;
        if (titleBlockTypeName != null) p["titleBlockTypeName"] = titleBlockTypeName;
        p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("create_sheet", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "place_viewport"), Description("Place a view on a sheet as a viewport. positionX/positionY are the viewport CENTRE in mm in sheet coordinates; omit both to centre it on the sheet. The response reports sheetSizeMm, viewportOutlineMm and fitsOnSheet — an UNCROPPED view yields a viewport far bigger than the sheet and its content lands outside the frame, so crop the view first (at 1:100 a 16 x 13.5 m crop is 160 x 135 mm on paper).")]
    public static async Task<string> PlaceViewport(
        RevitConnectionManager revit,
        [Description("Sheet element ID")] long sheetId,
        [Description("View element ID to place")] long viewId,
        [Description("X coordinate for viewport center, in mm")] double? positionX = null,
        [Description("Y coordinate for viewport center, in mm")] double? positionY = null,
        [Description("Rotation: none | clockwise | counterclockwise. Default: none")] string? rotation = null,
        [Description("Viewport type (ElementType) id to apply")] long? viewportTypeId = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["sheetId"] = sheetId,
            ["viewId"] = viewId,
        };
        if (positionX != null) p["positionX"] = positionX;
        if (positionY != null) p["positionY"] = positionY;
        if (rotation != null) p["rotation"] = rotation;
        if (viewportTypeId != null) p["viewportTypeId"] = viewportTypeId;
        var result = await revit.ExecuteAsync("place_viewport", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_schedule"), Description("Create a new schedule view in Revit.")]
    public static async Task<string> CreateSchedule(
        RevitConnectionManager revit,
        [Description("Schedule name")] string name,
        [Description("Category to schedule (e.g. Walls, Doors, Rooms)")] string category,
        [Description("Parameter fields to include in the schedule. JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? fields = null,
        [Description("Schedule type: regular | material_takeoff | key_schedule | sheet_list | view_list. Default: regular")] string? scheduleType = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["name"] = name,
            ["categoryName"] = category,
        };
        if (fields != null)
        {
            if (!JsonArrayParam.TryParse(fields, out var fieldsArray))
                return JsonArrayParam.InvalidArrayResult("create_schedule", "fields", fields);
            p["fields"] = fieldsArray;
        }
        if (scheduleType != null) p["scheduleType"] = scheduleType;
        var result = await revit.ExecuteAsync("create_schedule", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_key_schedule"), Description("Creates a key schedule (ViewSchedule.CreateKeySchedule) — a reusable finish/typology key table (room finish keys, dwelling-unit typologies), different from create_schedule/create_preset_schedule which only build element-instance schedules.")]
    public static async Task<string> CreateKeySchedule(
        RevitConnectionManager revit,
        [Description("Category the keys apply to (e.g. Rooms, OST_Rooms)")] string categoryName,
        [Description("Name for the new key schedule")] string? name = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["categoryName"] = categoryName };
        if (name != null) p["name"] = name;
        var result = await revit.ExecuteAsync("create_key_schedule", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_schedule_data"), Description("Export schedule data as JSON from an existing schedule view. availableFields is omitted unless includeAvailableFields=true: it lists every schedulable parameter of the project and used to dwarf a 10-row request.")]
    public static async Task<string> GetScheduleData(
        RevitConnectionManager revit,
        [Description("Schedule view element ID")] long scheduleId,
        [Description("Maximum number of body rows to return. Default: 500")] int? maxRows = null,
        [Description("Also return every schedulable field of the project (hundreds of entries; ignores maxRows). Default: false")] bool includeAvailableFields = false,
        CancellationToken ct = default)
    {
        var p = new JObject { ["scheduleId"] = scheduleId };
        if (maxRows != null) p["maxRows"] = maxRows;
        p["includeAvailableFields"] = includeAvailableFields;
        var result = await revit.ExecuteAsync("get_schedule_data", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_preset_schedule"), Description("Create a schedule from a predefined template. preset = door_by_room | window_by_room | room_finish | material_takeoff | sheet_list | view_list. material_takeoff also requires categoryName (e.g. OST_Walls).")]
    public static async Task<string> CreatePresetSchedule(
        RevitConnectionManager revit,
        // The four names this described before — RoomFinish, DoorHardware, WallQuantities,
        // WindowSchedule — did not exist. Every one was rejected with "Unknown preset", so
        // following the documentation had a 100 % failure rate. These are read from the
        // switch in CreatePresetScheduleTool.
        [Description("Preset: door_by_room | window_by_room | room_finish | material_takeoff | sheet_list | view_list")] string preset,
        [Description("Custom name for the schedule")] string? name = null,
        // Read by the runtime and never published, which made material_takeoff
        // unreachable through MCP however it was called.
        [Description("Category for material_takeoff, e.g. OST_Walls (ignored by the other presets)")] string? categoryName = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["preset"] = preset };
        if (name != null) p["name"] = name;
        if (categoryName != null) p["categoryName"] = categoryName;
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
        [Description("View IDs to apply/remove template on. JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? viewIds = null,
        [Description("Template element ID (for apply)")] long? templateId = null,
        [Description("Template name (alternative to templateId)")] string? templateName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (viewIds != null)
        {
            if (!JsonArrayParam.TryParse(viewIds, out var viewIdsArray))
                return JsonArrayParam.InvalidArrayResult("apply_view_template", "viewIds", viewIds);
            p["viewIds"] = viewIdsArray;
        }
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

    [McpServerTool(Name = "manage_scope_boxes"), Description("Inventory, rename, move, or assign-to-views existing scope boxes (OST_VolumeOfInterest). The Revit API has no method to create one from scratch — draw it by hand once, then manage it here. action=list|rename|move|assign_to_views|create (create returns a structured unsupported result).")]
    public static async Task<string> ManageScopeBoxes(
        RevitConnectionManager revit,
        [Description("Action: list | rename | move | assign_to_views | create. Default: list")] string action = "list",
        [Description("Scope box element ID (rename, move)")] long? elementId = null,
        [Description("New name (rename)")] string? newName = null,
        [Description("Move translation as JSON {x,y,z} in mm (move)")] string? translation = null,
        [Description("Scope box element ID to assign, or 0 to clear (assign_to_views)")] long? scopeBoxId = null,
        [Description("View element IDs to apply the scope box to, as a JSON array of numbers (assign_to_views)")] System.Text.Json.JsonElement? viewIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (elementId != null) p["elementId"] = elementId;
        if (newName != null) p["newName"] = newName;
        if (translation != null) p["translation"] = JObject.Parse(translation);
        if (scopeBoxId != null) p["scopeBoxId"] = scopeBoxId;
        if (viewIds != null)
        {
            if (!JsonArrayParam.TryParse(viewIds, out var viewIdsArray))
                return JsonArrayParam.InvalidArrayResult("manage_scope_boxes", "viewIds", viewIds);
            p["viewIds"] = viewIdsArray;
        }
        var result = await revit.ExecuteAsync("manage_scope_boxes", p, ct);
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
        [Description("Template IDs (for duplicate/delete). JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? templateIds = null,
        [Description("Template ID (for rename)")] long? templateId = null,
        [Description("New name (for rename or duplicate)")] string? newName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (filterViewType != null) p["filterViewType"] = filterViewType;
        if (templateIds != null)
        {
            if (!JsonArrayParam.TryParse(templateIds, out var templateIdsArray))
                return JsonArrayParam.InvalidArrayResult("manage_view_templates", "templateIds", templateIds);
            p["templateIds"] = templateIdsArray;
        }
        if (templateId != null) p["templateId"] = templateId;
        if (newName != null) p["newName"] = newName;
        var result = await revit.ExecuteAsync("manage_view_templates", p, ct);
        return result.ToString();
    }

    // ── Sheet tools ─────────────────────────────────────────────────────

    [McpServerTool(Name = "batch_create_sheets"), Description("Create multiple sheets with title blocks and optional view placement. sheets is a JSON array: [{number, name, titleBlockName?, viewIds?}]. Each sheet's viewIds are centred in the title block's real frame (the sheet origin is NOT the frame corner) and tiled one per cell when there are several. Previews by default — the dry run reports duplicate sheet numbers and views already placed elsewhere; set dryRun=false to create.")]
    public static async Task<string> BatchCreateSheets(
        RevitConnectionManager revit,
        [Description("JSON array of sheet specs: [{number, name, titleBlockName?, viewIds?}]")] string sheets,
        [Description("Default title block family-type name used when a sheet spec omits titleBlockName")] string? defaultTitleBlockName = null,
        [Description("Preview without creating anything. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["sheets"] = JArray.Parse(sheets), ["dryRun"] = dryRun };
        if (defaultTitleBlockName != null) p["defaultTitleBlockName"] = defaultTitleBlockName;
        var result = await revit.ExecuteAsync("batch_create_sheets", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_placeholder_sheets"), Description("Create, list, convert, or delete placeholder sheets. action=create|list|convert|delete.")]
    public static async Task<string> CreatePlaceholderSheets(
        RevitConnectionManager revit,
        [Description("Action: create | list | convert | delete. Default: create")] string? action = null,
        [Description("JSON array of sheet specs for create: [{number, name}]")] System.Text.Json.JsonElement? sheets = null,
        [Description("Sheet IDs (for convert/delete). JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? sheetIds = null,
        [Description("Title block type element ID (for convert)")] long? titleBlockId = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (sheets != null)
        {
            if (!JsonArrayParam.TryParse(sheets, out var sheetsArray))
                return JsonArrayParam.InvalidArrayResult("create_placeholder_sheets", "sheets", sheets);
            p["sheets"] = sheetsArray;
        }
        if (sheetIds != null)
        {
            if (!JsonArrayParam.TryParse(sheetIds, out var sheetIdsArray))
                return JsonArrayParam.InvalidArrayResult("create_placeholder_sheets", "sheetIds", sheetIds);
            p["sheetIds"] = sheetIdsArray;
        }
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
        [Description("Number of copies. Default: 1")] int? copies = null,
        [Description("Duplicate placed views as well. Default: true")] bool duplicateViews = true,
        [Description("Keep legends on the new sheets. Default: true")] bool keepLegends = true,
        [Description("Keep schedules on the new sheets. Default: true")] bool keepSchedules = true,
        [Description("Copy source sheet revisions. Default: false")] bool copyRevisions = false,
        [Description("Prefix applied to generated sheet numbers")] string? sheetNumberPrefix = null,
        [Description("Suffix applied to generated sheet numbers")] string? sheetNumberSuffix = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["sheetId"] = sheetId };
        if (newNumber != null) p["newNumber"] = newNumber;
        if (newName != null) p["newName"] = newName;
        if (copies != null) p["copies"] = copies;
        p["duplicateViews"] = duplicateViews;
        p["keepLegends"] = keepLegends;
        p["keepSchedules"] = keepSchedules;
        p["copyRevisions"] = copyRevisions;
        if (sheetNumberPrefix != null) p["sheetNumberPrefix"] = sheetNumberPrefix;
        if (sheetNumberSuffix != null) p["sheetNumberSuffix"] = sheetNumberSuffix;
        var result = await revit.ExecuteAsync("duplicate_sheet_with_content", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "duplicate_sheet_with_views"), Description("Duplicate a sheet N times with configurable view duplication options.")]
    public static async Task<string> DuplicateSheetWithViews(
        RevitConnectionManager revit,
        [Description("Sheet element ID to duplicate")] long sheetId,
        [Description("Number of copies. Default: 1")] int? copies = null,
        [Description("Duplicate placed views as well. Default: true")] bool duplicateViews = true,
        [Description("Keep legends on the new sheets. Default: true")] bool keepLegends = true,
        [Description("Keep schedules on the new sheets. Default: true")] bool keepSchedules = true,
        [Description("Prefix applied to new sheet numbers")] string? newSheetNumberPrefix = null,
        [Description("View duplicate option: Duplicate | DuplicateWithDetailing | DuplicateAsDependent. Default: DuplicateWithDetailing")] string? viewDuplicateOption = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["sheetId"] = sheetId };
        if (copies != null) p["copies"] = copies;
        p["duplicateViews"] = duplicateViews;
        p["keepLegends"] = keepLegends;
        p["keepSchedules"] = keepSchedules;
        if (newSheetNumberPrefix != null) p["newSheetNumberPrefix"] = newSheetNumberPrefix;
        if (viewDuplicateOption != null) p["viewDuplicateOption"] = viewDuplicateOption;
        var result = await revit.ExecuteAsync("duplicate_sheet_with_views", p, ct);
        return result.ToString();
    }

    // ── Schedule tools ──────────────────────────────────────────────────

    [McpServerTool(Name = "delete_schedule"), Description("Delete a schedule by ID or name. Previews by default: the dry run names the schedule and reports the cascade, including the viewports that placed it on sheets. Set dryRun=false to execute.")]
    public static async Task<string> DeleteSchedule(
        RevitConnectionManager revit,
        [Description("Schedule element ID")] long? scheduleId = null,
        [Description("Schedule name")] string? scheduleName = null,
        [Description("Preview without deleting. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dryRun"] = dryRun };
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
