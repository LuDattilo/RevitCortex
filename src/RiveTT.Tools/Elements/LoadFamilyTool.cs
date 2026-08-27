using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Loads a .rfa family, lists loaded families, or duplicates a family type.
/// </summary>
[ToolSafety(false, false)]
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

        // The overload without IFamilyLoadOptions returns false (does nothing) the
        // moment a same-named family already exists in the project — the normal
        // case for reloading a family edited outside Revit, and get_server_capabilities'
        // documented way to do it. Default to overwriting: see P1.7 in
        // PLAN_CORRECTION.md.
        var overwriteExisting = input["overwriteExisting"]?.Value<bool>() ?? true;

        if (!File.Exists(familyPath))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"familyPath does not exist: {familyPath}");

        if (!session.RequestConfirmation("load family", 1))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

        using var tx = new Transaction(doc, "RiveTT: Load Family");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        if (doc.LoadFamily(familyPath, new Utilities.OverwritingFamilyLoadOptions(overwriteExisting), out var family))
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
        // The path exists and was readable — a false return here means Revit
        // itself refused it (corrupt file, category not loadable in this
        // document, family already identical), not a bad path.
        return CortexResult<object>.Fail(CortexErrorCode.Unknown,
            "Revit refused to load the family from a valid path: the file may be corrupt, its category may " +
            "not be loadable into this document, or (with overwriteExisting=false) a family of the same name " +
            "already exists.",
            suggestion: overwriteExisting
                ? "Check the .rfa opens cleanly in Revit and its category matches the target document."
                : "Retry with overwriteExisting=true to update the family already in the project.");
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

        var sourceType = doc.GetElement(new ElementId(sourceTypeId)) as FamilySymbol;
        if (sourceType == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Source family type not found");

        using var tx = new Transaction(doc, "RiveTT: Duplicate Family Type");
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
