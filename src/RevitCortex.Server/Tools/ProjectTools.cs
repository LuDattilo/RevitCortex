using System.ComponentModel;
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
        [Description("Filter by category")] string? category = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (category != null) p["category"] = category;
        var result = await revit.ExecuteAsync("get_available_family_types", p, ct);
        return result.ToString();
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

    [McpServerTool(Name = "audit_families"), Description("Audit families in the Revit project.")]
    public static async Task<string> AuditFamilies(
        RevitConnectionManager revit,
        [Description("Filter by category")] string? categoryFilter = null,
        [Description("Include unused families in the audit")] bool includeUnused = false,
        CancellationToken ct = default)
    {
        var p = new JObject { ["includeUnused"] = includeUnused };
        if (categoryFilter != null) p["categoryFilter"] = categoryFilter;
        var result = await revit.ExecuteAsync("audit_families", p, ct);
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
        [Description("Audit filters as JSON string")] string? filters = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (filters != null) p["filters"] = filters;
        var result = await revit.ExecuteAsync("workflow_model_audit", p, ct);
        return result.ToString();
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
        [Description("Level element ID where the room will be placed")] int levelId,
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
        [Description("Element ID of the source type to duplicate")] int sourceTypeId,
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

    [McpServerTool(Name = "batch_rename"), Description("Batch rename elements in the Revit project.")]
    public static async Task<string> BatchRename(
        RevitConnectionManager revit,
        [Description("Array of element IDs to rename")] int[]? elementIds = null,
        [Description("Target category for batch rename")] string? targetCategory = null,
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

    [McpServerTool(Name = "rename_families"), Description("Rename loaded families with find/replace, prefix, or suffix")]
    public static async Task<string> RenameFamilies(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("rename_families", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "tag_rooms"), Description("Tag rooms in the current view")]
    public static async Task<string> TagRooms(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("tag_rooms", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "tag_walls"), Description("Tag walls at their midpoints")]
    public static async Task<string> TagWalls(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("tag_walls", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "wipe_empty_tags"), Description("Find and remove empty or orphaned tags")]
    public static async Task<string> WipeEmptyTags(
        RevitConnectionManager revit,
        [Description("Preview changes without applying. Default: true")] bool dryRun = true,
        [Description("View element ID to search in")] int? viewId = null,
        [Description("JSON array of category names to filter")] string? categories = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["dryRun"] = dryRun };
        if (viewId != null) p["viewId"] = viewId;
        if (categories != null) p["categories"] = categories;
        var result = await revit.ExecuteAsync("wipe_empty_tags", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "set_material_properties"), Description("Set identity and product info on Revit materials")]
    public static async Task<string> SetMaterialProperties(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("set_material_properties", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "modify_schedule"), Description("Modify schedule fields, sorting, or rename")]
    public static async Task<string> ModifySchedule(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("modify_schedule", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "send_code_to_revit"), Description("Execute custom C# code in the Revit context")]
    public static async Task<string> SendCodeToRevit(
        RevitConnectionManager revit,
        [Description("C# code to execute")] string code,
        [Description("Transaction mode: auto, manual, or readonly. Default: auto")] string? transactionMode = "auto",
        CancellationToken ct = default)
    {
        var p = new JObject { ["code"] = code };
        if (transactionMode != null) p["transactionMode"] = transactionMode;
        var result = await revit.ExecuteAsync("send_code_to_revit", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "workflow_clash_review"), Description("Detect clashes and create 3D section box view")]
    public static async Task<string> WorkflowClashReview(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("workflow_clash_review", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "workflow_data_roundtrip"), Description("Export parameters to Excel for external editing, then re-import")]
    public static async Task<string> WorkflowDataRoundtrip(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("workflow_data_roundtrip", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "workflow_room_documentation"), Description("Auto-generate callout views and sections from rooms")]
    public static async Task<string> WorkflowRoomDocumentation(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("workflow_room_documentation", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "workflow_sheet_set"), Description("Auto-create sheets with title blocks from a definition list")]
    public static async Task<string> WorkflowSheetSet(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("workflow_sheet_set", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_shared_parameters"), Description("List all project parameters with bindings and categories")]
    public static async Task<string> GetSharedParameters(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_shared_parameters", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "lines_per_view_count"), Description("Count detail lines per view for performance auditing")]
    public static async Task<string> LinesPerViewCount(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("lines_per_view_count", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "list_family_sizes"), Description("List families with type and instance counts")]
    public static async Task<string> ListFamilySizes(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("list_family_sizes", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "list_schedulable_fields"), Description("Discover available schedulable fields for a category")]
    public static async Task<string> ListSchedulableFields(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("list_schedulable_fields", JObject.Parse(data), ct);
        return result.ToString();
    }
}
