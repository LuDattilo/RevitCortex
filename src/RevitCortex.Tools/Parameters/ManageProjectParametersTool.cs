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
    public string Description => "Lists, creates, deletes, or modifies project parameters.";
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
                "delete" => DeleteParameter(doc, input, session),
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

            var paramType = definition.GetDataType()?.TypeId ?? "Unknown";
            var paramGroup = definition.GetGroupTypeId()?.TypeId ?? "Unknown";

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
                "parameterName is required for create action. Example: {\"action\":\"create\",\"parameterName\":\"MyParam\",\"categories\":[\"OST_Doors\"],\"dataType\":\"Text\",\"isInstance\":true}. Do not retry with empty params — ask the user what parameter name and categories to use.");

        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var isInstance = input["isInstance"]?.Value<bool>() ?? true;
        var dataType = input["dataType"]?.Value<string>() ?? "Text";

        if (categories.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "categories array is required for create action. Provide OST_* codes (preferred, language-independent) or English display names, e.g. [\"OST_Doors\",\"OST_Windows\"]. Do not retry with the same empty input — ask the user which categories the new parameter should bind to.");

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

#if REVIT2023_OR_GREATER
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

    private static CortexResult<object> DeleteParameter(Document doc, JObject input, CortexSession session)
    {
        var parameterName = input["parameterName"]?.Value<string>();
        if (string.IsNullOrEmpty(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterName is required for delete action. Example: {\"action\":\"delete\",\"parameterName\":\"MyParam\"}. Do not retry with empty params — first call {\"action\":\"list\"} to discover existing parameter names, then ask the user which one to delete.");

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

        if (!session.RequestConfirmation("delete project parameter", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

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
                "parameterName is required for modify action. Example: {\"action\":\"modify\",\"parameterName\":\"MyParam\",\"categories\":[\"OST_Windows\"],\"categoriesMode\":\"add\"}. Do not retry with empty params — first call {\"action\":\"list\"} to discover existing names.");

        var requestedCategories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var categoriesMode = (input["categoriesMode"]?.Value<string>() ?? "add").ToLowerInvariant();

        if (categoriesMode != "add" && categoriesMode != "remove" && categoriesMode != "replace")
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unknown categoriesMode: {categoriesMode}",
                suggestion: "Use one of: add, remove, replace");

        if (requestedCategories.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "categories array is required for modify action. Provide OST_* codes (preferred, language-independent) or English display names, e.g. [\"OST_Doors\"]. Do not retry with empty categories — ask the user which category bindings to modify.");

        // Collect all (Definition, ElementBinding) pairs up-front: the BindingMap iterator
        // must not be alive when we mutate the map (Insert/Remove/ReInsert), otherwise Revit
        // silently rejects the mutation and returns false.
        var allDefs = new List<Definition>();
        var allBindings = new Dictionary<string, ElementBinding>(StringComparer.Ordinal);
        {
            var map = doc.ParameterBindings;
            var iter = map.ForwardIterator();
            while (iter.MoveNext())
            {
                var d = iter.Key;
                if (d == null) continue;
                allDefs.Add(d);
                if (iter.Current is ElementBinding eb)
                    allBindings[d.Name] = eb;
            }
        }

        Definition? targetDef = allDefs.FirstOrDefault(d => d.Name == parameterName);
        ElementBinding? existingBinding = targetDef != null && allBindings.TryGetValue(targetDef.Name, out var b) ? b : null;

        if (targetDef == null || existingBinding == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Parameter '{parameterName}' not found in project bindings");

        var app = doc.Application;
        var addedCategories = new List<string>();
        var removedCategories = new List<string>();

        CategorySet newCategorySet;

        if (categoriesMode == "replace")
        {
            newCategorySet = BuildCategorySet(doc, app, requestedCategories);
            if (newCategorySet.Size == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No valid categories resolved for replace");
            foreach (Category c in newCategorySet) addedCategories.Add(c.Name);
        }
        else
        {
            // Build the target category set from category NAMES (not Category objects
            // lifted off the existing binding). Category instances re-fetched via
            // doc.Settings.Categories are the canonical entries the BindingMap uses
            // for equality; copying from existingBinding.Categories has been observed
            // to leave the bindingMap mutation in an inconsistent state in Revit 2025.
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Category c in existingBinding.Categories) existingNames.Add(c.Name);

            if (categoriesMode == "add")
            {
                // Start with existing names, add the new ones, resolve each from doc.Settings.
                var resultNames = new List<string>(existingNames);
                foreach (var catName in requestedCategories)
                {
                    var catId = CategoryResolver.ResolveToId(doc, catName);
                    if (catId == null) continue;
                    var cat = Autodesk.Revit.DB.Category.GetCategory(doc, catId);
                    if (cat == null || !cat.AllowsBoundParameters) continue;
                    if (!existingNames.Contains(cat.Name))
                    {
                        resultNames.Add(cat.Name);
                        addedCategories.Add(cat.Name);
                        existingNames.Add(cat.Name);
                    }
                }
                newCategorySet = BuildCategorySet(doc, app, resultNames);
            }
            else // remove
            {
                var toRemoveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var catName in requestedCategories)
                {
                    var catId = CategoryResolver.ResolveToId(doc, catName);
                    if (catId == null) continue;
                    var cat = Autodesk.Revit.DB.Category.GetCategory(doc, catId);
                    if (cat == null) continue;
                    if (existingNames.Contains(cat.Name))
                    {
                        toRemoveNames.Add(cat.Name);
                        removedCategories.Add(cat.Name);
                    }
                }
                var keptNames = existingNames.Where(n => !toRemoveNames.Contains(n)).ToList();
                if (keptNames.Count == 0)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        "Refusing to remove all categories: a project parameter must remain bound to at least one category. Use 'delete' action to remove the parameter entirely.");
                newCategorySet = BuildCategorySet(doc, app, keptNames);
            }
        }

        // Post-build sanity: BuildCategorySet silently drops categories that fail
        // AllowsBoundParameters, so a 'replace' with only-invalid categories can reach
        // here with an empty set. Reject explicitly so the caller sees InvalidInput
        // rather than the misleading PermissionDenied that ReInsert would surface.
        if (newCategorySet.Size == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Resolved category set is empty (none of the requested categories allow parameter binding).",
                suggestion: "Check the category names and ensure they support bound parameters.");

        bool isInstance = existingBinding is InstanceBinding;
        ElementBinding newBinding = isInstance
            ? (ElementBinding)app.Create.NewInstanceBinding(newCategorySet)
            : (ElementBinding)app.Create.NewTypeBinding(newCategorySet);

        // Fresh BindingMap reference inside the transaction and collect-then-mutate pattern
        // (see the collection loop above): iterating while holding a BindingMap reference and
        // then mutating via the same reference silently fails. ReInsert against a built-in
        // Revit-owned parameter (e.g. 'Material') returns false by design.
        using var tx = new Transaction(doc, "RevitCortex: Modify Project Parameter");
        tx.Start();
        var freshMap = doc.ParameterBindings;
        bool persistReInsert = freshMap.ReInsert(targetDef, newBinding);
        tx.Commit();

        if (!persistReInsert)
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                $"Revit rejected the modification of parameter '{parameterName}'. This usually means the parameter is built-in or owned by Revit/an add-in and its category bindings cannot be changed via the API.",
                suggestion: "Try the modification on a user-created project parameter instead.");

        var allCategories = newCategorySet.Cast<Category>().Select(c => c.Name).ToList();

        return CortexResult<object>.Ok(new
        {
            action = "modify",
            parameterName,
            categoriesMode,
            addedCategories,
            removedCategories,
            totalCategories = allCategories
        });
    }

    private static CategorySet BuildCategorySet(Document doc, Autodesk.Revit.ApplicationServices.Application app, IEnumerable<string> categoryNames)
    {
        var set = app.Create.NewCategorySet();
        foreach (var name in categoryNames)
        {
            var catId = CategoryResolver.ResolveToId(doc, name);
            if (catId == null) continue;
            var cat = Autodesk.Revit.DB.Category.GetCategory(doc, catId);
            if (cat != null && cat.AllowsBoundParameters && !set.Contains(cat))
                set.Insert(cat);
        }
        return set;
    }

#if REVIT2023_OR_GREATER
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
