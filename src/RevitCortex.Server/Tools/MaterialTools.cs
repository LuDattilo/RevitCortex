using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class MaterialTools
{
    [McpServerTool(Name = "get_materials"), Description("List all materials in the active Revit document.")]
    public static async Task<string> GetMaterials(
        RevitConnectionManager revit,
        [Description("Strip numeric appearance props (transparency/shininess/smoothness). Default: false")] bool compact = false,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_materials", new JObject(), ct);
        return ToolResponseShaper.Shape("get_materials", result, compact, summaryOnly: false).ToString();
    }

    [McpServerTool(Name = "create_material"), Description("Create a new material in the Revit project.")]
    public static async Task<string> CreateMaterial(
        RevitConnectionManager revit,
        [Description("Material name")] string name,
        [Description("Material class (e.g. Concrete, Finish, Insulation)")] string? materialClass = null,
        [Description("Color as hex string (e.g. #808080)")] string? color = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["name"] = name };
        if (materialClass != null) p["materialClass"] = materialClass;
        if (color != null) p["color"] = color;
        var result = await revit.ExecuteAsync("create_material", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_compound_structure"), Description("Get wall/floor/roof/ceiling layer structure by type ID or name.")]
    public static async Task<string> GetCompoundStructure(
        RevitConnectionManager revit,
        [Description("Type element ID")] long? typeId = null,
        [Description("Type name")] string? typeName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (typeId != null) p["typeId"] = typeId;
        if (typeName != null) p["typeName"] = typeName;
        var result = await revit.ExecuteAsync("get_compound_structure", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "set_compound_structure"), Description("Modify compound structure on a wall/floor/roof/ceiling type. action=replace|add|remove|modify|set_wrapping. set_wrapping sets openingWrapping (none|exterior|interior|both), endCap (none|exterior|interior), and per-layer layerWrapping.")]
    public static async Task<string> SetCompoundStructure(
        RevitConnectionManager revit,
        [Description("Type element ID")] long typeId,
        [Description("Action: replace | add | remove | modify | set_wrapping. Default: replace")] string? action = null,
        [Description("Layer definitions as JSON array for replace: [{function, materialName, widthMm}]")] string? layers = null,
        [Description("Opening (insert) wrapping for set_wrapping: none | exterior | interior | both")] string? openingWrapping = null,
        [Description("End cap condition for set_wrapping: none | exterior | interior")] string? endCap = null,
        [Description("Per-layer wrapping for set_wrapping: JSON [{layerIndex, wraps:bool}]")] string? layerWrapping = null,
        [Description("Preview changes without applying")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject { ["typeId"] = typeId, ["dryRun"] = dryRun };
        if (action != null) p["action"] = action;
        if (layers != null) p["layers"] = JArray.Parse(layers);
        if (openingWrapping != null) p["openingWrapping"] = openingWrapping;
        if (endCap != null) p["endCap"] = endCap;
        if (layerWrapping != null) p["layerWrapping"] = JArray.Parse(layerWrapping);
        var result = await revit.ExecuteAsync("set_compound_structure", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_material_properties"), Description("Get detailed material properties (physical, thermal, appearance) by material ID or name.")]
    public static async Task<string> GetMaterialProperties(
        RevitConnectionManager revit,
        [Description("Material element ID (alternative to materialName)")] long? materialId = null,
        [Description("Material name (alternative to materialId)")] string? materialName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (materialId != null) p["materialId"] = materialId;
        if (materialName != null) p["materialName"] = materialName;
        var result = await revit.ExecuteAsync("get_material_properties", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_material_quantities"), Description("Calculate material area and volume across elements, optionally filtered by category or restricted to the current selection.")]
    public static async Task<string> GetMaterialQuantities(
        RevitConnectionManager revit,
        [Description("Category filters (e.g. Walls, Floors)")] string[]? categoryFilters = null,
        [Description("Restrict to the current Revit selection. Default: false")] bool? selectedElementsOnly = null,
        [Description("Max rows returned. Default: 50")] int? maxResults = null,
        [Description("Cap on elements processed (default 20000). Above the cap the tool fails with a structured error instead of freezing Revit — narrow with categoryFilters/selectedElementsOnly or raise this deliberately.")] int? maxElements = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (categoryFilters != null) p["categoryFilters"] = new JArray(categoryFilters);
        if (selectedElementsOnly != null) p["selectedElementsOnly"] = selectedElementsOnly;
        if (maxResults != null) p["maxResults"] = maxResults;
        if (maxElements != null) p["maxElements"] = maxElements;
        var result = await revit.ExecuteAsync("get_material_quantities", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "delete_material"), Description("Delete a material from the project by ID or name.")]
    public static async Task<string> DeleteMaterial(
        RevitConnectionManager revit,
        [Description("Material element ID (alternative to materialName)")] long? materialId = null,
        [Description("Material name (alternative to materialId)")] string? materialName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (materialId != null) p["materialId"] = materialId;
        if (materialName != null) p["materialName"] = materialName;
        var result = await revit.ExecuteAsync("delete_material", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "duplicate_material"), Description("Duplicate an existing material with a new name.")]
    public static async Task<string> DuplicateMaterial(
        RevitConnectionManager revit,
        [Description("New material name")] string newName,
        [Description("Source material element ID (alternative to sourceMaterialName)")] long? sourceMaterialId = null,
        [Description("Source material name (alternative to sourceMaterialId)")] string? sourceMaterialName = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["newName"] = newName };
        if (sourceMaterialId != null) p["sourceMaterialId"] = sourceMaterialId;
        if (sourceMaterialName != null) p["sourceMaterialName"] = sourceMaterialName;
        var result = await revit.ExecuteAsync("duplicate_material", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "duplicate_family_type"), Description("Duplicate a loadable family type with a new name and optional parameter overrides.")]
    public static async Task<string> DuplicateFamilyType(
        RevitConnectionManager revit,
        [Description("New type name")] string newName,
        [Description("Source type element ID (alternative to sourceTypeName + familyName)")] long? sourceTypeId = null,
        [Description("Source type name (used with familyName)")] string? sourceTypeName = null,
        [Description("Family name (used with sourceTypeName)")] string? familyName = null,
        [Description("Parameter overrides as JSON object: {paramName: value, ...}")] string? parameterOverrides = null,
        CancellationToken ct = default)
    {
        var p = new JObject { ["newName"] = newName };
        if (sourceTypeId != null) p["sourceTypeId"] = sourceTypeId;
        if (sourceTypeName != null) p["sourceTypeName"] = sourceTypeName;
        if (familyName != null) p["familyName"] = familyName;
        if (parameterOverrides != null) p["parameterOverrides"] = JObject.Parse(parameterOverrides);
        var result = await revit.ExecuteAsync("duplicate_family_type", p, ct);
        return result.ToString();
    }
}
