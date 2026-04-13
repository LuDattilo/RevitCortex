using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Duplicates a loadable family type (FamilySymbol) with a new name,
/// optionally setting parameter values on the new type in the same transaction.
/// </summary>
public class DuplicateFamilyTypeTool : ICortexTool
{
    public string Name => "duplicate_family_type";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Duplicates a loadable family type (door, window, furniture, etc.) with a new name. Optionally sets type parameters on the new type in the same operation.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var sourceTypeId   = input["sourceTypeId"]?.Value<long?>();
        var sourceTypeName = input["sourceTypeName"]?.Value<string>();
        var familyName     = input["familyName"]?.Value<string>();
        var newName        = input["newName"]?.Value<string>();
        var parameterOverrides = input["parameterOverrides"] as JObject;

        if (string.IsNullOrWhiteSpace(newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "newName is required",
                suggestion: "Provide the name for the duplicated type");

        try
        {
            FamilySymbol? source = null;

            if (sourceTypeId.HasValue)
            {
                source = doc!.GetElement(ToolHelpers.ToElementId(sourceTypeId.Value)) as FamilySymbol;
                if (source == null)
                    return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                        $"Element {sourceTypeId} not found or is not a FamilySymbol",
                        suggestion: "Provide a valid loadable family type ID. Use get_available_family_types to list them.");
            }
            else if (!string.IsNullOrWhiteSpace(sourceTypeName))
            {
                source = FindFamilySymbol(doc!, sourceTypeName!, familyName);
                if (source == null)
                {
                    var hint = string.IsNullOrWhiteSpace(familyName)
                        ? "If the type name exists in multiple families, add familyName to disambiguate."
                        : $"No type '{sourceTypeName}' found in family '{familyName}'.";
                    return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                        $"Family type '{sourceTypeName}' not found",
                        suggestion: $"{hint} Use get_available_family_types to list available types.");
                }
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Provide sourceTypeId or sourceTypeName to identify the source type");
            }

            // Check if target name already exists in the same family
            var existing = FindFamilySymbol(doc!, newName!, source.FamilyName);
            if (existing != null)
            {
                return CortexResult<object>.Ok(new
                {
                    typeId = ToolHelpers.GetElementIdValue(existing),
                    typeName = existing.Name,
                    familyName = existing.FamilyName,
                    categoryName = existing.Category?.Name ?? "",
                    alreadyExisted = true
                });
            }

            using (var tx = new Transaction(doc!, "RevitCortex: Duplicate Family Type"))
            {
                tx.Start();

                var newType = source.Duplicate(newName) as FamilySymbol;
                if (newType == null)
                {
                    tx.RollBack();
                    return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                        "Duplicate returned null — the type could not be created");
                }

                // Apply parameter overrides if provided
                var appliedParams = new List<object>();
                var failedParams = new List<object>();

                if (parameterOverrides != null)
                {
                    foreach (var prop in parameterOverrides.Properties())
                    {
                        var param = newType.LookupParameter(prop.Name);
                        if (param == null || param.IsReadOnly)
                        {
                            failedParams.Add(new { name = prop.Name, reason = param == null ? "not found" : "read-only" });
                            continue;
                        }

                        try
                        {
                            bool set = SetParameterValue(param, prop.Value);
                            if (set)
                                appliedParams.Add(new { name = prop.Name, value = prop.Value.ToString() });
                            else
                                failedParams.Add(new { name = prop.Name, reason = "type mismatch" });
                        }
                        catch (Exception ex)
                        {
                            failedParams.Add(new { name = prop.Name, reason = ex.Message });
                        }
                    }
                }

                tx.Commit();

                var result = new Dictionary<string, object?>
                {
                    ["typeId"] = ToolHelpers.GetElementIdValue(newType),
                    ["typeName"] = newType.Name,
                    ["familyName"] = newType.FamilyName,
                    ["categoryName"] = newType.Category?.Name ?? "",
                    ["sourceTypeName"] = source.Name,
                    ["alreadyExisted"] = false
                };

                if (appliedParams.Count > 0)
                    result["appliedParameters"] = appliedParams;
                if (failedParams.Count > 0)
                    result["failedParameters"] = failedParams;

                return CortexResult<object>.Ok(result);
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to duplicate family type: {ex.Message}");
        }
    }

    private static FamilySymbol? FindFamilySymbol(Document doc, string typeName, string? familyName)
    {
        var collector = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>();

        if (!string.IsNullOrWhiteSpace(familyName))
            collector = collector.Where(fs => fs.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase));

        return collector.FirstOrDefault(fs => fs.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SetParameterValue(Parameter param, JToken value)
    {
        switch (param.StorageType)
        {
            case StorageType.Double:
                if (value.Type == JTokenType.Float || value.Type == JTokenType.Integer)
                {
                    param.Set(value.Value<double>());
                    return true;
                }
                return false;

            case StorageType.Integer:
                if (value.Type == JTokenType.Integer)
                {
                    param.Set(value.Value<int>());
                    return true;
                }
                return false;

            case StorageType.String:
                param.Set(value.ToString());
                return true;

            case StorageType.ElementId:
                if (value.Type == JTokenType.Integer)
                {
                    param.Set(ToolHelpers.ToElementId(value.Value<long>()));
                    return true;
                }
                return false;

            default:
                return false;
        }
    }
}
