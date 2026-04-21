using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Duplicates a system family type (wall, floor, roof, ceiling) with a new name.
/// </summary>
public class DuplicateSystemTypeTool : ICortexTool
{
    public string Name => "duplicate_system_type";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Duplicates a system family type (wall, floor, roof, ceiling) with a new name. Returns the new type ID.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sourceTypeId   = input["sourceTypeId"]?.Value<long?>();
        var sourceTypeName = input["sourceTypeName"]?.Value<string>();
        var category       = input["category"]?.Value<string>();
        var newName        = input["newName"]?.Value<string>();

        if (string.IsNullOrWhiteSpace(newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "newName is required",
                suggestion: "Provide the name for the duplicated type");

        try
        {
            // Resolve source type
            ElementType? sourceType = null;

            if (sourceTypeId.HasValue)
            {
#if REVIT2024_OR_GREATER
                sourceType = doc.GetElement(new ElementId(sourceTypeId.Value)) as ElementType;
#else
                sourceType = doc.GetElement(new ElementId((int)sourceTypeId.Value)) as ElementType;
#endif
                if (sourceType == null)
                    return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                        $"Type {sourceTypeId} not found or is not an ElementType");
            }
            else if (!string.IsNullOrWhiteSpace(sourceTypeName))
            {
                sourceType = FindTypeByName(doc, sourceTypeName!, category);
                if (sourceType == null)
                    return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                        $"Type '{sourceTypeName}' not found" + (category != null ? $" in category {category}" : ""),
                        suggestion: "Use get_available_family_types to list available types");
            }
            else
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "Provide sourceTypeId or sourceTypeName to identify the source type");
            }

            // Check if target name already exists
            var existing = FindTypeByName(doc, newName!, category);
            if (existing != null)
            {
                long existingIdValue;
#if REVIT2024_OR_GREATER
                existingIdValue = existing.Id.Value;
#else
                existingIdValue = (long)existing.Id.IntegerValue;
#endif
                return CortexResult<object>.Ok(new
                {
                    typeId = existingIdValue,
                    typeName = existing.Name,
                    typeCategory = existing.Category?.Name ?? "",
                    alreadyExisted = true
                });
            }

            // Duplicate
            using (var tx = new Transaction(doc, "RevitCortex: Duplicate System Type"))
            {
                tx.Start();
                var newType = sourceType.Duplicate(newName);
                tx.Commit();

                long newIdValue;
#if REVIT2024_OR_GREATER
                newIdValue = newType.Id.Value;
#else
                newIdValue = (long)newType.Id.IntegerValue;
#endif

                return CortexResult<object>.Ok(new
                {
                    typeId = newIdValue,
                    typeName = newType.Name,
                    typeCategory = newType.Category?.Name ?? "",
                    sourceTypeName = sourceType.Name,
                    alreadyExisted = false
                });
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to duplicate type: {ex.Message}");
        }
    }

    private static ElementType? FindTypeByName(Document doc, string typeName, string? category)
    {
        var collector = new FilteredElementCollector(doc)
            .WhereElementIsElementType();

        if (!string.IsNullOrEmpty(category))
        {
            var catId = CategoryResolver.ResolveToId(doc, category!);
            if (catId != null && catId != ElementId.InvalidElementId)
                collector = collector.OfCategoryId(catId);
        }

        return collector
            .OfType<ElementType>()
            .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
    }
}
