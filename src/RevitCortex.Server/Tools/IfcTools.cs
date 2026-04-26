using System.ComponentModel;
using System.Linq;
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

    [McpServerTool(Name = "ifc_validate_request"), Description("Validate IFC file path, extension, and schema version.")]
    public static async Task<string> IfcValidateRequest(
        RevitConnectionManager revit,
        [Description("Path to the IFC file to validate")] string filePath,
        CancellationToken ct = default)
    {
        var p = new JObject { ["filePath"] = filePath };
        var result = await revit.ExecuteAsync("ifc_validate_request", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_link"), Description("Link an IFC file into the active document (creates a .ifc.RVT sidecar file managed by Revit).")]
    public static async Task<string> IfcLink(
        RevitConnectionManager revit,
        [Description("Path to the IFC file to link")] string ifcFilePath,
        [Description("Optional path to the companion .ifc.RVT (defaults to alongside the IFC)")] string? revitFilePath = null,
        [Description("Recreate the link if one already exists. Default: true")] bool? recreateLink = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["ifcFilePath"] = ifcFilePath };
        if (revitFilePath != null) p["revitFilePath"] = revitFilePath;
        if (recreateLink != null) p["recreateLink"] = recreateLink;
        var result = await revit.ExecuteAsync("ifc_link", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_reload_link"), Description("Reload an existing IFC link, optionally from a new file.")]
    public static async Task<string> IfcReloadLink(
        RevitConnectionManager revit,
        [Description("Link TYPE element ID of the IFC link")] long linkTypeId,
        [Description("Optional new IFC file path (triggers a relink)")] string? newIfcFilePath = null,
        [Description("Recreate the link if needed. Default: true")] bool? recreateLink = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["linkTypeId"] = linkTypeId };
        if (newIfcFilePath != null) p["newIfcFilePath"] = newIfcFilePath;
        if (recreateLink != null) p["recreateLink"] = recreateLink;
        var result = await revit.ExecuteAsync("ifc_reload_link", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_open_or_import"), Description("Open or import an IFC file as a native Revit project (actions: open | import).")]
    public static async Task<string> IfcOpenOrImport(
        RevitConnectionManager revit,
        [Description("Path to the IFC file")] string filePath,
        [Description("Action: open | import. Default: open")] string? action = null,
        [Description("Intent: reference | coordination | development. Default: reference")] string? intent = null,
        [Description("Force import even if a native RVT already exists. Default: false")] bool? forceImport = null,
        [Description("Auto-join walls after import. Default: true")] bool? autoJoin = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["filePath"] = filePath };
        if (action != null) p["action"] = action;
        if (intent != null) p["intent"] = intent;
        if (forceImport != null) p["forceImport"] = forceImport;
        if (autoJoin != null) p["autoJoin"] = autoJoin;
        var result = await revit.ExecuteAsync("ifc_open_or_import", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_export_basic"), Description("Export the active document to IFC with a minimal set of options.")]
    public static async Task<string> IfcExportBasic(
        RevitConnectionManager revit,
        [Description("Output directory for the IFC file")] string outputDirectory,
        [Description("File name (without extension); default derived from project name")] string? fileName = null,
        [Description("IFC schema: IFC2x3CV2 | IFC4 | IFC4RV. Default: IFC4RV")] string? fileVersion = null,
        [Description("View ID to filter the export (optional)")] long? filterViewId = null,
        [Description("Export base quantities. Default: false")] bool? exportBaseQuantities = null,
        [Description("Split walls and columns by level. Default: false")] bool? wallAndColumnSplitting = null,
        [Description("Space boundary level: 0, 1, 2. Default: 0")] int? spaceBoundaryLevel = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["outputDirectory"] = outputDirectory };
        if (fileName != null) p["fileName"] = fileName;
        if (fileVersion != null) p["fileVersion"] = fileVersion;
        if (filterViewId != null) p["filterViewId"] = filterViewId;
        if (exportBaseQuantities != null) p["exportBaseQuantities"] = exportBaseQuantities;
        if (wallAndColumnSplitting != null) p["wallAndColumnSplitting"] = wallAndColumnSplitting;
        if (spaceBoundaryLevel != null) p["spaceBoundaryLevel"] = spaceBoundaryLevel;
        // IFC export on large models can take several minutes — use 15 min timeout.
        var result = await revit.ExecuteAsync("ifc_export_basic", p, commandTimeoutSeconds: 900, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_export_with_configuration"), Description("Export using a named configuration (built-in or custom) with optional key/value overrides.")]
    public static async Task<string> IfcExportWithConfiguration(
        RevitConnectionManager revit,
        [Description("Output directory for the IFC file")] string outputDirectory,
        [Description("Configuration name (e.g. IFC 2x3 Coordination View 2.0)")] string configurationName,
        [Description("File name (without extension); default derived from project name")] string? fileName = null,
        [Description("View ID to filter the export (optional)")] long? filterViewId = null,
        [Description("Overrides as JSON object {key: string, ...}")] string? overrides = null,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["outputDirectory"] = outputDirectory,
            ["configurationName"] = configurationName,
        };
        if (fileName != null) p["fileName"] = fileName;
        if (filterViewId != null) p["filterViewId"] = filterViewId;
        if (overrides != null) p["overrides"] = JObject.Parse(overrides);
        // IFC export on large models can take several minutes — use 15 min timeout.
        var result = await revit.ExecuteAsync("ifc_export_with_configuration", p, commandTimeoutSeconds: 900, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_list_export_configurations"), Description("List available built-in export configurations")]
    public static async Task<string> IfcListExportConfigurations(
        RevitConnectionManager revit,
        [Description("Strip per-config description text. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("ifc_list_export_configurations", new JObject(), ct);
        return ToolResponseShaper.Shape("ifc_list_export_configurations", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "ifc_get_export_configuration"), Description("Get full details of a specific export configuration by name.")]
    public static async Task<string> IfcGetExportConfiguration(
        RevitConnectionManager revit,
        [Description("Configuration name")] string configurationName,
        CancellationToken ct = default)
    {
        var p = new JObject { ["configurationName"] = configurationName };
        var result = await revit.ExecuteAsync("ifc_get_export_configuration", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_set_family_mapping_file"), Description("Set the family mapping file used by subsequent IFC exports.")]
    public static async Task<string> IfcSetFamilyMappingFile(
        RevitConnectionManager revit,
        [Description("Path to the family mapping .txt file")] string filePath,
        CancellationToken ct = default)
    {
        var p = new JObject { ["filePath"] = filePath };
        var result = await revit.ExecuteAsync("ifc_set_family_mapping_file", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_analyze_rebuildability"), Description("Analyze IFC DirectShapes and score feasibility of rebuilding them as native Revit elements.")]
    public static async Task<string> IfcAnalyzeRebuildability(
        RevitConnectionManager revit,
        [Description("Category filter (e.g. OST_Walls). Optional.")] string? categoryFilter = null,
        [Description("Max elements to analyze. Default: 200")] int? maxElements = null,
        [Description("Strip per-result extras. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (categoryFilter != null) p["categoryFilter"] = categoryFilter;
        if (maxElements != null) p["maxElements"] = maxElements;
        var result = await revit.ExecuteAsync("ifc_analyze_rebuildability", p, ct);
        return ToolResponseShaper.Shape("ifc_analyze_rebuildability", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "ifc_list_rebuild_candidates"), Description("List elements above a rebuild confidence threshold.")]
    public static async Task<string> IfcListRebuildCandidates(
        RevitConnectionManager revit,
        [Description("Category filter (e.g. OST_Walls). Optional.")] string? categoryFilter = null,
        [Description("Minimum confidence 0.0-1.0. Default: 0.5")] double? minConfidence = null,
        [Description("Max elements returned. Default: 100")] int? maxElements = null,
        [Description("Strip per-candidate extras. Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (categoryFilter != null) p["categoryFilter"] = categoryFilter;
        if (minConfidence != null) p["minConfidence"] = minConfidence;
        if (maxElements != null) p["maxElements"] = maxElements;
        var result = await revit.ExecuteAsync("ifc_list_rebuild_candidates", p, ct);
        return ToolResponseShaper.Shape("ifc_list_rebuild_candidates", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "ifc_rebuild_walls"), Description("Rebuild native walls from IFC DirectShapes. dryRun defaults to true.")]
    public static async Task<string> IfcRebuildWalls(
        RevitConnectionManager revit,
        [Description("Element IDs of DirectShapes to rebuild")] long[] elementIds,
        [Description("Target wall type element ID (optional)")] long? wallTypeId = null,
        [Description("Structural walls. Default: false")] bool? structural = null,
        [Description("Preview without creating. Default: true")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()) };
        if (wallTypeId != null) p["wallTypeId"] = wallTypeId;
        if (structural != null) p["structural"] = structural;
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("ifc_rebuild_walls", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_rebuild_floors"), Description("Rebuild native floors from IFC DirectShapes. dryRun defaults to true.")]
    public static async Task<string> IfcRebuildFloors(
        RevitConnectionManager revit,
        [Description("Element IDs of DirectShapes to rebuild")] long[] elementIds,
        [Description("Target floor type element ID (optional)")] long? floorTypeId = null,
        [Description("Preview without creating. Default: true")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()) };
        if (floorTypeId != null) p["floorTypeId"] = floorTypeId;
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("ifc_rebuild_floors", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_rebuild_roofs"), Description("Rebuild native roofs from IFC DirectShapes. dryRun defaults to true.")]
    public static async Task<string> IfcRebuildRoofs(
        RevitConnectionManager revit,
        [Description("Element IDs of DirectShapes to rebuild")] long[] elementIds,
        [Description("Target roof type element ID (optional)")] long? roofTypeId = null,
        [Description("Preview without creating. Default: true")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()) };
        if (roofTypeId != null) p["roofTypeId"] = roofTypeId;
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("ifc_rebuild_roofs", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_rebuild_structural_members"), Description("Rebuild columns and beams from IFC DirectShapes. dryRun defaults to true.")]
    public static async Task<string> IfcRebuildStructuralMembers(
        RevitConnectionManager revit,
        [Description("Element IDs of DirectShapes to rebuild")] long[] elementIds,
        [Description("Member type: columns | beams | all. Default: all")] string? memberType = null,
        [Description("Family symbol element ID to use (optional)")] long? familySymbolId = null,
        [Description("Preview without creating. Default: true")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()) };
        if (memberType != null) p["memberType"] = memberType;
        if (familySymbolId != null) p["familySymbolId"] = familySymbolId;
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("ifc_rebuild_structural_members", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_rebuild_openings"), Description("Cut openings in rebuilt walls/floors based on IFC opening DirectShapes.")]
    public static async Task<string> IfcRebuildOpenings(
        RevitConnectionManager revit,
        [Description("Element IDs of opening DirectShapes")] long[] elementIds,
        [Description("Element IDs of host walls/floors where openings should be cut")] long[]? hostElementIds = null,
        [Description("Preview without cutting. Default: true")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()) };
        if (hostElementIds != null) p["hostElementIds"] = new JArray(hostElementIds.Cast<object>().ToArray());
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("ifc_rebuild_openings", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_rebuild_family_instances"), Description("Place family instances (doors, windows, furniture) from IFC DirectShapes.")]
    public static async Task<string> IfcRebuildFamilyInstances(
        RevitConnectionManager revit,
        [Description("Element IDs of DirectShapes to rebuild")] long[] elementIds,
        [Description("Category filter (e.g. OST_Doors). Optional.")] string? categoryFilter = null,
        [Description("Preview without creating. Default: true")] bool? dryRun = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()) };
        if (categoryFilter != null) p["categoryFilter"] = categoryFilter;
        if (dryRun != null) p["dryRun"] = dryRun;
        var result = await revit.ExecuteAsync("ifc_rebuild_family_instances", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_compare_original_vs_rebuilt"), Description("Compare volume/geometry between the original DirectShape and its native rebuild.")]
    public static async Task<string> IfcCompareOriginalVsRebuilt(
        RevitConnectionManager revit,
        [Description("Original DirectShape element ID")] long originalElementId,
        [Description("Rebuilt native element ID")] long rebuiltElementId,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["originalElementId"] = originalElementId,
            ["rebuiltElementId"] = rebuiltElementId,
        };
        var result = await revit.ExecuteAsync("ifc_compare_original_vs_rebuilt", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "ifc_tag_unreconstructable_elements"), Description("Tag IFC DirectShapes that cannot be rebuilt by writing a marker parameter.")]
    public static async Task<string> IfcTagUnreconstructableElements(
        RevitConnectionManager revit,
        [Description("Element IDs to tag")] long[] elementIds,
        [Description("Tag value to write. Default: IFC_UNRECONSTRUCTABLE")] string? tagValue = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["elementIds"] = new JArray(elementIds.Cast<object>().ToArray()) };
        if (tagValue != null) p["tagValue"] = tagValue;
        var result = await revit.ExecuteAsync("ifc_tag_unreconstructable_elements", p, ct);
        return result.ToString();
    }
}
