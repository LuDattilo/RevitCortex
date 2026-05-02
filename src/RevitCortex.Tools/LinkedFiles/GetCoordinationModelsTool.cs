using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
#if REVIT2026_OR_GREATER
using Autodesk.Revit.DB.ExternalData;
#endif
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.LinkedFiles;

/// <summary>
/// Read-only listing of Autodesk Revit Coordination Models.
/// </summary>
public class GetCoordinationModelsTool : ICortexTool
{
    private const int DefaultMaxInstances = 100;
    private const int MaxInstancesCap = 250;
    private const double MmPerFoot = 304.8;

    public string Name => "get_coordination_models";
    public string Category => "LinkedFiles";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read-only listing of Autodesk Revit Coordination Models with type metadata and optional instances.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = ToolHelpers.GetDocument(session);
        if (doc == null)
        {
            return CortexResult<object>.Fail(
                CortexErrorCode.InvalidInput,
                "No active document in session",
                suggestion: "Open a Revit document before using this tool");
        }

        var rawMaxInstances = input["maxInstances"]?.Value<int?>();
        if (rawMaxInstances.HasValue && rawMaxInstances.Value < 0)
        {
            return CortexResult<object>.Fail(
                CortexErrorCode.InvalidInput,
                "maxInstances cannot be negative",
                suggestion: "Use 0 or a positive integer up to 250");
        }

        var nameFilter = input["nameFilter"]?.Value<string>();
        var includeInstances = input["includeInstances"]?.Value<bool?>() ?? true;
        var maxInstances = NormalizeMaxInstances(rawMaxInstances);

        try
        {
#if REVIT2026_OR_GREATER
            return ExecuteWithCoordinationModelApi(doc, nameFilter, includeInstances, maxInstances);
#else
            return UnsupportedTargetResult();
#endif
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    public static int NormalizeMaxInstances(int? rawValue)
    {
        if (!rawValue.HasValue)
        {
            return DefaultMaxInstances;
        }

        return Math.Min(rawValue.Value, MaxInstancesCap);
    }

    public static bool MatchesNameFilter(string? filter, string candidate)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return candidate.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool MatchesAnyNameFilter(string? filter, string typeName, IEnumerable<string> instanceNames)
    {
        if (MatchesNameFilter(filter, typeName))
        {
            return true;
        }

        foreach (var instanceName in instanceNames)
        {
            if (MatchesNameFilter(filter, instanceName))
            {
                return true;
            }
        }

        return false;
    }

    private static CortexResult<object> UnsupportedTargetResult()
    {
        return CortexResult<object>.Ok(new
        {
            apiAvailable = false,
            modelCount = 0,
            totalInstances = 0,
            models = new object[0],
            message = "Coordination Model API is not available for this Revit target."
        });
    }

#if REVIT2026_OR_GREATER
    private static CortexResult<object> ExecuteWithCoordinationModelApi(
        Document doc,
        string? nameFilter,
        bool includeInstances,
        int maxInstances)
    {
        var typeIds = CoordinationModelLinkUtils.GetAllCoordinationModelTypeIds(doc).ToList();
        var instanceIds = CoordinationModelLinkUtils.GetAllCoordinationModelInstanceIds(doc).ToList();

        var instancesByType = new Dictionary<ElementId, List<Element>>();
        foreach (var instanceId in instanceIds)
        {
            var instance = doc.GetElement(instanceId);
            if (instance == null)
            {
                continue;
            }

            var typeId = instance.GetTypeId();
            List<Element>? typeInstances;
            if (!instancesByType.TryGetValue(typeId, out typeInstances))
            {
                typeInstances = new List<Element>();
                instancesByType[typeId] = typeInstances;
            }

            typeInstances.Add(instance);
        }

        var models = new List<object>();
        var totalMatchedInstances = 0;
        var totalReturnedInstances = 0;
        var remainingInstances = maxInstances;

        foreach (var typeId in typeIds)
        {
            var cmType = doc.GetElement(typeId) as ElementType;
            if (cmType == null)
            {
                continue;
            }

            var data = CoordinationModelLinkUtils.GetCoordinationModelTypeData(doc, cmType);
            var typeName = GetTypeName(cmType, data);
            List<Element>? typeInstances;
            if (!instancesByType.TryGetValue(typeId, out typeInstances))
            {
                typeInstances = new List<Element>();
            }

            if (!MatchesAnyNameFilter(nameFilter, typeName, typeInstances.Select(i => i.Name)))
            {
                continue;
            }

            totalMatchedInstances += typeInstances.Count;
            var returnedInstances = new List<object>();
            if (includeInstances && remainingInstances > 0)
            {
                foreach (var instance in typeInstances.Take(remainingInstances))
                {
                    returnedInstances.Add(CreateInstancePayload(instance));
                    totalReturnedInstances++;
                    remainingInstances--;

                    if (remainingInstances == 0)
                    {
                        break;
                    }
                }
            }

            var pathType = data != null ? data.GetPathType().ToString() : null;
            models.Add(new
            {
                typeId = ToolHelpers.GetElementIdValue(cmType.Id),
                typeName,
                pathType,
                isCloud = data != null && data.GetPathType() == CoordinationModelLinkPathType.Cloud,
                path = GetPath(data),
                instanceCount = typeInstances.Count,
                instances = returnedInstances
            });
        }

        return CortexResult<object>.Ok(new
        {
            apiAvailable = true,
            modelCount = models.Count,
            totalInstances = totalReturnedInstances,
            matchedInstances = totalMatchedInstances,
            models,
            message = models.Count == 0
                ? "No coordination models found in the active document."
                : $"Found {models.Count} coordination model type(s)."
        });
    }

    private static string GetTypeName(ElementType cmType, CoordinationModelLinkData? data)
    {
        if (data != null && !string.IsNullOrWhiteSpace(data.ModelName))
        {
            return data.ModelName;
        }

        return cmType.Name;
    }

    private static string GetPath(CoordinationModelLinkData? data)
    {
        if (data == null)
        {
            return "";
        }

        if (!string.IsNullOrWhiteSpace(data.DisplayPath))
        {
            return data.DisplayPath;
        }

        return data.SourcePath ?? "";
    }

    private static object CreateInstancePayload(Element instance)
    {
        var origin = GetOriginPayload(instance);
        return new
        {
            instanceId = ToolHelpers.GetElementIdValue(instance.Id),
            name = instance.Name,
            origin
        };
    }

    private static object? GetOriginPayload(Element instance)
    {
        var transform = GetInstanceTransform(instance);
        if (transform != null)
        {
            return CreateOriginPayload(transform.Origin);
        }

        var locationPoint = instance.Location as LocationPoint;
        if (locationPoint == null)
        {
            return null;
        }

        return CreateOriginPayload(locationPoint.Point);
    }

    private static Transform? GetInstanceTransform(Element instance)
    {
        var transform = InvokeTransformMethod(instance, "GetTotalTransform");
        if (transform != null)
        {
            return transform;
        }

        return InvokeTransformMethod(instance, "GetTransform");
    }

    private static Transform? InvokeTransformMethod(Element instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, Type.EmptyTypes);
        if (method == null)
        {
            return null;
        }

        return method.Invoke(instance, null) as Transform;
    }

    private static object CreateOriginPayload(XYZ point)
    {
        return new
        {
            x = Math.Round(point.X * MmPerFoot, 1),
            y = Math.Round(point.Y * MmPerFoot, 1),
            z = Math.Round(point.Z * MmPerFoot, 1)
        };
    }
#endif
}
