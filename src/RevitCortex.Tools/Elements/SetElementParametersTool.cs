using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class SetElementParametersTool : ICortexTool
{
    public string Name => "set_element_parameters";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set Element Parameters";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var requests = input["requests"]?.ToObject<List<SetParameterRequest>>();
        if (requests == null || requests.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "requests array is required",
                suggestion: "Provide [{\"elementId\": 123, \"parameterName\": \"Comments\", \"value\": \"test\"}]");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var results = new List<object>();
        var successCount = 0;
        var failCount = 0;

        if (!session.RequestConfirmation("modify parameters on", requests.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Set Parameters");
        tx.Start();

        try
        {
            foreach (var req in requests)
            {
#if REVIT2024_OR_GREATER
                var elementId = new ElementId(req.ElementId);
#else
                var elementId = new ElementId((int)req.ElementId);
#endif
                var element = doc.GetElement(elementId);
                if (element == null)
                {
                    results.Add(new { elementId = req.ElementId, parameterName = req.ParameterName,
                        success = false, message = $"Element {req.ElementId} not found" });
                    failCount++;
                    continue;
                }

                // Try instance parameter first, then fall back to type parameter
                var param = element.LookupParameter(req.ParameterName);
                if (param == null)
                {
                    var typeId = element.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                    {
                        var typeElem = doc.GetElement(typeId);
                        param = typeElem?.LookupParameter(req.ParameterName);
                    }
                }

                if (param == null)
                {
                    results.Add(new { elementId = req.ElementId, parameterName = req.ParameterName,
                        success = false, message = $"Parameter '{req.ParameterName}' not found" });
                    failCount++;
                    continue;
                }

                if (param.IsReadOnly)
                {
                    results.Add(new { elementId = req.ElementId, parameterName = req.ParameterName,
                        success = false, message = $"Parameter '{req.ParameterName}' is read-only" });
                    failCount++;
                    continue;
                }

                try
                {
                    bool set = SetParameterValue(param, req.Value);
                    if (set)
                    {
                        results.Add(new { elementId = req.ElementId, parameterName = req.ParameterName,
                            success = true, message = "Parameter set successfully" });
                        successCount++;
                    }
                    else
                    {
                        results.Add(new { elementId = req.ElementId, parameterName = req.ParameterName,
                            success = false, message = "Failed to set parameter value" });
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new { elementId = req.ElementId, parameterName = req.ParameterName,
                        success = false, message = ex.Message });
                    failCount++;
                }
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
            message = $"Set {successCount}/{requests.Count} parameters successfully",
            successCount,
            failCount,
            results
        });
    }

    private static bool SetParameterValue(Parameter param, object? value)
    {
        if (value == null) return false;

        // Values from JSON deserialization arrive as JToken — handle explicitly
        if (value is JToken jToken)
        {
            return param.StorageType switch
            {
                StorageType.String => param.Set(jToken.Value<string>() ?? ""),
                StorageType.Integer => param.Set(jToken.Value<int>()),
                StorageType.Double => param.Set(jToken.Value<double>()),
#if REVIT2024_OR_GREATER
                StorageType.ElementId => param.Set(new ElementId(jToken.Value<long>())),
#else
                StorageType.ElementId => param.Set(new ElementId((int)jToken.Value<long>())),
#endif
                _ => false
            };
        }

        switch (param.StorageType)
        {
            case StorageType.String:
                return param.Set(value.ToString());
            case StorageType.Integer:
                if (value is int intVal) return param.Set(intVal);
                if (value is long longAsInt) return param.Set((int)longAsInt);
                if (int.TryParse(value.ToString(), out var parsedInt)) return param.Set(parsedInt);
                return false;
            case StorageType.Double:
                if (value is double dblVal) return param.Set(dblVal);
                if (double.TryParse(value.ToString(), out var parsedDbl)) return param.Set(parsedDbl);
                return false;
            case StorageType.ElementId:
                if (long.TryParse(value.ToString(), out var parsedId))
#if REVIT2024_OR_GREATER
                    return param.Set(new ElementId(parsedId));
#else
                    return param.Set(new ElementId((int)parsedId));
#endif
                return false;
            default:
                return false;
        }
    }

    private class SetParameterRequest
    {
        public long ElementId { get; set; }
        public string ParameterName { get; set; } = "";
        public object? Value { get; set; }
    }
}
