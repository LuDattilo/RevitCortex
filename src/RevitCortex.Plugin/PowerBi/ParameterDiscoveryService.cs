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
    /// Returns all non-Internal categories from the document, with instance counts
    /// for categories that have elements. Uses doc.Settings.Categories as the
    /// authoritative source (same as Visibility/Graphics) so the tab split is
    /// identical to Revit's own V/G dialog: Model / Annotation / AnalyticalModel.
    /// Only categories that appear in V/G (i.e. non-Internal) are returned.
    /// Instance count is set to 0 for categories with no instances — callers can
    /// filter these out or show them greyed.
    /// </summary>
    public List<CategoryInfo> DiscoverCategories(Document doc, CancellationToken ct = default)
    {
        var bicUnderlying = Enum.GetUnderlyingType(typeof(BuiltInCategory));

        // ── Step 1: build the category registry from doc.Settings.Categories ──
        // This is the same source Revit uses for Visibility/Graphics — it gives us
        // the correct CategoryType for every category without having to iterate elements.
        var registry = new Dictionary<long, CategoryInfo>();

        try
        {
            foreach (Category cat in doc.Settings.Categories)
            {
                ct.ThrowIfCancellationRequested();
                if (cat == null) continue;

                // Skip Internal — sketch lines, area boundaries, sun path, etc.
                if (IsInternalCategory(cat)) continue;

                long idValue;
                try { idValue = GetCategoryIdValueLong(cat.Id); } catch { continue; }
                if (registry.ContainsKey(idValue)) continue;

                string display;
                try { display = cat.Name ?? $"Category {idValue}"; }
                catch { display = $"Category {idValue}"; }

                string catType;
                try { catType = ClassifyCategoryType(cat); }
                catch { catType = "Model"; }

                BuiltInCategory? bic = TryGetBuiltInCategory(idValue, bicUnderlying);
                string ost = bic.HasValue ? bic.Value.ToString() : $"OST_{idValue}";

                registry[idValue] = new CategoryInfo
                {
                    OstCode = ost,
                    DisplayName = display,
                    InstanceCount = 0,
                    CategoryType = catType
                };
            }
        }
        catch (Exception)
        {
            // If Settings.Categories fails entirely fall through to element-based fallback
        }

        // ── Step 2: count instances per category ──
        // We iterate elements only to get the counts — category classification
        // already comes from Step 1.
        var skippedIds = new HashSet<long>();

        try
        {
            var iter = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .GetElementIterator();

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

                if (skippedIds.Contains(idValue)) continue;

                if (registry.TryGetValue(idValue, out var info))
                {
                    info.InstanceCount++;
                }
                else
                {
                    // Category not in Settings.Categories — likely Internal; skip it.
                    skippedIds.Add(idValue);
                }
            }
        }
        catch (Exception)
        {
            // Count step failure is non-fatal — we still return the category list
        }

        // Return only categories with at least one instance (or all if registry only)
        return registry.Values
            .Where(c => c.InstanceCount > 0)
            .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns true if the category should be excluded from the PBI export wizard.
    /// Revit's CategoryType.Internal includes sketch lines, area boundaries, sun path
    /// and other internal primitives that are never useful in a BI context.
    /// </summary>
    public static bool IsInternalCategory(Category cat)
    {
        try { return cat.CategoryType == CategoryType.Internal; }
        catch { return false; }
    }

    private static string ClassifyCategoryType(Category cat)
    {
        try
        {
            // Revit's CategoryType: Model, Annotation, Internal, AnalyticalModel.
            // Internal is filtered out before we get here (see DiscoverCategories).
            switch (cat.CategoryType)
            {
                case CategoryType.Model: return "Model";
                case CategoryType.Annotation: return "Annotation";
                case CategoryType.AnalyticalModel: return "Analytical";
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
