using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;

namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// Discovers categories and parameters from a Revit document. The Revit API
/// must be touched on the main thread, so callers are expected to schedule
/// these methods through ExternalEvent/Idling. The discovery itself is
/// optimised for responsiveness (sampled, cancellable) so even very large
/// models complete in a few seconds.
/// </summary>
public class ParameterDiscoveryService
{
    /// <summary>
    /// Returns every Model category that has at least one instance in the document,
    /// with the instance count. Sorted alphabetically by display name.
    /// </summary>
    public List<CategoryInfo> DiscoverCategories(Document doc, CancellationToken ct = default)
    {
        // Use long as the universal key — works for net48 (where IntegerValue is int)
        // and net8 (where ElementId.Value is long). BuiltInCategory enum may have
        // either int or long underlying type depending on the Revit version, so we
        // reflect on it once and cast through Convert to dodge ArgumentException.
        var counts = new Dictionary<long, int>();
        var bicById = new Dictionary<long, BuiltInCategory>();
        var displayById = new Dictionary<long, string>();
        var typeById = new Dictionary<long, string>();

        var bicEnumType = typeof(BuiltInCategory);
        var bicUnderlying = Enum.GetUnderlyingType(bicEnumType);

        var iter = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementIterator();
        while (iter.MoveNext())
        {
            ct.ThrowIfCancellationRequested();
            var elem = iter.Current;
            if (elem == null) continue;

            Category? cat = null;
            try { cat = elem.Category; } catch { continue; }
            if (cat == null) continue;

            long idValue;
            try { idValue = GetCategoryIdValueLong(cat.Id); } catch { continue; }

            if (!counts.ContainsKey(idValue))
            {
                counts[idValue] = 0;

                // Match BuiltInCategory by name based on Category.Id, avoiding
                // Enum.IsDefined which throws when the value type doesn't match
                // the enum's underlying type.
                BuiltInCategory? bic = TryGetBuiltInCategory(idValue, bicUnderlying);
                if (bic.HasValue) bicById[idValue] = bic.Value;

                try { displayById[idValue] = cat.Name ?? $"Category {idValue}"; }
                catch { displayById[idValue] = $"Category {idValue}"; }

                // Capture Revit's CategoryType so the UI can split categories
                // into Model/Annotation/Analytical tabs like V/G overrides.
                try { typeById[idValue] = ClassifyCategoryType(cat); }
                catch { typeById[idValue] = "Model"; }
            }
            counts[idValue]++;
        }

        var result = new List<CategoryInfo>();
        foreach (var kvp in counts)
        {
            ct.ThrowIfCancellationRequested();
            string display = displayById.TryGetValue(kvp.Key, out var d) ? d : $"Category {kvp.Key}";
            string ost = bicById.ContainsKey(kvp.Key)
                ? bicById[kvp.Key].ToString()
                : "OST_" + kvp.Key;

            string catType = typeById.TryGetValue(kvp.Key, out var t) ? t : "Model";
            result.Add(new CategoryInfo
            {
                OstCode = ost,
                DisplayName = display,
                InstanceCount = kvp.Value,
                CategoryType = catType
            });
        }

        return result.OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string ClassifyCategoryType(Category cat)
    {
        try
        {
            // Revit's CategoryType: Model, Annotation, Internal, AnalyticalModel.
            // We surface "Imported" as a 5th bucket (CAD/IFC links) — handled below.
            switch (cat.CategoryType)
            {
                case CategoryType.Model: return "Model";
                case CategoryType.Annotation: return "Annotation";
                case CategoryType.AnalyticalModel: return "Analytical";
                case CategoryType.Internal: return "Internal";
                default: return "Model";
            }
        }
        catch
        {
            return "Model";
        }
    }

    /// <summary>
    /// Casts a long category id to BuiltInCategory if it matches a defined value.
    /// Avoids Enum.IsDefined's strict type check by converting the value to the
    /// enum's actual underlying type before comparing.
    /// </summary>
    private static BuiltInCategory? TryGetBuiltInCategory(long idValue, Type underlying)
    {
        try
        {
            object boxed = underlying == typeof(long)
                ? (object)idValue
                : (object)(int)idValue; // BuiltInCategory underlying on R23/R24 is int
            if (Enum.IsDefined(typeof(BuiltInCategory), boxed))
                return (BuiltInCategory)Enum.ToObject(typeof(BuiltInCategory), boxed);
        }
        catch { /* not a known BIC — fine */ }
        return null;
    }

    private static long GetCategoryIdValueLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    /// <summary>
    /// Returns every ViewSchedule in the document (including key schedules but
    /// excluding internal "Schedule:" panel placeholders), with column count
    /// and row count. Sorted alphabetically by name.
    /// </summary>
    public List<ScheduleInfo> DiscoverSchedules(Document doc, CancellationToken ct = default)
    {
        var result = new List<ScheduleInfo>();
        var collector = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule));

