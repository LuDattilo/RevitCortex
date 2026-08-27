using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Sheets;

/// <summary>
/// Creates multiple sheets at once with title blocks and optional view placement.
/// </summary>
[ToolSafety(false, false)]
public class BatchCreateSheetsTool : ICortexTool
{
    public string Name => "batch_create_sheets";
    public string Category => "Sheets";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Creates multiple sheets at once with title blocks and optional view placement. Viewports are centred "
        + "in the title block's real frame (not the sheet origin, which is not the frame corner); several views "
        + "on one sheet are tiled one per cell. Previews by default: set dryRun=false to create.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sheetsArray = input["sheets"]?.ToObject<List<JObject>>() ?? new List<JObject>();
        var defaultTitleBlockName = input["defaultTitleBlockName"]?.Value<string>();
        var dryRun = ToolHelpers.GetDryRun(input);

        if (sheetsArray.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheets array is required");

        try
        {
            // Resolve default title block
            var defaultTbId = ResolveTitleBlock(doc, defaultTitleBlockName);

            if (dryRun)
                return Preview(doc, sheetsArray, defaultTbId, defaultTitleBlockName);

            var results = new List<object>();
            var outsideFrame = 0;
            // Counted here, not read back off the result objects: a failed sheet has no view
            // fields at all, and a dynamic read of a missing member throws. Reconciling
            // requested vs. placed is what caught workflow_sheet_set's dropped viewIds before
            // the two tools were merged — batch_create_sheets now carries that check itself.
            var requestedViews = 0;
            var placedTotal = 0;

            using var tx = new Transaction(doc, "RiveTT: Batch Create Sheets");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            foreach (var sheetDef in sheetsArray)
            {
                var number = sheetDef["number"]?.Value<string>();
                var name = sheetDef["name"]?.Value<string>();
                var tbName = sheetDef["titleBlockName"]?.Value<string>();
                var viewIds = sheetDef["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
                requestedViews += viewIds.Count;

                var tbId = !string.IsNullOrEmpty(tbName) ? ResolveTitleBlock(doc, tbName) : defaultTbId;
                if (tbId == ElementId.InvalidElementId)
                {
                    results.Add(new { number, name, success = false, reason = "No title block found" });
                    continue;
                }

                try
                {
                    var sheet = ViewSheet.Create(doc, tbId);
                    if (!string.IsNullOrEmpty(number)) sheet.SheetNumber = number;
                    if (!string.IsNullOrEmpty(name)) sheet.Name = name;

                    // The frame must be measured AFTER the sheet exists: it comes from the
                    // title block instance placed on it. Viewports used to go to a hardcoded
                    // (0.5 ft; 0.5 ft), which is not the frame corner — on the French A1 block
                    // whose origin sits 650 mm inside the frame, every drawing landed off the
                    // paper. SheetFrame is shared with place_viewport so the two agree.
                    var frame = SheetFrame.Measure(doc, sheet);
                    var cells = SheetFrame.Subdivide(frame, viewIds.Count);

                    var placedViews = new List<SheetFrame.Placement>();
                    var placedHere = 0;
                    for (var i = 0; i < viewIds.Count; i++)
                    {
                        var placement = SheetFrame.PlaceCentred(
                            doc, sheet, ToolHelpers.ToElementId(viewIds[i]), cells[i]);
                        placedViews.Add(placement);
                        if (SheetFrame.WasPlaced(placement)) placedHere++;
                    }
                    outsideFrame += SheetFrame.CountOutsideFrame(placedViews);
                    placedTotal += placedHere;

                    results.Add(new
                    {
                        sheetId = ToolHelpers.GetElementIdValue(sheet.Id),
                        number = sheet.SheetNumber,
                        name = sheet.Name,
                        success = true,
                        frameOutlineMm = SheetFrame.Describe(frame),
                        requestedViewCount = viewIds.Count,
                        placedCount = placedHere,
                        placedViews
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new { number, name, success = false, reason = ex.Message });
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            var warnings = new List<string>();
            // A viewport that overflows the frame is only visible by opening the sheet.
            // Surface it here rather than reporting an unqualified success.
            if (outsideFrame > 0)
                warnings.Add($"{outsideFrame} viewport(s) are larger than the sheet frame and draw outside the "
                    + "border. Crop those views first: paper size = crop size / view scale.");
            if (requestedViews != placedTotal)
                warnings.Add($"{requestedViews - placedTotal} of {requestedViews} requested view(s) were not "
                    + "placed: Revit refuses a view already placed on another sheet. See each sheet's "
                    + "placedViews[].reason.");

            return CortexResult<object>.Ok(new
            {
                createdCount = results.Count(r => ((dynamic)r).success),
                sheets = results,
                requestedViewCount = requestedViews,
                placedViewCount = placedTotal,
                viewportsOutsideFrame = outsideFrame,
                warnings
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reports what would be created without touching the model: the resolved title block
    /// per sheet, the views that would be placed, and any sheet number already taken —
    /// Revit rejects a duplicate number, and finding that out mid-batch leaves half a set.
    /// </summary>
    private static CortexResult<object> Preview(
        Document doc, List<JObject> sheetsArray, ElementId defaultTbId, string? defaultTitleBlockName)
    {
        var existingNumbers = new HashSet<string>(
            new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Select(s => s.SheetNumber),
            StringComparer.OrdinalIgnoreCase);

        // A view already placed on a sheet cannot be placed again. CanAddViewToSheet needs a
        // real sheet id, which does not exist yet in a preview, so the check is done against
        // the existing viewports instead.
        var viewsOnSheets = new Dictionary<long, string>();
        foreach (var vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
        {
            var host = doc.GetElement(vp.SheetId) as ViewSheet;
            viewsOnSheets[ToolHelpers.GetElementIdValue(vp.ViewId)] = host?.SheetNumber ?? "?";
        }

        var planned = new List<object>();
        foreach (var sheetDef in sheetsArray)
        {
            var number = sheetDef["number"]?.Value<string>();
            var name = sheetDef["name"]?.Value<string>();
            var tbName = sheetDef["titleBlockName"]?.Value<string>();
            var viewIds = sheetDef["viewIds"]?.ToObject<List<long>>() ?? new List<long>();

            var tbId = !string.IsNullOrEmpty(tbName) ? ResolveTitleBlock(doc, tbName) : defaultTbId;
            var tb = tbId != ElementId.InvalidElementId ? doc.GetElement(tbId) as FamilySymbol : null;

            var views = viewIds.Select(vid =>
            {
                var view = doc.GetElement(ToolHelpers.ToElementId(vid)) as View;
                var onSheet = viewsOnSheets.TryGetValue(vid, out var hostNumber) ? hostNumber : null;
                return (object)new
                {
                    viewId = vid,
                    found = view != null,
                    viewName = view?.Name,
                    viewScale = view?.Scale,
                    cropActive = view?.CropBoxActive,
                    alreadyOnSheet = onSheet != null,
                    alreadyOnSheetNumber = onSheet
                };
            }).ToList();

            planned.Add(new
            {
                number,
                name,
                titleBlock = tb != null ? $"{tb.FamilyName}: {tb.Name}" : null,
                titleBlockFound = tb != null,
                numberAlreadyUsed = !string.IsNullOrEmpty(number) && existingNumbers.Contains(number!),
                viewCount = viewIds.Count,
                views
            });
        }

        var blocked = planned.Count(p => ((dynamic)p).numberAlreadyUsed || !((dynamic)p).titleBlockFound);

        return CortexResult<object>.Ok(new
        {
            dryRun = true,
            message = $"DryRun: {sheetsArray.Count} sheet(s) would be created"
                    + (blocked > 0 ? $", {blocked} blocked (duplicate number or missing title block)" : "")
                    + ". Set dryRun=false to execute.",
            wouldCreateCount = sheetsArray.Count - blocked,
            blockedCount = blocked,
            defaultTitleBlock = defaultTitleBlockName,
            sheets = planned
        });
    }

    private static ElementId ResolveTitleBlock(Document doc, string? name)
    {
        var tbs = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>();

        if (!string.IsNullOrEmpty(name))
        {
            var match = tbs.FirstOrDefault(tb =>
                tb.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                tb.FamilyName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                $"{tb.FamilyName}: {tb.Name}".Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Id;
        }

        var first = tbs.FirstOrDefault();
        return first?.Id ?? ElementId.InvalidElementId;
    }
}
