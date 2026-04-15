using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class IfcTools
{
    [McpServerTool(Name = "ifc_get_capabilities"), Description("Detect IFC version support and revit-ifc add-in presence")]
    public static async Task<string> IfcGetCapabilities(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_get_capabilities", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_validate_request"), Description("Validate IFC file path, extension, and schema version")]
    public static async Task<string> IfcValidateRequest(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_validate_request", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_link"), Description("Link an IFC file into the active document")]
    public static async Task<string> IfcLink(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_link", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_reload_link"), Description("Reload an existing IFC link, optionally from a new file")]
    public static async Task<string> IfcReloadLink(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_reload_link", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_open_or_import"), Description("Open or import an IFC file")]
    public static async Task<string> IfcOpenOrImport(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_open_or_import", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_export_basic"), Description("Export to IFC with basic options")]
    public static async Task<string> IfcExportBasic(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_export_basic", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_export_with_configuration"), Description("Export using named configurations with overrides")]
    public static async Task<string> IfcExportWithConfiguration(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_export_with_configuration", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_list_export_configurations"), Description("List available built-in export configurations")]
    public static async Task<string> IfcListExportConfigurations(
        RevitConnectionManager revit,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_list_export_configurations", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_get_export_configuration"), Description("Get full details of a specific export configuration")]
    public static async Task<string> IfcGetExportConfiguration(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_get_export_configuration", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_set_family_mapping_file"), Description("Set family mapping file for IFC exports")]
    public static async Task<string> IfcSetFamilyMappingFile(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_set_family_mapping_file", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_analyze_rebuildability"), Description("Analyze IFC DirectShapes for native reconstruction feasibility")]
    public static async Task<string> IfcAnalyzeRebuildability(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_analyze_rebuildability", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_list_rebuild_candidates"), Description("List elements above a rebuild confidence threshold")]
    public static async Task<string> IfcListRebuildCandidates(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_list_rebuild_candidates", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_rebuild_walls"), Description("Rebuild native walls from IFC DirectShapes")]
    public static async Task<string> IfcRebuildWalls(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_rebuild_walls", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_rebuild_floors"), Description("Rebuild native floors from IFC DirectShapes")]
    public static async Task<string> IfcRebuildFloors(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_rebuild_floors", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_rebuild_roofs"), Description("Rebuild native roofs from IFC DirectShapes")]
    public static async Task<string> IfcRebuildRoofs(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_rebuild_roofs", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_rebuild_structural_members"), Description("Rebuild columns and beams from IFC DirectShapes")]
    public static async Task<string> IfcRebuildStructuralMembers(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_rebuild_structural_members", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_rebuild_openings"), Description("Cut openings in rebuilt walls/floors")]
    public static async Task<string> IfcRebuildOpenings(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_rebuild_openings", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_rebuild_family_instances"), Description("Place doors, windows from IFC DirectShapes")]
    public static async Task<string> IfcRebuildFamilyInstances(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_rebuild_family_instances", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_compare_original_vs_rebuilt"), Description("Compare volume/geometry between original and rebuilt")]
    public static async Task<string> IfcCompareOriginalVsRebuilt(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_compare_original_vs_rebuilt", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_tag_unreconstructable_elements"), Description("Tag elements that cannot be rebuilt")]
    public static async Task<string> IfcTagUnreconstructableElements(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_tag_unreconstructable_elements", JObject.Parse(data), ct);
        return result.ToString();
    }
}
