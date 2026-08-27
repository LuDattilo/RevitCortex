using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

[ToolSafety(true, false)]
public class GetElementParametersTool : ICortexTool
{
    public string Name => "get_element_parameters";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public string Description =>
        "Get parameters of elements by ID. Numeric values are returned in project display units with " +
        "an explicit unit and the Revit internal value. Missing IDs are reported in notFoundIds, " +
        "never as an element with empty parameters.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var elementIds = input["elementIds"]?.ToObject<long[]>();
        if (elementIds == null || elementIds.Length == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "elementIds is required and cannot be empty",
                suggestion: "Provide an array of Revit element IDs, e.g. {\"elementIds\": [606873]}");

        var includeTypeParams = input["includeTypeParameters"]?.Value<bool>() ?? true;
        var requestedNames = input["parameterNames"]?.ToObject<string[]>() ?? Array.Empty<string>();

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var results = new List<object>();
        var notFoundIds = new List<long>();
        var unresolved = new List<object>();
        // Cache type elements to avoid repeated lookups when many elements share the same type
        var typeCache = new Dictionary<ElementId, Element?>();

        foreach (var id in elementIds)
        {
            var elementId = new ElementId(id);
            var element = doc.GetElement(elementId);
            if (element == null)
            {
                // A deleted or foreign ID must never look like an element with no
                // parameters: it gets found=false here and an entry in notFoundIds.
                notFoundIds.Add(id);
                results.Add(new
                {
                    elementId = id,
                    found = false,
                    error = $"Element {id} does not exist in this document (deleted, or from a linked model)."
                });
                continue;
            }

            var parameters = new List<object>();

            if (requestedNames.Length > 0)
            {
                // Targeted mode: language-independent resolution, and every name that
                // cannot be resolved is reported instead of returning an empty value.
                foreach (var requested in requestedNames)
                {
                    var parameter = ParameterNameResolver.Resolve(element, requested, doc, out var matchedBy);
                    if (parameter == null)
                    {
                        unresolved.Add(new
                        {
                            elementId = id,
                            requested,
                            suggestions = ParameterNameResolver.Suggest(
                                requested, ParameterNameResolver.AvailableNames(element, doc))
                        });
                        continue;
                    }

                    parameters.Add(ExtractParameter(parameter, isType: false, requested, matchedBy));
                }
            }
            else
            {
                foreach (Parameter param in element.Parameters)
                    parameters.Add(ExtractParameter(param, isType: false));

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
                                parameters.Add(ExtractParameter(param, isType: true));
                        }
                    }
                }
            }

            results.Add(new
            {
                elementId = element.Id.Value,
                found = true,
                elementName = element.Name,
                category = element.Category?.Name,
                categoryBic = CategoryResolver.DescribeBuiltInCategory(element.Category),
                parameters
            });
        }

        var foundCount = elementIds.Length - notFoundIds.Count;
        var message = notFoundIds.Count == 0
            ? $"Retrieved parameters for {foundCount} element(s)."
            : $"Retrieved parameters for {foundCount} of {elementIds.Length} element(s); " +
              $"{notFoundIds.Count} ID(s) do not exist in this document.";

        return CortexResult<object>.Ok(new
        {
            message,
            requestedCount = elementIds.Length,
            foundCount,
            notFoundCount = notFoundIds.Count,
            notFoundIds,
            unresolvedParameterNames = unresolved,
            unitPolicy = "value = project display units, unit names it, internalValue = Revit internal units (ft/ft²/ft³)",
            elements = results
        });
    }

    private static object ExtractParameter(
        Parameter param, bool isType, string? requestedName = null, string? matchedBy = null)
    {
        var prefix = isType ? "[Type] " : "";
        var formatted = ParameterValueFormatter.Format(param);

        return new
        {
            name = prefix + (param.Definition?.Name ?? "Unknown"),
            requestedName,
            matchedBy,
            builtInParameter = ParameterLookup.GetBuiltInParameterName(param),
            value = formatted.Value,
            displayValue = formatted.DisplayValue,
            unit = formatted.Unit,
            internalValue = formatted.InternalValue,
            hasValue = param.HasValue,
            isReadOnly = param.IsReadOnly,
            isShared = param.IsShared,
            storageType = param.StorageType.ToString(),
            groupName = param.Definition?.GetGroupTypeId()?.TypeId ?? string.Empty
        };
    }
}
