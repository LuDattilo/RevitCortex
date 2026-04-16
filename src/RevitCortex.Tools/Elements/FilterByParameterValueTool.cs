using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Filters elements by parameter value conditions with flexible matching
/// (equals, contains, greater_than, etc.). Supports scope filtering
/// (whole model, active view, selection) and instance/type parameter lookup.
/// </summary>
public class FilterByParameterValueTool : ICortexTool
{
    public string Name => "filter_by_parameter_value";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Filters elements by parameter value conditions with flexible matching (equals, contains, greater_than, etc.). Supports scope filtering (whole model, active view, selection) and instance/type parameter lookup.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var categories      = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var parameterName   = input["parameterName"]?.Value<string>() ?? "";
        var condition       = input["condition"]?.Value<string>() ?? "equals";
        var value           = input["value"]?.Value<string>() ?? "";
        var caseSensitive   = input["caseSensitive"]?.Value<bool>() ?? false;
        var scope           = input["scope"]?.Value<string>() ?? "whole_model";
        var parameterType   = input["parameterType"]?.Value<string>() ?? "both";
        var returnParameters = input["returnParameters"]?.ToObject<List<string>>() ?? new List<string>();

        if (string.IsNullOrWhiteSpace(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterName is required");

        try
        {
            // Pre-resolve category IDs once (avoid repeated lookups)
            var resolvedCatIds = new List<ElementId>();
            if (categories.Count > 0)
            {
                foreach (var cat in categories)
                {
                    var catId = CategoryResolver.ResolveToId(doc, cat);
                    if (catId != null && catId != ElementId.InvalidElementId)
                        resolvedCatIds.Add(catId);
                }
                if (resolvedCatIds.Count == 0)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"None of the specified categories could be resolved: {string.Join(", ", categories)}");
            }

            // Collect elements using Revit API category filter (much faster than post-filter)
            List<Element> elements;
            if (resolvedCatIds.Count > 0)
            {
                elements = new List<Element>();
                foreach (var catId in resolvedCatIds)
                {
                    FilteredElementCollector collector;
                    switch (scope.ToLower())
                    {
                        case "active_view":
                            collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
                            break;
                        case "selection":
                            var uiDoc = new UIDocument(doc);
                            var selectedIds = uiDoc.Selection.GetElementIds();
                            if (selectedIds.Count == 0)
                                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                                    "No elements selected in Revit",
                                    suggestion: "Select elements first, or use scope 'whole_model'");
                            collector = new FilteredElementCollector(doc, selectedIds);
                            break;
                        default:
                            collector = new FilteredElementCollector(doc);
                            break;
                    }
                    elements.AddRange(collector.OfCategoryId(catId).WhereElementIsNotElementType().ToList());
                }
                // Deduplicate if multiple categories matched the same elements
                if (resolvedCatIds.Count > 1)
                    elements = elements.GroupBy(e => e.Id).Select(g => g.First()).ToList();
            }
            else
            {
                // No category filter: collect all (original behavior)
                FilteredElementCollector collector;
                switch (scope.ToLower())
                {
                    case "active_view":
                        collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
                        break;
                    case "selection":
                        var uiDoc2 = new UIDocument(doc);
                        var selectedIds2 = uiDoc2.Selection.GetElementIds();
                        if (selectedIds2.Count == 0)
                            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                                "No elements selected in Revit",
                                suggestion: "Select elements first, or use scope 'whole_model'");
                        collector = new FilteredElementCollector(doc, selectedIds2);
                        break;
                    default:
                        collector = new FilteredElementCollector(doc);
                        break;
                }
                elements = collector.WhereElementIsNotElementType().ToList();
            }

            // Filter by parameter value (with type element cache)
            var matchedElements = new List<object>();
            var typeCache = new Dictionary<ElementId, Element?>();

