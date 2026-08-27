using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Creates a new sheet with optional title block and numbering.
/// </summary>
[ToolSafety(false, false)]
public class CreateSheetTool : ICortexTool
{
    public string Name => "create_sheet";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a new sheet with optional title block and numbering.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sheetNumber = input["sheetNumber"]?.Value<string>();
        var sheetName = input["sheetName"]?.Value<string>();
        var titleBlockFamilyName = input["titleBlockFamilyName"]?.Value<string>();
        var titleBlockTypeName = input["titleBlockTypeName"]?.Value<string>();
        // titleBlockId is the name the MCP surface has always used; titleBlockTypeId
        // was the runtime name this tool read. Only the second one was honored, so
        // every sheet came out with the default 210x297 "Sheet" type and no title
        // block. Both names are accepted now.
        var titleBlockTypeId = input["titleBlockId"]?.Value<long>()
                               ?? input["titleBlockTypeId"]?.Value<long>()
                               ?? -1;
        var dryRun = input["dryRun"]?.Value<bool>() ?? false;

        try
        {
            // Resolve title block
            ElementId tbId = ElementId.InvalidElementId;

            if (titleBlockTypeId > 0)
            {
                var elem = doc.GetElement(new ElementId(titleBlockTypeId));
                if (elem is FamilySymbol symbolCandidate &&
                    symbolCandidate.Category?.Id == new ElementId(BuiltInCategory.OST_TitleBlocks))
                {
                    tbId = elem.Id;
                }
                else
                {
                    // An explicit id that cannot be used must fail loudly. Falling back
                    // to "any title block" (or to none) silently produced a blank A4
                    // sheet that looked like a success.
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"titleBlockId {titleBlockTypeId} is not a title block type in this document " +
                        $"(resolved to: {DescribeElement(doc, titleBlockTypeId)}).",
                        suggestion: "Pass the ElementId of an OST_TitleBlocks FamilySymbol. " +
                                    $"Available: {DescribeAvailableTitleBlocks(doc)}",
                        context: new Dictionary<string, object>
                        {
                            ["availableTitleBlocks"] = ListTitleBlocks(doc)
                        });
                }
            }

            if (tbId == ElementId.InvalidElementId && !string.IsNullOrEmpty(titleBlockFamilyName))
            {
                var symbols = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>();

                FamilySymbol? match = null;
                if (!string.IsNullOrEmpty(titleBlockTypeName))
                    match = symbols.FirstOrDefault(s =>
                        s.FamilyName.Equals(titleBlockFamilyName, StringComparison.OrdinalIgnoreCase) &&
                        s.Name.Equals(titleBlockTypeName, StringComparison.OrdinalIgnoreCase));

                match ??= symbols.FirstOrDefault(s =>
                    s.FamilyName.Equals(titleBlockFamilyName, StringComparison.OrdinalIgnoreCase));

                if (match != null) tbId = match.Id;
            }

            if (tbId == ElementId.InvalidElementId && !string.IsNullOrEmpty(titleBlockTypeName))
            {
                var byTypeName = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => s.Name.Equals(titleBlockTypeName, StringComparison.OrdinalIgnoreCase));
                if (byTypeName != null) tbId = byTypeName.Id;
            }

            // No titleBlockId/family/type name given: leave tbId unresolved and let
            // ViewSheet.Create(doc, InvalidElementId) produce the bare 210x297 mm
            // sheet the description promises. Silently picking "the first title
            // block found" here is exactly the surprise fallback this tool's own
            // description says it never does for an unusable id — see P1.3 in
            // PLAN_CORRECTION.md.

            var resolvedTitleBlock = tbId == ElementId.InvalidElementId
                ? null
                : doc.GetElement(tbId) as FamilySymbol;

            if (dryRun)
            {
                var availableTitleBlocks = ListTitleBlocks(doc);
                return CortexResult<object>.Ok(new
                {
                    message = resolvedTitleBlock != null
                        ? $"DryRun: sheet would be created with title block '{resolvedTitleBlock.FamilyName} / {resolvedTitleBlock.Name}'."
                        : availableTitleBlocks.Count == 0
                            ? "DryRun: sheet would be created WITHOUT a title block (none available in this document)."
                            : "DryRun: sheet would be created WITHOUT a title block (none was requested). Pass " +
                              "titleBlockId, titleBlockFamilyName, or titleBlockTypeName to use one.",
                    sheetNumber,
                    sheetName,
                    titleBlockId = tbId == ElementId.InvalidElementId
                        ? (long?)null
                        : ToolHelpers.GetElementIdValue(tbId),
                    titleBlockFamily = resolvedTitleBlock?.FamilyName,
                    titleBlockType = resolvedTitleBlock?.Name,
                    hasTitleBlock = resolvedTitleBlock != null,
                    availableTitleBlocks
                });
            }

            using var tx = new Transaction(doc, "RiveTT: Create Sheet");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            // Activate title block if needed
            if (tbId != ElementId.InvalidElementId)
            {
                var symbol = doc.GetElement(tbId) as FamilySymbol;
                if (symbol != null && !symbol.IsActive)
                {
                    symbol.Activate();
                    doc.Regenerate();
                }
            }

            var sheet = ViewSheet.Create(doc, tbId);

            if (!string.IsNullOrEmpty(sheetNumber))
                sheet.SheetNumber = sheetNumber;
            if (!string.IsNullOrEmpty(sheetName))
                sheet.Name = sheetName;

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            // Report the title block actually applied: a caller must be able to see,
            // without a second read, whether the sheet has a frame or is a bare
            // default 210x297 sheet.
            var placedTitleBlock = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault();

            return CortexResult<object>.Ok(new
            {
                sheetId = ToolHelpers.GetElementIdValue(sheet.Id),
                sheetNumber = sheet.SheetNumber,
                sheetName = sheet.Name,
                titleBlockId = placedTitleBlock == null
                    ? (long?)null
                    : ToolHelpers.GetElementIdValue(placedTitleBlock.GetTypeId()),
                titleBlockFamily = resolvedTitleBlock?.FamilyName,
                titleBlockType = resolvedTitleBlock?.Name,
                hasTitleBlock = placedTitleBlock != null,
                warnings = placedTitleBlock == null
                    ? new[]
                    {
                        "No title block was placed: the sheet is the bare Revit sheet type (210x297 mm, no frame). " +
                        "Load a title block family, then use place_title_block on this sheet."
                    }
                    : Array.Empty<string>()
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to create sheet: {ex.Message}");
        }
    }

    private static string DescribeElement(Document doc, long rawId)
    {
        var element = doc.GetElement(new ElementId(rawId));
        if (element == null) return "no element with this id";
        return $"{element.GetType().Name} '{element.Name}' (category {element.Category?.Name ?? "none"})";
    }

    internal static List<object> ListTitleBlocks(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .OrderBy(s => s.FamilyName)
            .ThenBy(s => s.Name)
            .Select(s => (object)new
            {
                titleBlockId = ToolHelpers.GetElementIdValue(s.Id),
                familyName = s.FamilyName,
                typeName = s.Name
            })
            .ToList();
    }

    private static string DescribeAvailableTitleBlocks(Document doc)
    {
        var symbols = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .OrderBy(s => s.FamilyName)
            .Take(15)
            .Select(s => $"{ToolHelpers.GetElementIdValue(s.Id)}={s.FamilyName}/{s.Name}")
            .ToList();

        return symbols.Count == 0
            ? "none loaded in this document"
            : string.Join(", ", symbols);
    }
}
