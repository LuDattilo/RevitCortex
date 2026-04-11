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
/// Adds a prefix and/or suffix to a parameter value on matching elements.
/// Supports dry-run preview mode.
/// </summary>
public class AddPrefixSuffixTool : ICortexTool
{
    public string Name => "add_prefix_suffix";
    public string Category => "Parameters";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Adds a prefix and/or suffix to a parameter value on matching elements. Supports dry-run preview mode.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var parameterName = input["parameterName"]?.Value<string>();
        if (string.IsNullOrEmpty(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterName is required");

        var prefix = input["prefix"]?.Value<string>() ?? "";
        var suffix = input["suffix"]?.Value<string>() ?? "";
        if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(suffix))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "At least one of prefix or suffix is required");

        var separator = input["separator"]?.Value<string>() ?? "";
        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var scope = input["scope"]?.Value<string>() ?? "whole_model";
        var skipEmpty = input["skipEmpty"]?.Value<bool>() ?? true;
        var filterValue = input["filterValue"]?.Value<string>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        try
        {
            // Collect elements
            var elements = CollectElements(doc, categories, scope);

            int modified = 0;
            int skipped = 0;
            int errors = 0;
            var preview = new List<object>();

            Transaction? tx = null;
            if (!dryRun)
            {
                if (!session.RequestConfirmation("modify parameters on", elements.Count))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                tx = new Transaction(doc, "RevitCortex: Add Prefix/Suffix");
                tx.Start();
            }

            try
            {
                foreach (var elem in elements)
                {
                    var param = elem.LookupParameter(parameterName);
                    if (param == null || param.IsReadOnly)
                    {
                        skipped++;
                        continue;
                    }

                    var currentValue = param.AsString() ?? param.AsValueString() ?? "";

                    if (skipEmpty && string.IsNullOrEmpty(currentValue))
                    {
                        skipped++;
                        continue;
                    }

                    if (!string.IsNullOrEmpty(filterValue) &&
                        !currentValue.Equals(filterValue, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        continue;
                    }

                    // Build new value
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(prefix)) parts.Add(prefix);
                    parts.Add(currentValue);
                    if (!string.IsNullOrEmpty(suffix)) parts.Add(suffix);
                    var newValue = string.Join(separator, parts);

                    if (dryRun)
                    {
                        preview.Add(new
                        {
                            elementId = ToolHelpers.GetElementIdValue(elem.Id),
                            currentValue,
                            newValue
                        });
                        modified++;
                    }
                    else
                    {
                        try
                        {
                            param.Set(newValue);
                            modified++;
                        }
                        catch
                        {
                            errors++;
                        }
                    }
                }

                if (tx != null)
                {
                    tx.Commit();
                    tx.Dispose();
                }

                var result = new Dictionary<string, object>
                {
                    ["dryRun"] = dryRun,
                    ["modified"] = modified,
                    ["skipped"] = skipped,
                    ["errors"] = errors,
                    ["totalProcessed"] = elements.Count
                };

                if (dryRun && preview.Count > 0)
                    result["preview"] = preview.Take(100).ToList(); // Cap preview at 100

                return CortexResult<object>.Ok(result);
            }
            catch
            {
                if (tx != null && tx.GetStatus() == TransactionStatus.Started)
                {
                    tx.RollBack();
                    tx.Dispose();
                }
                throw;
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to add prefix/suffix: {ex.Message}");
        }
    }

    private static List<Element> CollectElements(Document doc, List<string> categories, string scope)
    {
        FilteredElementCollector collector;
        if (scope == "active_view")
        {
            collector = new FilteredElementCollector(doc, doc.ActiveView.Id);
        }
        else if (scope == "selection")
        {
            var uidoc = new Autodesk.Revit.UI.UIDocument(doc);
            var selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds.Count == 0)
                return new List<Element>();
            collector = new FilteredElementCollector(doc, selectedIds);
        }
        else
        {
            collector = new FilteredElementCollector(doc);
        }

        var elements = collector.WhereElementIsNotElementType().ToList();

        if (categories.Count > 0)
        {
            elements = elements.Where(e =>
                e.Category != null &&
                categories.Any(c => CategoryResolver.CategoryMatches(doc, e, c)))
                .ToList();
        }

        return elements;
    }
}
