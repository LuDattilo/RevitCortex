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
                // Skip model categories that can never carry materials (rooms, grids,
                // levels, lines, links...): GetMaterialIds on 100K+ elements blocks the
                // UI thread for seconds. View-specific elements are kept because detail
                // items can reference materials even though their category reports no
                // material quantities — a false "unused" here would get deleted.
                var cat = elem.Category;
                if (cat == null) continue;
                if (!cat.HasMaterialQuantities && !elem.ViewSpecific) continue;
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
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();
                int deletedTypes = 0, deletedMaterials = 0, deletedTemplates = 0, deletedFilters = 0;

                // Accumulate per-item delete failures instead of swallowing them silently:
                // an element may be in use/locked/pinned and refuse deletion. Surfacing this
                // lets the caller distinguish "deleted N, 0 failed" from "deleted N, M failed".
                var failures = new List<object>();
                void TryDelete(long id, string kind, Action onSuccess)
                {
                    try { doc.Delete(ToolHelpers.ToElementId(id)); onSuccess(); }
                    catch (Exception ex) { failures.Add(new { id, kind, reason = ex.Message }); }
                }

                foreach (var ut in unusedTypes)
                    TryDelete(ut.id, "type", () => deletedTypes++);
                foreach (var um in unusedMaterials)
                    TryDelete(um.id, "material", () => deletedMaterials++);
                foreach (var vt in unusedViewTemplates)
                    TryDelete((long)vt.id, "viewTemplate", () => deletedTemplates++);
                foreach (var f in unusedFilters)
                    TryDelete((long)f.id, "filter", () => deletedFilters++);

                if (tx.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
                return CortexResult<object>.Ok(new
                {
                    dryRun = false,
                    deletedTypes,
                    deletedMaterials,
                    deletedViewTemplates = deletedTemplates,
                    deletedFilters,
                    totalDeleted = deletedTypes + deletedMaterials + deletedTemplates + deletedFilters,
                    failedCount = failures.Count,
                    failures = failures.Take(50).ToList()
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
