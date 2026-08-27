using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Exports element data as JSON or CSV. Supports category filtering (OST_* codes),
/// explicit or auto-discovered parameter columns, and value-based row filtering.
/// Mirrors the fork's ExportElementsDataEventHandler.
/// </summary>
[ToolSafety(true, false)]
public class ExportElementsDataTool : ICortexTool
{
    public string Name => "export_elements_data";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Exports element data as JSON or CSV. Supports category filtering (OST_* codes), explicit or auto-discovered parameter columns, and value-based row filtering. Mirrors the fork's ExportElementsDataEventHandler.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        // ── Parse inputs ───────────────────────────────────────────────────
        var elementIds         = input["elementIds"]?.ToObject<long[]>() ?? Array.Empty<long>();
        var countOnly          = input["countOnly"]?.Value<bool>() ?? false;
        var categories         = input["categories"]?.ToObject<string[]>() ?? Array.Empty<string>();
        var parameterNames     = input["parameterNames"]?.ToObject<string[]>() ?? Array.Empty<string>();
        var includeTypeParams  = input["includeTypeParameters"]?.Value<bool>() ?? false;
        var includeElementId   = input["includeElementId"]?.Value<bool>() ?? true;
        var outputFormat       = (input["outputFormat"]?.Value<string>() ?? "json").ToLowerInvariant();
        var maxElements        = input["maxElements"]?.Value<int>() ?? 100;
        var filterParamName    = input["filterParameterName"]?.Value<string>() ?? "";
        var filterValue        = input["filterValue"]?.Value<string>() ?? "";
        var filterOperator     = (input["filterOperator"]?.Value<string>() ?? "equals").ToLowerInvariant();

        if (maxElements <= 0) maxElements = 100;
        if (outputFormat != "csv") outputFormat = "json";

