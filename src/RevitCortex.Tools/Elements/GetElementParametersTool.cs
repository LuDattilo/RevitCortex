using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

public class GetElementParametersTool : ICortexTool
{
    public string Name => "get_element_parameters";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Get Element Parameters";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var elementIds = input["elementIds"]?.ToObject<long[]>();
        if (elementIds == null || elementIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is required and cannot be empty",
                suggestion: "Provide an array of Revit element IDs, e.g. {\"elementIds\": [606873]}");

        var includeTypeParams = input["includeTypeParameters"]?.Value<bool>() ?? true;

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var results = new List<object>();
        // Cache type elements to avoid repeated lookups when many elements share the same type
        var typeCache = new Dictionary<ElementId, Element?>();

        foreach (var id in elementIds)
        {
#if REVIT2024_OR_GREATER
            var elementId = new ElementId(id);
#else
            var elementId = new ElementId((int)id);
#endif
            var element = doc.GetElement(elementId);
            if (element == null)
            {
                results.Add(new { elementId = id, error = $"Element {id} not found" });
                continue;
            }

            var parameters = new List<object>();

            // Instance parameters
            foreach (Parameter param in element.Parameters)
            {
                parameters.Add(ExtractParameter(param, isType: false));
            }

            // Type parameters (with cache)
            if (includeTypeParams)
            {
                var typeId = element.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    if (!typeCache.TryGetValue(typeId, out var typeElement))
                    {
                        typeElement = doc.GetElement(typeId);
                        typeCache[typeId] = typeElement;
                    }

                    if (typeElement != null)
                    {
                        foreach (Parameter param in typeElement.Parameters)
                        {
                            parameters.Add(ExtractParameter(param, isType: true));
                        }
                    }
                }
            }

            results.Add(new
            {
#if REVIT2024_OR_GREATER
                elementId = element.Id.Value,
#else
                elementId = element.Id.IntegerValue,
#endif
                elementName = element.Name,
                category = element.Category?.Name,
                parameters
            });
        }

        return CortexResult<object>.Ok(new
        {
            message = $"Retrieved parameters for {results.Count} elements",
            elements = results
        });
    }

    private static object ExtractParameter(Parameter param, bool isType)
    {
        var prefix = isType ? "[Type] " : "";
        object? value = null;

        if (param.HasValue)
        {
            value = param.StorageType switch
            {
                StorageType.String => param.AsString(),
                StorageType.Integer => (object)param.AsInteger(),
                StorageType.Double => param.AsDouble(),
#if REVIT2024_OR_GREATER
                StorageType.ElementId => param.AsElementId().Value,
#else
                StorageType.ElementId => (object)param.AsElementId().IntegerValue,
#endif
                _ => param.AsValueString()
            };
        }

        return new
        {
            name = prefix + (param.Definition?.Name ?? "Unknown"),
            value,
            hasValue = param.HasValue,
            isReadOnly = param.IsReadOnly,
            isShared = param.IsShared,
            storageType = param.StorageType.ToString(),
            groupName = param.Definition?.GetGroupTypeId()?.TypeId ?? string.Empty
        };
    }
}
