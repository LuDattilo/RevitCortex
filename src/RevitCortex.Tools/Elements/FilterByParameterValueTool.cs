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
            // Collect elements based on scope
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

            var allElements = collector.WhereElementIsNotElementType().ToList();

            // Filter by categories if specified
            List<Element> elements;
            if (categories.Count > 0)
            {
                elements = allElements
                    .Where(e => categories.Any(cat => CategoryResolver.CategoryMatches(doc, e, cat)))
                    .ToList();
            }
            else
            {
                elements = allElements;
            }

            // Filter by parameter value
            var matchedElements = new List<object>();
            foreach (var elem in elements)
            {
                string? paramValue = GetParameterValue(doc, elem, parameterName, parameterType);
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
                    { "typeName", GetTypeName(doc, elem) },
                    { "matchedValue", paramValue }
                };

                if (returnParameters.Count > 0)
                {
                    var extraParams = new Dictionary<string, string>();
                    foreach (var rpName in returnParameters)
                    {
                        var rp = elem.LookupParameter(rpName);
                        if (rp != null)
                        {
                            extraParams[rpName] = rp.AsValueString() ?? rp.AsString() ?? "";
                        }
                        else
                        {
                            var typeElem = doc.GetElement(elem.GetTypeId());
                            if (typeElem != null)
                            {
                                var tp = typeElem.LookupParameter(rpName);
                                if (tp != null)
                                    extraParams[rpName] = tp.AsValueString() ?? tp.AsString() ?? "";
                            }
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

    private static string? GetParameterValue(Document doc, Element elem, string paramName, string paramType)
    {
        Parameter? param = null;
        bool checkInstance = paramType == "instance" || paramType == "both";
        bool checkType = paramType == "type" || paramType == "both";

        if (checkInstance)
            param = elem.LookupParameter(paramName);

        if (param == null && checkType)
        {
            var typeElem = doc.GetElement(elem.GetTypeId());
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

    private static string GetTypeName(Document doc, Element elem)
    {
        var typeElem = doc.GetElement(elem.GetTypeId());
        return typeElem?.Name ?? "";
    }
}
