using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Parameters;

/// <summary>
/// Lists, creates, deletes, or modifies project parameters.
/// </summary>
public class ManageProjectParametersTool : ICortexTool
{
    public string Name => "manage_project_parameters";
    public string Category => "Parameters";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var action = input["action"]?.Value<string>() ?? "list";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "list"   => ListParameters(doc),
                "create" => CreateParameter(doc, input),
                "delete" => DeleteParameter(doc, input),
                "modify" => ModifyParameter(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use one of: list, create, delete, modify")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to manage project parameters: {ex.Message}");
        }
    }

    private static CortexResult<object> ListParameters(Document doc)
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

            var isShared = definition is ExternalDefinition;
            var guid = isShared ? ((ExternalDefinition)definition).GUID.ToString() : "";
            var isInstance = binding is InstanceBinding;

#if REVIT2024_OR_GREATER
            var paramType = definition.GetDataType()?.TypeId ?? "Unknown";
            var paramGroup = definition.GetGroupTypeId()?.TypeId ?? "Unknown";
#else
            var paramType = definition.ParameterType.ToString();
            var paramGroup = definition.ParameterGroup.ToString();
#endif

            parameters.Add(new
            {
                name = definition.Name,
                isShared,
                guid,
                isInstance,
                parameterType = paramType,
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

    private static CortexResult<object> CreateParameter(Document doc, JObject input)
    {
        var parameterName = input["parameterName"]?.Value<string>();
        if (string.IsNullOrEmpty(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterName is required for create action");

        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var isInstance = input["isInstance"]?.Value<bool>() ?? true;
        var dataType = input["dataType"]?.Value<string>() ?? "Text";

        if (categories.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "categories array is required for create action");

        var app = doc.Application;

        // Create temp shared parameter file for project parameter
        var originalFile = app.SharedParametersFilename;
        var tempPath = Path.Combine(Path.GetTempPath(), $"RevitCortex_TempParams_{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempPath, "");

        try
        {
            app.SharedParametersFilename = tempPath;
            var tempSharedFile = app.OpenSharedParameterFile();
            var group = tempSharedFile.Groups.Create("RevitCortex_Temp");

#if REVIT2024_OR_GREATER
            var specTypeId = ResolveSpecTypeId(dataType);
            var options = new ExternalDefinitionCreationOptions(parameterName, specTypeId);
#else
            var paramType = ResolveParameterType(dataType);
            var options = new ExternalDefinitionCreationOptions(parameterName, paramType);
#endif

            var definition = group.Definitions.Create(options);

            var categorySet = app.Create.NewCategorySet();
            var boundCategories = new List<string>();

            foreach (var catName in categories)
            {
                var catId = CategoryResolver.ResolveToId(doc, catName);
                if (catId == null) continue;
                var cat = Autodesk.Revit.DB.Category.GetCategory(doc, catId);
                if (cat != null && cat.AllowsBoundParameters)
                {
                    categorySet.Insert(cat);
                    boundCategories.Add(cat.Name);
                }
            }

            if (categorySet.Size == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No valid categories resolved");

            ElementBinding binding = isInstance
                ? (ElementBinding)app.Create.NewInstanceBinding(categorySet)
                : (ElementBinding)app.Create.NewTypeBinding(categorySet);

            using var tx = new Transaction(doc, "RevitCortex: Create Project Parameter");
            tx.Start();
            doc.ParameterBindings.Insert(definition, binding);
            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                action = "create",
                parameterName,
                isInstance,
                dataType,
                boundCategories
            });
        }
        finally
        {
            // Restore original shared parameter file
            if (!string.IsNullOrEmpty(originalFile))
                app.SharedParametersFilename = originalFile;
            try { File.Delete(tempPath); } catch { /* cleanup best-effort */ }
        }
    }

    private static CortexResult<object> DeleteParameter(Document doc, JObject input)
    {
        var parameterName = input["parameterName"]?.Value<string>();
        if (string.IsNullOrEmpty(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterName is required for delete action");

        var bindingMap = doc.ParameterBindings;
        var iterator = bindingMap.ForwardIterator();
        Definition? targetDef = null;

        while (iterator.MoveNext())
        {
            if (iterator.Key.Name == parameterName)
            {
                targetDef = iterator.Key;
                break;
            }
        }

        if (targetDef == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Parameter '{parameterName}' not found in project bindings");

        using var tx = new Transaction(doc, "RevitCortex: Delete Project Parameter");
        tx.Start();
        var removed = bindingMap.Remove(targetDef);
        tx.Commit();

        return CortexResult<object>.Ok(new
        {
            action = "delete",
            parameterName,
            success = removed
        });
    }

    private static CortexResult<object> ModifyParameter(Document doc, JObject input)
    {
        var parameterName = input["parameterName"]?.Value<string>();
        if (string.IsNullOrEmpty(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterName is required for modify action");

        var additionalCategories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();

        var bindingMap = doc.ParameterBindings;
        var iterator = bindingMap.ForwardIterator();
        Definition? targetDef = null;
        ElementBinding? existingBinding = null;

        while (iterator.MoveNext())
        {
            if (iterator.Key.Name == parameterName)
            {
                targetDef = iterator.Key;
                existingBinding = iterator.Current as ElementBinding;
                break;
            }
        }

        if (targetDef == null || existingBinding == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Parameter '{parameterName}' not found in project bindings");

        var app = doc.Application;
        var categorySet = existingBinding.Categories;
        var addedCategories = new List<string>();

        foreach (var catName in additionalCategories)
        {
            var catId = CategoryResolver.ResolveToId(doc, catName);
            if (catId == null) continue;
            var cat = Autodesk.Revit.DB.Category.GetCategory(doc, catId);
            if (cat != null && cat.AllowsBoundParameters && !categorySet.Contains(cat))
            {
                categorySet.Insert(cat);
                addedCategories.Add(cat.Name);
            }
        }

        ElementBinding newBinding = existingBinding is InstanceBinding
            ? (ElementBinding)app.Create.NewInstanceBinding(categorySet)
            : (ElementBinding)app.Create.NewTypeBinding(categorySet);

        using var tx = new Transaction(doc, "RevitCortex: Modify Project Parameter");
        tx.Start();
        bindingMap.ReInsert(targetDef, newBinding);
        tx.Commit();

        var allCategories = categorySet.Cast<Category>().Select(c => c.Name).ToList();

        return CortexResult<object>.Ok(new
        {
            action = "modify",
            parameterName,
            addedCategories,
            totalCategories = allCategories
        });
    }

#if REVIT2024_OR_GREATER
    private static ForgeTypeId ResolveSpecTypeId(string dataType)
    {
        return dataType.ToLowerInvariant() switch
        {
            "text" or "string" => SpecTypeId.String.Text,
            "integer" or "int" => SpecTypeId.Int.Integer,
            "number" or "double" or "real" => SpecTypeId.Number,
            "length" => SpecTypeId.Length,
            "area" => SpecTypeId.Area,
            "volume" => SpecTypeId.Volume,
            "angle" => SpecTypeId.Angle,
            "yesno" or "boolean" or "bool" => SpecTypeId.Boolean.YesNo,
            "url" => SpecTypeId.String.Url,
            _ => SpecTypeId.String.Text
        };
    }
#else
    private static ParameterType ResolveParameterType(string dataType)
    {
        return dataType.ToLowerInvariant() switch
        {
            "text" or "string" => ParameterType.Text,
            "integer" or "int" => ParameterType.Integer,
            "number" or "double" or "real" => ParameterType.Number,
            "length" => ParameterType.Length,
            "area" => ParameterType.Area,
            "volume" => ParameterType.Volume,
            "angle" => ParameterType.Angle,
            "yesno" or "boolean" or "bool" => ParameterType.YesNo,
            "url" => ParameterType.URL,
            _ => ParameterType.Text
        };
    }
#endif
}
