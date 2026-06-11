using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

public class SetElementParametersTool : ICortexTool
{
    public string Name => "set_element_parameters";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Set element parameters. Numeric values are written in Revit internal units (feet); pass a string with a unit (e.g. \"3000 mm\", \"3 m\") to write a display value that Revit parses unit- and locale-aware. A null value clears the parameter.";
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
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
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

                var param = ParameterLookup.FindParameter(
                    element,
                    req.ParameterName,
                    req.BuiltInParameter,
                    out var requestedParameter,
                    out var matchedBuiltInParameter);

                if (param == null)
                {
                    results.Add(new { elementId = req.ElementId, parameterName = requestedParameter,
                        builtInParameter = matchedBuiltInParameter,
                        success = false, message = $"Parameter '{requestedParameter}' not found" });
                    failCount++;
                    continue;
                }

                if (param.IsReadOnly)
                {
                    results.Add(new { elementId = req.ElementId, parameterName = requestedParameter,
                        resolvedParameterName = param.Definition?.Name,
                        builtInParameter = matchedBuiltInParameter,
                        success = false, message = $"Parameter '{requestedParameter}' is read-only" });
                    failCount++;
                    continue;
                }

                try
                {
                    bool isClear = req.Value == null || (req.Value is JToken jt && jt.Type == JTokenType.Null);
                    bool set = isClear ? ClearParameterValue(param) : SetParameterValue(param, req.Value);
                    if (set)
                    {
                        results.Add(new { elementId = req.ElementId, parameterName = requestedParameter,
                            resolvedParameterName = param.Definition?.Name,
                            builtInParameter = matchedBuiltInParameter,
                            success = true, message = isClear ? "Parameter cleared" : "Parameter set successfully" });
                        successCount++;
                    }
                    else
                    {
                        results.Add(new { elementId = req.ElementId, parameterName = requestedParameter,
                            resolvedParameterName = param.Definition?.Name,
                            builtInParameter = matchedBuiltInParameter,
                            success = false, message = isClear ? "Failed to clear parameter value" : "Failed to set parameter value" });
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new { elementId = req.ElementId, parameterName = requestedParameter,
                        resolvedParameterName = param.Definition?.Name,
                        builtInParameter = matchedBuiltInParameter,
                        success = false, message = ex.Message });
                    failCount++;
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
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
            if (jToken.Type == JTokenType.Null || jToken.Type == JTokenType.Undefined)
                return false;

            switch (param.StorageType)
            {
                case StorageType.String:
                    return param.Set(jToken.Value<string>() ?? "");
                case StorageType.Integer:
                    return param.Set(jToken.Value<int>());
                case StorageType.Double:
                    // A string token on a Double parameter is a display value (e.g. "3000 mm",
                    // "3 m"): let Revit parse the unit + locale via SetValueString. A numeric
                    // token is a raw internal-units value (feet) — preserve existing behavior.
                    if (jToken.Type == JTokenType.String)
                        return SetDoubleFromString(param, jToken.Value<string>() ?? "");
                    return param.Set(jToken.Value<double>());
#if REVIT2024_OR_GREATER
                case StorageType.ElementId:
                    return param.Set(new ElementId(jToken.Value<long>()));
#else
                case StorageType.ElementId:
                    return param.Set(new ElementId((int)jToken.Value<long>()));
#endif
                default:
                    return false;
            }
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
                if (value is string s) return SetDoubleFromString(param, s);
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

    /// <summary>
    /// Sets a Double parameter from a string. A bare number ("3000") is treated as a raw
    /// internal-units value for backward compatibility; anything with a unit or non-numeric
    /// content ("3000 mm", "3 m") is handed to SetValueString for unit-/locale-aware parsing.
    /// </summary>
    private static bool SetDoubleFromString(Parameter param, string text)
    {
        var trimmed = (text ?? "").Trim();
        if (double.TryParse(trimmed, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var raw))
            return param.Set(raw);
        return param.SetValueString(trimmed);
    }

    /// <summary>Clears a parameter to its empty/zero value.</summary>
    private static bool ClearParameterValue(Parameter param)
    {
        switch (param.StorageType)
        {
            case StorageType.String:
                return param.Set(string.Empty);
            case StorageType.Integer:
                return param.Set(0);
            case StorageType.Double:
                return param.Set(0.0);
            case StorageType.ElementId:
                return param.Set(ElementId.InvalidElementId);
            default:
                return false;
        }
    }

    private class SetParameterRequest
    {
        public long ElementId { get; set; }
        public string ParameterName { get; set; } = "";
        public string BuiltInParameter { get; set; } = "";
        public object? Value { get; set; }
    }
}
