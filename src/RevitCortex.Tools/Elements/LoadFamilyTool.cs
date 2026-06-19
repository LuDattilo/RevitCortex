using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Loads a .rfa family, lists loaded families, or duplicates a family type.
/// </summary>
public class LoadFamilyTool : ICortexTool
{
    public string Name => "load_family";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Loads a .rfa family, lists loaded families, or duplicates a family type.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var action = input["action"]?.Value<string>() ?? "list";

        try
        {
            return action.ToLowerInvariant() switch
            {
                "load" => LoadFamily(doc, input, session),
                "list" => ListFamilies(doc, input),
                "duplicate_type" => DuplicateType(doc, input),
                _ => CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}", suggestion: "Use: load, list, duplicate_type")
            };
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static CortexResult<object> LoadFamily(Document doc, JObject input, CortexSession session)
    {
        var familyPath = input["familyPath"]?.Value<string>();
        if (string.IsNullOrEmpty(familyPath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "familyPath is required for load");

        if (!session.RequestConfirmation("load family", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RevitCortex: Load Family");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        if (doc.LoadFamily(familyPath, out var family))
        {
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            var types = family.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .Where(fs => fs != null)
                .Select(fs => new { id = ToolHelpers.GetElementIdValue(fs!.Id), name = fs.Name })
                .ToList();

            return CortexResult<object>.Ok(new
            {
                familyId = ToolHelpers.GetElementIdValue(family.Id),
                familyName = family.Name,
                categoryName = family.FamilyCategory?.Name,
                typeCount = types.Count,
                types
            });
        }

        tx.RollBack();
        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            "Failed to load family (may already be loaded or path invalid)");
    }

    private static CortexResult<object> ListFamilies(Document doc, JObject input)
    {
        var categoryFilter = input["categoryFilter"]?.Value<string>();
        var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>();

        if (!string.IsNullOrEmpty(categoryFilter))
        {
            var catId = Utilities.CategoryResolver.ResolveToId(doc, categoryFilter!);
            if (catId != ElementId.InvalidElementId)
                families = families.Where(f => f.FamilyCategory?.Id == catId);
        }

        var result = families.Select(f => new
        {
            id = ToolHelpers.GetElementIdValue(f.Id),
            name = f.Name,
            category = f.FamilyCategory?.Name,
            isEditable = f.IsEditable,
            typeCount = f.GetFamilySymbolIds().Count
        }).ToList();

        return CortexResult<object>.Ok(new { familyCount = result.Count, families = result });
    }

    private static CortexResult<object> DuplicateType(Document doc, JObject input)
    {
        var sourceTypeId = input["sourceTypeId"]?.Value<long>() ?? 0;
        var newTypeName = input["newTypeName"]?.Value<string>();

        if (sourceTypeId <= 0 || string.IsNullOrEmpty(newTypeName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sourceTypeId and newTypeName required");

#if REVIT2024_OR_GREATER
        var sourceType = doc.GetElement(new ElementId(sourceTypeId)) as FamilySymbol;
#else
        var sourceType = doc.GetElement(new ElementId((int)sourceTypeId)) as FamilySymbol;
#endif
        if (sourceType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Source family type not found");

        using var tx = new Transaction(doc, "RevitCortex: Duplicate Family Type");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();
        var newType = sourceType.Duplicate(newTypeName) as FamilySymbol;
        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                suggestion: "Fix the reported model errors and retry.");

        return CortexResult<object>.Ok(new
        {
            newTypeId = newType != null ? ToolHelpers.GetElementIdValue(newType.Id) : 0,
            newTypeName = newType?.Name,
            familyName = sourceType.FamilyName
        });
    }
}
