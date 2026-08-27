using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using RiveTT.Tools.Elements;

namespace RiveTT.Tools.Parameters;

/// <summary>
/// Bulk set, prefix, suffix, find/replace, or clear parameter values on elements.
/// </summary>
[ToolSafety(false, true)]
public class BulkModifyParameterValuesTool : ICortexTool
{
    public string Name => "batch_modify_parameter_values";
    public string Category => "Parameters";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Bulk set, prefix, suffix, find/replace, or clear parameter values on elements.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var categoryName = input["categoryName"]?.Value<string>();
        var parameterName = input["parameterName"]?.Value<string>();
        var operation = input["operation"]?.Value<string>() ?? "set";
        var value = input["value"]?.Value<string>() ?? "";
        var findText = input["findText"]?.Value<string>() ?? "";
        var replaceText = input["replaceText"]?.Value<string>() ?? "";
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;
        var onlyEmpty = input["onlyEmpty"]?.Value<bool>() ?? false;
        // Sample preview: in dryRun callers almost always want only the counts (CLAUDE.md
        // documents this explicitly). On a 1000-element bulk a 100-element preview is ~5KB
        // of wasted MCP tokens. Default off; set true to opt back into the legacy 100-row sample.
        var includeSample = input["includeSample"]?.Value<bool>() ?? false;
        var sampleLimit = input["sampleLimit"]?.Value<int>() ?? 100;

        if (string.IsNullOrEmpty(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "parameterName is required");

        try
        {
            // Collect target elements
            IEnumerable<Element> elements;
            string resolvedScope;
            if (input["elementIds"] != null || input["selectionToken"] != null ||
                input["savedSelectionName"] != null || input["scope"] != null)
            {
                var resolved = ElementScopeResolver.Resolve(doc, input, session,
                    out resolvedScope, out var scopeError);
                if (scopeError != null) return scopeError;
                elements = resolved;
            }
            else if (!string.IsNullOrEmpty(categoryName))
            {
                var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryName!);
                if (catId == ElementId.InvalidElementId)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"Category not found: {categoryName}");
                elements = new FilteredElementCollector(doc).OfCategoryId(catId).WhereElementIsNotElementType();
                resolvedScope = "category";
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "categoryName, elementIds, selectionToken, savedSelectionName, or scope is required");
            }

            var elementList = elements.ToList();
            var modified = new List<object>();
            var failures = new List<object>();
            var skipped = 0;

            if (!dryRun)
            {
                if (!session.RequestConfirmation("modify parameters on", elementList.Count))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                using var tx = new Transaction(doc, "RiveTT: Bulk Modify Parameters");
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();
                ProcessElements(elementList, parameterName!, operation, value, findText, replaceText, onlyEmpty, modified, ref skipped, failures);
                if (tx.Commit() != TransactionStatus.Committed)
                    return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                        $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                        suggestion: "Fix the reported model errors and retry.");
            }
            else
            {
                ProcessElements(elementList, parameterName!, operation, value, findText, replaceText, onlyEmpty, modified, ref skipped, failures, true);
            }

            // Only emit the sample when the caller asks for it. Counts always go out.
            object? sample = includeSample
                ? modified.Take(Math.Max(0, sampleLimit)).ToList()
                : null;

            return CortexResult<object>.Ok(new
            {
                dryRun,
                modifiedCount = modified.Count,
                processedCount = elementList.Count,
                skippedCount = skipped,
                failedCount = failures.Count,
                failures = failures.Take(50).ToList(),
                sampleIncluded = includeSample,
                sampleLimit = includeSample ? (int?)sampleLimit : null,
                modified = sample,
                resolvedScope
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static void ProcessElements(IEnumerable<Element> elements, string parameterName, string operation,
        string value, string findText, string replaceText, bool onlyEmpty,
        List<object> modified, ref int skipped, List<object> failures, bool dryRun = false)
    {
        foreach (var elem in elements)
        {
            var param = ParameterLookup.FindParameter(elem, parameterName, null, out _, out _);
            if (param == null || param.IsReadOnly) { skipped++; continue; }

            var oldValue = param.StorageType == StorageType.String ? param.AsString() ?? "" : param.AsValueString() ?? "";
            if (onlyEmpty && !string.IsNullOrEmpty(oldValue)) { skipped++; continue; }

            string newValue;
            switch (operation.ToLowerInvariant())
            {
                case "set": newValue = value; break;
                case "prefix": newValue = value + oldValue; break;
                case "suffix": newValue = oldValue + value; break;
                case "find_replace": newValue = oldValue.Replace(findText, replaceText); break;
                case "clear": newValue = ""; break;
                default: skipped++; continue;
            }

            // Determine whether newValue is assignable to this parameter's storage type.
            // A numeric parameter with an unparsable value must NOT be reported as modified
            // (the previous code added it to `modified` even though Set() never ran). This
            // check also makes dryRun honest: it now predicts the same skips as the real run.
            bool assignable =
                param.StorageType == StorageType.String ||
                (param.StorageType == StorageType.Double && double.TryParse(newValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _)) ||
                (param.StorageType == StorageType.Integer && int.TryParse(newValue, out _)) ||
                param.StorageType == StorageType.ElementId;

            if (!assignable)
            {
                skipped++;
                failures.Add(new { id = ToolHelpers.GetElementIdValue(elem.Id), reason = $"value '{newValue}' is not assignable to a {param.StorageType} parameter" });
                continue;
            }

            if (!dryRun)
            {
                try
                {
                    if (param.StorageType == StorageType.String)
                        param.Set(newValue);
                    else if (param.StorageType == StorageType.Double && double.TryParse(newValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                        param.Set(d);
                    else if (param.StorageType == StorageType.Integer && int.TryParse(newValue, out var i))
                        param.Set(i);
                    else { skipped++; continue; }  // ElementId or unhandled: not written
                }
                catch (Exception ex)
                {
                    // Per-element Set failure must not abort the whole batch.
                    failures.Add(new { id = ToolHelpers.GetElementIdValue(elem.Id), reason = ex.Message });
                    continue;
                }
            }

            modified.Add(new { id = ToolHelpers.GetElementIdValue(elem.Id), oldValue, newValue });
        }
    }
}
