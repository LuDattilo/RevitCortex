using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class ParameterTools
{
    [McpServerTool(Name = "set_element_parameters"), Description("Set parameter values on one or more elements. Each request specifies an element ID, parameter name, and new value.")]
    public static async Task<string> SetElementParameters(
        RevitConnectionManager revit,
        [Description("JSON array of requests: [{elementId, parameterName, value}]")] string requests,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["requests"] = JArray.Parse(requests),
        };
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
        var result = await revit.ExecuteAsync("bulk_modify_parameter_values", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "filter_by_parameter_value"), Description("Filter elements by category and parameter value. Returns elements whose parameter matches the specified value.")]
    public static async Task<string> FilterByParameterValue(
        RevitConnectionManager revit,
        [Description("Category to filter (e.g. Walls, Doors)")] string category,
        [Description("Parameter name to filter by")] string parameterName,
        [Description("Value to match")] string value,
        [Description("Parameter type: instance, type, or both. Default: both")] string? parameterType = "both",
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["category"] = category,
            ["parameterName"] = parameterName,
            ["value"] = value,
        };
        if (parameterType != null) p["parameterType"] = parameterType;
        var result = await revit.ExecuteAsync("filter_by_parameter_value", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "sync_csv_parameters"), Description("Synchronize parameter values from CSV data into Revit elements.")]
    public static async Task<string> SyncCsvParameters(
        RevitConnectionManager revit,
        [Description("CSV data as a string")] string data,
        CancellationToken ct = default)
    {
        var p = JObject.Parse(data);
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

    [McpServerTool(Name = "add_shared_parameter"), Description("Add a shared parameter to project categories")]
    public static async Task<string> AddSharedParameter(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("add_shared_parameter", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "manage_project_parameters"), Description("List, create, delete, or modify project parameters")]
    public static async Task<string> ManageProjectParameters(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("manage_project_parameters", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "transfer_parameters"), Description("Copy parameter values from source to target elements")]
    public static async Task<string> TransferParameters(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("transfer_parameters", JObject.Parse(data), ct);
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

    [McpServerTool(Name = "manage_global_parameters"), Description("List, create, read, update, or delete global parameters (project-level named values). Actions: list, get, create, set, delete.")]
    public static async Task<string> ManageGlobalParameters(
        RevitConnectionManager revit,
        [Description("Action: list | get | create | set | delete")] string action = "list",
        [Description("Parameter name (required for get/create/set/delete)")] string? name = null,
        [Description("Data type for create: text, integer, number, length, area, volume, angle, yesno")] string? dataType = null,
        [Description("Value to set (for create or set actions)")] string? value = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["action"] = action };
        if (name     != null) p["name"]     = name;
        if (dataType != null) p["dataType"] = dataType;
        if (value    != null) p["value"]    = value;
        var result = await revit.ExecuteAsync("manage_global_parameters", p, ct);
        return result.ToString();
    }
}
