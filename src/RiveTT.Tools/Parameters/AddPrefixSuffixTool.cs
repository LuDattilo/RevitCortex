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
/// Adds a prefix and/or suffix to a parameter value on matching elements.
/// Supports dry-run preview mode.
/// </summary>
[ToolSafety(false, true)]
public class AddPrefixSuffixTool : ICortexTool
{
    public string Name => "batch_rename_affix";
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
        var skipEmpty = input["skipEmpty"]?.Value<bool>() ?? true;
        var filterValue = input["filterValue"]?.Value<string>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;
        var includeDetails = input["includeDetails"]?.Value<bool>() ?? false;
        var sampleLimit = Math.Clamp(input["sampleLimit"]?.Value<int>() ?? 20, 0, 500);

        try
        {
            var resolved = ElementScopeResolver.Resolve(doc, input, session,
                out var resolvedScope, out var scopeError);
            if (scopeError != null) return scopeError;
            var elements = resolved.Where(e => e is not ElementType).ToList();
            if (categories.Count > 0)
                elements = elements.Where(e => e.Category != null &&
                    categories.Any(c => CategoryResolver.CategoryMatches(doc, e, c))).ToList();

            int modified = 0;
            int skipped = 0;
            int errors = 0;
            var preview = new List<object>();

            Transaction? tx = null;
            TransactionFailureHandling.FailureCapture? txFailures = null;
            if (!dryRun)
            {
                if (!session.RequestConfirmation("modify parameters on", elements.Count))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                tx = new Transaction(doc, "RiveTT: Add Prefix/Suffix");
                txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();
            }

            try
            {
                foreach (var elem in elements)
                {
                    var param = ParameterLookup.FindParameter(elem, parameterName, input["builtInParameter"]?.Value<string>(),
                        out _, out _);
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
                    var committed = tx.Commit() == TransactionStatus.Committed;
                    tx.Dispose();
                    tx = null;
                    if (!committed)
                        return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                            $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures!)}",
                            suggestion: "Fix the reported model errors and retry.");
                }

                var result = new Dictionary<string, object>
                {
                    ["dryRun"] = dryRun,
                    ["modified"] = modified,
                    ["skipped"] = skipped,
                    ["errors"] = errors,
                    ["totalProcessed"] = elements.Count,
                    ["resolvedScope"] = resolvedScope
                };

                if (includeDetails && preview.Count > 0)
                    result["details"] = preview.Take(sampleLimit).ToList();
                result["includeDetails"] = includeDetails;
                result["sampleLimit"] = sampleLimit;

                return CortexResult<object>.Ok(result);
            }
            catch
            {
                if (tx != null)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
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

}
