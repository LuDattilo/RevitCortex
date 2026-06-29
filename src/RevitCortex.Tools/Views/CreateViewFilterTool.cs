using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Views;

/// <summary>
/// Creates, applies, or lists view filters with optional parameter rules and graphic overrides.
/// </summary>
[ToolSafety(false, false)]
public class CreateViewFilterTool : ICortexTool
{
    public string Name => "create_view_filter";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates, applies, or lists view filters. A filter can carry one rule (parameterName/filterRule/filterValue) or several via a 'rules' array combined with AND/OR (logic). Apply supports color overrides.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "create";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "list" => ListFilters(doc),
                "create" => CreateFilter(doc, input),
                "apply" => ApplyFilter(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: list, create, apply")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static CortexResult<object> ListFilters(Document doc)
    {
        var filters = new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .Select(f => new
            {
                id = ToolHelpers.GetElementIdValue(f.Id),
                name = f.Name,
                categoryCount = f.GetCategories().Count
            }).ToList();
        return CortexResult<object>.Ok(new { filterCount = filters.Count, filters });
    }

    private static CortexResult<object> CreateFilter(Document doc, JObject input)
    {
        var filterName = input["filterName"]?.Value<string>();
        if (string.IsNullOrEmpty(filterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "filterName is required");

        var categories = input["categoryNames"]?.ToObject<List<string>>() ?? new List<string>();
        if (categories.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "categoryNames is required");

        var catIds = new List<ElementId>();
        foreach (var catName in categories)
        {
            var catId = CategoryResolver.ResolveToId(doc, catName);
            if (catId != null) catIds.Add(catId);
        }

        if (catIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No valid categories resolved");

        using var tx = new Transaction(doc, "RevitCortex: Create View Filter");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        var filter = ParameterFilterElement.Create(doc, filterName, catIds);

        // A sample element from the first category, used to resolve parameter ids by name.
        var testElem = new FilteredElementCollector(doc)
            .OfCategoryId(catIds[0])
            .WhereElementIsNotElementType()
            .FirstOrDefault();

        // Collect rules: single (parameterName/filterRule/filterValue) and/or a `rules` array.
        var ruleSpecs = new List<(string name, string rule, string value)>();
        var singleName = input["parameterName"]?.Value<string>();
        var singleRule = input["filterRule"]?.Value<string>();
        if (!string.IsNullOrEmpty(singleName) && !string.IsNullOrEmpty(singleRule))
            ruleSpecs.Add((singleName!, singleRule!, input["filterValue"]?.Value<string>() ?? ""));

        var rulesArray = input["rules"] as JArray;
        if (rulesArray != null)
        {
            foreach (var r in rulesArray.OfType<JObject>())
            {
                var n = r["parameterName"]?.Value<string>();
                var rl = r["rule"]?.Value<string>() ?? r["filterRule"]?.Value<string>();
                if (!string.IsNullOrEmpty(n) && !string.IsNullOrEmpty(rl))
                    ruleSpecs.Add((n!, rl!, r["value"]?.Value<string>() ?? r["filterValue"]?.Value<string>() ?? ""));
            }
        }

        var rulesApplied = 0;
        var ruleWarnings = new List<string>();
        if (ruleSpecs.Count > 0 && testElem != null)
        {
            var filters = new List<ElementFilter>();
            foreach (var spec in ruleSpecs)
            {
                var param = testElem.LookupParameter(spec.name);
                if (param == null) { ruleWarnings.Add($"Parameter '{spec.name}' not found on sample element"); continue; }
                var rule = CreateRule(param.Id, spec.rule, spec.value, param.StorageType);
                if (rule == null) { ruleWarnings.Add($"Unsupported rule '{spec.rule}' for '{spec.name}'"); continue; }
                filters.Add(new ElementParameterFilter(rule));
            }

            if (filters.Count == 1)
            {
                filter.SetElementFilter(filters[0]);
                rulesApplied = 1;
            }
            else if (filters.Count > 1)
            {
                var logic = (input["logic"]?.Value<string>() ?? "and").ToLowerInvariant();
                ElementFilter combined = logic == "or"
                    ? new LogicalOrFilter(filters)
                    : (ElementFilter)new LogicalAndFilter(filters);
                filter.SetElementFilter(combined);
                rulesApplied = filters.Count;
            }
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new
        {
            filterId = ToolHelpers.GetElementIdValue(filter.Id),
            filterName = filter.Name,
            categoryCount = catIds.Count,
            rulesApplied,
            warnings = ruleWarnings
        });
    }

    private static CortexResult<object> ApplyFilter(Document doc, JObject input)
    {
        var filterId = input["filterId"]?.Value<long>() ?? 0;
        var viewId = input["viewId"]?.Value<long>() ?? 0;
        var isVisible = input["isVisible"]?.Value<bool>() ?? true;

        if (filterId <= 0 || viewId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "filterId and viewId are required");

#if REVIT2024_OR_GREATER
        var filter = doc.GetElement(new ElementId(filterId)) as ParameterFilterElement;
        var view = doc.GetElement(new ElementId(viewId)) as View;
#else
        var filter = doc.GetElement(new ElementId((int)filterId)) as ParameterFilterElement;
        var view = doc.GetElement(new ElementId((int)viewId)) as View;
#endif
        if (filter == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Filter not found");
        if (view == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "View not found");

        using var tx = new Transaction(doc, "RevitCortex: Apply View Filter");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        view.AddFilter(filter.Id);
        view.SetFilterVisibility(filter.Id, isVisible);

        // Apply color overrides if specified
        var r = input["overrideR"]?.Value<int?>();
        var g = input["overrideG"]?.Value<int?>();
        var b = input["overrideB"]?.Value<int?>();
        if (r.HasValue && g.HasValue && b.HasValue)
        {
            var overrides = new OverrideGraphicSettings();
            var color = new Color((byte)r.Value, (byte)g.Value, (byte)b.Value);
            overrides.SetProjectionLineColor(color);
            overrides.SetSurfaceForegroundPatternColor(color);
            view.SetFilterOverrides(filter.Id, overrides);
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new
        {
            applied = true,
            filterName = filter.Name,
            viewName = view.Name,
            isVisible
        });
    }

    private static FilterRule? CreateRule(ElementId paramId, string rule, string value, StorageType storageType)
    {
        // Note: Revit 2025+ removed the bool caseSensitive parameter from string filter rules.
        // The 3-arg overload (paramId, value, caseSensitive) only exists in Revit 2024 and earlier.
        switch (rule.ToLowerInvariant())
        {
            case "equals":
                return storageType == StorageType.String
#if REVIT2025_OR_GREATER
                    ? ParameterFilterRuleFactory.CreateEqualsRule(paramId, value)
#else
                    ? ParameterFilterRuleFactory.CreateEqualsRule(paramId, value, false)
#endif
                    : int.TryParse(value, out var intVal)
                        ? ParameterFilterRuleFactory.CreateEqualsRule(paramId, intVal)
                        : null;
            case "not_equals":
                return storageType == StorageType.String
#if REVIT2025_OR_GREATER
                    ? ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, value)
#else
                    ? ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, value, false)
#endif
                    : null;
            case "contains":
#if REVIT2025_OR_GREATER
                return ParameterFilterRuleFactory.CreateContainsRule(paramId, value);
#else
                return ParameterFilterRuleFactory.CreateContainsRule(paramId, value, false);
#endif
            case "begins_with":
#if REVIT2025_OR_GREATER
                return ParameterFilterRuleFactory.CreateBeginsWithRule(paramId, value);
#else
                return ParameterFilterRuleFactory.CreateBeginsWithRule(paramId, value, false);
#endif
            case "ends_with":
#if REVIT2025_OR_GREATER
                return ParameterFilterRuleFactory.CreateEndsWithRule(paramId, value);
#else
                return ParameterFilterRuleFactory.CreateEndsWithRule(paramId, value, false);
#endif
            case "greater_than":
                return double.TryParse(value, out var dblVal)
                    ? ParameterFilterRuleFactory.CreateGreaterRule(paramId, dblVal, 1e-6)
                    : null;
            case "less_than":
                return double.TryParse(value, out var dblVal2)
                    ? ParameterFilterRuleFactory.CreateLessRule(paramId, dblVal2, 1e-6)
                    : null;
            default:
                return null;
        }
    }
}
