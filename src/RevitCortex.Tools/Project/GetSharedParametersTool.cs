using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Lists all project parameters (shared and project-specific) with their
/// bindings, parameter types, and applicable categories.
/// </summary>
public class GetSharedParametersTool : ICortexTool
{
    public string Name => "get_shared_parameters";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var categoryFilter = input["categoryFilter"]?.Value<string>() ?? "";

        try
        {
            var parameters = new List<object>();
            var bindingMap = doc.ParameterBindings;
            var iterator = bindingMap.ForwardIterator();

            while (iterator.MoveNext())
            {
                var definition = iterator.Key;
                var binding = iterator.Current as ElementBinding;
                if (binding == null) continue;

                var categories = binding.Categories.Cast<Category>()
                    .Select(c => c.Name)
                    .ToList();

                if (!string.IsNullOrEmpty(categoryFilter) &&
                    !categories.Any(c => c.IndexOf(categoryFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;

                var isShared = definition is ExternalDefinition;
                var guid = isShared ? ((ExternalDefinition)definition).GUID.ToString() : "";
                var isInstance = binding is InstanceBinding;

#if REVIT2024_OR_GREATER
                var paramType = definition.GetDataType().TypeId ?? "";
#else
                var paramType = definition.ParameterType.ToString();
#endif
                var paramGroup = definition.ParameterGroup.ToString();

                parameters.Add(new
                {
                    name        = definition.Name,
                    isShared,
                    guid,
                    isInstance,
                    parameterType  = paramType,
                    parameterGroup = paramGroup,
                    categories
                });
            }

            return CortexResult<object>.Ok(new
            {
                parameterCount = parameters.Count,
                parameters
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get shared parameters: {ex.Message}");
        }
    }
}
