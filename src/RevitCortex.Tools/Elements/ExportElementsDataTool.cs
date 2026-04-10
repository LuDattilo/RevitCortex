using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Exports element data as JSON or CSV. Supports category filtering (OST_* codes),
/// explicit or auto-discovered parameter columns, and value-based row filtering.
/// Mirrors the fork's ExportElementsDataEventHandler.
/// </summary>
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
            var elements = CollectElements(doc, categories);
            int totalCount = elements.Count;

            // ── Apply filter ───────────────────────────────────────────────
            if (!string.IsNullOrEmpty(filterParamName) && !string.IsNullOrEmpty(filterValue))
                elements = ApplyFilter(elements, doc, filterParamName, filterValue, filterOperator);

            int filteredCount = elements.Count;

            // ── Truncate ───────────────────────────────────────────────────
            bool truncated = elements.Count > maxElements;
            elements = elements.Take(maxElements).ToList();

            // ── Resolve columns ────────────────────────────────────────────
            var columns = BuildColumns(doc, elements, parameterNames, includeElementId, includeTypeParams);

            // ── Build rows ─────────────────────────────────────────────────
            var rows = BuildRows(doc, elements, columns, includeElementId, includeTypeParams, parameterNames);

            // ── Format output ──────────────────────────────────────────────
            object data = outputFormat == "csv" ? BuildCsv(columns, rows) : (object)rows;

            string filterHint = "";
            if (filteredCount == 0 && totalCount > 0 && !string.IsNullOrEmpty(filterParamName))
                filterHint = $" Note: filter '{filterParamName}' {filterOperator} '{filterValue}' matched 0 of {totalCount} elements.";

            var categoriesUsed = categories.Length > 0
                ? (IEnumerable<string>)categories
                : new[] { "All" };

            return CortexResult<object>.Ok(new
            {
                totalCount,
                filteredCount,
                exportedCount = elements.Count,
                truncated,
                categoriesUsed,
                outputFormat,
                columns,
                data,
                message = $"Exported {elements.Count} elements ({filteredCount} after filter, {totalCount} total). Format: {outputFormat.ToUpperInvariant()}.{filterHint}"
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Export elements data failed: {ex.Message}");
        }
    }

    // ── Element collection ─────────────────────────────────────────────────

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
            if (!Enum.TryParse<BuiltInCategory>(cat, ignoreCase: true, out var bic))
                throw new ArgumentException($"'{cat}' is not a valid BuiltInCategory. Use OST_* codes.");

            var found = new FilteredElementCollector(doc)
                .WherePasses(new ElementCategoryFilter(bic))
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
            string? rawValue = GetParameterRawValue(element, doc, paramName);
            if (rawValue == null) continue;

            bool match = false;
            if (filterOperator is "greater_than" or "less_than")
            {
                if (double.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double dVal) &&
                    double.TryParse(filterValue, NumberStyles.Any, CultureInfo.InvariantCulture, out double fVal))
                {
                    match = filterOperator == "greater_than" ? dVal > fVal : dVal < fVal;
                }
            }
            else
            {
                match = filterOperator switch
                {
                    "equals"     => rawValue.Equals(filterValue, StringComparison.OrdinalIgnoreCase),
                    "not_equals" => !rawValue.Equals(filterValue, StringComparison.OrdinalIgnoreCase),
                    "contains"   => rawValue.IndexOf(filterValue, StringComparison.OrdinalIgnoreCase) >= 0,
                    _            => rawValue.Equals(filterValue, StringComparison.OrdinalIgnoreCase)
                };
            }

            if (match) result.Add(element);
        }
        return result;
    }

    // ── Column resolution ──────────────────────────────────────────────────

    private static List<string> BuildColumns(
        Document doc,
        List<Element> elements,
        string[] parameterNames,
        bool includeElementId,
        bool includeTypeParams)
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
            // Auto-discover from first 50 elements
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
                    var typeId = elem.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                    {
                        var typeElem = doc.GetElement(typeId);
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
        string[] explicitParamNames)
    {
        var rows = new List<Dictionary<string, object?>>();

        foreach (var element in elements)
        {
            var row = new Dictionary<string, object?>();

            if (includeElementId)
                row["ElementId"] = GetElementIdLong(element);

            row["Category"] = element.Category?.Name ?? "";
            row["Name"]     = element.Name ?? "";

            // Instance parameters
            var instanceParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Parameter param in element.Parameters)
            {
                var defName = param.Definition?.Name;
                if (!string.IsNullOrEmpty(defName))
                    instanceParams[defName!] = GetParameterDisplayValue(param, doc);
            }

            // Type parameters
            var typeParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool needTypeParams = includeTypeParams || (explicitParamNames != null && explicitParamNames.Length > 0);
            if (needTypeParams)
            {
                var typeId = element.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    var typeElem = doc.GetElement(typeId);
                    if (typeElem != null)
                    {
                        foreach (Parameter param in typeElem.Parameters)
                        {
                            var defName = param.Definition?.Name;
                            if (!string.IsNullOrEmpty(defName))
                                typeParams[defName!] = GetParameterDisplayValue(param, doc);
                        }
                    }
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

            rows.Add(row);
        }

        return rows;
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

    private static string GetParameterDisplayValue(Parameter param, Document doc)
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
                    var refElem = doc.GetElement(eid);
                    return refElem?.Name ?? GetElementIdString(eid);

                default:
                    return "";
            }
        }
        catch
        {
            return "";
        }
    }

    private static string? GetParameterRawValue(Element element, Document doc, string paramName)
    {
        // Try direct lookup, then case-insensitive search on instance
        var param = element.LookupParameter(paramName);
        if (param == null)
        {
            foreach (Parameter p in element.Parameters)
            {
                if (p.Definition?.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase) == true)
                {
                    param = p;
                    break;
                }
            }
        }

        // Fallback: type parameters
        if (param == null)
        {
            var typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                var typeElem = doc.GetElement(typeId);
                param = typeElem?.LookupParameter(paramName);

                if (param == null && typeElem != null)
                {
                    foreach (Parameter p in typeElem.Parameters)
                    {
                        if (p.Definition?.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            param = p;
                            break;
                        }
                    }
                }
            }
        }

        if (param == null) return null;

        return param.StorageType switch
        {
            StorageType.String    => param.AsString() ?? "",
            StorageType.Integer   => param.AsInteger().ToString(),
            StorageType.Double    => param.AsDouble().ToString("F6", CultureInfo.InvariantCulture),
            StorageType.ElementId => GetElementIdString(param.AsElementId()),
            _                     => ""
        };
    }

    // ── ElementId helpers ──────────────────────────────────────────────────

    private static long GetElementIdLong(Element elem)
    {
#if REVIT2024_OR_GREATER
        return elem.Id.Value;
#else
        return elem.Id.IntegerValue;
#endif
    }

    private static string GetElementIdString(ElementId? eid)
    {
        if (eid == null || eid == ElementId.InvalidElementId) return "";
#if REVIT2024_OR_GREATER
        return eid.Value.ToString();
#else
        return eid.IntegerValue.ToString();
#endif
    }
}
