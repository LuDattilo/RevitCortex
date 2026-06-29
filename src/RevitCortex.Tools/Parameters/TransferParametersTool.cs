using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Parameters;

/// <summary>
/// Copies parameter values from a source element to target elements.
/// </summary>
[ToolSafety(false, true)]
public class TransferParametersTool : ICortexTool
{
    public string Name => "transfer_parameters";
    public string Category => "Parameters";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Copies parameter values from a source element to target elements.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sourceId = input["sourceElementId"]?.Value<long>() ?? 0;
        var targetIds = input["targetElementIds"]?.ToObject<List<long>>() ?? new List<long>();
        var parameterNames = input["parameterNames"]?.ToObject<List<string>>() ?? new List<string>();
        var includeType = input["includeType"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        if (sourceId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sourceElementId is required");
        if (targetIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "targetElementIds is required");

        try
        {
#if REVIT2024_OR_GREATER
            var source = doc.GetElement(new ElementId(sourceId));
#else
            var source = doc.GetElement(new ElementId((int)sourceId));
#endif
            if (source == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Source element not found");

            // Collect source parameter values
            var sourceValues = new Dictionary<string, (StorageType type, object? val)>();
            foreach (Parameter p in source.Parameters)
            {
                if (parameterNames.Count > 0 && !parameterNames.Contains(p.Definition.Name)) continue;
                sourceValues[p.Definition.Name] = (p.StorageType, GetParamValue(p));
            }

            if (includeType)
            {
                var sourceType = doc.GetElement(source.GetTypeId());
                if (sourceType != null)
                {
                    foreach (Parameter p in sourceType.Parameters)
                    {
                        if (parameterNames.Count > 0 && !parameterNames.Contains(p.Definition.Name)) continue;
                        if (!sourceValues.ContainsKey(p.Definition.Name))
                            sourceValues[p.Definition.Name] = (p.StorageType, GetParamValue(p));
                    }
                }
            }

            var results = new List<object>();

            if (!dryRun)
            {
                if (!session.RequestConfirmation("transfer parameters to", targetIds.Count))
                    return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

                using var tx = new Transaction(doc, "RevitCortex: Transfer Parameters");
                tx.Start();
                results = TransferToTargets(doc, targetIds, sourceValues, includeType);
                tx.Commit();
            }
            else
            {
                results = PreviewTransfer(doc, targetIds, sourceValues);
            }

            return CortexResult<object>.Ok(new
            {
                dryRun,
                sourceId,
                parameterCount = sourceValues.Count,
                targetCount = results.Count,
                results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static List<object> TransferToTargets(Document doc, List<long> targetIds,
        Dictionary<string, (StorageType type, object? val)> sourceValues, bool includeType)
    {
        var results = new List<object>();
        foreach (var tid in targetIds)
        {
#if REVIT2024_OR_GREATER
            var target = doc.GetElement(new ElementId(tid));
#else
            var target = doc.GetElement(new ElementId((int)tid));
#endif
            if (target == null) { results.Add(new { id = tid, success = false, reason = "Not found" }); continue; }

            int set = 0;
            foreach (var kv in sourceValues)
            {
                var param = target.LookupParameter(kv.Key);
                if (param != null && !param.IsReadOnly && SetParamValue(param, kv.Value.type, kv.Value.val))
                    set++;

                if (includeType)
                {
                    var targetType = doc.GetElement(target.GetTypeId());
                    if (targetType != null)
                    {
                        var typeParam = targetType.LookupParameter(kv.Key);
                        if (typeParam != null && !typeParam.IsReadOnly && SetParamValue(typeParam, kv.Value.type, kv.Value.val))
                            set++;
                    }
                }
            }
            results.Add(new { id = tid, success = true, parametersSet = set });
        }
        return results;
    }

    private static List<object> PreviewTransfer(Document doc, List<long> targetIds,
        Dictionary<string, (StorageType type, object? val)> sourceValues)
    {
        var results = new List<object>();
        foreach (var tid in targetIds)
        {
#if REVIT2024_OR_GREATER
            var target = doc.GetElement(new ElementId(tid));
#else
            var target = doc.GetElement(new ElementId((int)tid));
#endif
            if (target == null) { results.Add(new { id = tid, success = false, reason = "Not found" }); continue; }
            var matchCount = sourceValues.Keys.Count(k => target.LookupParameter(k) != null);
            results.Add(new { id = tid, success = true, matchingParameters = matchCount });
        }
        return results;
    }

    private static object? GetParamValue(Parameter p)
    {
        return p.StorageType switch
        {
            StorageType.String => p.AsString(),
            StorageType.Integer => p.AsInteger(),
            StorageType.Double => p.AsDouble(),
            StorageType.ElementId => p.AsElementId(),
            _ => null
        };
    }

    private static bool SetParamValue(Parameter p, StorageType srcType, object? val)
    {
        if (val == null) return false;
        try
        {
            return p.StorageType switch
            {
                StorageType.String when val is string s => (p.Set(s), true).Item2,
                StorageType.Integer when val is int i => (p.Set(i), true).Item2,
                StorageType.Double when val is double d => (p.Set(d), true).Item2,
                StorageType.ElementId when val is ElementId eid => (p.Set(eid), true).Item2,
                _ => false
            };
        }
        catch { return false; }
    }
}