        foreach (ViewSchedule sch in collector)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (sch == null) continue;
                if (sch.IsTemplate) continue;

                // IsTitleblockRevisionSchedule throws on some schedules — guard it
                bool isRev;
                try { isRev = sch.IsTitleblockRevisionSchedule; }
                catch { isRev = false; }
                if (isRev) continue;

                int columnCount = 0;
                int rowCount = 0;
                string categoryName = "";

                try
                {
                    var def = sch.Definition;
                    columnCount = def?.GetFieldCount() ?? 0;
                }
                catch { /* some schedules have no definition */ }

                try
                {
                    var data = sch.GetTableData();
                    var body = data?.GetSectionData(SectionType.Body);
                    if (body != null)
                    {
                        rowCount = body.NumberOfRows;
                        if (rowCount > 0) rowCount--; // drop header row
                    }
                }
                catch { /* schedule body not available */ }

                try { if (sch.Category != null) categoryName = sch.Category.Name; }
                catch { }

                result.Add(new ScheduleInfo
                {
                    ScheduleId = GetIdValue(sch.Id),
                    Name = sch.Name ?? "",
                    CategoryName = categoryName,
                    ColumnCount = columnCount,
                    RowCount = rowCount
                });
            }
            catch
            {
                // Skip a schedule if anything else goes wrong — never break the whole discovery
            }
        }

        return result.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static long GetIdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    /// <summary>
    /// Discovers all parameters available across the given categories, with
    /// per-parameter coverage (% of sampled elements that have a value). Sampling
    /// is capped at <paramref name="sampleSize"/> per category for performance.
    /// </summary>
    public List<ParameterInfo> DiscoverParameters(
        Document doc,
        IEnumerable<string> categoryOstCodes,
        bool includeTypeParameters,
        int sampleSize = 200,
        CancellationToken ct = default)
    {
        var instanceStats = new Dictionary<string, ParamStats>();
        var typeStats = new Dictionary<string, ParamStats>();
        var typeCache = new Dictionary<ElementId, Element?>();

        foreach (var ostCode in categoryOstCodes)
        {
            ct.ThrowIfCancellationRequested();
            if (!Enum.TryParse<BuiltInCategory>(ostCode, out var bic)) continue;

            var elems = new FilteredElementCollector(doc)
                .OfCategory(bic)
                .WhereElementIsNotElementType()
                .Take(sampleSize)
                .ToList();

            int sampled = elems.Count;
            if (sampled == 0) continue;

            foreach (var elem in elems)
            {
                ct.ThrowIfCancellationRequested();
                CollectParamStats(elem, instanceStats, scope: "Instance");

                if (includeTypeParameters)
                {
                    var typeId = elem.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                    {
                        if (!typeCache.TryGetValue(typeId, out var typeElem))
                        {
                            typeElem = doc.GetElement(typeId);
                            typeCache[typeId] = typeElem;
                        }
                        if (typeElem != null)
                            CollectParamStats(typeElem, typeStats, scope: "Type");
                    }
                }
            }

            // Update sample sizes for coverage calculation
            foreach (var stat in instanceStats.Values) stat.SampleSize += sampled;
            if (includeTypeParameters)
                foreach (var stat in typeStats.Values) stat.SampleSize += sampled;
        }

        var result = new List<ParameterInfo>();
        foreach (var kvp in instanceStats)
        {
            result.Add(new ParameterInfo
            {
                Name = kvp.Key,
                Scope = "Instance",
                GroupName = kvp.Value.GroupName,
                IsReadOnly = kvp.Value.IsReadOnly,
                IsShared = kvp.Value.IsShared,
                CoveragePercent = kvp.Value.SampleSize > 0
                    ? (int)Math.Round(100.0 * kvp.Value.PopulatedCount / kvp.Value.SampleSize)
                    : 0
            });
        }
        foreach (var kvp in typeStats)
        {
            result.Add(new ParameterInfo
            {
                Name = kvp.Key,
                Scope = "Type",
                GroupName = kvp.Value.GroupName,
                IsReadOnly = kvp.Value.IsReadOnly,
                IsShared = kvp.Value.IsShared,
                CoveragePercent = kvp.Value.SampleSize > 0
                    ? (int)Math.Round(100.0 * kvp.Value.PopulatedCount / kvp.Value.SampleSize)
                    : 0
            });
        }

        return result
            .OrderBy(p => p.Scope)
            .ThenBy(p => p.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void CollectParamStats(Element elem,
        Dictionary<string, ParamStats> stats,
        string scope)
    {
        try
        {
            foreach (Parameter p in elem.Parameters)
            {
                if (p == null) continue;
                string name;
                try { name = p.Definition?.Name ?? ""; } catch { continue; }
                if (string.IsNullOrEmpty(name)) continue;

                if (!stats.TryGetValue(name, out var s))
                {
                    s = new ParamStats
                    {
                        GroupName = GetGroupDisplayName(p),
                        IsReadOnly = SafeIsReadOnly(p),
                        IsShared = SafeIsShared(p)
                    };
                    stats[name] = s;
                }
                if (SafeIsReadOnly(p)) s.IsReadOnly = true;
                if (HasMeaningfulValue(p)) s.PopulatedCount++;
            }
        }
        catch
        {
            // Skip elements whose parameter iteration explodes
        }
    }

    private static bool SafeIsReadOnly(Parameter p)
    {
        try { return p.IsReadOnly; } catch { return false; }
    }

    private static bool SafeIsShared(Parameter p)
    {
        try { return p.IsShared; } catch { return false; }
    }

    private static bool HasMeaningfulValue(Parameter p)
    {
        if (!p.HasValue) return false;
        return p.StorageType switch
        {
            StorageType.String => !string.IsNullOrEmpty(p.AsString()),
            StorageType.Integer => p.AsInteger() != 0,
            StorageType.Double => Math.Abs(p.AsDouble()) > 1e-9,
            StorageType.ElementId => p.AsElementId() != ElementId.InvalidElementId,
            _ => false
        };
    }

    private static string GetGroupDisplayName(Parameter p)
    {
        try
        {
            var def = p.Definition;
            if (def == null) return "Other";
#if REVIT2024_OR_GREATER
            // R24+: use ForgeTypeId via GetGroupTypeId (Definition.ParameterGroup is gone on R25+)
            try
            {
                var groupId = def.GetGroupTypeId();
                if (groupId != null && !string.IsNullOrEmpty(groupId.TypeId))
                {
                    var label = LabelUtils.GetLabelForGroup(groupId);
                    if (!string.IsNullOrEmpty(label)) return label;
                }
            }
            catch { /* GetGroupTypeId throws on some defs */ }
            return "Other";
#else
            // R23: legacy enum still works
            try
            {
                return LabelUtils.GetLabelFor(def.ParameterGroup) ?? "Other";
            }
            catch { return "Other"; }
#endif
        }
        catch
        {
            return "Other";
        }
    }

    private class ParamStats
    {
        public int PopulatedCount;
        public int SampleSize;
        public string GroupName = "Other";
        public bool IsReadOnly;
        public bool IsShared;
    }
}
