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
/// Retrieves detailed properties for a specific material including
/// structural and thermal asset data.
/// </summary>
[ToolSafety(true, false)]
public class GetMaterialPropertiesTool : ICortexTool
{
    public string Name => "get_material_properties";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Retrieves detailed properties for a specific material including structural and thermal asset data.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var materialId   = input["materialId"]?.Value<long?>();
        var materialName = input["materialName"]?.Value<string>();

        if (materialId == null && string.IsNullOrWhiteSpace(materialName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Provide either materialId or materialName",
                suggestion: "Use get_materials to find material IDs first");

        try
        {
            Material? material = null;

            if (materialId.HasValue)
            {
#if REVIT2024_OR_GREATER
                material = doc.GetElement(new ElementId(materialId.Value)) as Material;
#else
                material = doc.GetElement(new ElementId((int)materialId.Value)) as Material;
#endif
            }

            if (material == null && !string.IsNullOrWhiteSpace(materialName))
            {
                material = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => m.Name.Equals(materialName, StringComparison.OrdinalIgnoreCase));
            }

            if (material == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Material not found (id={materialId}, name={materialName})",
                    suggestion: "Use get_materials to list available materials");

            var result = new Dictionary<string, object?>
            {
#if REVIT2024_OR_GREATER
                ["id"] = material.Id.Value,
#else
                ["id"] = (long)material.Id.IntegerValue,
#endif
                ["name"]             = material.Name,
                ["materialClass"]    = material.MaterialClass,
                ["materialCategory"] = material.MaterialCategory,
                ["color"]            = FormatColor(material.Color),
                ["transparency"]     = material.Transparency,
                ["shininess"]        = material.Shininess,
                ["smoothness"]       = material.Smoothness
            };

            // Structural properties
            if (material.StructuralAssetId != ElementId.InvalidElementId)
            {
                try
                {
                    var propSet = doc.GetElement(material.StructuralAssetId) as PropertySetElement;
                    var asset = propSet?.GetStructuralAsset();
                    if (asset != null)
                    {
                        var structural = new Dictionary<string, object?>();
                        TrySet(structural, "density", () => asset.Density);
                        TrySet(structural, "youngModulusX", () => asset.YoungModulus.X);
                        TrySet(structural, "youngModulusY", () => asset.YoungModulus.Y);
                        TrySet(structural, "youngModulusZ", () => asset.YoungModulus.Z);
                        TrySet(structural, "poissonRatioX", () => asset.PoissonRatio.X);
                        TrySet(structural, "poissonRatioY", () => asset.PoissonRatio.Y);
                        TrySet(structural, "poissonRatioZ", () => asset.PoissonRatio.Z);
                        TrySet(structural, "shearModulusX", () => asset.ShearModulus.X);
                        TrySet(structural, "shearModulusY", () => asset.ShearModulus.Y);
                        TrySet(structural, "shearModulusZ", () => asset.ShearModulus.Z);
                        TrySet(structural, "thermalExpansionCoefficientX", () => asset.ThermalExpansionCoefficient.X);
                        TrySet(structural, "thermalExpansionCoefficientY", () => asset.ThermalExpansionCoefficient.Y);
                        TrySet(structural, "thermalExpansionCoefficientZ", () => asset.ThermalExpansionCoefficient.Z);
                        TrySet(structural, "behavior", () => asset.Behavior.ToString());
                        TrySet(structural, "minimumYieldStress", () => asset.MinimumYieldStress);
                        TrySet(structural, "minimumTensileStrength", () => asset.MinimumTensileStrength);
                        TrySet(structural, "subClass", () => asset.SubClass.ToString());
                        result["structuralProperties"] = structural;
                    }
                }
                catch (Exception ex)
                {
                    result["structuralProperties"] = new { error = ex.Message };
                }
            }

            // Thermal properties
            if (material.ThermalAssetId != ElementId.InvalidElementId)
            {
                try
                {
                    var propSet = doc.GetElement(material.ThermalAssetId) as PropertySetElement;
                    var asset = propSet?.GetThermalAsset();
                    if (asset != null)
                    {
                        var thermal = new Dictionary<string, object?>();
                        TrySet(thermal, "thermalConductivity", () => asset.ThermalConductivity);
                        TrySet(thermal, "specificHeat", () => asset.SpecificHeat);
                        TrySet(thermal, "density", () => asset.Density);
                        TrySet(thermal, "emissivity", () => asset.Emissivity);
                        TrySet(thermal, "permeability", () => asset.Permeability);
                        TrySet(thermal, "porosity", () => asset.Porosity);
                        result["thermalProperties"] = thermal;
                    }
                }
                catch (Exception ex)
                {
                    result["thermalProperties"] = new { error = ex.Message };
                }
            }

            return CortexResult<object>.Ok(result);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get material properties: {ex.Message}");
        }
    }

    private static void TrySet(Dictionary<string, object?> dict, string key, Func<object> getter)
    {
        try { dict[key] = getter(); }
        catch { /* property may not exist for this material type */ }
    }

    private static string? FormatColor(Color? color)
    {
        if (color == null || !color.IsValid) return null;
        return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    }
}
