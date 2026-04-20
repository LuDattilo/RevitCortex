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
/// Comprehensive family audit with health scores, unused detection, in-place identification,
/// and instance counts. Merges audit_families and check_family_health.
/// </summary>
public class AuditFamiliesTool : ICortexTool
{
    public string Name => "audit_families";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Family audit with instance counts, unused detection, and in-place identification. Covers loadable (.rfa) families by default; set includeSystemFamilies=true to also list system-family types (wall/floor/roof/ceiling types).";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var includeUnused = input["includeUnused"]?.Value<bool>() ?? true;
        var categoryFilter = input["categoryFilter"]?.Value<string>();
        var sortBy = input["sortBy"]?.Value<string>() ?? "instance_count";
        var includeSystemFamilies = input["includeSystemFamilies"]?.Value<bool>() ?? false;

        try
        {
            ElementId? filterCatId = null;
            if (!string.IsNullOrEmpty(categoryFilter))
            {
                filterCatId = Utilities.CategoryResolver.ResolveToId(doc, categoryFilter!);
                if (filterCatId == null || filterCatId.Equals(ElementId.InvalidElementId)) filterCatId = null;
            }

            // ── Loadable families ─────────────────────────────────────────
            var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>();
            if (filterCatId != null)
                // Use .Equals() because ElementId does NOT overload == on net48 (R2023/R2024) —
                // reference equality there silently excludes every family.
                families = families.Where(f => f.FamilyCategory?.Id.Equals(filterCatId) == true);

            // Count instances per family (loadable)
            var instanceCounts = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e is FamilyInstance)
                .Cast<FamilyInstance>()
                .GroupBy(fi => fi.Symbol?.Family?.Id)
                .Where(g => g.Key != null)
                .ToDictionary(g => g.Key!, g => g.Count());

            var familyList = families.Select(f =>
            {
                var count = instanceCounts.TryGetValue(f.Id, out var cnt) ? cnt : 0;
                return new
                {
                    id = ToolHelpers.GetElementIdValue(f.Id),
                    name = f.Name,
                    category = f.FamilyCategory?.Name,
                    kind = "loadable",
                    isInPlace = f.IsInPlace,
                    isEditable = f.IsEditable,
                    instanceCount = count,
                    typeCount = f.GetFamilySymbolIds().Count,
                    isUnused = count == 0 && f.IsEditable
                };
            }).ToList();

            // ── System families (opt-in) ─────────────────────────────────
            // In Revit a "system family" is not a Family element; it's the implicit
            // grouping of all ElementTypes sharing a subclass (WallType, FloorType, ...).
            // We surface each subclass+category as one row, with a typeCount and an
            // aggregated instanceCount obtained by walking every instance of the category.
            // Note: we enumerate instances via OfCategory(OST_*) rather than OfClass(instanceClass)
            // because RoofBase is abstract (cannot be passed to OfClass) and because the OST
            // filter captures all concrete subclasses (FootPrintRoof + ExtrusionRoof, etc.).
            if (includeSystemFamilies)
            {
                var systemGroups = new (Type typeClass, BuiltInCategory bic, string name)[]
                {
                    (typeof(WallType),     BuiltInCategory.OST_Walls,    "Walls"),
                    (typeof(FloorType),    BuiltInCategory.OST_Floors,   "Floors"),
                    (typeof(RoofType),     BuiltInCategory.OST_Roofs,    "Roofs"),
                    (typeof(CeilingType),  BuiltInCategory.OST_Ceilings, "Ceilings"),
                };

                foreach (var (typeClass, bic, displayName) in systemGroups)
                {
                    var types = new FilteredElementCollector(doc).OfClass(typeClass).Cast<ElementType>().ToList();
                    if (filterCatId != null)
                        // .Equals() — see comment at loadable filter above.
                        types = types.Where(t => t.Category?.Id.Equals(filterCatId) == true).ToList();
                    if (types.Count == 0) continue;

                    // Count instances per type id by walking all elements of the category.
                    var typeInstanceCounts = new FilteredElementCollector(doc)
                        .OfCategory(bic)
                        .WhereElementIsNotElementType()
                        .GroupBy(e => e.GetTypeId())
                        .Where(g => g.Key != ElementId.InvalidElementId)
                        .ToDictionary(g => g.Key, g => g.Count());

                    // Group rows by category so the output mirrors loadable families:
                    // one row = one system "family" (category), with typeCount and instance sum.
                    var byCat = types.GroupBy(t => t.Category?.Name ?? displayName);
                    foreach (var catGroup in byCat)
                    {
                        int totalInstances = catGroup.Sum(t =>
                            typeInstanceCounts.TryGetValue(t.Id, out var c) ? c : 0);

                        familyList.Add(new
                        {
                            id = (long)-1,   // synthetic row — no Family element backs it
                            name = $"[System] {catGroup.Key}",
                            category = (string?)catGroup.Key,
                            kind = "system",
                            isInPlace = false,
                            isEditable = true,
                            instanceCount = totalInstances,
                            typeCount = catGroup.Count(),
                            isUnused = totalInstances == 0
                        });
                    }
                }
            }

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
                loadableCount = familyList.Count(f => f.kind == "loadable"),
                systemCount = familyList.Count(f => f.kind == "system"),
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
                includeSystemFamilies,
                families = familyList.Take(200).ToList()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
