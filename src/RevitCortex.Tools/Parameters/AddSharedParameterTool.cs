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
/// Adds a shared parameter to project categories from the shared parameter file.
/// Creates the group/definition if it doesn't exist.
/// </summary>
[ToolSafety(false, false)]
public class AddSharedParameterTool : ICortexTool
{
    public string Name => "add_shared_parameter";
    public string Category => "Parameters";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Adds a shared parameter to project categories from the shared parameter file. Creates the group/definition if it doesn't exist.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var parameterName = input["parameterName"]?.Value<string>();
        if (string.IsNullOrEmpty(parameterName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "parameterName is required");

        var groupName = input["groupName"]?.Value<string>() ?? "RevitCortex";
        var categories = input["categories"]?.ToObject<List<string>>() ?? new List<string>();
        var isInstance = input["isInstance"]?.Value<bool>() ?? true;
        var dataType = input["dataType"]?.Value<string>() ?? "text";

        if (categories.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "categories array is required (at least one category)");

        var app = doc.Application;
        // H8: if we have to point Revit at a temp shared-parameter file, remember the
        // user's original filename and restore it afterwards. Without this, every later
        // OpenSharedParameterFile() in the session would return our temp file, silently
        // breaking all shared-parameter workflows.
        var originalSharedParamFile = app.SharedParametersFilename;
        bool overrodeSharedParamFile = false;
        try
        {
            var sharedParamFile = app.OpenSharedParameterFile();
            if (sharedParamFile == null)
            {
                // Create a unique temp shared parameter file if none loaded (unique name
                // avoids concurrent-session collisions on a static path).
                var tempPath = Path.Combine(Path.GetTempPath(),
                    $"RevitCortex_SharedParams_{Guid.NewGuid():N}.txt");
                File.WriteAllText(tempPath, "");
                app.SharedParametersFilename = tempPath;
                overrodeSharedParamFile = true;
                sharedParamFile = app.OpenSharedParameterFile();
                if (sharedParamFile == null)
                    return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                        "Could not open or create shared parameter file");
            }

            // Find or create group
            var group = sharedParamFile.Groups.get_Item(groupName)
                ?? sharedParamFile.Groups.Create(groupName);

            // Find or create definition with the requested data type
            var definition = group.Definitions.get_Item(parameterName);
            if (definition == null)
            {
#if REVIT2023_OR_GREATER
                var externalDefOptions = new ExternalDefinitionCreationOptions(
                    parameterName, ResolveSpecTypeId(dataType));
#else
                var externalDefOptions = new ExternalDefinitionCreationOptions(
                    parameterName, ResolveParameterType(dataType));
#endif
                definition = group.Definitions.Create(externalDefOptions);
            }

            // Build category set
            var categorySet = app.Create.NewCategorySet();
            var boundCategories = new List<string>();
            var unresolvedCategories = new List<string>();

            foreach (var catName in categories)
            {
                var catId = CategoryResolver.ResolveToId(doc, catName);
                if (catId == null)
                {
                    unresolvedCategories.Add(catName);
                    continue;
                }
                var cat = Autodesk.Revit.DB.Category.GetCategory(doc, catId);
                if (cat != null && cat.AllowsBoundParameters)
                {
                    categorySet.Insert(cat);
                    boundCategories.Add(cat.Name);
                }
                else
                {
                    unresolvedCategories.Add(catName);
                }
            }

            if (categorySet.Size == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "No valid categories found for parameter binding",
                    suggestion: "Check category names. Use OST_* codes for language-independent matching.");

            // Create binding
            ElementBinding binding = isInstance
                ? (ElementBinding)app.Create.NewInstanceBinding(categorySet)
                : (ElementBinding)app.Create.NewTypeBinding(categorySet);

            using var tx = new Transaction(doc, "RevitCortex: Add Shared Parameter");
            tx.Start();

            try
            {
                var success = doc.ParameterBindings.Insert(definition, binding);
                if (!success)
                {
                    // Parameter might already exist — try rebind
                    success = doc.ParameterBindings.ReInsert(definition, binding);
                }

                tx.Commit();

                var guid = definition is ExternalDefinition extDef ? extDef.GUID.ToString() : "";

                return CortexResult<object>.Ok(new
                {
                    parameterName,
                    guid,
                    dataType,
                    isInstance,
                    boundCategories,
                    unresolvedCategories,
                    success
                });
            }
            catch
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                throw;
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to add shared parameter: {ex.Message}");
        }
        finally
        {
            // H8: restore the user's original shared-parameter file if we overrode it.
            if (overrodeSharedParamFile)
            {
                try { app.SharedParametersFilename = originalSharedParamFile; } catch { }
            }
        }
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
