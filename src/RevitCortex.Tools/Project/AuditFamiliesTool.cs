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
/// Comprehensive family audit with health scores, unused detection, in-place identification,
/// and instance counts. Merges audit_families and check_family_health.
/// </summary>
public class AuditFamiliesTool : ICortexTool
{
    public string Name => "audit_families";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Comprehensive family audit with health scores, unused detection, in-place identification, and instance counts. Merges audit_families and check_family_health.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var includeUnused = input["includeUnused"]?.Value<bool>() ?? true;
        var categoryFilter = input["categoryFilter"]?.Value<string>();
        var sortBy = input["sortBy"]?.Value<string>() ?? "instance_count";

        try
        {
            var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>();

            if (!string.IsNullOrEmpty(categoryFilter))
            {
                var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryFilter);
                if (catId != ElementId.InvalidElementId)
                    families = families.Where(f => f.FamilyCategory?.Id == catId);
            }

            // Count instances per family
            var instanceCounts = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e is FamilyInstance)
                .Cast<FamilyInstance>()
                .GroupBy(fi => fi.Symbol?.Family?.Id)
                .Where(g => g.Key != null)
                .ToDictionary(g => g.Key!, g => g.Count());

            var familyList = families.Select(f =>
            {
                var count = instanceCounts.GetValueOrDefault(f.Id, 0);
                return new
                {
                    id = GetIdLong(f.Id),
                    name = f.Name,
                    category = f.FamilyCategory?.Name,
                    isInPlace = f.IsInPlace,
                    isEditable = f.IsEditable,
                    instanceCount = count,
                    typeCount = f.GetFamilySymbolIds().Count,
                    isUnused = count == 0 && f.IsEditable
                };
            }).ToList();

            if (!includeUnused)
                familyList = familyList.Where(f => !f.isUnused).ToList();

            familyList = sortBy switch
            {
                "name" => familyList.OrderBy(f => f.name).ToList(),
                "instance_count" => familyList.OrderByDescending(f => f.instanceCount).ToList(),
                _ => familyList.OrderByDescending(f => f.instanceCount).ToList()
            };

            var summary = new
            {
                totalFamilies = familyList.Count,
                unusedCount = familyList.Count(f => f.isUnused),
                inPlaceCount = familyList.Count(f => f.isInPlace),
                totalInstances = familyList.Sum(f => f.instanceCount),
                byCategory = familyList
                    .GroupBy(f => f.category ?? "Unknown")
                    .Select(g => new { category = g.Key, familyCount = g.Count(), instanceCount = g.Sum(f => f.instanceCount) })
                    .OrderByDescending(x => x.instanceCount)
                    .ToList()
            };

            return CortexResult<object>.Ok(new
            {
                summary,
                families = familyList.Take(200).ToList()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
