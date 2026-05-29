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
                "list"             => ListParameters(doc),
                "create"           => CreateParameter(doc, input),
                "delete"           => DeleteParameter(doc, input, session),
                "modify"           => ModifyParameter(doc, input),
                "set_group"        => SetParameterGroup(doc, input, session),
                "set_binding_type" => SetBindingType(doc, input, session),
                "rename"           => RenameParameter(doc, input, session),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use one of: list, create, delete, modify, set_group, set_binding_type, rename")
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
            // OpenSharedParameterFile can return null (file not yet readable, locked,
            // or hijacked by another add-in). Without this guard the next line throws
            // a NullReferenceException that escapes as a generic "An error occurred
            // invoking" before the router can log or surface a useful message.
            if (tempSharedFile == null)
                return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                    "Could not open a temporary shared parameter file to back the new project parameter.",
                    suggestion: "Another add-in may be holding the shared parameter file. Close other Revit add-ins, or set a valid Shared Parameters file in Manage → Shared Parameters, then retry.");

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

            bool inserted;
            using (var tx = new Transaction(doc, "RevitCortex: Create Project Parameter"))
            {
                tx.Start();
                inserted = doc.ParameterBindings.Insert(definition, binding);
                tx.Commit();
            }

            if (!inserted)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rejected creating project parameter '{parameterName}'. A parameter with this name may already be bound.",
                    suggestion: "Call action:list to check existing parameters; use a different name or action:modify to change its bindings.");

            return CortexResult<object>.Ok(new
            {
                action = "create",
                parameterName,
                isInstance,
                dataType,
                boundCategories
            });
        }
        catch (Exception ex)
        {
            // Turn any unhandled Revit/IO/COM exception into a structured failure
            // with the real message, instead of a generic "An error occurred invoking".
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to create project parameter '{parameterName}': {ex.Message}",
                suggestion: "If another add-in is interfering with the shared parameter file, close it and retry, or create the parameter from a shared parameter file via add_shared_parameter.");
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

        // A shared-based project parameter (ExternalDefinition) is removed cleanly
        // via BindingMap.Remove. A NON-shared (internal) project parameter hits
        // Autodesk bug REVIT-136670: BindingMap.Remove returns true but leaves the
        // parameter in place. For those we must delete the underlying
        // ParameterElement instead. We branch accordingly and then VERIFY the
        // binding is actually gone before reporting success.
        bool isShared = targetDef is ExternalDefinition;
        string method;

        using (var tx = new Transaction(doc, "RevitCortex: Delete Project Parameter"))
        {
            tx.Start();

            if (isShared)
            {
                bindingMap.Remove(targetDef);
                method = "BindingMap.Remove";
            }
            else
            {
                // Find the ParameterElement backing this non-shared definition and
                // delete the element — the documented workaround for REVIT-136670.
                var paramElement = new FilteredElementCollector(doc)
                    .OfClass(typeof(ParameterElement))
                    .Cast<ParameterElement>()
                    .FirstOrDefault(pe => pe.GetDefinition()?.Name == parameterName);

                if (paramElement != null)
                {
                    doc.Delete(paramElement.Id);
                    method = "Document.Delete(ParameterElement)";
                }
                else
                {
                    // Fall back to Remove if the element can't be located.
                    bindingMap.Remove(targetDef);
                    method = "BindingMap.Remove (ParameterElement not found)";
                }
            }

            tx.Commit();
        }

        // Verify: re-scan the bindings. The parameter must no longer be present.
        bool stillBound = false;
        var verifyIter = doc.ParameterBindings.ForwardIterator();
        while (verifyIter.MoveNext())
        {
            if (verifyIter.Key.Name == parameterName) { stillBound = true; break; }
        }

        if (stillBound)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Parameter '{parameterName}' could not be removed (still bound after delete via {method}). " +
                (isShared ? "" : "This is a non-shared project parameter affected by Revit bug REVIT-136670."),
                suggestion: "The parameter may be in use, locked, or built-in. Verify in Revit's Project Parameters dialog.");

        return CortexResult<object>.Ok(new
        {
            action = "delete",
            parameterName,
            isShared,
            method,
            success = true
        });
    }

    /// <summary>
    /// Toggles a project parameter between Instance and Type binding. The Revit API
    /// has no in-place toggle: we capture the existing categories + group, Remove the
    /// binding, then Insert a fresh binding of the opposite type with the same
    /// categories. Collect-then-mutate (iterator must be closed before mutating).
    /// </summary>
    private static CortexResult<object> SetBindingType(Document doc, JObject input, CortexSession session)
    {
        var parameterName = input["parameterName"]?.Value<string>();
        if (string.IsNullOrEmpty(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterName is required for set_binding_type. Example: {\"action\":\"set_binding_type\",\"parameterName\":\"MyParam\",\"isInstance\":false}. First call {\"action\":\"list\"} to discover names.");

        var targetIsInstance = input["isInstance"]?.Value<bool>();
        if (targetIsInstance == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "isInstance (bool) is required for set_binding_type: true = instance parameter, false = type parameter.");

        // Capture definition, current binding, categories and group up-front.
        Definition? targetDef = null;
        ElementBinding? existingBinding = null;
        {
            var iter = doc.ParameterBindings.ForwardIterator();
            while (iter.MoveNext())
            {
                if (iter.Key?.Name == parameterName)
                {
                    targetDef = iter.Key;
                    existingBinding = iter.Current as ElementBinding;
                    break;
                }
            }
        }

        if (targetDef == null || existingBinding == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Parameter '{parameterName}' not found in project bindings");

        bool currentIsInstance = existingBinding is InstanceBinding;
        if (currentIsInstance == targetIsInstance.Value)
            return CortexResult<object>.Ok(new
            {
                action = "set_binding_type",
                parameterName,
                isInstance = currentIsInstance,
                changed = false,
                message = $"Parameter is already {(currentIsInstance ? "instance" : "type")}-bound."
            });

        // Preserve categories (by name → re-fetched canonical Category objects) and group.
        var catNames = existingBinding.Categories.Cast<Category>().Select(c => c.Name).ToList();
        var group = (targetDef as InternalDefinition)?.GetGroupTypeId();

        if (!session.RequestConfirmation(
                $"change '{parameterName}' to {(targetIsInstance.Value ? "instance" : "type")} parameter", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        var app = doc.Application;
        var categorySet = BuildCategorySet(doc, app, catNames);
        if (categorySet.Size == 0)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                "Could not rebuild the parameter's category set.");

        ElementBinding newBinding = targetIsInstance.Value
            ? (ElementBinding)app.Create.NewInstanceBinding(categorySet)
            : (ElementBinding)app.Create.NewTypeBinding(categorySet);

        using (var tx = new Transaction(doc, "RevitCortex: Change Parameter Binding Type"))
        {
            tx.Start();
            var freshMap = doc.ParameterBindings;
            freshMap.Remove(targetDef);
            bool inserted = group != null
                ? freshMap.Insert(targetDef, newBinding, group)
                : freshMap.Insert(targetDef, newBinding);
            if (!inserted)
            {
                tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                    $"Revit rejected re-binding '{parameterName}'. The parameter may be built-in or Revit-owned.",
                    suggestion: "Instance/type toggle only works on user-created project parameters.");
            }
            tx.Commit();
        }

        return CortexResult<object>.Ok(new
        {
            action = "set_binding_type",
            parameterName,
            isInstance = targetIsInstance.Value,
            changed = true,
            categories = catNames
        });
    }

    /// <summary>
    /// Renaming a bound project parameter is NOT supported by the Revit API
    /// (Definition.Name is read-only for shared and non-shared definitions; only
    /// global parameters can be renamed). We surface this clearly rather than
    /// silently failing, and point at the only real workaround.
    /// </summary>
    private static CortexResult<object> RenameParameter(Document doc, JObject input, CortexSession session)
    {
        var parameterName = input["parameterName"]?.Value<string>();
        var newName = input["newName"]?.Value<string>();
        if (string.IsNullOrEmpty(parameterName) || string.IsNullOrEmpty(newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Both parameterName and newName are required for rename.");

        // Confirm the parameter exists so the error is specific.
        bool exists = false;
        bool isShared = false;
        var iter = doc.ParameterBindings.ForwardIterator();
        while (iter.MoveNext())
        {
            if (iter.Key?.Name == parameterName)
            {
                exists = true;
                isShared = iter.Key is ExternalDefinition;
                break;
            }
        }

        if (!exists)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Parameter '{parameterName}' not found in project bindings");

        return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
            $"The Revit API cannot rename a bound {(isShared ? "shared" : "non-shared")} project parameter — " +
            "Definition.Name is read-only and only global parameters support renaming. " +
            "Renaming is only possible through Revit's Project Parameters dialog UI.",
            suggestion: "Workaround: create a new project parameter with the desired name (same data type/categories), " +
                        "copy values across elements with transfer_parameters or bulk_modify_parameter_values, " +
                        "then delete the old parameter with {\"action\":\"delete\"}.");
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

    private static CortexResult<object> SetParameterGroup(Document doc, JObject input, CortexSession session)
    {
        var requestedNames = input["parameterNames"]?.ToObject<List<string>>() ?? new List<string>();
        // Back-compat: allow single 'parameterName' too
        var single = input["parameterName"]?.Value<string>();
        if (!string.IsNullOrEmpty(single)) requestedNames.Add(single!);

        var targetGroup = input["targetGroup"]?.Value<string>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? false;

        if (requestedNames.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterNames (string[]) or parameterName is required for set_group action. Example: {\"action\":\"set_group\",\"parameterNames\":[\"BCA_RES_Stato-Conservazione\"],\"targetGroup\":\"IdentityData\"}. Do not retry empty — first call {\"action\":\"list\"} to discover names.");

        if (string.IsNullOrEmpty(targetGroup))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "targetGroup is required. Example values: IdentityData, Data, Constraints, Geometry, Graphics, Materials, Text, General, PhasingFilter, Visibility, Construction, ElectricalEngineering, Mechanical, Plumbing, Energy, ModelProperties, IFC, AnalysisResults, Other.",
                suggestion: "Pass a GroupTypeId short name (e.g. 'IdentityData') or a full ForgeTypeId (e.g. 'autodesk.parameter.group:identityData-1.0.0').");

        var resolved = ResolveGroupTypeId(targetGroup!);
        if (resolved == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Unknown targetGroup '{targetGroup}'. Use one of the documented short names or a full ForgeTypeId.",
                suggestion: "Examples: IdentityData, Data, Constraints, Geometry, Graphics, Materials, Text, General, ModelProperties, IFC.");

        // Collect (Definition) up-front; do not iterate while mutating.
        var allDefs = new List<InternalDefinition>();
        {
            var iter = doc.ParameterBindings.ForwardIterator();
            while (iter.MoveNext())
            {
                if (iter.Key is InternalDefinition d) allDefs.Add(d);
            }
        }

        var nameSet = new HashSet<string>(requestedNames, StringComparer.OrdinalIgnoreCase);
        var matched = allDefs.Where(d => nameSet.Contains(d.Name)).ToList();

        var notFound = requestedNames
            .Where(n => !matched.Any(d => string.Equals(d.Name, n, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var planned = new List<object>();
        var skipped = new List<object>();
        var modifiable = new List<InternalDefinition>();

        foreach (var def in matched)
        {
            // Built-in parameters cannot be regrouped.
            if (def.BuiltInParameter != BuiltInParameter.INVALID)
            {
                skipped.Add(new { name = def.Name, reason = "built-in parameter (cannot be regrouped)" });
                continue;
            }

            var current = def.GetGroupTypeId()?.TypeId ?? "";
            if (current == resolved.TypeId)
            {
                skipped.Add(new { name = def.Name, reason = "already in target group", group = current });
                continue;
            }

            planned.Add(new { name = def.Name, fromGroup = current, toGroup = resolved.TypeId });
            modifiable.Add(def);
        }

        if (dryRun)
        {
            return CortexResult<object>.Ok(new
            {
                action = "set_group",
                dryRun = true,
                targetGroup = resolved.TypeId,
                plannedCount = planned.Count,
                skippedCount = skipped.Count,
                notFoundCount = notFound.Count,
                planned,
                skipped,
                notFound
            });
        }

        if (modifiable.Count == 0)
            return CortexResult<object>.Ok(new
            {
                action = "set_group",
                modifiedCount = 0,
                skippedCount = skipped.Count,
                notFoundCount = notFound.Count,
                skipped,
                notFound,
                message = "No parameters required modification."
            });

        if (!session.RequestConfirmation("change parameter group", modifiable.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        var modified = new List<object>();
        var failed = new List<object>();

        using (var tx = new Transaction(doc, "RevitCortex: Set Parameter Group"))
        {
            tx.Start();
            foreach (var def in modifiable)
            {
                var fromGroup = def.GetGroupTypeId()?.TypeId ?? "";
                try
                {
                    def.SetGroupTypeId(resolved);
                    modified.Add(new { name = def.Name, fromGroup, toGroup = resolved.TypeId });
                }
                catch (Exception ex)
                {
                    failed.Add(new { name = def.Name, error = ex.Message });
                }
            }
            tx.Commit();
        }

        return CortexResult<object>.Ok(new
        {
            action = "set_group",
            targetGroup = resolved.TypeId,
            modifiedCount = modified.Count,
            skippedCount = skipped.Count,
            notFoundCount = notFound.Count,
            failedCount = failed.Count,
            modified,
            skipped,
            notFound,
            failed
        });
    }

    private static ForgeTypeId? ResolveGroupTypeId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var trimmed = input.Trim();

        // Full ForgeTypeId form (e.g. "autodesk.parameter.group:identityData-1.0.0")
        if (trimmed.Contains(":") || trimmed.Contains("."))
        {
            try { return new ForgeTypeId(trimmed); } catch { /* fall through */ }
        }

        // Short-name lookup against GroupTypeId static class
        var key = trimmed.ToLowerInvariant().Replace("_", "").Replace("-", "");
        return key switch
        {
            "identitydata" => GroupTypeId.IdentityData,
            "data" => GroupTypeId.Data,
            "constraints" => GroupTypeId.Constraints,
            "geometry" => GroupTypeId.Geometry,
            "graphics" => GroupTypeId.Graphics,
            "materials" or "materialsandfinishes" => GroupTypeId.Materials,
            "text" => GroupTypeId.Text,
            "general" => GroupTypeId.General,
            "phasing" or "phasingfilter" => GroupTypeId.Phasing,
            "visibility" => GroupTypeId.Visibility,
            "construction" => GroupTypeId.Construction,
            "electrical" => GroupTypeId.Electrical,
            "electricalengineering" => GroupTypeId.ElectricalEngineering,
            "electricallighting" => GroupTypeId.ElectricalLighting,
            "electricalloads" => GroupTypeId.ElectricalLoads,
            "mechanical" => GroupTypeId.Mechanical,
            "mechanicalairflow" => GroupTypeId.MechanicalAirflow,
            "plumbing" => GroupTypeId.Plumbing,
            "fireprotection" => GroupTypeId.FireProtection,
            "ifc" => GroupTypeId.Ifc,
            "analysisresults" => GroupTypeId.AnalysisResults,
            "structural" => GroupTypeId.Structural,
            "structuralanalysis" => GroupTypeId.StructuralAnalysis,
            _ => null
        };
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
