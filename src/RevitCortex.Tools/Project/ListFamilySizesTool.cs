using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Lists families with type and instance counts, sorted by instance count, type count, or name.
/// Useful for identifying bloated/unused families.
/// </summary>
[ToolSafety(true, false)]
public class ListFamilySizesTool : ICortexTool
{
    public string Name => "list_family_sizes";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists families with type/instance counts and, optionally, the family file size in KB (measured by exporting each family to a temp file). Sortable by instanceCount, typeCount, name, or sizeKB. Useful for identifying bloated, unused, or oversized families.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var limit       = input["limit"]?.Value<int>() ?? 50;
        var sortBy      = input["sortBy"]?.Value<string>() ?? "instanceCount";
        var categories  = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        // Computing real file size is expensive (one EditFamily + Save per family) so it's opt-in.
        // Defaulted off to keep the existing fast path intact for most callers.
        var includeSize = input["includeSize"]?.Value<bool>() ?? false;

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
                    var catId = CategoryResolver.ResolveToId(doc, cat);
                    if (catId != null && catId != ElementId.InvalidElementId)
                    {
#if REVIT2024_OR_GREATER
                        catIds.Add(catId.Value);
#else
                        catIds.Add((long)catId.IntegerValue);
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

            // Build family info (size calc deferred so we can apply it only to the limited subset
            // after sorting — except when sortBy=sizeKB, which forces full computation up front).
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

                return new FamilyInfo
                {
#if REVIT2024_OR_GREATER
                    FamilyId = f.Id.Value,
#else
                    FamilyId = (long)f.Id.IntegerValue,
#endif
                    Family        = f,
                    FamilyName    = f.Name,
                    Category      = f.FamilyCategory?.Name ?? "Unknown",
                    TypeCount     = typeIds.Count,
                    InstanceCount = instanceCount,
                    IsInPlace     = f.IsInPlace,
                    IsEditable    = f.IsEditable,
                    SizeKB        = null // populated below when includeSize=true
                };
            }).ToList();

            var normalizedSort = sortBy.ToLowerInvariant();
            // Compute sizes BEFORE sorting only when sort needs them, otherwise compute after
            // the sort-and-limit pass so we touch at most `limit` families instead of all.
            if (includeSize && normalizedSort == "sizekb")
            {
                foreach (var fi in familyInfos)
                    fi.SizeKB = TryGetFamilySizeKB(doc, fi.Family);
            }

            // Sort
            var sorted = normalizedSort switch
            {
                "typecount" => familyInfos.OrderByDescending(f => f.TypeCount).ToList(),
                "name"      => familyInfos.OrderBy(f => f.FamilyName).ToList(),
                "sizekb"    => familyInfos.OrderByDescending(f => f.SizeKB ?? -1).ToList(),
                _           => familyInfos.OrderByDescending(f => f.InstanceCount).ToList()
            };

            var limited = sorted.Take(limit).ToList();

            if (includeSize && normalizedSort != "sizekb")
            {
                foreach (var fi in limited)
                    if (fi.SizeKB == null)
                        fi.SizeKB = TryGetFamilySizeKB(doc, fi.Family);
            }

            return CortexResult<object>.Ok(new
            {
                totalFamilies  = familyInfos.Count,
                totalInstances = familyInfos.Sum(f => f.InstanceCount),
                returnedCount  = limited.Count,
                truncated      = familyInfos.Count > limit,
                sortedBy       = sortBy,
                sizeMeasured   = includeSize,
                families       = limited.Select(f => new
                {
                    familyId      = f.FamilyId,
                    familyName    = f.FamilyName,
                    category      = f.Category,
                    typeCount     = f.TypeCount,
                    instanceCount = f.InstanceCount,
                    isInPlace     = f.IsInPlace,
                    isEditable    = f.IsEditable,
                    sizeKB        = f.SizeKB
                })
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to list family sizes: {ex.Message}");
        }
    }

    /// <summary>
    /// Measure a family's file size by opening its FamilyDocument, saving to a temp file,
    /// reading the size, and cleaning up. Returns null if the family can't be edited
    /// (in-place families, system families, or families locked by Revit).
    /// </summary>
    private static long? TryGetFamilySizeKB(Document hostDoc, Family family)
    {
        if (family == null || !family.IsEditable || family.IsInPlace) return null;

        string? tempPath = null;
        Document? famDoc = null;
        try
        {
            famDoc = hostDoc.EditFamily(family);
            if (famDoc == null) return null;

            tempPath = Path.Combine(Path.GetTempPath(),
                $"rcfam-{Guid.NewGuid():N}.rfa");

            var saveOpts = new SaveAsOptions { OverwriteExistingFile = true };
            famDoc.SaveAs(tempPath, saveOpts);

            var size = new FileInfo(tempPath).Length;
            return (long)Math.Round(size / 1024.0);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { famDoc?.Close(false); } catch { /* best-effort cleanup */ }
            if (tempPath != null && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup */ }
            }
        }
    }

    private sealed class FamilyInfo
    {
        public long FamilyId { get; set; }
        public Family Family { get; set; } = null!;
        public string FamilyName { get; set; } = "";
        public string Category { get; set; } = "";
        public int TypeCount { get; set; }
        public int InstanceCount { get; set; }
        public bool IsInPlace { get; set; }
        public bool IsEditable { get; set; }
        public long? SizeKB { get; set; }
    }
}
