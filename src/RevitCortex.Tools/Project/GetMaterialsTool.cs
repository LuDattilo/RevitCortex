using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Lists all materials in the project with optional filtering by material class or name.
/// </summary>
public class GetMaterialsTool : ICortexTool
{
    public string Name => "get_materials";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists all materials in the project with optional filtering by material class or name.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var materialClass = input["materialClass"]?.Value<string>() ?? "";
        var nameFilter    = input["nameFilter"]?.Value<string>() ?? "";

        try
        {
            var allMaterials = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>();

            if (!string.IsNullOrEmpty(materialClass))
                allMaterials = allMaterials.Where(m =>
                    string.Equals(m.MaterialClass, materialClass, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(nameFilter))
                allMaterials = allMaterials.Where(m =>
                    m.Name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0);

            var materials = allMaterials.Select(m => new
            {
#if REVIT2024_OR_GREATER
                id = m.Id.Value,
#else
                id = (long)m.Id.IntegerValue,
#endif
                name             = m.Name,
                materialClass    = m.MaterialClass,
                materialCategory = m.MaterialCategory,
                color            = FormatColor(m.Color),
                transparency     = m.Transparency,
                shininess        = m.Shininess,
                smoothness       = m.Smoothness,
                hasAppearanceAsset = m.AppearanceAssetId != ElementId.InvalidElementId,
                hasStructuralAsset = m.StructuralAssetId != ElementId.InvalidElementId,
                hasThermalAsset    = m.ThermalAssetId != ElementId.InvalidElementId
            }).ToList();

            return CortexResult<object>.Ok(new
            {
                materialCount = materials.Count,
                materials
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get materials: {ex.Message}");
        }
    }

    private static string? FormatColor(Color? color)
    {
        if (color == null || !color.IsValid) return null;
        return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }
}
