using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Filters elements by parameter value conditions with flexible matching
/// (equals, contains, greater_than, etc.). Supports scope filtering
/// (whole model, active view, selection) and instance/type parameter lookup.
/// </summary>
[ToolSafety(true, false)]
public class FilterByParameterValueTool : ICortexTool
{
    public string Name => "filter_by_parameter_value";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Filters elements by one parameter condition, or several combined with AND/OR via the 'conditions' array. Flexible matching (equals, contains, greater_than, is_empty, etc.). Supports scope filtering (whole model, active view, selection) and instance/type parameter lookup.";
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

        // Multi-condition mode: an array of {parameterName, condition, value, parameterType?}
        // combined with AND (default) or OR. Falls back to the single-parameter inputs.
        var conditionsToken = input["conditions"] as JArray;
        if (conditionsToken == null && input["conditions"]?.Type == JTokenType.String)
        {
            try { conditionsToken = JArray.Parse(input["conditions"]!.Value<string>() ?? "[]"); }
            catch
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "conditions must be a JSON array or a JSON array string");
            }
        }
        var logic           = (input["logic"]?.Value<string>() ?? "and").ToLowerInvariant();

        var clauses = new List<FilterClause>();
        if (conditionsToken != null && conditionsToken.Count > 0)
        {
            foreach (var c in conditionsToken.OfType<JObject>())
            {
                var name = c["parameterName"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(name)) continue;
                clauses.Add(new FilterClause(
                    name!,
                    c["condition"]?.Value<string>() ?? "equals",
                    c["value"]?.Value<string>() ?? "",
                    c["parameterType"]?.Value<string>() ?? parameterType));
            }
            if (clauses.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "conditions array provided but no valid {parameterName, condition, value} entries found");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(parameterName))
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "parameterName (or a non-empty conditions array) is required");
            clauses.Add(new FilterClause(parameterName, condition, value, parameterType));
        }

        try
        {
            // Pre-validate active_view scope — doc.ActiveView can be null when the request
            // arrives without a UI context (socket handler marshalling, document load, etc.).
            if (scope.Equals("active_view", StringComparison.OrdinalIgnoreCase) && doc.ActiveView == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "scope='active_view' but there is no active view in the document.",
                    suggestion: "Activate a view in Revit, or use scope='whole_model'.");

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

            var scoped = ElementScopeResolver.Resolve(doc, input, session,
                out var resolvedScope, out var scopeError, scope);
            if (scopeError != null) return scopeError;
            var elements = scoped.Where(element => element is not ElementType).ToList();
            if (resolvedCatIds.Count > 0)
                elements = elements.Where(element => element.Category != null &&
                    resolvedCatIds.Contains(element.Category.Id)).ToList();

            // Filter by parameter value (with type element cache)
            var matchedElements = new List<object>();
            var typeCache = new Dictionary<ElementId, Element?>();

            bool useOr = logic == "or";

            foreach (var elem in elements)
            {
                // Evaluate every clause; a null parameter value counts as "not matched"
                // unless the condition is is_empty (handled inside MatchesCondition).
                string? firstMatchedValue = null;
                bool overall = useOr ? false : true;
                foreach (var clause in clauses)
                {
                    string? pv = GetParameterValue(doc, elem, clause.ParameterName, clause.ParameterType, typeCache);
                    bool clauseMatch = MatchesCondition(pv ?? "", clause.Value, clause.Condition, caseSensitive);
                    if (clauseMatch && firstMatchedValue == null) firstMatchedValue = pv ?? "";
                    if (useOr) overall |= clauseMatch;
                    else       overall &= clauseMatch;
                    if (!useOr && !overall) break; // AND short-circuit
                }
                if (!overall) continue;

                var elementData = new Dictionary<string, object>
                {
                    { "elementId", elem.Id.Value },
                    { "category", elem.Category?.Name ?? "Unknown" },
                    { "familyName", GetFamilyName(elem) },
                    { "typeName", GetTypeName(doc, elem, typeCache) },
                    { "matchedValue", firstMatchedValue ?? "" }
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
                resolvedScope,
                logic         = clauses.Count > 1 ? logic : null,
                conditions    = clauses.Select(c => new { c.ParameterName, c.Condition, c.Value }).ToList(),
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
            param = ParameterLookup.FindParameterOnElement(elem, paramName);

        if (param == null && checkType)
        {
            var typeElem = GetCachedTypeElement(doc, elem, typeCache);
            if (typeElem != null)
                param = ParameterLookup.FindParameterOnElement(typeElem, paramName);
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

    /// <summary>One parameter-value condition.</summary>
    private sealed class FilterClause
    {
        public string ParameterName { get; }
        public string Condition { get; }
        public string Value { get; }
        public string ParameterType { get; }

        public FilterClause(string parameterName, string condition, string value, string parameterType)
        {
            ParameterName = parameterName;
            Condition = condition;
            Value = value;
            ParameterType = parameterType;
        }
    }
}