        try
        {
            // ── Collect elements ───────────────────────────────────────────
            var notFoundIds = new List<long>();
            var elements = elementIds.Length > 0
                ? CollectById(doc, elementIds, categories, notFoundIds)
                : CollectElements(doc, categories);
            int totalCount = elements.Count;

            // ── Apply filter ───────────────────────────────────────────────
            // is_empty / is_not_empty are declared by the MCP schema and take no
            // filterValue; requiring one made them silently no-ops.
            var valuelessOperator = filterOperator is "is_empty" or "is_not_empty";
            if (!string.IsNullOrEmpty(filterParamName) &&
                (!string.IsNullOrEmpty(filterValue) || valuelessOperator))
                elements = ApplyFilter(elements, doc, filterParamName, filterValue, filterOperator);

            int filteredCount = elements.Count;

            // ── Count-only mode ────────────────────────────────────────────
            // Sizing an export before running it: a 145-element export with type
            // parameters produced a 400 KB response that no client could display,
            // and there was no way to know that in advance.
            if (countOnly)
            {
                return CortexResult<object>.Ok(new
                {
                    countOnly = true,
                    totalCount,
                    filteredCount,
                    wouldExportCount = Math.Min(filteredCount, maxElements),
                    maxElements,
                    notFoundIds,
                    estimatedColumnCount = EstimateColumnCount(doc, elements, parameterNames, includeTypeParams),
                    message = $"{filteredCount} element(s) match. A full export would return " +
                              $"{Math.Min(filteredCount, maxElements)} row(s). Raise maxElements or narrow the " +
                              "categories/parameterNames before exporting."
                });
            }

            // ── Truncate ───────────────────────────────────────────────────
            bool truncated = elements.Count > maxElements;
            elements = elements.Take(maxElements).ToList();

            // ── Type element cache (shared across column discovery and row building)
            var typeCache = new Dictionary<ElementId, Element?>();

            // ── Resolve columns ────────────────────────────────────────────
            var columns = BuildColumns(doc, elements, parameterNames, includeElementId, includeTypeParams, typeCache);

            // ── Build rows ─────────────────────────────────────────────────
            var unresolved = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var rows = BuildRows(doc, elements, columns, includeElementId, includeTypeParams, parameterNames,
                typeCache, unresolved);

            // ── Format output ──────────────────────────────────────────────
            object data = outputFormat == "csv" ? BuildCsv(columns, rows) : (object)rows;

            string filterHint = "";
            if (filteredCount == 0 && totalCount > 0 && !string.IsNullOrEmpty(filterParamName))
                filterHint = $" Note: filter '{filterParamName}' {filterOperator} '{filterValue}' matched 0 of {totalCount} elements.";

            var categoriesUsed = categories.Length > 0
                ? (IEnumerable<string>)categories
                : new[] { "All" };

            // An unresolved parameter name used to produce a column of empty strings,
            // which reads exactly like "the parameter exists and is empty". Report it.
            var unresolvedReport = unresolved
                .Select(entry => new { requested = entry.Key, suggestions = entry.Value })
                .Cast<object>()
                .ToList();

            var unresolvedHint = unresolvedReport.Count == 0
                ? ""
                : $" WARNING: {unresolvedReport.Count} parameter name(s) matched nothing on any exported element " +
                  $"({string.Join(", ", unresolved.Keys)}); their columns are empty for that reason, not because " +
                  "the values are empty.";

            return CortexResult<object>.Ok(new
            {
                totalCount,
                filteredCount,
                exportedCount = elements.Count,
                truncated,
                categoriesUsed,
                requestedElementIds = elementIds.Length,
                notFoundIds,
                outputFormat,
                columns,
                unresolvedParameterNames = unresolvedReport,
                data,
                message = $"Exported {elements.Count} elements ({filteredCount} after filter, {totalCount} total). Format: {outputFormat.ToUpperInvariant()}.{filterHint}{unresolvedHint}"
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Export elements data failed: {ex.Message}");
        }
    }

    // ── Element collection ─────────────────────────────────────────────────

    /// <summary>
    /// Explicit-id mode. Applied BEFORE any pagination: the previous version had no
    /// elementIds input at all, so asking for one element returned the first 100
    /// elements of the model — the most misleading possible answer.
    /// </summary>
    private static List<Element> CollectById(
        Document doc, long[] elementIds, string[] categories, List<long> notFoundIds)
    {
        var categoryIds = new HashSet<ElementId>();
        foreach (var category in categories ?? Array.Empty<string>())
        {
            var categoryId = CategoryResolver.ResolveToId(doc, category);
            if (categoryId == null || categoryId == ElementId.InvalidElementId)
                throw new ArgumentException(
                    $"'{category}' is not a recognized category. Use OST_* codes (e.g. OST_Walls), English friendly names (Walls, Foundations), or the localized display name.");
            categoryIds.Add(categoryId);
        }

        var elements = new List<Element>();
        foreach (var rawId in elementIds.Distinct())
        {
            var element = doc.GetElement(new ElementId(rawId));
            if (element == null)
            {
                notFoundIds.Add(rawId);
                continue;
            }

            if (categoryIds.Count > 0 &&
                (element.Category == null || !categoryIds.Contains(element.Category.Id)))
            {
                continue;
            }

            elements.Add(element);
        }

        return elements;
    }

    private static int EstimateColumnCount(
        Document doc, List<Element> elements, string[] parameterNames, bool includeTypeParams)
    {
        if (parameterNames != null && parameterNames.Length > 0) return parameterNames.Length + 3;

        var sample = elements.FirstOrDefault();
        if (sample == null) return 3;

        var count = sample.Parameters.Size;
        if (includeTypeParams)
        {
            var typeId = sample.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
                count += doc.GetElement(typeId)?.Parameters.Size ?? 0;
        }

        return count + 3;
    }

    private static List<Element> CollectElements(Document doc, string[] categories)
    {
        if (categories == null || categories.Length == 0)
        {
            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model)
                .ToList();
        }

        var result = new List<Element>();
        foreach (var cat in categories)
        {
            var catId = CategoryResolver.ResolveToId(doc, cat);
            if (catId == null || catId == ElementId.InvalidElementId)
                throw new ArgumentException(
                    $"'{cat}' is not a recognized category. Use OST_* codes (e.g. OST_Walls), English friendly names (Walls, Foundations), or the localized display name.");

            var found = new FilteredElementCollector(doc)
                .OfCategoryId(catId)
                .WhereElementIsNotElementType()
                .ToList();
            result.AddRange(found);
        }

        // Deduplicate
        return result
            .GroupBy(e => GetElementIdLong(e))
            .Select(g => g.First())
            .ToList();
    }

