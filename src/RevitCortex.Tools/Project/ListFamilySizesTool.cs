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
/// Lists families with type and instance counts, sorted by instance count, type count, or name.
/// Useful for identifying bloated/unused families.
/// </summary>
public class ListFamilySizesTool : ICortexTool
{
    public string Name => "list_family_sizes";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists families with type and instance counts, sorted by instance count, type count, or name. Useful for identifying bloated/unused families.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var limit      = input["limit"]?.Value<int>() ?? 50;
        var sortBy     = input["sortBy"]?.Value<string>() ?? "instanceCount";
        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();

        try
        {
            var families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .ToList();

            // Category filter
            if (categories.Count > 0)
            {
                var catIds = new HashSet<long>();
                foreach (var cat in categories)
                {
                    string bicName = cat.StartsWith("OST_") ? cat : "OST_" + cat;
                    if (Enum.TryParse<BuiltInCategory>(bicName, true, out var bic))
                    {
#if REVIT2024_OR_GREATER
                        catIds.Add(new ElementId(bic).Value);
#else
                        catIds.Add((long)new ElementId(bic).IntegerValue);
#endif
                    }
                }
                if (catIds.Count > 0)
                {
                    families = families.Where(f =>
                    {
                        if (f.FamilyCategory == null) return false;
#if REVIT2024_OR_GREATER
                        return catIds.Contains(f.FamilyCategory.Id.Value);
#else
                        return catIds.Contains((long)f.FamilyCategory.Id.IntegerValue);
#endif
                    }).ToList();
                }
            }

            // Instance count lookup (single pass)
            var instanceCountByTypeId = new Dictionary<long, int>();
            foreach (var fi in new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>())
            {
                if (fi.Symbol == null) continue;
#if REVIT2024_OR_GREATER
                long typeIdValue = fi.Symbol.Id.Value;
#else
                long typeIdValue = (long)fi.Symbol.Id.IntegerValue;
#endif
                instanceCountByTypeId.TryGetValue(typeIdValue, out int existing);
                instanceCountByTypeId[typeIdValue] = existing + 1;
            }

            // Build family info
            var familyInfos = families.Select(f =>
            {
                var typeIds = f.GetFamilySymbolIds();
                int instanceCount = 0;
                foreach (var typeId in typeIds)
                {
#if REVIT2024_OR_GREATER
                    long key = typeId.Value;
#else
                    long key = (long)typeId.IntegerValue;
#endif
                    if (instanceCountByTypeId.TryGetValue(key, out int count))
                        instanceCount += count;
                }

                return new
                {
#if REVIT2024_OR_GREATER
                    familyId = f.Id.Value,
#else
                    familyId = (long)f.Id.IntegerValue,
#endif
                    familyName    = f.Name,
                    category      = f.FamilyCategory?.Name ?? "Unknown",
                    typeCount     = typeIds.Count,
                    instanceCount,
                    isInPlace     = f.IsInPlace,
                    isEditable    = f.IsEditable
                };
            }).ToList();

            // Sort
            var sorted = sortBy.ToLowerInvariant() switch
            {
                "typecount" => familyInfos.OrderByDescending(f => f.typeCount).ToList(),
                "name"      => familyInfos.OrderBy(f => f.familyName).ToList(),
                _           => familyInfos.OrderByDescending(f => f.instanceCount).ToList()
            };

            var limited = sorted.Take(limit).ToList();

            return CortexResult<object>.Ok(new
            {
                totalFamilies  = familyInfos.Count,
                totalInstances = familyInfos.Sum(f => f.instanceCount),
                returnedCount  = limited.Count,
                truncated      = familyInfos.Count > limit,
                sortedBy       = sortBy,
                families       = limited
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to list family sizes: {ex.Message}");
        }
    }
}
