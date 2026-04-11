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

        if (categories.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "categories array is required (at least one category)");

        try
        {
            var app = doc.Application;
            var sharedParamFile = app.OpenSharedParameterFile();
            if (sharedParamFile == null)
            {
                // Create a temp shared parameter file if none loaded
                var tempPath = Path.Combine(Path.GetTempPath(), "RevitCortex_SharedParams.txt");
                if (!File.Exists(tempPath))
                    File.WriteAllText(tempPath, "");
                app.SharedParametersFilename = tempPath;
                sharedParamFile = app.OpenSharedParameterFile();
                if (sharedParamFile == null)
                    return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                        "Could not open or create shared parameter file");
            }

            // Find or create group
            var group = sharedParamFile.Groups.get_Item(groupName)
                ?? sharedParamFile.Groups.Create(groupName);

            // Find or create definition
            var definition = group.Definitions.get_Item(parameterName);
            if (definition == null)
            {
                var externalDefOptions = new ExternalDefinitionCreationOptions(parameterName, SpecTypeId.String.Text);
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
    }
}