    // ── Filter ─────────────────────────────────────────────────────────────

    private static List<Element> ApplyFilter(
        List<Element> elements,
        Document doc,
        string paramName,
        string filterValue,
        string filterOperator)
    {
        var result = new List<Element>();
        foreach (var element in elements)
        {
            var candidateValues = GetParameterFilterValues(element, doc, paramName);

            if (filterOperator is "is_empty" or "is_not_empty")
            {
                var isEmpty = candidateValues.Count == 0 ||
                              candidateValues.All(value => string.IsNullOrWhiteSpace(value));
                if (filterOperator == "is_empty" ? isEmpty : !isEmpty) result.Add(element);
                continue;
            }

            if (candidateValues.Count == 0)
            {
                // not_equals must still keep elements that simply do not carry the
                // parameter — they are, trivially, not equal to the filter value.
                if (filterOperator == "not_equals") result.Add(element);
                continue;
            }

            bool match;
            if (filterOperator is "greater_than" or "less_than")
            {
                match = candidateValues.Any(value =>
                    double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeric) &&
                    double.TryParse(filterValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var bound) &&
                    (filterOperator == "greater_than" ? numeric > bound : numeric < bound));
            }
            else if (filterOperator == "not_equals")
            {
                match = candidateValues.All(value =>
                    !Matches(value, filterValue, "equals"));
            }
            else
            {
                match = candidateValues.Any(value => Matches(value, filterValue, filterOperator));
            }

            if (match) result.Add(element);
        }
        return result;
    }

    private static bool Matches(string value, string filterValue, string filterOperator)
    {
        return filterOperator switch
        {
            "contains"   => value.IndexOf(filterValue, StringComparison.OrdinalIgnoreCase) >= 0,
            "startswith" => value.StartsWith(filterValue, StringComparison.OrdinalIgnoreCase),
            "endswith"   => value.EndsWith(filterValue, StringComparison.OrdinalIgnoreCase),
            _            => value.Equals(filterValue, StringComparison.OrdinalIgnoreCase) ||
                            // Accent/case-insensitive equality so "RDC"/"rdc" and
                            // "Béton"/"beton" behave the same as everywhere else.
                            ParameterNameResolver.Normalize(value) == ParameterNameResolver.Normalize(filterValue)
        };
    }

    // ── Column resolution ──────────────────────────────────────────────────

    private static List<string> BuildColumns(
        Document doc,
        List<Element> elements,
        string[] parameterNames,
        bool includeElementId,
        bool includeTypeParams,
        Dictionary<ElementId, Element?> typeCache)
    {
        var columns = new List<string>();

        if (includeElementId) columns.Add("ElementId");
        columns.Add("Category");
        columns.Add("Name");

        if (parameterNames != null && parameterNames.Length > 0)
        {
            foreach (var p in parameterNames)
                if (!columns.Contains(p))
                    columns.Add(p);
        }
        else
        {
            // Auto-discover from first 50 elements (with type cache)
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var elem in elements.Take(50))
            {
                foreach (Parameter param in elem.Parameters)
                {
                    var defName = param.Definition?.Name;
                    if (!string.IsNullOrEmpty(defName)) names.Add(defName!);
                }

                if (includeTypeParams)
                {
                    var typeElem = GetCachedTypeElement(doc, elem, typeCache);
                    if (typeElem != null)
                    {
                        foreach (Parameter param in typeElem.Parameters)
                        {
                            var defName = param.Definition?.Name;
                            if (!string.IsNullOrEmpty(defName)) names.Add(defName!);
                        }
                    }
                }
            }

            foreach (var name in names.OrderBy(n => n))
                if (!columns.Contains(name))
                    columns.Add(name);
        }

        return columns;
    }

    // ── Row building ───────────────────────────────────────────────────────

    private static List<Dictionary<string, object?>> BuildRows(
        Document doc,
        List<Element> elements,
        List<string> columns,
        bool includeElementId,
        bool includeTypeParams,
        string[] explicitParamNames,
        Dictionary<ElementId, Element?> typeCache,
        Dictionary<string, List<string>> unresolved)
    {
        var rows = new List<Dictionary<string, object?>>();
        bool hasExplicitParams = explicitParamNames != null && explicitParamNames.Length > 0;
        // Cache ElementId→Name resolutions to avoid repeated doc.GetElement for display values
        var elementIdNameCache = new Dictionary<ElementId, string>();

        foreach (var element in elements)
        {
            var row = new Dictionary<string, object?>();

            if (includeElementId)
                row["ElementId"] = GetElementIdLong(element);

            row["Category"] = element.Category?.Name ?? "";
            row["Name"]     = element.Name ?? "";

            if (includeTypeParams)
            {
                // Full enumeration mode: extract all instance + type params
                var instanceParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Parameter param in element.Parameters)
                {
                    var defName = param.Definition?.Name;
                    if (!string.IsNullOrEmpty(defName))
                        instanceParams[defName!] = GetParameterDisplayValueCached(param, doc, elementIdNameCache);
                }

                var typeParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var typeElem = GetCachedTypeElement(doc, element, typeCache);
                if (typeElem != null)
                {
                    foreach (Parameter param in typeElem.Parameters)
                    {
                        var defName = param.Definition?.Name;
                        if (!string.IsNullOrEmpty(defName))
                            typeParams[defName!] = GetParameterDisplayValueCached(param, doc, elementIdNameCache);
                    }
                }

                foreach (var col in columns)
                {
                    if (col is "ElementId" or "Category" or "Name") continue;
                    if (instanceParams.TryGetValue(col, out string? iVal))
                        row[col] = iVal;
                    else if (typeParams.TryGetValue(col, out string? tVal))
                        row[col] = tVal;
                    else
                        row[col] = "";
                }
            }
            else if (hasExplicitParams)
            {
                // Targeted mode: language-independent resolution. LookupParameter alone
                // compares the localized display name, so "Mark"/"Level"/"Width" came
                // back empty on a French document with no warning.
                foreach (var col in columns)
                {
                    if (col is "ElementId" or "Category" or "Name") continue;

                    var param = ParameterNameResolver.Resolve(element, col, doc);
                    if (param == null)
                    {
                        if (!unresolved.ContainsKey(col))
                            unresolved[col] = ParameterNameResolver.Suggest(
                                col, ParameterNameResolver.AvailableNames(element, doc));
                        row[col] = "";
                        continue;
                    }

                    // A name that resolves on at least one element is not unresolved.
                    unresolved.Remove(col);
                    row[col] = GetParameterDisplayValueCached(param, doc, elementIdNameCache);
                }
            }
            else
            {
                // Instance-only enumeration (no type params requested)
                var instanceParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Parameter param in element.Parameters)
                {
                    var defName = param.Definition?.Name;
                    if (!string.IsNullOrEmpty(defName))
                        instanceParams[defName!] = GetParameterDisplayValueCached(param, doc, elementIdNameCache);
                }

                foreach (var col in columns)
                {
                    if (col is "ElementId" or "Category" or "Name") continue;
                    row[col] = instanceParams.TryGetValue(col, out string? iVal) ? iVal : "";
                }
            }

            rows.Add(row);
        }

        return rows;
    }

    // ── Type element cache helper ─────────────────────────────────────────

    private static Element? GetCachedTypeElement(Document doc, Element element, Dictionary<ElementId, Element?> cache)
    {
        var typeId = element.GetTypeId();
        if (typeId == ElementId.InvalidElementId) return null;

        if (cache.TryGetValue(typeId, out var cached))
            return cached;

        var typeElem = doc.GetElement(typeId);
        cache[typeId] = typeElem;
        return typeElem;
    }

    // ── CSV output ─────────────────────────────────────────────────────────

    private static string BuildCsv(List<string> columns, List<Dictionary<string, object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(";", columns.Select(EscapeCsv)));

        foreach (var row in rows)
        {
            var fields = columns.Select(col =>
            {
                row.TryGetValue(col, out object? val);
                return EscapeCsv(val?.ToString() ?? "");
            });
            sb.AppendLine(string.Join(";", fields));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    // ── Parameter value helpers ────────────────────────────────────────────

    private static string GetParameterDisplayValueCached(Parameter param, Document doc, Dictionary<ElementId, string> eidCache)
    {
        if (param == null) return "";
        try
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    return param.AsString() ?? "";

                case StorageType.Integer:
                    try
                    {
                        string? yesNo = param.AsValueString();
                        if (yesNo is "Yes" or "No") return yesNo;
                    }
                    catch { }
                    return param.AsInteger().ToString();

                case StorageType.Double:
                    try
                    {
                        string? formatted = param.AsValueString();
                        if (!string.IsNullOrEmpty(formatted)) return formatted;
                    }
                    catch { }
                    return param.AsDouble().ToString("F4", CultureInfo.InvariantCulture);

                case StorageType.ElementId:
                    var eid = param.AsElementId();
                    if (eid == null || eid == ElementId.InvalidElementId) return "";
                    if (eidCache.TryGetValue(eid, out var cachedName))
                        return cachedName;
                    var refElem = doc.GetElement(eid);
                    var name = refElem?.Name ?? GetElementIdString(eid);
                    eidCache[eid] = name;
                    return name;

                default:
                    return "";
            }
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Values a filter compares against. An ElementId parameter yields the
    /// referenced element's NAME plus its raw id, because
    /// <c>filterParameterName="Level", filterValue="RDC"</c> means the level named
    /// RDC — the previous version compared "RDC" to the id "512913" and matched
    /// 0 of 138 rooms.
    /// </summary>
    private static List<string> GetParameterFilterValues(Element element, Document doc, string paramName)
    {
        var param = ParameterNameResolver.Resolve(element, paramName, doc);
        if (param == null) return new List<string>();

        var values = new List<string>();
        switch (param.StorageType)
        {
            case StorageType.String:
                values.Add(param.AsString() ?? "");
                break;
            case StorageType.Integer:
                values.Add(param.AsInteger().ToString(CultureInfo.InvariantCulture));
                var yesNo = SafeValueString(param);
                if (yesNo != null) values.Add(yesNo);
                break;
            case StorageType.Double:
                values.Add(param.AsDouble().ToString("F6", CultureInfo.InvariantCulture));
                var formatted = SafeValueString(param);
                if (formatted != null) values.Add(formatted);
                break;
            case StorageType.ElementId:
                var referencedId = param.AsElementId();
                values.Add(GetElementIdString(referencedId));
                var referenced = referencedId == null || referencedId == ElementId.InvalidElementId
                    ? null
                    : doc.GetElement(referencedId);
                if (referenced?.Name != null) values.Add(referenced.Name);
                break;
        }

        return values.Where(value => value.Length > 0).ToList();
    }

    private static string? SafeValueString(Parameter param)
    {
        try
        {
            var value = param.AsValueString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    // ── ElementId helpers ──────────────────────────────────────────────────

    private static long GetElementIdLong(Element elem)
    {
        return elem.Id.Value;
    }

    private static string GetElementIdString(ElementId? eid)
    {
        if (eid == null || eid == ElementId.InvalidElementId) return "";
        return eid.Value.ToString();
    }
}