            foreach (var elem in elements)
            {
                string? paramValue = GetParameterValue(doc, elem, parameterName, parameterType, typeCache);
                if (paramValue == null) continue;

                if (!MatchesCondition(paramValue, value, condition, caseSensitive))
                    continue;

                var elementData = new Dictionary<string, object>
                {
#if REVIT2024_OR_GREATER
                    { "elementId", elem.Id.Value },
#else
                    { "elementId", (long)elem.Id.IntegerValue },
#endif
                    { "category", elem.Category?.Name ?? "Unknown" },
                    { "familyName", GetFamilyName(elem) },
                    { "typeName", GetTypeName(doc, elem, typeCache) },
                    { "matchedValue", paramValue }
                };

                if (returnParameters.Count > 0)
                {
                    var extraParams = new Dictionary<string, string>();
                    // Reuse cached type element instead of re-fetching
                    var typeElem = GetCachedTypeElement(doc, elem, typeCache);
                    foreach (var rpName in returnParameters)
                    {
                        var rp = elem.LookupParameter(rpName);
                        if (rp != null)
                        {
                            extraParams[rpName] = rp.AsValueString() ?? rp.AsString() ?? "";
                        }
                        else if (typeElem != null)
                        {
                            var tp = typeElem.LookupParameter(rpName);
                            if (tp != null)
                                extraParams[rpName] = tp.AsValueString() ?? tp.AsString() ?? "";
                        }
                    }
                    elementData["parameters"] = extraParams;
                }

                matchedElements.Add(elementData);
            }

            return CortexResult<object>.Ok(new
            {
                matchCount    = matchedElements.Count,
                totalScanned  = elements.Count,
                parameterName,
                condition,
                value,
                elements = matchedElements
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Filter by parameter value failed: {ex.Message}");
        }
    }

    private static string? GetParameterValue(Document doc, Element elem, string paramName, string paramType, Dictionary<ElementId, Element?> typeCache)
    {
        Parameter? param = null;
        bool checkInstance = paramType == "instance" || paramType == "both";
        bool checkType = paramType == "type" || paramType == "both";

        if (checkInstance)
            param = elem.LookupParameter(paramName);

        if (param == null && checkType)
        {
            var typeElem = GetCachedTypeElement(doc, elem, typeCache);
            if (typeElem != null)
                param = typeElem.LookupParameter(paramName);
        }

        if (param == null) return null;
        return param.AsValueString() ?? param.AsString() ?? "";
    }

    private static bool MatchesCondition(string paramValue, string compareValue, string condition, bool caseSensitive)
    {
        string pv = caseSensitive ? paramValue : paramValue.ToLowerInvariant();
        string cv = caseSensitive ? compareValue : compareValue.ToLowerInvariant();

        switch (condition.ToLower())
        {
            case "equals":          return pv == cv;
            case "not_equals":      return pv != cv;
            case "contains":        return pv.Contains(cv);
            case "not_contains":    return !pv.Contains(cv);
            case "begins_with":     return pv.StartsWith(cv);
            case "not_begins_with": return !pv.StartsWith(cv);
            case "ends_with":       return pv.EndsWith(cv);
            case "not_ends_with":   return !pv.EndsWith(cv);
            case "greater_than":
                if (double.TryParse(paramValue, out double pNum) && double.TryParse(compareValue, out double cNum))
                    return pNum > cNum;
                return string.Compare(pv, cv, StringComparison.Ordinal) > 0;
            case "less_than":
                if (double.TryParse(paramValue, out double pNum2) && double.TryParse(compareValue, out double cNum2))
                    return pNum2 < cNum2;
                return string.Compare(pv, cv, StringComparison.Ordinal) < 0;
            case "is_empty":     return string.IsNullOrEmpty(paramValue);
            case "is_not_empty": return !string.IsNullOrEmpty(paramValue);
            default:             return false;
        }
    }

    private static string GetFamilyName(Element elem)
    {
        if (elem is FamilyInstance fi)
            return fi.Symbol?.Family?.Name ?? "";
        return "";
    }

    private static string GetTypeName(Document doc, Element elem, Dictionary<ElementId, Element?> typeCache)
    {
        var typeElem = GetCachedTypeElement(doc, elem, typeCache);
        return typeElem?.Name ?? "";
    }

    private static Element? GetCachedTypeElement(Document doc, Element elem, Dictionary<ElementId, Element?> cache)
    {
        var typeId = elem.GetTypeId();
        if (typeId == ElementId.InvalidElementId) return null;

        if (cache.TryGetValue(typeId, out var cached))
            return cached;

        var typeElem = doc.GetElement(typeId);
        cache[typeId] = typeElem;
        return typeElem;
    }
}
