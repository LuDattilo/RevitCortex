using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class ProjectTools
{
    [McpServerTool(Name = "get_warnings"), Description("Get model warnings from the active Revit document.")]
    public static async Task<string> GetWarnings(
        RevitConnectionManager revit,
        [Description("Maximum number of warnings to return")] int maxWarnings = 50,
        CancellationToken ct = default)
    {
        var p = new JObject { ["maxWarnings"] = maxWarnings };
        var result = await revit.ExecuteAsync("get_warnings", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_phases"), Description("List all project phases in the active Revit document.")]
    public static async Task<string> GetPhases(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_phases", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_worksets"), Description("List all worksets in the active Revit document.")]
    public static async Task<string> GetWorksets(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_worksets", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "load_family"), Description("Load a family into the Revit project.")]
    public static async Task<string> LoadFamily(
        RevitConnectionManager revit,
        [Description("Action to perform (e.g. list, load)")] string action = "list",
        [Description("Path to the family file (.rfa)")] string? familyPath = null,
        [Description("Filter by category")] string? categoryFilter = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (familyPath != null) p["familyPath"] = familyPath;
        if (categoryFilter != null) p["categoryFilter"] = categoryFilter;
        var result = await revit.ExecuteAsync("load_family", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_available_family_types"), Description("List available family types in the Revit project.")]
    public static async Task<string> GetAvailableFamilyTypes(
        RevitConnectionManager revit,
        [Description("Filter by category names (OST codes, English, or localized labels)")] string[]? categoryList = null,
        [Description("Case-insensitive substring filter on family or type name")] string? familyNameFilter = null,
        [Description("Max types to return. Default: 100")] int? limit = null,
        [Description("Return a compact payload without uniqueId-heavy rows. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (categoryList != null) p["categoryList"] = new JArray(categoryList);
        if (familyNameFilter != null) p["familyNameFilter"] = familyNameFilter;
        if (limit != null) p["limit"] = limit;
        var result = await revit.ExecuteAsync("get_available_family_types", p, ct);
        return ToolResponseShaper.Shape("get_available_family_types", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "analyze_model_statistics"), Description("Analyze element counts by category in the active Revit document.")]
    public static async Task<string> AnalyzeModelStatistics(
        RevitConnectionManager revit,
        [Description("Return compact summary")] bool compact = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["compact"] = compact };
        var result = await revit.ExecuteAsync("analyze_model_statistics", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "check_model_health"), Description("Run a model health check and return a health score.")]
    public static async Task<string> CheckModelHealth(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("check_model_health", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "audit_families"), Description("Audit families in the Revit project. Lists loadable (.rfa) families by default; set includeSystemFamilies=true to also list system-family types (wall/floor/roof/ceiling types, including Foundation Slab).")]
    public static async Task<string> AuditFamilies(
        RevitConnectionManager revit,
        [Description("Filter by category (OST_* code, English friendly name, or localized display name)")] string? categoryFilter = null,
        [Description("Include unused families in the audit")] bool includeUnused = false,
        [Description("Also enumerate system-family types (WallType/FloorType/RoofType/CeilingType). Default: false")] bool? includeSystemFamilies = null,
        [Description("Sort order: instance_count | name. Default: instance_count")] string? sortBy = null,
        [Description("Return compact family rows without audit booleans (isInPlace/isEditable/isUnused/kind). Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject { ["includeUnused"] = includeUnused };
        if (categoryFilter != null) p["categoryFilter"] = categoryFilter;
        if (includeSystemFamilies != null) p["includeSystemFamilies"] = includeSystemFamilies;
        if (sortBy != null) p["sortBy"] = sortBy;
        var result = await revit.ExecuteAsync("audit_families", p, ct);
        return ToolResponseShaper.Shape("audit_families", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "manage_phase_filters"), Description("List, set, or create Revit Phase Filters. Actions: list | set | create. The 'set' action changes one presentation (New | Demolished | Existing | Temporary) on one filter and preserves the other three. Presentations: None | ByCategory | Overridden | NotDisplayed.")]
    public static async Task<string> ManagePhaseFilters(
        RevitConnectionManager revit,
        [Description("Action: list | set | create. Default: list")] string? action = null,
        [Description("Filter name (for set/create)")] string? filterName = null,
        [Description("Filter element ID (alternative to filterName for set)")] long? filterId = null,
        [Description("Phase status to modify (for set): New | Demolished | Existing | Temporary")] string? status = null,
        [Description("Presentation value (for set): None | ByCategory | Overridden | NotDisplayed")] string? presentation = null,
        [Description("Name of the new filter (for create)")] string? name = null,
        [Description("New-phase presentation (for create). Default: ByCategory")] string? newStatus = null,
        [Description("Existing-phase presentation (for create). Default: ByCategory")] string? existingStatus = null,
        [Description("Demolished-phase presentation (for create). Default: ByCategory")] string? demolishedStatus = null,
        [Description("Temporary-phase presentation (for create). Default: ByCategory")] string? temporaryStatus = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (filterName != null) p["filterName"] = filterName;
        if (filterId != null) p["filterId"] = filterId;
        if (status != null) p["status"] = status;
        if (presentation != null) p["presentation"] = presentation;
        if (name != null) p["name"] = name;
        if (newStatus != null) p["newStatus"] = newStatus;
        if (existingStatus != null) p["existingStatus"] = existingStatus;
        if (demolishedStatus != null) p["demolishedStatus"] = demolishedStatus;
        if (temporaryStatus != null) p["temporaryStatus"] = temporaryStatus;
        var result = await revit.ExecuteAsync("manage_phase_filters", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "purge_unused"), Description("Purge unused elements from the Revit project.")]
    public static async Task<string> PurgeUnused(
        RevitConnectionManager revit,
        [Description("Preview changes without applying")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dryRun"] = dryRun };
        var result = await revit.ExecuteAsync("purge_unused", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "clash_detection"), Description("Detect clashes between two element categories.")]
    public static async Task<string> ClashDetection(
        RevitConnectionManager revit,
        [Description("First category for clash detection")] string category1,
        [Description("Second category for clash detection")] string category2,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["category1"] = category1,
            ["category2"] = category2,
        };
        var result = await revit.ExecuteAsync("clash_detection", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "workflow_model_audit"), Description("Run a complete model audit workflow.")]
    public static async Task<string> WorkflowModelAudit(
        RevitConnectionManager revit,
        [Description("Include warnings in the response. Default: true")] bool? includeWarnings = null,
        [Description("Include family lists in the response. Default: true")] bool? includeFamilies = null,
        [Description("Maximum grouped warnings returned. Default: 50")] int? maxWarnings = null,
        [Description("Strip warnings/families to summary fields only. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (includeWarnings != null) p["includeWarnings"] = includeWarnings;
        if (includeFamilies != null) p["includeFamilies"] = includeFamilies;
        if (maxWarnings != null) p["maxWarnings"] = maxWarnings;
        var result = await revit.ExecuteAsync("workflow_model_audit", p, ct);
        return ToolResponseShaper.Shape("workflow_model_audit", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "create_level"), Description("Create a new level in the Revit project.")]
    public static async Task<string> CreateLevel(
        RevitConnectionManager revit,
        [Description("Name of the new level")] string name,
        [Description("Elevation of the level")] double elevation,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["name"] = name,
            ["elevation"] = elevation,
        };
        var result = await revit.ExecuteAsync("create_level", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_room"), Description("Create a new room in the Revit project.")]
    public static async Task<string> CreateRoom(
        RevitConnectionManager revit,
        [Description("Level element ID where the room will be placed")] long levelId,
        [Description("X coordinate for room placement")] double x,
        [Description("Y coordinate for room placement")] double y,
        [Description("Room name")] string? name = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["levelId"] = levelId,
            ["x"] = x,
            ["y"] = y,
        };
        if (name != null) p["name"] = name;
        var result = await revit.ExecuteAsync("create_room", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "delete_element"), Description("Delete elements from the Revit project.")]
    public static async Task<string> DeleteElement(
        RevitConnectionManager revit,
        [Description("Array of element IDs to delete")] int[] elementIds,
        [Description("Preview changes without applying")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["elementIds"] = new JArray(elementIds.Select(id => (object)id).ToArray()),
            ["dryRun"] = dryRun,
        };
        var result = await revit.ExecuteAsync("delete_element", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "duplicate_system_type"), Description("Duplicate an existing system type with a new name.")]
    public static async Task<string> DuplicateSystemType(
        RevitConnectionManager revit,
        [Description("Element ID of the source type to duplicate")] long sourceTypeId,
        [Description("Name for the new duplicated type")] string newName,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["sourceTypeId"] = sourceTypeId,
            ["newName"] = newName,
        };
        var result = await revit.ExecuteAsync("duplicate_system_type", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "batch_rename"), Description("Batch rename elements or system types in the Revit project. Supports both loadable-family elements and system types (wall/floor/ceiling/roof types).")]
    public static async Task<string> BatchRename(
        RevitConnectionManager revit,
        [Description("Array of element IDs to rename. Use this when you already have specific IDs.")] int[]? elementIds = null,
        [Description("Target category to rename. Valid values: views | sheets | levels | grids | rooms | walltypes | floortypes | ceilingtypes | rooftypes. Use 'floortypes' to rename system floor types.")] string? targetCategory = null,
        [Description("Text to find")] string? findText = null,
        [Description("Replacement text")] string? replaceText = null,
        [Description("Prefix to add")] string? prefix = null,
        [Description("Suffix to add")] string? suffix = null,
        [Description("Preview changes without applying")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dryRun"] = dryRun };
        if (elementIds != null) p["elementIds"] = new JArray(elementIds.Select(id => (object)id).ToArray());
        if (targetCategory != null) p["targetCategory"] = targetCategory;
        if (findText != null) p["findText"] = findText;
        if (replaceText != null) p["replaceText"] = replaceText;
        if (prefix != null) p["prefix"] = prefix;
        if (suffix != null) p["suffix"] = suffix;
        var result = await revit.ExecuteAsync("batch_rename", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "rename_families"), Description("Rename loaded families (and optionally their types) with find/replace, prefix, or suffix operations.")]
    public static async Task<string> RenameFamilies(
        RevitConnectionManager revit,
        [Description("Operation: prefix | suffix | findReplace. Default: prefix")] string? operation = null,
        [Description("Prefix string (for prefix operation)")] string? prefix = null,
        [Description("Suffix string (for suffix operation)")] string? suffix = null,
        [Description("Text to find (for findReplace operation)")] string? findText = null,
        [Description("Replacement text (for findReplace operation)")] string? replaceText = null,
        [Description("Categories to restrict the rename (e.g. Doors, Windows)")] string[]? categories = null,
        [Description("Also rename the family types. Default: false")] bool? renameTypes = null,
        [Description("Preview without writing. Default: true")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (operation != null) p["operation"] = operation;
        if (prefix != null) p["prefix"] = prefix;
        if (suffix != null) p["suffix"] = suffix;
        if (findText != null) p["findText"] = findText;
        if (replaceText != null) p["replaceText"] = replaceText;
        if (categories != null) p["categories"] = new JArray(categories);
        if (renameTypes != null) p["renameTypes"] = renameTypes;
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("rename_families", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "tag_rooms"), Description("Tag rooms in the active view. Operates on the active view only — activate the correct view first.")]
    public static async Task<string> TagRooms(
        RevitConnectionManager revit,
        [Description("Use leader on tags. Default: false")] bool? useLeader = null,
        [Description("Room IDs to tag (optional; tags all rooms in view when omitted)")] long[]? roomIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (useLeader != null) p["useLeader"] = useLeader;
        if (roomIds != null) p["roomIds"] = new JArray(roomIds.Cast<object>().ToArray());
        var result = await revit.ExecuteAsync("tag_rooms", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "tag_walls"), Description("Tag walls at their midpoints in the active view. Operates on the active view only.")]
    public static async Task<string> TagWalls(
        RevitConnectionManager revit,
        [Description("Use leader on tags. Default: false")] bool? useLeader = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (useLeader != null) p["useLeader"] = useLeader;
        var result = await revit.ExecuteAsync("tag_walls", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "wipe_empty_tags"), Description("Find and remove empty or orphaned tags")]
    public static async Task<string> WipeEmptyTags(
        RevitConnectionManager revit,
        [Description("Preview changes without applying. Default: true")] bool dryRun = true,
        [Description("View element ID to search in")] long? viewId = null,
        [Description("JSON array of category names to filter")] string? categories = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dryRun"] = dryRun };
        if (viewId != null) p["viewId"] = viewId;
        if (categories != null) p["categories"] = categories;
        var result = await revit.ExecuteAsync("wipe_empty_tags", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "set_material_properties"), Description("Set identity and product info on Revit materials. Pass a JSON array of request objects: [{materialId|materialName, properties:{...}}].")]
    public static async Task<string> SetMaterialProperties(
        RevitConnectionManager revit,
        [Description("JSON array of requests: [{materialId|materialName, properties:{key:value, ...}}]")] string requests,
        [Description("Preview changes without applying. Default: true")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["requests"] = JArray.Parse(requests) };
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("set_material_properties", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "modify_schedule"), Description("Modify schedule fields, sorting, or rename the schedule. Supported actions: add_field, remove_field, set_sorting, clear_sorting, rename.")]
    public static async Task<string> ModifySchedule(
        RevitConnectionManager revit,
        [Description("Action: add_field | remove_field | set_sorting | clear_sorting | rename. Default: add_field")] string? action = null,
        [Description("Schedule element ID (alternative to scheduleName)")] long? scheduleId = null,
        [Description("Schedule name (alternative to scheduleId)")] string? scheduleName = null,
        [Description("Field names for add_field/remove_field actions")] string[]? fieldNames = null,
        [Description("Sort field specs as JSON array: [{fieldName, ascending:true}]")] string? sortFields = null,
        [Description("New schedule name (for rename action)")] string? newName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (scheduleId != null) p["scheduleId"] = scheduleId;
        if (scheduleName != null) p["scheduleName"] = scheduleName;
        if (fieldNames != null) p["fieldNames"] = new JArray(fieldNames);
        if (sortFields != null) p["sortFields"] = JArray.Parse(sortFields);
        if (newName != null) p["newName"] = newName;
        var result = await revit.ExecuteAsync("modify_schedule", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "send_code_to_revit"), Description("LAST RESORT ONLY — execute custom C# code in Revit. Always prefer dedicated tools (batch_rename, set_element_parameters, export_to_excel, etc.) over this tool. Use only when no dedicated tool covers the operation. Scripts are saved to ~/.revitcortex/scripts/ and require user confirmation before execution.")]
    public static async Task<string> SendCodeToRevit(
        RevitConnectionManager revit,
        [Description("C# code to execute. Globals available: document (Document), uiDocument (UIDocument), app (Application).")] string code,
        [Description("Transaction mode: auto | manual | readonly. Default: auto")] string? transactionMode = "auto",
        [Description("YOU decide — never ask the user. true = REUSABLE (kept permanently) if the script is generic and could run again on other models or sessions (e.g. a utility, a report, a recurring audit). false = TEMP (deleted at Revit close) if the script is specific to this one request, these specific element IDs, or this exact model. Default: false.")] bool? reusable = false,
        [Description("Short human-readable name for the script file (no spaces, max 40 chars). Example: 'floor-thickness-audit'")] string? scriptName = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["code"] = code };
        if (transactionMode != null) p["transactionMode"] = transactionMode;
        if (reusable != null) p["reusable"] = reusable;
        if (scriptName != null) p["scriptName"] = scriptName;
        var result = await revit.ExecuteAsync("send_code_to_revit", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "workflow_clash_review"), Description("Detect clashes between two categories and create a 3D section-boxed view for visual review.")]
    public static async Task<string> WorkflowClashReview(
        RevitConnectionManager revit,
        [Description("First category (e.g. OST_Walls)")] string categoryA,
        [Description("Second category (e.g. OST_Pipes)")] string categoryB,
        [Description("Intersection tolerance in mm. Default: 0")] double? tolerance = null,
        [Description("Create a section-boxed 3D view around detected clashes. Default: true")] bool? createSectionBox = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["categoryA"] = categoryA,
            ["categoryB"] = categoryB,
        };
        if (tolerance != null) p["tolerance"] = tolerance;
        if (createSectionBox != null) p["createSectionBox"] = createSectionBox;
        var result = await revit.ExecuteAsync("workflow_clash_review", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "workflow_data_roundtrip"), Description("Export parameters to Excel for external editing, then re-import once the file has been saved.")]
    public static async Task<string> WorkflowDataRoundtrip(
        RevitConnectionManager revit,
        [Description("Path to the .xlsx file (created on export, read on import)")] string filePath,
        [Description("Categories to include (e.g. Walls, Doors)")] string[]? categories = null,
        [Description("Parameter names to include")] string[]? parameterNames = null,
        [Description("Include type-level parameters. Default: false")] bool? includeTypeParameters = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["filePath"] = filePath };
        if (categories != null) p["categories"] = new JArray(categories);
        if (parameterNames != null) p["parameterNames"] = new JArray(parameterNames);
        if (includeTypeParameters != null) p["includeTypeParameters"] = includeTypeParameters;
        var result = await revit.ExecuteAsync("workflow_data_roundtrip", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "workflow_room_documentation"), Description("Auto-generate callout views (and optionally sections) for every room on a level.")]
    public static async Task<string> WorkflowRoomDocumentation(
        RevitConnectionManager revit,
        [Description("Level name to document")] string levelName,
        [Description("Also create sections per room. Default: true")] bool? createSections = null,
        [Description("Boundary offset in mm. Default: 300")] double? offset = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["levelName"] = levelName };
        if (createSections != null) p["createSections"] = createSections;
        if (offset != null) p["offset"] = offset;
        var result = await revit.ExecuteAsync("workflow_room_documentation", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "workflow_sheet_set"), Description("Auto-create a set of sheets with title blocks from a definition list: [{number, name, viewIds?}].")]
    public static async Task<string> WorkflowSheetSet(
        RevitConnectionManager revit,
        [Description("JSON array of sheet specs: [{number, name, viewIds?}]")] string sheets,
        [Description("Default title block family-type name")] string? titleBlockName = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["sheets"] = JArray.Parse(sheets) };
        if (titleBlockName != null) p["titleBlockName"] = titleBlockName;
        var result = await revit.ExecuteAsync("workflow_sheet_set", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_shared_parameters"), Description("List all project parameters with their bindings and categories, optionally filtered by category.")]
    public static async Task<string> GetSharedParameters(
        RevitConnectionManager revit,
        [Description("Category filter (e.g. OST_Walls); empty for all")] string? categoryFilter = null,
        [Description("Strip per-parameter description. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (categoryFilter != null) p["categoryFilter"] = categoryFilter;
        var result = await revit.ExecuteAsync("get_shared_parameters", p, ct);
        return ToolResponseShaper.Shape("get_shared_parameters", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "lines_per_view_count"), Description("Count detail and/or model lines per view for performance auditing. Always set threshold >= 20 on large models; the tool has an automatic 300-view cap.")]
    public static async Task<string> LinesPerViewCount(
        RevitConnectionManager revit,
        [Description("Only report views at or above this line count. Default: 0")] int? threshold = null,
        [Description("Count detail lines. Default: true")] bool? includeDetailLines = null,
        [Description("Count model lines. Default: true")] bool? includeModelLines = null,
        [Description("Max views returned. Default: 200")] int? limit = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (threshold != null) p["threshold"] = threshold;
        if (includeDetailLines != null) p["includeDetailLines"] = includeDetailLines;
        if (includeModelLines != null) p["includeModelLines"] = includeModelLines;
        if (limit != null) p["limit"] = limit;
        var result = await revit.ExecuteAsync("lines_per_view_count", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "list_family_sizes"), Description("List loaded families with their type and instance counts.")]
    public static async Task<string> ListFamilySizes(
        RevitConnectionManager revit,
        [Description("Max families returned. Default: 50")] int? limit = null,
        [Description("Sort by: instanceCount | typeCount | name. Default: instanceCount")] string? sortBy = null,
        [Description("Categories to restrict the list")] string[]? categories = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (limit != null) p["limit"] = limit;
        if (sortBy != null) p["sortBy"] = sortBy;
        if (categories != null) p["categories"] = new JArray(categories);
        var result = await revit.ExecuteAsync("list_family_sizes", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "list_schedulable_fields"), Description("Discover available schedulable fields for a category.")]
    public static async Task<string> ListSchedulableFields(
        RevitConnectionManager revit,
        [Description("Category name (e.g. OST_Rooms). Default: OST_Rooms")] string? categoryName = null,
        [Description("Schedule type: regular | key | material-takeoff. Default: regular")] string? scheduleType = null,
        [Description("Return a compact payload. Default: false")] bool compact = false,
        [Description("Return names/counts only. Default: false")] bool summaryOnly = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (categoryName != null) p["categoryName"] = categoryName;
        if (scheduleType != null) p["scheduleType"] = scheduleType;
        var result = await revit.ExecuteAsync("list_schedulable_fields", p, ct);
        return ToolResponseShaper.Shape("list_schedulable_fields", result, compact, summaryOnly).ToString();
    }

    [McpServerTool(Name = "manage_project_units"), Description("Get or set project units (length, area, volume, angle, etc.). Actions: get, set, list_valid_units.")]
    public static async Task<string> ManageProjectUnits(
        RevitConnectionManager revit,
        [Description("Action: get | set | list_valid_units")] string action = "get",
        [Description("Spec type for set/list_valid_units: length, area, volume, angle, slope, number, currency, mass, force, speed, temperature")] string? specType = null,
        [Description("Unit to set (e.g. meters, millimeters, feet, inches, degrees)")] string? unit = null,
        [Description("Optional display accuracy (e.g. 0.01)")] double? accuracy = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (specType != null) p["specType"] = specType;
        if (unit     != null) p["unit"]     = unit;
        if (accuracy != null) p["accuracy"] = accuracy;
        var result = await revit.ExecuteAsync("manage_project_units", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "manage_additional_settings"), Description("Manage Additional Settings (Manage tab): line styles, line weights, line patterns, fill patterns, halftone/underlay.")]
    public static async Task<string> ManageAdditionalSettings(
        RevitConnectionManager revit,
        [Description("Action: list_line_styles | create_line_style | set_line_style | list_line_weights | list_line_patterns | list_fill_patterns | get_halftone | set_halftone")] string action,
        [Description("Name of the line style (for create/set)")] string? name = null,
        [Description("Line weight number 1-16 (for create/set line style)")] int? lineWeight = null,
        [Description("Line pattern name (for create/set line style, e.g. 'Solid', 'Dash')")] string? linePatternName = null,
        [Description("Color red component 0-255")] int? colorR = null,
        [Description("Color green component 0-255")] int? colorG = null,
        [Description("Color blue component 0-255")] int? colorB = null,
        [Description("Halftone brightness percent 0-100 (for set_halftone)")] int? halftonePercent = null,
        [Description("Underlay brightness percent 0-100 (for set_halftone)")] int? underlayBrightness = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (name            != null) p["name"]              = name;
        if (lineWeight      != null) p["lineWeight"]        = lineWeight;
        if (linePatternName != null) p["linePatternName"]   = linePatternName;
        if (colorR          != null) p["colorR"]            = colorR;
        if (colorG          != null) p["colorG"]            = colorG;
        if (colorB          != null) p["colorB"]            = colorB;
        if (halftonePercent    != null) p["halftonePercent"]    = halftonePercent;
        if (underlayBrightness != null) p["underlayBrightness"] = underlayBrightness;
        var result = await revit.ExecuteAsync("manage_additional_settings", p, ct);
        return result.ToString();
    }
}
