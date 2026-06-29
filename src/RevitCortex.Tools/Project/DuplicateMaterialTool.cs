using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Duplicates an existing material with a new name, copying all properties and assets.
/// </summary>
[ToolSafety(false, false)]
public class DuplicateMaterialTool : ICortexTool
{
    public string Name => "duplicate_material";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Duplicates an existing material with a new name, copying color, class, transparency, and optionally appearance/structural/thermal assets.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sourceMaterialId   = input["sourceMaterialId"]?.Value<long?>();
        var sourceMaterialName = input["sourceMaterialName"]?.Value<string>();
        var newName            = input["newName"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "newName is required",
                suggestion: "Provide a name for the duplicate material");

        if (sourceMaterialId == null && string.IsNullOrWhiteSpace(sourceMaterialName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Provide sourceMaterialId or sourceMaterialName",
                suggestion: "Use get_materials to find the source material");

        try
        {
            Material? source = null;

            if (sourceMaterialId.HasValue)
            {
#if REVIT2024_OR_GREATER
                source = doc.GetElement(new ElementId(sourceMaterialId.Value)) as Material;
#else
                source = doc.GetElement(new ElementId((int)sourceMaterialId.Value)) as Material;
#endif
            }

            if (source == null && !string.IsNullOrWhiteSpace(sourceMaterialName))
            {
                source = new FilteredElementCollector(doc)
                    .OfClass(typeof(Material))
                    .Cast<Material>()
                    .FirstOrDefault(m => m.Name.Equals(sourceMaterialName, StringComparison.OrdinalIgnoreCase));
            }

            if (source == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"Source material not found (id={sourceMaterialId}, name={sourceMaterialName})",
                    suggestion: "Use get_materials to list available materials");

            ElementId newMatId;

            using (var tx = new Transaction(doc, "RevitCortex: Duplicate Material"))
            {
                tx.Start();

                newMatId = Material.Create(doc, newName);
                var newMat = doc.GetElement(newMatId) as Material;
                if (newMat == null)
                {
                    tx.RollBack();
                    return CortexResult<object>.Fail(CortexErrorCode.Unknown, "Failed to create duplicate material");
                }

                // Copy basic properties
                newMat.MaterialClass = source.MaterialClass;
                newMat.MaterialCategory = source.MaterialCategory;
                if (source.Color != null && source.Color.IsValid)
                    newMat.Color = source.Color;
                newMat.Transparency = source.Transparency;
                newMat.Shininess = source.Shininess;
                newMat.Smoothness = source.Smoothness;

                // Copy appearance asset
                if (source.AppearanceAssetId != ElementId.InvalidElementId)
                {
                    try
                    {
                        var srcAsset = doc.GetElement(source.AppearanceAssetId) as AppearanceAssetElement;
                        if (srcAsset != null)
                        {
                            var dupAsset = srcAsset.Duplicate($"{newName}_Appearance");
                            newMat.AppearanceAssetId = dupAsset.Id;
                        }
                    }
                    catch { /* appearance asset duplication not critical */ }
                }

                // Copy structural asset
                if (source.StructuralAssetId != ElementId.InvalidElementId)
                {
                    try
                    {
                        var srcPropSet = doc.GetElement(source.StructuralAssetId) as PropertySetElement;
                        if (srcPropSet != null)
                        {
                            var dupPropSet = PropertySetElement.Create(doc, srcPropSet.GetStructuralAsset());
                            newMat.StructuralAssetId = dupPropSet.Id;
                        }
                    }
                    catch { /* structural asset copy not critical */ }
                }

                // Copy thermal asset
                if (source.ThermalAssetId != ElementId.InvalidElementId)
                {
                    try
                    {
                        var srcPropSet = doc.GetElement(source.ThermalAssetId) as PropertySetElement;
                        if (srcPropSet != null)
                        {
                            var dupPropSet = PropertySetElement.Create(doc, srcPropSet.GetThermalAsset());
                            newMat.ThermalAssetId = dupPropSet.Id;
                        }
                    }
                    catch { /* thermal asset copy not critical */ }
                }

                tx.Commit();
            }

            long newIdValue;
#if REVIT2024_OR_GREATER
            newIdValue = newMatId.Value;
#else
            newIdValue = (long)newMatId.IntegerValue;
#endif

            return CortexResult<object>.Ok(new
            {
                materialId = newIdValue,
                name = newName,
                sourceName = source.Name,
                message = $"Material '{source.Name}' duplicated as '{newName}'"
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to duplicate material: {ex.Message}");
        }
    }
}
