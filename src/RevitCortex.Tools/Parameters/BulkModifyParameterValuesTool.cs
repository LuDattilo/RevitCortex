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
/// Bulk set, prefix, suffix, find/replace, or clear parameter values on elements.
/// </summary>
public class BulkModifyParameterValuesTool : ICortexTool
{
    public string Name => "bulk_modify_parameter_values";
    public string Category => "Parameters";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Bulk set, prefix, suffix, find/replace, or clear parameter values on elements.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var elementIds = input["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
        var categoryName = input["categoryName"]?.Value<string>();
        var parameterName = input["parameterName"]?.Value<string>();
        var operation = input["operation"]?.Value<string>() ?? "set";
        var value = input["value"]?.Value<string>() ?? "";
        var findText = input["findText"]?.Value<string>() ?? "";
        var replaceText = input["replaceText"]?.Value<string>() ?? "";
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;
        var onlyEmpty = input["onlyEmpty"]?.Value<bool>() ?? false;

        if (string.IsNullOrEmpty(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "parameterName is required");

        try
        {
            // Collect target elements
            IEnumerable<Element> elements;
            if (elementIds.Count > 0)
            {
                elements = elementIds
                    .Select(id =>
                    {
#if REVIT2024_OR_GREATER
                        return doc.GetElement(new ElementId(id));
#else
                        return doc.GetElement(new ElementId((int)id));
#endif
                    })
                    .Where(e => e != null)!;
            }
            else if (!string.IsNullOrEmpty(categoryName))
            {
                var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryName);
                if (catId == ElementId.InvalidElementId)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"Category not found: {categoryName}");
                elements = new FilteredElementCollector(doc).OfCategoryId(catId).WhereElementIsNotElementType();
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds or categoryName required");
            }

            var elementList = elements.ToList();
            var modified = new List<object>();
            var skipped = 0;

            if (!dryRun)
            {
                if (!session.RequestConfirmation("modify parameters on", elementList.Count))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                using var tx = new Transaction(doc, "RevitCortex: Bulk Modify Parameters");
                tx.Start();
                ProcessElements(elementList, parameterName, operation, value, findText, replaceText, onlyEmpty, modified, ref skipped);
                tx.Commit();
            }
            else
            {
                ProcessElements(elementList, parameterName, operation, value, findText, replaceText, onlyEmpty, modified, ref skipped, true);
            }

            return CortexResult<object>.Ok(new
            {
                dryRun,
                modifiedCount = modified.Count,
                skippedCount = skipped,
                modified = modified.Take(100).ToList()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static void ProcessElements(IEnumerable<Element> elements, string parameterName, string operation,
        string value, string findText, string replaceText, bool onlyEmpty,
        List<object> modified, ref int skipped, bool dryRun = false)
    {
        foreach (var elem in elements)
        {
            var param = elem.LookupParameter(parameterName);
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

            if (!dryRun)
            {
                if (param.StorageType == StorageType.String)
                    param.Set(newValue);
                else if (param.StorageType == StorageType.Double && double.TryParse(newValue, out var d))
                    param.Set(d);
                else if (param.StorageType == StorageType.Integer && int.TryParse(newValue, out var i))
                    param.Set(i);
            }

            modified.Add(new { id = ToolHelpers.GetElementIdValue(elem.Id), oldValue, newValue });
        }
    }
}
