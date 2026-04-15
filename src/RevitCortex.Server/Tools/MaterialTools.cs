using System.ComponentModel;
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
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_materials", new JObject(), ct);
        return result.ToString();
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
        [Description("Type element ID")] int? typeId = null,
        [Description("Type name")] string? typeName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (typeId != null) p["typeId"] = typeId;
        if (typeName != null) p["typeName"] = typeName;
        var result = await revit.ExecuteAsync("get_compound_structure", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "set_compound_structure"), Description("Set or replace compound structure layers on a wall/floor/roof/ceiling type.")]
    public static async Task<string> SetCompoundStructure(
        RevitConnectionManager revit,
        [Description("Type element ID")] int typeId,
        [Description("Layer definitions as JSON array: [{function, materialName, widthMm}]")] string layers,
        [Description("Preview changes without applying")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["typeId"] = typeId,
            ["dryRun"] = dryRun,
            ["layers"] = JArray.Parse(layers),
        };
        var result = await revit.ExecuteAsync("set_compound_structure", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_material_properties"), Description("Get detailed material properties including structural and thermal data")]
    public static async Task<string> GetMaterialProperties(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_material_properties", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_material_quantities"), Description("Calculate material area and volume across elements")]
    public static async Task<string> GetMaterialQuantities(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_material_quantities", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "delete_material"), Description("Delete a material from the project")]
    public static async Task<string> DeleteMaterial(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("delete_material", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "duplicate_material"), Description("Duplicate an existing material with a new name")]
    public static async Task<string> DuplicateMaterial(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("duplicate_material", JObject.Parse(data), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "duplicate_family_type"), Description("Duplicate a loadable family type with a new name")]
    public static async Task<string> DuplicateFamilyType(
        RevitConnectionManager revit,
        [Description("JSON parameters")] string data,
        CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("duplicate_family_type", JObject.Parse(data), ct);
        return result.ToString();
    }
}
