using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Parameters;

/// <summary>
/// Clears parameter values on elements by category, view, or selection scope.
/// </summary>
public class ClearParameterValuesTool : ICortexTool
{
    public string Name => "clear_parameter_values";
    public string Category => "Parameters";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Clears parameter values on elements by category, view, or selection scope.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var parameterName = input["parameterName"]?.Value<string>();
        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var scope = input["scope"]?.Value<string>() ?? "whole_model";
        var filterValue = input["filterValue"]?.Value<string>();
        var parameterType = input["parameterType"]?.Value<string>() ?? "instance";
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (string.IsNullOrEmpty(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "parameterName is required");

        try
        {
            var collector = scope == "active_view" && doc.ActiveView != null
                ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                : new FilteredElementCollector(doc);

            IEnumerable<Element> elements = collector.WhereElementIsNotElementType().ToList();

            if (categories.Count > 0)
            {
                var catIds = categories
                    .Select(c => Utilities.CategoryResolver.ResolveToId(doc, c))
                    .Where(id => id != ElementId.InvalidElementId)
                    .ToHashSet();
                elements = elements.Where(e => e.Category != null && catIds.Contains(e.Category.Id));
            }

            var cleared = new List<object>();
            var skipped = 0;

            if (!dryRun)
            {
                if (!session.RequestConfirmation("clear parameter values on", elements.Count()))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                using var tx = new Transaction(doc, "RevitCortex: Clear Parameter Values");
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();

                foreach (var elem in elements)
                {
                    var target = parameterType == "type" ? doc.GetElement(elem.GetTypeId()) : elem;
                    if (target == null) { skipped++; continue; }

                    var param = target.LookupParameter(parameterName);
                    if (param == null || param.IsReadOnly) { skipped++; continue; }

                    var oldValue = param.StorageType == StorageType.String ? param.AsString() ?? "" : param.AsValueString() ?? "";
                    if (!string.IsNullOrEmpty(filterValue) && oldValue != filterValue) { skipped++; continue; }

                    ClearParam(param);
                    cleared.Add(new { id = ToolHelpers.GetElementIdValue(elem.Id), oldValue });
                }

                if (tx.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
            }
            else
            {
                foreach (var elem in elements)
                {
                    var target = parameterType == "type" ? doc.GetElement(elem.GetTypeId()) : elem;
                    if (target == null) { skipped++; continue; }

                    var param = target.LookupParameter(parameterName);
                    if (param == null || param.IsReadOnly) { skipped++; continue; }

                    var oldValue = param.StorageType == StorageType.String ? param.AsString() ?? "" : param.AsValueString() ?? "";
                    if (!string.IsNullOrEmpty(filterValue) && oldValue != filterValue) { skipped++; continue; }

                    cleared.Add(new { id = ToolHelpers.GetElementIdValue(elem.Id), oldValue });
                }
            }

            return CortexResult<object>.Ok(new
            {
                dryRun,
                clearedCount = cleared.Count,
                skippedCount = skipped,
                cleared = cleared.Take(100).ToList()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static void ClearParam(Parameter param)
    {
        switch (param.StorageType)
        {
            case StorageType.String: param.Set(""); break;
            case StorageType.Integer: param.Set(0); break;
            case StorageType.Double: param.Set(0.0); break;
            case StorageType.ElementId: param.Set(ElementId.InvalidElementId); break;
        }
    }
}
