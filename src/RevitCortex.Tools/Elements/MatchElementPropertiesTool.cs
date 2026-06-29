using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Copies parameter values from a source element to one or more target elements.
/// Matches parameters by name, respects read-only state, and handles all StorageTypes.
/// Mirrors the fork's MatchElementPropertiesEventHandler.
/// </summary>
[ToolSafety(false, true)]
public class MatchElementPropertiesTool : ICortexTool
{
    public string Name => "match_element_properties";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Copies parameter values from a source element to one or more target elements. Matches parameters by name, respects read-only state, and handles all StorageTypes. Mirrors the fork's MatchElementPropertiesEventHandler.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var sourceElementId = input["sourceElementId"]?.Value<long?>();
        if (sourceElementId == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "sourceElementId is required",
                suggestion: "Provide the element ID of the source element, e.g. {\"sourceElementId\": 123456}");

        var targetElementIds = input["targetElementIds"]?.ToObject<long[]>();
        if (targetElementIds == null || targetElementIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "targetElementIds is required and cannot be empty");

        var parameterNames    = input["parameterNames"]?.ToObject<string[]>() ?? Array.Empty<string>();
        var includeTypeParams = input["includeTypeParameters"]?.Value<bool>() ?? false;

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        // ── Resolve source element ─────────────────────────────────────────
        var sourceElem = doc.GetElement(ToElementId(sourceElementId.Value));
        if (sourceElem == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Source element {sourceElementId} not found");

        // ── Collect parameter values from source ───────────────────────────
        var sourceValues = CollectSourceValues(doc, sourceElem, parameterNames, includeTypeParams);
        if (sourceValues.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No matching parameters found on source element");

        try
        {
            int totalCopied = 0;
            var results     = new List<object>();

            using var tx = new Transaction(doc, "RevitCortex: Match Element Properties");
            tx.Start();

            try
            {
                foreach (var targetId in targetElementIds)
                {
                    var targetElem = doc.GetElement(ToElementId(targetId));
                    if (targetElem == null)
                    {
                        results.Add(new { elementId = targetId, parametersCopied = 0,
                            parameters = Array.Empty<string>(), error = $"Element {targetId} not found" });
                        continue;
                    }

                    int copiedCount   = 0;
                    var paramsCopied  = new List<string>();

                    foreach (var kvp in sourceValues)
                    {
                        // Look for matching parameter on target instance first
                        var targetParam = targetElem.LookupParameter(kvp.Key);

                        // Optionally check type
                        if (targetParam == null && includeTypeParams)
                        {
                            var typeId = targetElem.GetTypeId();
                            if (typeId != ElementId.InvalidElementId)
                                targetParam = doc.GetElement(typeId)?.LookupParameter(kvp.Key);
                        }

                        if (targetParam == null || targetParam.IsReadOnly) continue;

                        try
                        {
                            CopyParameterValue(targetParam, kvp.Value);
                            copiedCount++;
                            paramsCopied.Add(kvp.Key);
                        }
                        catch
                        {
                            // Skip parameters that cannot be copied (type mismatch, formula, etc.)
                        }
                    }

                    totalCopied += copiedCount;
                    results.Add(new
                    {
                        elementId         = targetId,
                        parametersCopied  = copiedCount,
                        parameters        = paramsCopied
                    });
                }

                tx.Commit();
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                throw;
            }

            return CortexResult<object>.Ok(new
            {
                sourceElementId = sourceElementId.Value,
                totalCopied,
                message = $"Copied {totalCopied} parameter value(s) across {targetElementIds.Length} element(s)",
                results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Match element properties failed: {ex.Message}");
        }
    }

    // ── Source value collection ────────────────────────────────────────────

    private static Dictionary<string, (StorageType Type, object Value)> CollectSourceValues(
        Document doc,
        Element element,
        string[] parameterNames,
        bool includeTypeParams)
    {
        var values         = new Dictionary<string, (StorageType, object)>(StringComparer.OrdinalIgnoreCase);
        bool filterByNames = parameterNames != null && parameterNames.Length > 0;

        void ProcessParameters(ParameterSet parameters)
        {
            foreach (Parameter param in parameters)
            {
                if (!param.HasValue || param.IsReadOnly) continue;
                string? name = param.Definition?.Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (filterByNames && !parameterNames!.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

                object? value = param.StorageType switch
                {
                    StorageType.String    => param.AsString(),
                    StorageType.Integer   => (object)param.AsInteger(),
                    StorageType.Double    => param.AsDouble(),
                    StorageType.ElementId => param.AsElementId(),
                    _                     => null
                };

                if (value != null)
                    values[name!] = (param.StorageType, value);
            }
        }

        ProcessParameters(element.Parameters);

        if (includeTypeParams)
        {
            var typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                var typeElem = doc.GetElement(typeId);
                if (typeElem != null)
                    ProcessParameters(typeElem.Parameters);
            }
        }

        return values;
    }

    // ── Parameter copy ─────────────────────────────────────────────────────

    private static void CopyParameterValue(Parameter target, (StorageType Type, object Value) source)
    {
        switch (source.Type)
        {
            case StorageType.String:
                target.Set((string)source.Value ?? "");
                break;
            case StorageType.Integer:
                target.Set((int)source.Value);
                break;
            case StorageType.Double:
                target.Set((double)source.Value);
                break;
            case StorageType.ElementId:
                target.Set((ElementId)source.Value);
                break;
        }
    }

    // ── ElementId helper ───────────────────────────────────────────────────

    private static ElementId ToElementId(long id)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(id);
#else
        return new ElementId((int)id);
#endif
    }
}
