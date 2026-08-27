using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Views;

/// <summary>
/// Places a view on a sheet at the specified position.
/// </summary>
[ToolSafety(false, false)]
public class PlaceViewportTool : ICortexTool
{
    public string Name => "place_viewport";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Places a view on a sheet. positionX/positionY are the viewport CENTRE in mm, measured in sheet coordinates; omit both to centre it on the sheet. The response reports the sheet size, the viewport's real outline and fitsOnSheet: an UNCROPPED view produces a viewport far larger than the sheet, and its content then lands outside the frame. Crop the view first — at 1:100 a 16 x 13.5 m crop is 160 x 135 mm on paper.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sheetId = input["sheetId"]?.Value<long>() ?? 0;
        var viewId = input["viewId"]?.Value<long>() ?? 0;
        var posXToken = input["positionX"];
        var posYToken = input["positionY"];
        var posXMm = posXToken?.Value<double>() ?? 0;
        var posYMm = posYToken?.Value<double>() ?? 0;
        var centreOnSheet = posXToken == null && posYToken == null;
        var rotation = input["rotation"]?.Value<string>();
        var viewportTypeId = input["viewportTypeId"]?.Value<long?>() ?? 0;

        if (sheetId <= 0 || viewId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sheetId and viewId are required");

        try
        {
            var sheet = doc.GetElement(new ElementId(sheetId)) as ViewSheet;
            var view = doc.GetElement(new ElementId(viewId)) as View;
            var viewEid = new ElementId(viewId);
            var sheetEid = new ElementId(sheetId);
            if (sheet == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Sheet not found");
            if (view == null) return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "View not found");

            // A schedule is not a viewport. Revit places it with ScheduleSheetInstance,
            // and CanAddViewToSheet answers a flat false — which used to surface as
            // "already placed or not placeable" and sent the caller looking at the sheet
            // instead of at the view type.
            if (view is ViewSchedule)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"'{view.Name}' is a SCHEDULE, and a schedule is not placed as a viewport.",
                    suggestion: "Schedules go on a sheet through ScheduleSheetInstance, which this tool "
                              + "does not cover. Place it from the Revit project browser, or ask for a "
                              + "dedicated tool.");

            if (!Viewport.CanAddViewToSheet(doc, sheetEid, viewEid))
            {
                // Distinguish the two causes rather than reporting both at once: one is
                // fixed by picking another view, the other by removing it from its sheet.
                var placedOn = new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .FirstOrDefault(vp => vp.ViewId == viewEid);

                if (placedOn != null)
                {
                    var host = doc.GetElement(placedOn.SheetId) as ViewSheet;
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"'{view.Name}' is already placed on sheet {host?.SheetNumber ?? "?"} "
                        + $"{host?.Name}. A view can live on one sheet only.",
                        suggestion: "Delete that viewport first, or duplicate the view with "
                                  + "duplicate_view and place the copy.");
                }

                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Revit refuses '{view.Name}' ({view.ViewType}) on this sheet, and it is not "
                    + "already placed elsewhere.",
                    suggestion: "Legends and some view types cannot be placed on every sheet. Check "
                              + "the view type with get_current_view_info.");
            }

            var sheetWidthFt = sheet.get_Parameter(BuiltInParameter.SHEET_WIDTH)?.AsDouble() ?? 0;
            var sheetHeightFt = sheet.get_Parameter(BuiltInParameter.SHEET_HEIGHT)?.AsDouble() ?? 0;

            // Measured by the shared helper rather than inline here. This tool held the
            // original, correct implementation; batch_create_sheets cloned a broken one and
            // the two drifted. SheetFrame is that logic, once, and it adds the fallbacks
            // this copy lacked — a sheet with no title block reported [0,0]x[0,0], which made
            // fitsOnSheet permanently false with no usable paper size to work from.
            var frame = SheetFrame.Measure(doc, sheet);

            var frameMinXMm = frame.MinXMm;
            var frameMinYMm = frame.MinYMm;
            var frameMaxXMm = frame.MaxXMm;
            var frameMaxYMm = frame.MaxYMm;

            if (centreOnSheet)
            {
                if (!frame.IsKnown)
                    return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                        $"Sheet {sheet.SheetNumber} has no title block and no measurable extent, "
                        + "so there is no centre to place the view at.",
                        suggestion: "Add a title block with place_title_block, or pass positionX and "
                                  + "positionY explicitly in mm.");

                // No position given: the middle of the frame, wherever the frame is.
                posXMm = frame.CentreXMm;
                posYMm = frame.CentreYMm;
            }

            var position = new XYZ(posXMm / MmPerFoot, posYMm / MmPerFoot, 0);

            using var tx = new Transaction(doc, "RiveTT: Place Viewport");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            var viewport = Viewport.Create(doc, sheetEid, viewEid, position);

            // Optional rotation
            if (!string.IsNullOrEmpty(rotation))
            {
                viewport.Rotation = rotation!.ToLowerInvariant() switch
                {
                    "clockwise"        => ViewportRotation.Clockwise,
                    "counterclockwise" => ViewportRotation.Counterclockwise,
                    _                  => ViewportRotation.None,
                };
            }

            // Optional viewport type (controls title/detail-number appearance)
            if (viewportTypeId > 0)
            {
                var vpType = doc.GetElement(ToolHelpers.ToElementId(viewportTypeId));
                if (vpType is ElementType && viewport.IsValidType(vpType.Id))
                    viewport.ChangeTypeId(vpType.Id);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            // What was actually placed, and whether it fits. A viewport whose outline
            // exceeds the sheet draws its content outside the frame, which is only
            // visible by opening the sheet — the caller must be told here.
            double outMinX = 0, outMinY = 0, outMaxX = 0, outMaxY = 0;
            var haveOutline = false;
            try
            {
                var outline = viewport.GetBoxOutline();
                outMinX = outline.MinimumPoint.X * MmPerFoot;
                outMinY = outline.MinimumPoint.Y * MmPerFoot;
                outMaxX = outline.MaximumPoint.X * MmPerFoot;
                outMaxY = outline.MaximumPoint.Y * MmPerFoot;
                haveOutline = true;
            }
            catch
            {
                // Outline is unavailable on some viewport states; the rest still stands.
            }

            var sheetWidthMm = sheetWidthFt * MmPerFoot;
            var sheetHeightMm = sheetHeightFt * MmPerFoot;
            // frame.Contains answers TRUE when the frame could not be measured: "unknown
            // paper size" must not be reported as "your drawing overflows".
            var fits = !haveOutline || frame.Contains(outMinX, outMinY, outMaxX, outMaxY);

            var warnings = new System.Collections.Generic.List<string>();
            if (!frame.IsKnown)
            {
                warnings.Add(
                    $"Sheet {sheet.SheetNumber} has no title block and no measurable extent, so " +
                    "fitsOnSheet could not be evaluated and is reported as null. Add a title block " +
                    "with place_title_block to get a real frame.");
            }
            if (haveOutline && frame.IsKnown && !fits)
            {
                warnings.Add(
                    $"The viewport spans {outMaxX - outMinX:F0} x {outMaxY - outMinY:F0} mm at " +
                    $"[{outMinX:F0}..{outMaxX:F0}] x [{outMinY:F0}..{outMaxY:F0}] mm, outside the frame " +
                    $"[{frameMinXMm:F0}..{frameMaxXMm:F0}] x [{frameMinYMm:F0}..{frameMaxYMm:F0}] mm: part " +
                    "of the drawing falls off the sheet. Two independent causes — an UNCROPPED view makes " +
                    "the viewport metres wide (crop it: paper size = crop size / view scale), and the sheet " +
                    "origin is not the frame corner, so compute positions from frameOutlineMm below, not " +
                    "from the sheet size.");
            }

            return CortexResult<object>.Ok(new
            {
                viewportId = ToolHelpers.GetElementIdValue(viewport.Id),
                sheetNumber = sheet.SheetNumber,
                viewName = view.Name,
                rotation = viewport.Rotation.ToString(),
                centredOnSheet = centreOnSheet,
                centreMm = new { x = Math.Round(posXMm, 1), y = Math.Round(posYMm, 1) },
                sheetSizeMm = new { width = Math.Round(sheetWidthMm, 1), height = Math.Round(sheetHeightMm, 1) },
                // Where the printable frame actually is, in sheet coordinates. Use
                // this to compute positions: it is NOT [0,0]..[width,height]. `source`
                // says whether it was measured on the title block or fell back.
                frameOutlineMm = SheetFrame.Describe(frame),
                viewportOutlineMm = haveOutline
                    ? new
                    {
                        minX = Math.Round(outMinX, 1), minY = Math.Round(outMinY, 1),
                        maxX = Math.Round(outMaxX, 1), maxY = Math.Round(outMaxY, 1),
                        widthMm = Math.Round(outMaxX - outMinX, 1),
                        heightMm = Math.Round(outMaxY - outMinY, 1)
                    }
                    : null,
                viewScale = view.Scale,
                cropActive = view.CropBoxActive,
                // Null rather than false when there was nothing to measure against.
                fitsOnSheet = (haveOutline && frame.IsKnown) ? fits : (bool?)null,
                warnings
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
