using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Elements;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Parameters;

[ToolSafety(false, true)]
public sealed class SyncCsvParametersTool : ICortexTool
{
    public string Name => "sync_csv_parameters";
    public string Category => "Parameters";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Synchronize parameter values from structured CSV/JSON rows using the shared localized/BuiltInParameter resolver.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = ToolHelpers.GetDocument(session);
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var rows = input["data"]?.ToObject<List<JObject>>() ?? new List<JObject>();
        if (rows.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "data array is required; each row needs elementId and parameter columns or a parameters object");

        var dryRun = ToolHelpers.GetDryRun(input);
        var includeDetails = input["includeDetails"]?.Value<bool>() ?? false;
        var sampleLimit = Math.Clamp(input["sampleLimit"]?.Value<int>() ?? 20, 0, 500);
        var parameterMap = input["parameterMap"] as JObject ?? new JObject();
        var details = new List<object>();
        var unmatchedHeaders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var processed = 0;
        var modified = 0;
        var skipped = 0;
        var errors = 0;

        Transaction? tx = null;
        TransactionFailureHandling.FailureCapture? failures = null;
        try
        {
            if (!dryRun)
            {
                tx = new Transaction(doc, "RiveTT: Sync CSV Parameters");
                failures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();
            }

            foreach (var row in rows)
            {
                processed++;
                var elementId = row["elementId"]?.Value<long>() ?? 0;
                var element = elementId > 0 ? doc.GetElement(ToolHelpers.ToElementId(elementId)) : null;
                if (element == null)
                {
                    errors++;
                    details.Add(new { elementId, success = false, reason = "Element not found" });
                    continue;
                }

                var values = row["parameters"] as JObject ?? new JObject(
                    row.Properties()
                        .Where(p => !p.Name.Equals("elementId", StringComparison.OrdinalIgnoreCase))
                        .Select(p => new JProperty(p.Name, p.Value.DeepClone())));

                var rowModified = 0;
                var rowFailures = new List<object>();
                foreach (var value in values.Properties())
                {
                    var mapping = ResolveMapping(value.Name, parameterMap);
                    var parameter = ParameterLookup.FindParameter(
                        element, mapping.ParameterName, mapping.BuiltInParameter,
                        out var requested, out var matchedBuiltIn);
                    if (parameter == null)
                    {
                        unmatchedHeaders[value.Name] = unmatchedHeaders.TryGetValue(value.Name, out var count)
                            ? count + 1 : 1;
                        rowFailures.Add(new
                        {
                            header = value.Name,
                            requestedParameter = requested,
                            reason = "Parameter not found on instance or type"
                        });
                        skipped++;
                        continue;
                    }
                    if (parameter.IsReadOnly)
                    {
                        rowFailures.Add(new { header = value.Name, reason = "Parameter is read-only" });
                        skipped++;
                        continue;
                    }

                    try
                    {
                        var assignable = SetElementParametersTool.CanSetParameterValue(parameter, value.Value);
                        if (!assignable)
                        {
                            rowFailures.Add(new
                            {
                                header = value.Name,
                                resolvedParameterName = parameter.Definition?.Name,
                                builtInParameter = matchedBuiltIn,
                                reason = $"Value is invalid for {parameter.StorageType}"
                            });
                            errors++;
                            continue;
                        }
                        if (!dryRun && !SetElementParametersTool.SetParameterValue(parameter, value.Value))
                        {
                            rowFailures.Add(new { header = value.Name, reason = "Revit rejected the value" });
                            errors++;
                            continue;
                        }
                        rowModified++;
                        modified++;
                    }
                    catch (Exception ex)
                    {
                        rowFailures.Add(new { header = value.Name, reason = ex.Message });
                        errors++;
                    }
                }

                details.Add(new
                {
                    elementId,
                    success = rowFailures.Count == 0,
                    parametersMatched = rowModified,
                    failedParameters = rowFailures
                });
            }

            if (tx != null)
            {
                if (tx.Commit() != TransactionStatus.Committed)
                    return TransactionFailureHandling.ToFailure(failures!,
                        "CSV parameter synchronization was rolled back",
                        "Resolve the listed model failures or split the input into smaller batches.");
                tx.Dispose();
                tx = null;
            }

            return CortexResult<object>.Ok(new
            {
                dryRun,
                processed,
                modified,
                skipped,
                errors,
                unmatchedHeaders = unmatchedHeaders.Select(p => new
                {
                    header = p.Key,
                    affectedRows = p.Value,
                    hint = "Use parameterMap with a BuiltInParameter enum name for locale-independent matching."
                }).ToList(),
                includeDetails,
                sampleLimit,
                details = includeDetails ? details.Take(sampleLimit).ToList() : null
            });
        }
        catch (Exception ex)
        {
            if (tx?.GetStatus() == TransactionStatus.Started) tx.RollBack();
            tx?.Dispose();
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to synchronize parameters: {ex.Message}");
        }
    }

    private static (string? ParameterName, string? BuiltInParameter) ResolveMapping(
        string header, JObject parameterMap)
    {
        var token = parameterMap[header];
        if (token is JObject obj)
            return (obj["parameterName"]?.Value<string>(), obj["builtInParameter"]?.Value<string>());
        if (token?.Type == JTokenType.String)
        {
            var mapped = token.Value<string>();
            return mapped?.StartsWith("BuiltInParameter.", StringComparison.OrdinalIgnoreCase) == true ||
                   mapped?.StartsWith("BIP:", StringComparison.OrdinalIgnoreCase) == true ||
                   mapped?.All(ch => char.IsUpper(ch) || char.IsDigit(ch) || ch == '_') == true
                ? (null, mapped!.Replace("BIP:", "", StringComparison.OrdinalIgnoreCase))
                : (mapped, null);
        }
        return (header, null);
    }
}
