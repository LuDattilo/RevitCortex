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
    public string Description => "Finds and removes unused families/types, materials, and (optionally) unreferenced view templates and view filters.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var dryRun = input["dryRun"]?.Value<bool>() ?? true;
        var maxElements = input["maxElements"]?.Value<int>() ?? 500;
        var includeViewTemplates = input["includeViewTemplates"]?.Value<bool>() ?? true;
        var includeFilters = input["includeFilters"]?.Value<bool>() ?? true;

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

            // Unused view templates: IsTemplate views not referenced by any non-template view.
            var unusedViewTemplates = new List<dynamic>();
            if (includeViewTemplates)
            {
                var allViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
                var referencedTemplateIds = allViews
                    .Where(v => !v.IsTemplate && v.ViewTemplateId != ElementId.InvalidElementId)
                    .Select(v => v.ViewTemplateId)
                    .ToHashSet();
                unusedViewTemplates = allViews
                    .Where(v => v.IsTemplate && !referencedTemplateIds.Contains(v.Id))
                    .Take(maxElements)
                    .Select(v => (dynamic)new { id = ToolHelpers.GetElementIdValue(v.Id), name = v.Name })
                    .ToList();
            }

            // Unused view filters: ParameterFilterElement not applied to any view.
            var unusedFilters = new List<dynamic>();
            if (includeFilters)
            {
                var appliedFilterIds = new HashSet<ElementId>();
                foreach (var v in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                {
                    try
                    {
                        if (!v.AreGraphicsOverridesAllowed()) continue;
                        foreach (var fid in v.GetFilters())
                            appliedFilterIds.Add(fid);
                    }
                    catch { /* some views reject GetFilters */ }
                }
                unusedFilters = new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterFilterElement))
                    .Where(f => !appliedFilterIds.Contains(f.Id))
                    .Take(maxElements)
                    .Select(f => (dynamic)new { id = ToolHelpers.GetElementIdValue(f.Id), name = f.Name })
                    .ToList();
            }

            if (!dryRun)
            {
                var purgeableCount = unusedTypes.Count + unusedMaterials.Count
                    + unusedViewTemplates.Count + unusedFilters.Count;
                if (!session.RequestConfirmation("purge", purgeableCount))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                using var tx = new Transaction(doc, "RevitCortex: Purge Unused");
                tx.Start();
                int deletedTypes = 0, deletedMaterials = 0, deletedTemplates = 0, deletedFilters = 0;

                foreach (var ut in unusedTypes)
                {
                    try { doc.Delete(ToolHelpers.ToElementId(ut.id)); deletedTypes++; } catch { }
                }
                foreach (var um in unusedMaterials)
                {
                    try { doc.Delete(ToolHelpers.ToElementId(um.id)); deletedMaterials++; } catch { }
                }
                foreach (var vt in unusedViewTemplates)
                {
                    try { doc.Delete(ToolHelpers.ToElementId((long)vt.id)); deletedTemplates++; } catch { }
                }
                foreach (var f in unusedFilters)
                {
                    try { doc.Delete(ToolHelpers.ToElementId((long)f.id)); deletedFilters++; } catch { }
                }

                tx.Commit();
                return CortexResult<object>.Ok(new
                {
                    dryRun = false,
                    deletedTypes,
                    deletedMaterials,
                    deletedViewTemplates = deletedTemplates,
                    deletedFilters,
                    totalDeleted = deletedTypes + deletedMaterials + deletedTemplates + deletedFilters
                });
            }

            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                unusedTypeCount = unusedTypes.Count,
                unusedMaterialCount = unusedMaterials.Count,
                unusedViewTemplateCount = unusedViewTemplates.Count,
                unusedFilterCount = unusedFilters.Count,
                unusedTypes = unusedTypes.Take(50).ToList(),
                unusedMaterials = unusedMaterials.Take(50).ToList(),
                unusedViewTemplates = unusedViewTemplates.Take(50).ToList(),
                unusedFilters = unusedFilters.Take(50).ToList()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
