using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RiveTT.Server.Connection;

namespace RiveTT.Server.Tools;

[McpServerToolType]
public static class ProjectTools
{
    [McpServerTool(Name = "list_warnings"), Description("Get model warnings from the active Revit document.")]
    public static async Task<string> GetWarnings(
        RevitConnectionManager revit,
        [Description("Maximum number of warnings to return")] int maxWarnings = 50,
        CancellationToken ct = default)
    {
        var p = new JObject { ["maxWarnings"] = maxWarnings };
        var result = await revit.ExecuteAsync("list_warnings", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "list_phases"), Description("List all project phases in the active Revit document.")]
    public static async Task<string> GetPhases(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("list_phases", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "list_worksets"), Description("List all worksets in the active Revit document.")]
    public static async Task<string> GetWorksets(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("list_worksets", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "manage_worksets"), Description("Create, rename, delete, or set the active workset (workshared models only). To LIST worksets use list_worksets.")]
    public static async Task<string> ManageWorksets(
        RevitConnectionManager revit,
        [Description("Action: create | rename | delete | set_active")] string action,
        [Description("Workset name (for create; also resolves the target for rename/delete/set_active if worksetId is omitted)")] string? name = null,
        [Description("Workset id (int) identifying the target for rename/delete/set_active")] int? worksetId = null,
        [Description("New name (for rename)")] string? newName = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (name != null) p["name"] = name;
        if (worksetId != null) p["worksetId"] = worksetId;
        if (newName != null) p["newName"] = newName;
        var result = await revit.ExecuteAsync("manage_worksets", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "load_family"), Description("Load a family into the Revit project, or reload one already there (e.g. after editing its .rfa outside Revit). Also lists loaded families or duplicates a family type.")]
    public static async Task<string> LoadFamily(
        RevitConnectionManager revit,
        [Description("Action to perform (e.g. list, load)")] string action = "list",
        [Description("Path to the family file (.rfa)")] string? familyPath = null,
        [Description("Filter by category")] string? categoryFilter = null,
        [Description("When a same-named family already exists, overwrite it (updates its types/parameters). Default: true. Set false to leave an existing family untouched.")] bool overwriteExisting = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action, ["overwriteExisting"] = overwriteExisting };
        if (familyPath != null) p["familyPath"] = familyPath;
        if (categoryFilter != null) p["categoryFilter"] = categoryFilter;
        var result = await revit.ExecuteAsync("load_family", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "edit_family"), Description("Edits a loaded family's type parameters in the background - no window opens. Pass familyId or familyName, and changes as JSON: [{typeName, parameters: {paramName: value}}]. Only existing types and parameter values (dimensions, materials, yes/no, text): not new types, not geometry. Numeric values are internal units (feet); pass a string with a unit (e.g. \"900 mm\") to write a display value. The project's copy is updated in place - nothing opens on screen. Use open_family instead for visual/geometry edits.")]
    public static async Task<string> EditFamily(
        RevitConnectionManager revit,
        [Description("Family element ID (alternative to familyName)")] long? familyId = null,
        [Description("Family name (alternative to familyId)")] string? familyName = null,
        [Description("JSON array: [{typeName, parameters: {paramName: value}}]")] System.Text.Json.JsonElement? changes = null,
        [Description("Preview without editing. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dryRun"] = dryRun };
        if (familyId != null) p["familyId"] = familyId;
        if (familyName != null) p["familyName"] = familyName;
        if (changes != null)
        {
            if (!JsonArrayParam.TryParse(changes, out var changesArray))
                return JsonArrayParam.InvalidArrayResult("edit_family", "changes", changes);
            p["changes"] = changesArray;
        }
        var result = await revit.ExecuteAsync("edit_family", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "list_family_types"), Description("List available family types in the Revit project.")]
    public static async Task<string> GetAvailableFamilyTypes(
        RevitConnectionManager revit,
        [Description("Filter by category names (OST codes, English, or localized labels). JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? categoryList = null,
        [Description("Case-insensitive substring filter on family or type name")] string? familyNameFilter = null,
        [Description("Max types to return. Default: 100")] int? limit = null,
        [Description("Return a compact payload without uniqueId-heavy rows. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (categoryList != null)
        {
            if (!JsonArrayParam.TryParse(categoryList, out var categoryListArray))
                return JsonArrayParam.InvalidArrayResult("list_family_types", "categoryList", categoryList);
            p["categoryList"] = categoryListArray;
        }
        if (familyNameFilter != null) p["familyNameFilter"] = familyNameFilter;
        if (limit != null) p["limit"] = limit;
        var result = await revit.ExecuteAsync("list_family_types", p, ct);
        return ToolResponseShaper.Shape("list_family_types", result, compact, summaryOnly: false).ToString();
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
        [Description("Also enumerate system-family types (WallType/FloorType/RoofType/CeilingType). Default: false")] bool includeSystemFamilies = false,
        [Description("Sort order: instance_count | name. Default: instance_count")] string? sortBy = null,
        [Description("Return compact family rows without audit booleans (isInPlace/isEditable/isUnused/kind). Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject { ["includeUnused"] = includeUnused };
        if (categoryFilter != null) p["categoryFilter"] = categoryFilter;
        p["includeSystemFamilies"] = includeSystemFamilies;
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

    [McpServerTool(Name = "purge_unused"), Description("Purge unused families/types and materials, and optionally unreferenced view templates and view filters, from the project.")]
    public static async Task<string> PurgeUnused(
        RevitConnectionManager revit,
        [Description("Preview changes without applying")] bool dryRun = true,
        [Description("Also purge view templates not referenced by any view. Default: true")] bool includeViewTemplates = true,
        [Description("Also purge view filters not applied to any view. Default: true")] bool includeFilters = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dryRun"] = dryRun };
        p["includeViewTemplates"] = includeViewTemplates;
        p["includeFilters"] = includeFilters;
        var result = await revit.ExecuteAsync("purge_unused", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "manage_sheet_sets"), Description("List, create, or delete named view/sheet sets (ViewSheetSet), so batch_export/printing can reuse a saved list instead of one passed on every call. action=list|create|delete. create needs name + viewIds/sheetIds. No rename API: recreate under a new name instead.")]
    public static async Task<string> ManageSheetSets(
        RevitConnectionManager revit,
        [Description("Action: list | create | delete. Default: list")] string action = "list",
        [Description("Sheet set name (create), or exact name to match (delete, alternative to elementId)")] string? name = null,
        [Description("View or sheet element IDs to include, as a JSON array of numbers (create)")] System.Text.Json.JsonElement? viewIds = null,
        [Description("Sheet set element ID (delete, alternative to name)")] long? elementId = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (name != null) p["name"] = name;
        if (viewIds != null)
        {
            if (!JsonArrayParam.TryParse(viewIds, out var viewIdsArray))
                return JsonArrayParam.InvalidArrayResult("manage_sheet_sets", "viewIds", viewIds);
            p["viewIds"] = viewIdsArray;
        }
        if (elementId != null) p["elementId"] = elementId;
        var result = await revit.ExecuteAsync("manage_sheet_sets", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "detect_clashes"), Description("Detect clashes between two element categories. Uses true solid-geometry intersection by default (fewer false positives than bounding boxes).")]
    public static async Task<string> ClashDetection(
        RevitConnectionManager revit,
        [Description("First category for clash detection (e.g. OST_Walls)")] string categoryA,
        [Description("Second category for clash detection (e.g. OST_Floors)")] string categoryB,
        [Description("Maximum number of clash pairs to return. Default: 100. Use higher values on dense models to discover the true clash count; lower for quick checks.")] int maxResults = 100,
        [Description("Bounding-box pre-filter tolerance in millimeters. Default: 0 (any overlap). Positive values shrink the test, ignoring tiny grazing intersections.")] double tolerance = 0,
        [Description("Confirm bbox candidates with true solid intersection. Default: true. Set false for a faster bbox-only approximation.")] bool useSolidGeometry = true,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["categoryA"] = categoryA,
            ["categoryB"] = categoryB,
            ["maxResults"] = maxResults,
            ["tolerance"] = tolerance,
        };
        p["useSolidGeometry"] = useSolidGeometry;
        var result = await revit.ExecuteAsync("detect_clashes", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "show_clashes"), Description("Detect clashes between two categories and create a 3D section-boxed view for visual review. Uses the same true solid-geometry intersection as detect_clashes (bounding-box pre-filter, then ElementIntersectsElementFilter), so the two tools return the same clashes on the same model.")]
    public static async Task<string> WorkflowClashReview(
        RevitConnectionManager revit,
        [Description("First category (e.g. OST_Walls)")] string categoryA,
        [Description("Second category (e.g. OST_Pipes)")] string categoryB,
        [Description("Intersection tolerance in mm. Default: 0")] double? tolerance = null,
        [Description("Create a section-boxed 3D view around detected clashes. Default: true")] bool createSectionBox = true,
        [Description("Confirm each bounding-box candidate against the real solids. Default: true; false is faster but over-reports.")] bool useSolidGeometry = true,
        [Description("Stop after this many clashes. Default: 100")] int? maxResults = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["categoryA"] = categoryA,
            ["categoryB"] = categoryB,
        };
        if (tolerance != null) p["tolerance"] = tolerance;
        if (maxResults != null) p["maxResults"] = maxResults;
        p["createSectionBox"] = createSectionBox;
        p["useSolidGeometry"] = useSolidGeometry;
        var result = await revit.ExecuteAsync("show_clashes", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "workflow_model_audit"), Description("Run a complete model audit workflow.")]
    public static async Task<string> WorkflowModelAudit(
        RevitConnectionManager revit,
        [Description("Include warnings in the response. Default: true")] bool includeWarnings = true,
        [Description("Include family lists in the response. Default: true")] bool includeFamilies = true,
        [Description("Maximum grouped warnings returned. Default: 50")] int? maxWarnings = null,
        [Description("Strip warnings/families to summary fields only. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        p["includeWarnings"] = includeWarnings;
        p["includeFamilies"] = includeFamilies;
        if (maxWarnings != null) p["maxWarnings"] = maxWarnings;
        var result = await revit.ExecuteAsync("workflow_model_audit", p, ct);
        return ToolResponseShaper.Shape("workflow_model_audit", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "create_level"), Description("Create, edit, rename, or delete a level. action=create|set|rename|delete. For set/rename/delete identify the level by levelId or name.")]
    public static async Task<string> CreateLevel(
        RevitConnectionManager revit,
        [Description("Level name (required for create; identifies the target for set/rename/delete when levelId is omitted)")] string? name = null,
        [Description("Elevation in mm (create, or set to change an existing level's elevation)")] double? elevation = null,
        [Description("Action: create | set | rename | delete. Default: create")] string? action = null,
        [Description("Level element id (identifies the target for set/rename/delete)")] long? levelId = null,
        [Description("Mark as a building story (create/set) Pass \"true\" or \"false\"; omit to leave unchanged.")] string? isBuildingStory = null,
        [Description("New name (for rename)")] string? newName = null,
        [Description("Preview changes without applying. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (name != null) p["name"] = name;
        if (elevation != null) p["elevation"] = elevation;
        if (levelId != null) p["levelId"] = levelId;
        if (isBuildingStory != null)
        {
            if (!TriStateFlag.TryParse(isBuildingStory, out var isBuildingStoryFlag))
                return TriStateFlag.InvalidFlagResult("create_level", "isBuildingStory", isBuildingStory);
            p["isBuildingStory"] = isBuildingStoryFlag;
        }
        if (newName != null) p["newName"] = newName;
        p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("create_level", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "create_room"), Description("Create a room at a point on a level. x/y are plan coordinates in mm; the level sets the elevation. A point that is not inside a closed loop of room-bounding elements, or that falls in an existing room, is refused (area 0, nothing left in the model) unless allowUnenclosed=true. dryRun reports which room already occupies the point.")]
    public static async Task<string> CreateRoom(
        RevitConnectionManager revit,
        [Description("Level element ID where the room will be placed")] long levelId,
        [Description("X coordinate for room placement, in mm")] double x,
        [Description("Y coordinate for room placement, in mm")] double y,
        [Description("Room name")] string? name = null,
        [Description("Room number")] string? number = null,
        [Description("Department")] string? department = null,
        [Description("Keep the room even if it is not enclosed (area 0). Default: false (refused)")] bool allowUnenclosed = false,
        [Description("Preview without creating: reports the room already occupying the point. Default: false")] bool dryRun = false,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["levelId"] = levelId,
            ["location"] = new JObject { ["x"] = x, ["y"] = y, ["z"] = 0 },
            ["allowUnenclosed"] = allowUnenclosed,
        };
        if (name != null) p["name"] = name;
        if (number != null) p["number"] = number;
        if (department != null) p["department"] = department;
        p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("create_room", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "delete_element"), Description(
        "Delete elements. The dryRun preview reports the real cascade (dependent tags, sketches, railings...) " +
        "and any group membership. Deleting a group MEMBER performs Revit's EXCLUSION: the element leaves " +
        "that instance only, the group type and the other instances keep their own copies, and Revit renames " +
        "the instance \"(membre exclu)\". Instances of one type are allowed to differ — that is a Revit " +
        "feature, reversible with Restore Excluded Members in the ribbon. The response reports every " +
        "exclusion it caused.")]
    public static async Task<string> DeleteElement(
        RevitConnectionManager revit,
        [Description("Array of element IDs to delete")] long[] elementIds,
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

    [McpServerTool(Name = "duplicate_system_type"), Description("Duplicate, rename, or delete a system type (wall, floor, roof, ceiling). action=duplicate|rename|delete.")]
    public static async Task<string> DuplicateSystemType(
        RevitConnectionManager revit,
        [Description("Element ID of the source/target type")] long? sourceTypeId = null,
        [Description("Name for the new type (duplicate) or new name (rename)")] string? newName = null,
        [Description("Action: duplicate | rename | delete. Default: duplicate")] string? action = null,
        [Description("Source/target type name (alternative to sourceTypeId)")] string? sourceTypeName = null,
        [Description("Category to disambiguate the type name (e.g. Walls)")] string? category = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (sourceTypeId != null) p["sourceTypeId"] = sourceTypeId;
        if (newName != null) p["newName"] = newName;
        if (sourceTypeName != null) p["sourceTypeName"] = sourceTypeName;
        if (category != null) p["category"] = category;
        var result = await revit.ExecuteAsync("duplicate_system_type", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "batch_rename"), Description("Batch rename elements or system types in the Revit project. Supports both loadable-family elements and system types (wall/floor/ceiling/roof types).")]
    public static async Task<string> BatchRename(
        RevitConnectionManager revit,
        [Description("Array of element IDs to rename. Use this when you already have specific IDs. JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? elementIds = null,
        [Description("Target category to rename. Valid values: views | sheets | levels | grids | rooms | walltypes | floortypes | ceilingtypes | rooftypes. Use 'floortypes' to rename system floor types.")] string? targetCategory = null,
        [Description("Text to find")] string? findText = null,
        [Description("Replacement text")] string? replaceText = null,
        [Description("Prefix to add")] string? prefix = null,
        [Description("Suffix to add")] string? suffix = null,
        [Description("Preview changes without applying")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dryRun"] = dryRun };
        if (elementIds != null)
        {
            if (!JsonArrayParam.TryParse(elementIds, out var elementIdsArray))
                return JsonArrayParam.InvalidArrayResult("batch_rename", "elementIds", elementIds);
            p["elementIds"] = elementIdsArray;
        }
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
        [Description("Categories to restrict the rename (e.g. Doors, Windows). JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? categories = null,
        [Description("Also rename the family types. Default: false")] bool renameTypes = false,
        [Description("Preview without writing. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (operation != null) p["operation"] = operation;
        if (prefix != null) p["prefix"] = prefix;
        if (suffix != null) p["suffix"] = suffix;
        if (findText != null) p["findText"] = findText;
        if (replaceText != null) p["replaceText"] = replaceText;
        if (categories != null)
        {
            if (!JsonArrayParam.TryParse(categories, out var categoriesArray))
                return JsonArrayParam.InvalidArrayResult("rename_families", "categories", categories);
            p["categories"] = categoriesArray;
        }
        p["renameTypes"] = renameTypes;
        p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("rename_families", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "tag_rooms"), Description("Tag rooms in a view. Pass viewId to target a specific view; without it the active view is used. Nothing in this surface can activate a view, so viewId is the only way to tag a view the user is not currently looking at.")]
    public static async Task<string> TagRooms(
        RevitConnectionManager revit,
        [Description("Use leader on tags. Default: false")] bool useLeader = false,
        [Description("Room IDs to tag (optional; tags all rooms in view when omitted). JSON array, e.g. [1,2]")] System.Text.Json.JsonElement? roomIds = null,
        [Description("View to tag in. Omit to use the currently active view.")] long? viewId = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        p["useLeader"] = useLeader;
        if (viewId != null) p["viewId"] = viewId;
        if (roomIds != null)
        {
            if (!JsonArrayParam.TryParse(roomIds, out var roomIdsArray))
                return JsonArrayParam.InvalidArrayResult("tag_rooms", "roomIds", roomIds);
            p["roomIds"] = roomIdsArray;
        }
        var result = await revit.ExecuteAsync("tag_rooms", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "tag_walls"), Description("Tag walls at their midpoints in the active view. Operates on the active view only. Tags all walls by default, or a subset via wallIds.")]
    public static async Task<string> TagWalls(
        RevitConnectionManager revit,
        [Description("Use leader on tags. Default: false")] bool useLeader = false,
        [Description("Tag orientation: horizontal | vertical. Default: horizontal")] string? orientation = null,
        [Description("Tag type (FamilySymbol) element ID. Default: first available wall tag type")] long? tagTypeId = null,
        [Description("JSON array of wall element IDs to tag. Omit to tag all walls in the view")] System.Text.Json.JsonElement? wallIds = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        p["useLeader"] = useLeader;
        if (orientation != null) p["orientation"] = orientation;
        if (tagTypeId != null) p["tagTypeId"] = tagTypeId;
        if (wallIds != null)
        {
            if (!JsonArrayParam.TryParse(wallIds, out var wallIdsArray))
                return JsonArrayParam.InvalidArrayResult("tag_walls", "wallIds", wallIds);
            p["wallIds"] = wallIdsArray;
        }
        var result = await revit.ExecuteAsync("tag_walls", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "delete_empty_tags"), Description("Find and remove empty or orphaned tags")]
    public static async Task<string> WipeEmptyTags(
        RevitConnectionManager revit,
        [Description("Preview changes without applying. Default: true")] bool dryRun = true,
        [Description("View element ID to search in")] long? viewId = null,
        [Description("JSON array of category names to filter")] System.Text.Json.JsonElement? categories = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dryRun"] = dryRun };
        if (viewId != null) p["viewId"] = viewId;
        if (categories != null)
        {
            if (!JsonArrayParam.TryParse(categories, out var categoriesArray))
                return JsonArrayParam.InvalidArrayResult("delete_empty_tags", "categories", categories);
            p["categories"] = categoriesArray;
        }
        var result = await revit.ExecuteAsync("delete_empty_tags", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "set_material_properties"), Description("Set identity, appearance, product info, and asset assignments on Revit materials. Each request is a FLAT object keyed by materialId plus any of: name, color (#RRGGBB), transparency, shininess, smoothness, materialClass, materialCategory, description, manufacturer, model, url, cost, mark, keynote, comments, appearanceAssetId, structuralAssetId, thermalAssetId (asset ids: 0/-1 clears).")]
    public static async Task<string> SetMaterialProperties(
        RevitConnectionManager revit,
        [Description("JSON array of flat requests, e.g. [{\"materialId\":123,\"color\":\"#FF0000\",\"appearanceAssetId\":456}]")] string requests,
        [Description("Preview changes without applying. Default: true")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["requests"] = JArray.Parse(requests) };
        p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("set_material_properties", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "modify_schedule"), Description("Modify schedule fields, sorting, filters, or rename the schedule. Supported actions: add_field, remove_field, set_sorting, clear_sorting, set_filter, clear_filter, rename.")]
    public static async Task<string> ModifySchedule(
        RevitConnectionManager revit,
        [Description("Action: add_field | remove_field | set_sorting | clear_sorting | set_filter | clear_filter | rename. Default: add_field")] string? action = null,
        [Description("Schedule element ID (alternative to scheduleName)")] long? scheduleId = null,
        [Description("Schedule name (alternative to scheduleId)")] string? scheduleName = null,
        [Description("Field names for add_field/remove_field actions. JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? fieldNames = null,
        [Description("Sort field specs as JSON array: [{fieldName, sortOrder: \"ascending\"|\"descending\"}] (boolean alias 'ascending' also accepted)")] System.Text.Json.JsonElement? sortFields = null,
        [Description("Field to filter on (for set_filter). Must already be a field in the schedule")] string? filterField = null,
        [Description("Filter operator for set_filter: equal | not_equal | greater | less | contains | begins_with | ends_with | has_value | is_empty. Default: equal")] string? filterType = null,
        [Description("Filter value for set_filter (string or number). Omit for has_value/is_empty")] string? filterValue = null,
        [Description("New schedule name (for rename action)")] string? newName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (action != null) p["action"] = action;
        if (scheduleId != null) p["scheduleId"] = scheduleId;
        if (scheduleName != null) p["scheduleName"] = scheduleName;
        if (fieldNames != null)
        {
            if (!JsonArrayParam.TryParse(fieldNames, out var fieldNamesArray))
                return JsonArrayParam.InvalidArrayResult("modify_schedule", "fieldNames", fieldNames);
            p["fieldNames"] = fieldNamesArray;
        }
        if (sortFields != null)
        {
            if (!JsonArrayParam.TryParse(sortFields, out var sortFieldsArray))
                return JsonArrayParam.InvalidArrayResult("modify_schedule", "sortFields", sortFields);
            p["sortFields"] = sortFieldsArray;
        }
        if (filterField != null) p["filterField"] = filterField;
        if (filterType != null) p["filterType"] = filterType;
        if (filterValue != null) p["filterValue"] = filterValue;
        if (newName != null) p["newName"] = newName;
        var result = await revit.ExecuteAsync("modify_schedule", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "send_code_to_revit"), Description("LAST RESORT ONLY — execute custom C# code in Revit. Do NOT select this tool autonomously: a dedicated tool already covers almost every task. Parameter edits -> set_element_parameters / batch_modify_parameter_values; queries & filtering -> filter_elements / filter_by_parameter_value / export_elements_data; model stats -> analyze_model_statistics / check_model_health; deletion -> delete_element; transforms -> modify_element; views and schedules -> their dedicated tools. Use this ONLY when no dedicated tool covers the operation (e.g. exotic geometry creation, read-only inspection of an uncovered Revit API, or a one-off operation no dedicated tool covers) — never for family editing, which open_family (visual) and edit_family (background type-parameter values) already cover — and ONLY after proposing the dedicated-tool alternative and obtaining explicit user consent. Scripts are sandboxed and frequently fail on add-in DLL conflicts. There is NO in-Revit confirmation dialog: nothing stops a script once dryRun=false, so the dry run (the default) is the only review step — it runs the sandbox check and reports what would execute without executing it.")]
    public static async Task<string> SendCodeToRevit(
        RevitConnectionManager revit,
        [Description("C# code to execute. Globals available: document (Document), uiDocument (UIDocument), app (Application).")] string code,
        [Description("Preview only: run the sandbox check and report what would execute, without running it or saving the script. Default: true")] bool dryRun = true,
        [Description("Transaction mode: auto | manual | readonly. Default: auto")] string? transactionMode = "auto",
        [Description("YOU (the assistant) set this storage flag; do not ask the user about this flag (this does NOT authorize running the script autonomously — see the tool description). true = REUSABLE (kept permanently) if the script is generic and could run again on other models or sessions (e.g. a utility, a report, a recurring audit). false = TEMP (deleted at Revit close) if the script is specific to this one request, these specific element IDs, or this exact model. Default: false.")] bool reusable = false,
        [Description("Short human-readable name for the script file (no spaces, max 40 chars). Example: 'floor-thickness-audit'")] string? scriptName = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["code"] = code, ["dryRun"] = dryRun };
        if (transactionMode != null) p["transactionMode"] = transactionMode;
        p["reusable"] = reusable;
        if (scriptName != null) p["scriptName"] = scriptName;
        var result = await revit.ExecuteAsync("send_code_to_revit", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "workflow_data_roundtrip"), Description("Export parameters to Excel for external editing, then re-import once the file has been saved.")]
    public static async Task<string> WorkflowDataRoundtrip(
        RevitConnectionManager revit,
        [Description("Path to the .xlsx file (created on export, read on import)")] string filePath,
        [Description("Categories to include (e.g. Walls, Doors). JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? categories = null,
        [Description("Parameter names to include. JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? parameterNames = null,
        [Description("Include type-level parameters. Default: false")] bool includeTypeParameters = false,
        CancellationToken ct = default)
    {
        var p = new JObject { ["filePath"] = filePath };
        if (categories != null)
        {
            if (!JsonArrayParam.TryParse(categories, out var categoriesArray))
                return JsonArrayParam.InvalidArrayResult("workflow_data_roundtrip", "categories", categories);
            p["categories"] = categoriesArray;
        }
        if (parameterNames != null)
        {
            if (!JsonArrayParam.TryParse(parameterNames, out var parameterNamesArray))
                return JsonArrayParam.InvalidArrayResult("workflow_data_roundtrip", "parameterNames", parameterNames);
            p["parameterNames"] = parameterNamesArray;
        }
        p["includeTypeParameters"] = includeTypeParameters;
        var result = await revit.ExecuteAsync("workflow_data_roundtrip", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "workflow_room_documentation"), Description("Auto-generate callout views (and optionally sections) for every room on a level.")]
    public static async Task<string> WorkflowRoomDocumentation(
        RevitConnectionManager revit,
        [Description("Level name to document")] string levelName,
        [Description("Also create sections per room. Default: true")] bool createSections = true,
        [Description("Boundary offset in mm. Default: 300")] double? offset = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["levelName"] = levelName };
        p["createSections"] = createSections;
        if (offset != null) p["offset"] = offset;
        var result = await revit.ExecuteAsync("workflow_room_documentation", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "list_shared_parameters"), Description("List all project parameters with their bindings and categories, optionally filtered by category.")]
    public static async Task<string> GetSharedParameters(
        RevitConnectionManager revit,
        [Description("Category filter (e.g. OST_Walls); empty for all")] string? categoryFilter = null,
        [Description("Strip per-parameter description. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (categoryFilter != null) p["categoryFilter"] = categoryFilter;
        var result = await revit.ExecuteAsync("list_shared_parameters", p, ct);
        return ToolResponseShaper.Shape("list_shared_parameters", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "count_lines_per_view"), Description("Count detail lines per view (single document pass, safe on any model size) plus a project-wide model line count. Model lines have no owner view, so they are reported once at project level, not per view.")]
    public static async Task<string> LinesPerViewCount(
        RevitConnectionManager revit,
        [Description("Only report views at or above this detail-line count. Default: 0")] int? threshold = null,
        [Description("Count detail lines. Default: true")] bool includeDetailLines = true,
        [Description("Count model lines (project-wide total). Default: true")] bool includeModelLines = true,
        [Description("Max views returned. Default: 200")] int? limit = null,
        [Description("Time budget in milliseconds before returning partial results. Default: 15000")] int? timeBudgetMs = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (threshold != null) p["threshold"] = threshold;
        p["includeDetailLines"] = includeDetailLines;
        p["includeModelLines"] = includeModelLines;
        if (limit != null) p["limit"] = limit;
        if (timeBudgetMs != null) p["timeBudgetMs"] = timeBudgetMs;
        var result = await revit.ExecuteAsync("count_lines_per_view", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "list_family_sizes"), Description("List loaded families with type/instance counts and, when includeSize=true, the family file size in KB measured by exporting each family to a temp file. Disabled by default because exporting is slow on models with many families.")]
    public static async Task<string> ListFamilySizes(
        RevitConnectionManager revit,
        [Description("Max families returned. Default: 50")] int? limit = null,
        [Description("Sort by: instanceCount | typeCount | name | sizeKB. Default: instanceCount. Note: sizeKB sort requires includeSize=true and forces size measurement on every family (slow on large models).")] string? sortBy = null,
        [Description("Categories to restrict the list. JSON array, e.g. [\"A\",\"B\"]")] System.Text.Json.JsonElement? categories = null,
        [Description("If true, measure each returned family's file size in KB. Default false. Cost: ~50-200ms per family (one EditFamily + temp save round-trip).")] bool includeSize = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (limit != null) p["limit"] = limit;
        if (sortBy != null) p["sortBy"] = sortBy;
        if (categories != null)
        {
            if (!JsonArrayParam.TryParse(categories, out var categoriesArray))
                return JsonArrayParam.InvalidArrayResult("list_family_sizes", "categories", categories);
            p["categories"] = categoriesArray;
        }
        p["includeSize"] = includeSize;
        var result = await revit.ExecuteAsync("list_family_sizes", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "list_schedulable_fields"), Description("Discover available schedulable fields for a category.")]
    public static async Task<string> ListSchedulableFields(
        RevitConnectionManager revit,
        [Description("Category name (e.g. OST_Rooms). Default: OST_Rooms")] string? categoryName = null,
        [Description("Schedule type: regular | key_schedule | material_takeoff (aliases: key, material-takeoff). Default: regular")] string? scheduleType = null,
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
