using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Finds and removes unused families, types, and materials.
/// </summary>
public class PurgeUnusedTool : ICortexTool
{
    public string Name => "purge_unused";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Finds and removes unused families, types, and materials.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var dryRun = input["dryRun"]?.Value<bool>() ?? true;
        var maxElements = input["maxElements"]?.Value<int>() ?? 500;

        try
        {
            // Find unused family symbols (types with no instances)
            var usedTypeIds = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.GetTypeId() != ElementId.InvalidElementId)
                .Select(e => e.GetTypeId())
                .Distinct()
                .ToHashSet();

            var unusedTypes = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Where(t => !usedTypeIds.Contains(t.Id))
                .Where(t => t is FamilySymbol)
                .Take(maxElements)
                .Select(t => new { id = ToolHelpers.GetElementIdValue(t.Id), name = t.Name, category = t.Category?.Name ?? "Unknown" })
                .ToList();

            // Find unused materials
            var usedMaterialIds = new HashSet<ElementId>();
            foreach (var elem in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                foreach (var matId in elem.GetMaterialIds(false))
                    usedMaterialIds.Add(matId);
            }

            var unusedMaterials = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .Where(m => !usedMaterialIds.Contains(m.Id))
                .Take(maxElements)
                .Select(m => new { id = ToolHelpers.GetElementIdValue(m.Id), name = m.Name })
                .ToList();

            if (!dryRun)
            {
                var purgeableCount = unusedTypes.Count + unusedMaterials.Count;
                if (!session.RequestConfirmation("purge", purgeableCount))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                using var tx = new Transaction(doc, "RevitCortex: Purge Unused");
                tx.Start();
                int deletedTypes = 0, deletedMaterials = 0;

                foreach (var ut in unusedTypes)
                {
#if REVIT2024_OR_GREATER
                    try { doc.Delete(new ElementId(ut.id)); deletedTypes++; } catch { }
#else
                    try { doc.Delete(new ElementId((int)ut.id)); deletedTypes++; } catch { }
#endif
                }

                foreach (var um in unusedMaterials)
                {
#if REVIT2024_OR_GREATER
                    try { doc.Delete(new ElementId(um.id)); deletedMaterials++; } catch { }
#else
                    try { doc.Delete(new ElementId((int)um.id)); deletedMaterials++; } catch { }
#endif
                }

                tx.Commit();
                return CortexResult<object>.Ok(new
                {
                    dryRun = false,
                    deletedTypes,
                    deletedMaterials,
                    totalDeleted = deletedTypes + deletedMaterials
                });
            }

            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                unusedTypeCount = unusedTypes.Count,
                unusedMaterialCount = unusedMaterials.Count,
                unusedTypes = unusedTypes.Take(50).ToList(),
                unusedMaterials = unusedMaterials.Take(50).ToList()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
