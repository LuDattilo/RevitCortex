using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class ParameterTools
{
    [McpServerTool(Name = "set_element_parameters"), Description("Set parameter values on one or more elements. Pass requests as a JSON-encoded array string. Supports parameterName by display name and builtInParameter by Revit API enum name, e.g. REBAR_SYSTEM_SPACING_TOP_DIR_1. UNITS: a numeric value on a length/area/etc. parameter is written in Revit internal units (feet) — to set a display value, pass a STRING WITH A UNIT (e.g. \"3000 mm\", \"3 m\") and Revit parses it unit- and locale-aware. A null value clears the parameter. Note: type-level parameters can be set through an instance when the type is resolvable.")]
    public static async Task<string> SetElementParameters(
        RevitConnectionManager revit,
        [Description("JSON-encoded array of set requests — pass as a string. Each item must have elementId, value, and either parameterName or builtInParameter. Use null to clear. For lengths/areas pass a unit string like \"3000 mm\" to avoid writing feet. Example: \"[{\\\"elementId\\\": 123, \\\"builtInParameter\\\": \\\"REBAR_SYSTEM_SPACING_TOP_DIR_1\\\", \\\"value\\\": \\\"200 mm\\\"}]\"")] string requests,
        CancellationToken ct = default)
    {
        JArray parsed;
        try { parsed = JArray.Parse(requests); }
        catch (Exception ex)
        {
            return $"{{\"error\": \"requests must be a JSON array encoded as a string. Parse failed: {ex.Message}. Example: [{{\\\"elementId\\\": 123, \\\"parameterName\\\": \\\"Comments\\\", \\\"value\\\": \\\"test\\\"}}]\"}}";
        }
        var p = new JObject { ["requests"] = parsed };
        var result = await revit.ExecuteAsync("set_element_parameters", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "bulk_modify_parameter_values"), Description("Bulk modify parameter values across elements by category. Supports set, find-and-replace, and other operations.")]
    public static async Task<string> BulkModifyParameterValues(
        RevitConnectionManager revit,
        [Description("Parameter name to modify")] string parameterName,
        [Description("Category name to filter elements (e.g. Walls, Doors)")] string? categoryName = null,
        [Description("Operation to perform (e.g. set, find_replace). Default: set")] string? operation = "set",
        [Description("Value to set")] string? value = null,
        [Description("Text to find (for find_replace operation)")] string? findText = null,
        [Description("Replacement text (for find_replace operation)")] string? replaceText = null,
        [Description("Preview changes without applying. Default: true")] bool? dryRun = true,
        [Description("If true, include up to `sampleLimit` modified elements in the response. Default false to keep dryRun payloads small — most callers only need the counts.")] bool? includeSample = null,
        [Description("How many modified elements to include when includeSample=true. Default 100.")] int? sampleLimit = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["parameterName"] = parameterName,
        };
        if (categoryName != null) p["categoryName"] = categoryName;
        if (operation != null) p["operation"] = operation;
        if (value != null) p["value"] = value;
        if (findText != null) p["findText"] = findText;
        if (replaceText != null) p["replaceText"] = replaceText;
        if (dryRun != null) p["dryRun"] = dryRun;
        if (includeSample != null) p["includeSample"] = includeSample;
        if (sampleLimit != null) p["sampleLimit"] = sampleLimit;
        var result = await revit.ExecuteAsync("bulk_modify_parameter_values", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "filter_by_parameter_value"), Description("Filter elements by one parameter condition, or several combined with AND/OR via the conditions array. Conditions: equals, not_equals, contains, not_contains, begins_with, ends_with, greater_than, less_than, is_empty, is_not_empty.")]
    public static async Task<string> FilterByParameterValue(
        RevitConnectionManager revit,
        [Description("Category to filter (e.g. Walls, Doors). Optional when scanning the whole model")] string? category = null,
        [Description("Parameter name (single-condition mode)")] string? parameterName = null,
        [Description("Condition (single-condition mode): equals | not_equals | contains | greater_than | less_than | is_empty | ... Default: equals")] string? condition = null,
        [Description("Value to match (single-condition mode)")] string? value = null,
        [Description("Multi-condition mode: JSON array of {parameterName, condition, value, parameterType?}")] string? conditions = null,
        [Description("How to combine multiple conditions: and | or. Default: and")] string? logic = null,
        [Description("Parameter type: instance, type, or both. Default: both")] string? parameterType = "both",
        [Description("Scope: whole_model | active_view | selection. Default: whole_model")] string? scope = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (category != null) p["categories"] = new JArray(category);
        if (parameterName != null) p["parameterName"] = parameterName;
        if (condition != null) p["condition"] = condition;
        if (value != null) p["value"] = value;
        if (conditions != null) p["conditions"] = JArray.Parse(conditions);
        if (logic != null) p["logic"] = logic;
        if (parameterType != null) p["parameterType"] = parameterType;
        if (scope != null) p["scope"] = scope;
        var result = await revit.ExecuteAsync("filter_by_parameter_value", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "sync_csv_parameters"), Description("Synchronize parameter values from CSV data into Revit elements.")]
    public static async Task<string> SyncCsvParameters(
        RevitConnectionManager revit,
        [Description("JSON array of rows: [{elementId, paramName1: value, paramName2: value, ...}]")] string data,
        [Description("Preview changes without applying. Default: true")] bool? dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["data"] = JArray.Parse(data) };
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("sync_csv_parameters", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "add_prefix_suffix"), Description("Add a prefix and/or suffix to parameter values across the model or a selection.")]
    public static async Task<string> AddPrefixSuffix(
        RevitConnectionManager revit,
        [Description("Parameter name to modify")] string parameterName,
        [Description("Prefix to prepend")] string? prefix = null,
        [Description("Suffix to append")] string? suffix = null,
        [Description("Scope: whole_model or selection. Default: whole_model")] string? scope = "whole_model",
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["parameterName"] = parameterName,
        };
        if (prefix != null) p["prefix"] = prefix;
        if (suffix != null) p["suffix"] = suffix;
        if (scope != null) p["scope"] = scope;
        var result = await revit.ExecuteAsync("add_prefix_suffix", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "clear_parameter_values"), Description("Clear parameter values on elements by category or scope")]
    public static async Task<string> ClearParameterValues(
        RevitConnectionManager revit,
        [Description("Parameter name to clear")] string parameterName,
        [Description("Scope: whole_model or selection. Default: whole_model")] string? scope = "whole_model",
        [Description("JSON array of category names to filter")] string? categories = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["parameterName"] = parameterName };
        if (scope != null) p["scope"] = scope;
        if (categories != null) p["categories"] = JArray.Parse(categories);
        var result = await revit.ExecuteAsync("clear_parameter_values", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "add_shared_parameter"), Description("Add a shared parameter to project categories. The data type of a newly created definition is honored (a typed shared parameter, not always Text).")]
    public static async Task<string> AddSharedParameter(
        RevitConnectionManager revit,
        [Description("Name of the shared parameter")] string parameterName,
        [Description("Categories to bind to (OST_* codes or display names)")] string[] categories,
        [Description("Group name in the shared parameter file. Default: RevitCortex")] string? groupName = null,
        [Description("Instance (true) or type (false) binding. Default: true")] bool? isInstance = null,
        [Description("Data type for a newly created definition: Text | Integer | Number | Length | Area | Volume | Angle | YesNo | URL. Default: Text. Ignored if the definition already exists in the shared parameter file.")] string? dataType = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["parameterName"] = parameterName,
            ["categories"] = new JArray(categories),
        };
        if (groupName != null) p["groupName"] = groupName;
        if (isInstance != null) p["isInstance"] = isInstance;
        if (dataType != null) p["dataType"] = dataType;
        var result = await revit.ExecuteAsync("add_shared_parameter", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "manage_project_parameters"), Description("Manage project parameters. Actions: list | create | delete | modify | set_group | set_binding_type | rename. 'delete' now correctly removes non-shared parameters too (works around Revit bug REVIT-136670 by deleting the ParameterElement) and verifies removal. 'modify' supports add/remove/replace on category bindings. 'set_group' bulk-changes the group assignment. 'set_binding_type' toggles a parameter between instance and type. 'rename' is NOT supported by the Revit API for bound project parameters and returns guidance (only global parameters can be renamed).")]
    public static async Task<string> ManageProjectParameters(
        RevitConnectionManager revit,
        [Description("Action: list | create | delete | modify | set_group | set_binding_type | rename")] string action = "list",
        [Description("Parameter name (required for create/delete/modify/set_group/set_binding_type/rename). For set_group you can also pass parameterNames[].")] string? parameterName = null,
        [Description("Data type for create: Text | Integer | Number | Length | Area | Volume | Angle | YesNo | URL")] string? dataType = null,
        [Description("Instance (true) or type (false) binding. Used on 'create' and on 'set_binding_type' (the target binding type).")] bool? isInstance = null,
        [Description("Categories list (OST_* codes or display names) — for create/modify")] string[]? categories = null,
        [Description("How modify applies 'categories': add (default, union), remove (unbind listed), replace (set to exactly the listed). Ignored for other actions.")] string? categoriesMode = null,
        [Description("Parameter names array — for set_group bulk operation, e.g. [\"BCA_RES_Stato-Conservazione\",\"BCA_CME_Codice-Tariffa\"]")] string[]? parameterNames = null,
        [Description("Target group for set_group action. Short names: IdentityData, Data, Constraints, Geometry, Graphics, Materials, Text, General, Phasing, Visibility, Construction, Electrical, ElectricalEngineering, ElectricalLighting, ElectricalLoads, Mechanical, MechanicalAirflow, Plumbing, FireProtection, Ifc, AnalysisResults, Structural, StructuralAnalysis. A full ForgeTypeId is also accepted.")] string? targetGroup = null,
        [Description("New name — only used by 'rename' (which returns API-limitation guidance for project parameters; use global parameters if you need rename).")] string? newName = null,
        [Description("Preview only (set_group). Default: false. When true, returns planned changes without applying.")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (parameterName != null) p["parameterName"] = parameterName;
        if (dataType != null) p["dataType"] = dataType;
        if (isInstance != null) p["isInstance"] = isInstance;
        if (categories != null) p["categories"] = new JArray(categories);
        if (categoriesMode != null) p["categoriesMode"] = categoriesMode;
        if (parameterNames != null) p["parameterNames"] = new JArray(parameterNames);
        if (targetGroup != null) p["targetGroup"] = targetGroup;
        if (newName != null) p["newName"] = newName;
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("manage_project_parameters", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "transfer_parameters"), Description("Copy parameter values from source element to one or more target elements.")]
    public static async Task<string> TransferParameters(
        RevitConnectionManager revit,
        [Description("Source element ID")] long sourceElementId,
        [Description("Target element IDs (array of long)")] long[] targetElementIds,
        [Description("Parameter names to copy; if empty, copies all writable parameters")] string[]? parameterNames = null,
        [Description("Also copy type-level parameters. Default: false")] bool? includeType = null,
        [Description("Preview changes without applying. Default: true")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["sourceElementId"] = sourceElementId,
            ["targetElementIds"] = new JArray(targetElementIds.Cast<object>().ToArray()),
        };
        if (parameterNames != null) p["parameterNames"] = new JArray(parameterNames);
        if (includeType != null) p["includeType"] = includeType;
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("transfer_parameters", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "export_shared_parameter_file"), Description("Export shared parameter file contents")]
    public static async Task<string> ExportSharedParameterFile(
        RevitConnectionManager revit,
        [Description("Output file path for the exported file")] string? outputPath = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (outputPath != null) p["outputPath"] = outputPath;
        var result = await revit.ExecuteAsync("export_shared_parameter_file", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "manage_global_parameters"), Description("Manage global parameters (project-level named values). Actions: list | get | create | set | delete | rename | set_formula | move_up | move_down | sort. Unlike project/shared parameters, global parameters CAN be renamed. 'set_formula' drives the value by a formula (empty string clears it). 'move_up'/'move_down' reorder within the group; 'sort' orders ascending/descending.")]
    public static async Task<string> ManageGlobalParameters(
        RevitConnectionManager revit,
        [Description("Action: list | get | create | set | delete | rename | set_formula | move_up | move_down | sort")] string action = "list",
        [Description("Parameter name (required for get/create/set/delete/rename/set_formula/move_up/move_down)")] string? name = null,
        [Description("Data type for create: text, integer, number, length, area, volume, angle, yesno")] string? dataType = null,
        [Description("Value to set (for create or set actions)")] string? value = null,
        [Description("New name — required for 'rename'")] string? newName = null,
        [Description("Formula expression — required for 'set_formula'. Pass an empty string to clear the formula.")] string? formula = null,
        [Description("Sort order for 'sort': ascending (default) | descending")] string? order = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (name     != null) p["name"]     = name;
        if (dataType != null) p["dataType"] = dataType;
        if (value    != null) p["value"]    = value;
        if (newName  != null) p["newName"]  = newName;
        if (formula  != null) p["formula"]  = formula;
        if (order    != null) p["order"]    = order;
        var result = await revit.ExecuteAsync("manage_global_parameters", p, ct);
        return result.ToString();
    }
}
