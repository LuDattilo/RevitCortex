using System.Linq;
using Autodesk.Revit.DB;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Utilities;

/// <summary>
/// Where a sheet's printable frame actually is, and where a viewport lands inside it.
///
/// The sheet origin is NOT the corner of the frame. The French A1 title block has its
/// origin 650 mm inside the frame and vertically centred, so any position computed
/// from the sheet size — or hardcoded near zero — lands off the paper. batch_create_sheets
/// placed every viewport at a hardcoded (0.5 ft; 0.5 ft) and put the drawing outside
/// the border on exactly that title block; place_viewport already measured the frame
/// correctly, and this type exists so the two cannot drift apart again.
///
/// The reference is the title block INSTANCE's bounding box on the sheet. When a sheet
/// carries no title block there is nothing to measure, and the SHEET_WIDTH/SHEET_HEIGHT
/// fallback is used with <see cref="FromTitleBlock"/> reporting false so the caller can
/// say the placement was a guess.
/// </summary>
public static class SheetFrame
{

    /// <summary>The printable frame in sheet coordinates, in millimetres.</summary>
    public sealed class Frame
    {
        public double MinXMm { get; init; }
        public double MinYMm { get; init; }
        public double MaxXMm { get; init; }
        public double MaxYMm { get; init; }

        /// <summary>
        /// True when the box came from a title block instance. False means it was
        /// derived from the sheet size and the frame position is unknown.
        /// </summary>
        public bool FromTitleBlock { get; init; }

        /// <summary>
        /// Which fallback produced this box: "titleBlock", "sheetSize", "viewOutline" or
        /// "unknown". A caller that reads fitsOnSheet needs to know whether the frame was
        /// measured or guessed — a sheet with no title block reported [0,0]x[0,0], which
        /// made every viewport look like it overflowed.
        /// </summary>
        public string Source { get; init; } = "unknown";

        /// <summary>False when no source could give a usable extent; fitsOnSheet is then meaningless.</summary>
        public bool IsKnown => WidthMm > 1 && HeightMm > 1;

        public double CentreXMm => (MinXMm + MaxXMm) / 2;
        public double CentreYMm => (MinYMm + MaxYMm) / 2;
        public double WidthMm => MaxXMm - MinXMm;
        public double HeightMm => MaxYMm - MinYMm;

        /// <summary>The frame centre as a sheet-coordinate point in FEET, for Viewport.Create.</summary>
        public XYZ CentreFeet => new(CentreXMm / MmPerFoot, CentreYMm / MmPerFoot, 0);

        /// <summary>
        /// True when the outline (in mm) sits inside the frame, with a 1 mm tolerance.
        /// An unmeasurable frame answers TRUE, not false: "I could not determine the paper
        /// size" must not be reported as "your drawing overflows".
        /// </summary>
        public bool Contains(double minXMm, double minYMm, double maxXMm, double maxYMm)
        {
            if (!IsKnown) return true;
            return minXMm >= MinXMm - 1 && minYMm >= MinYMm - 1
                && maxXMm <= MaxXMm + 1 && maxYMm <= MaxYMm + 1;
        }
    }

    /// <summary>
    /// Measures the printable frame of <paramref name="sheet"/>. Never throws and never
    /// returns null: without a title block it falls back to [0,0]..[sheet size] and says so.
    /// </summary>
    public static Frame Measure(Document doc, ViewSheet sheet)
    {
        var widthFt = sheet.get_Parameter(BuiltInParameter.SHEET_WIDTH)?.AsDouble() ?? 0;
        var heightFt = sheet.get_Parameter(BuiltInParameter.SHEET_HEIGHT)?.AsDouble() ?? 0;

        Element? titleBlock = null;
        try
        {
            titleBlock = new FilteredElementCollector(doc, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .FirstOrDefault();
        }
        catch
        {
            // A sheet that cannot be collected from is treated as frameless below.
        }

        BoundingBoxXYZ? box = null;
        try { box = titleBlock?.get_BoundingBox(sheet); } catch { }

        if (box != null)
        {
            return new Frame
            {
                MinXMm = box.Min.X * MmPerFoot,
                MinYMm = box.Min.Y * MmPerFoot,
                MaxXMm = box.Max.X * MmPerFoot,
                MaxYMm = box.Max.Y * MmPerFoot,
                FromTitleBlock = true,
                Source = "titleBlock"
            };
        }

        // No title block. SHEET_WIDTH/SHEET_HEIGHT are driven BY the title block on most
        // templates, so on a frameless sheet they are 0 — and returning [0,0]x[0,0] made
        // every viewport report fitsOnSheet:false with no usable paper size to work from.
        if (widthFt > 0 && heightFt > 0)
        {
            return new Frame
            {
                MinXMm = 0,
                MinYMm = 0,
                MaxXMm = widthFt * MmPerFoot,
                MaxYMm = heightFt * MmPerFoot,
                FromTitleBlock = false,
                Source = "sheetSize"
            };
        }

        // Last resort: the sheet's own paper-space bounds. Documented to return all
        // zeros for an empty view, which is why it is checked rather than trusted.
        try
        {
            var outline = sheet.Outline;
            if (outline != null)
            {
                var width = (outline.Max.U - outline.Min.U) * MmPerFoot;
                var height = (outline.Max.V - outline.Min.V) * MmPerFoot;
                if (width > 1 && height > 1)
                {
                    return new Frame
                    {
                        MinXMm = outline.Min.U * MmPerFoot,
                        MinYMm = outline.Min.V * MmPerFoot,
                        MaxXMm = outline.Max.U * MmPerFoot,
                        MaxYMm = outline.Max.V * MmPerFoot,
                        FromTitleBlock = false,
                        Source = "viewOutline"
                    };
                }
            }
        }
        catch
        {
            // Outline is unavailable on some sheet states; fall through to unknown.
        }

        // Nothing could give an extent. Say so instead of reporting a zero-size frame
        // that reads as a real measurement.
        return new Frame
        {
            MinXMm = 0,
            MinYMm = 0,
            MaxXMm = 0,
            MaxYMm = 0,
            FromTitleBlock = false,
            Source = "unknown"
        };
    }

    /// <summary>
    /// Splits the frame into <paramref name="count"/> cells, row-major from the top-left,
    /// on the squarest grid that holds them. Centring every viewport of a multi-view sheet
    /// on the same point stacks them all on top of each other; one cell each at least
    /// separates them. Cell centres are a layout, not a fit guarantee — an uncropped view
    /// still overflows its cell, which <see cref="PlaceCentred"/> reports per viewport.
    /// </summary>
    public static Frame[] Subdivide(Frame frame, int count)
    {
        if (count <= 1) return new[] { frame };

        var cols = (int)System.Math.Ceiling(System.Math.Sqrt(count));
        var rows = (int)System.Math.Ceiling(count / (double)cols);
        var cellW = frame.WidthMm / cols;
        var cellH = frame.HeightMm / rows;

        var cells = new Frame[count];
        for (var i = 0; i < count; i++)
        {
            var col = i % cols;
            // Row 0 is the TOP of the sheet, so it takes the highest Y band.
            var row = i / cols;
            var minX = frame.MinXMm + col * cellW;
            var maxY = frame.MaxYMm - row * cellH;
            cells[i] = new Frame
            {
                MinXMm = minX,
                MinYMm = maxY - cellH,
                MaxXMm = minX + cellW,
                MaxYMm = maxY,
                FromTitleBlock = frame.FromTitleBlock
            };
        }
        return cells;
    }

    /// <summary>
    /// The frame outline as a serialisable record, for the tool response. Callers must
    /// compute positions from this, not from the sheet size.
    /// </summary>
    public static object Describe(Frame frame) => new
    {
        minX = System.Math.Round(frame.MinXMm, 1),
        minY = System.Math.Round(frame.MinYMm, 1),
        maxX = System.Math.Round(frame.MaxXMm, 1),
        maxY = System.Math.Round(frame.MaxYMm, 1),
        widthMm = System.Math.Round(frame.WidthMm, 1),
        heightMm = System.Math.Round(frame.HeightMm, 1),
        fromTitleBlock = frame.FromTitleBlock,
        source = frame.Source,
        known = frame.IsKnown,
        note = frame.Source switch
        {
            "titleBlock" => null,
            "sheetSize" => "No title block on this sheet: the frame is the full sheet, and its "
                         + "origin is assumed to be (0,0).",
            "viewOutline" => "No title block and no sheet size: the extent comes from the sheet's "
                           + "own paper-space bounds.",
            _ => "This sheet has no title block and no measurable extent, so fitsOnSheet cannot "
               + "be judged. Add a title block with place_title_block."
        }
    };

    /// <summary>
    /// The outcome of one viewport placement. A concrete type, not an anonymous object:
    /// the callers count successes and overflows, and a `dynamic` read of a member that
    /// only one of two anonymous shapes carries throws at runtime.
    /// </summary>
    public sealed class Placement
    {
        public long ViewId { get; init; }
        public long? ViewportId { get; init; }
        public bool Success { get; init; }
        public string? Reason { get; init; }
        public string? ViewName { get; init; }
        public double? CentreXMm { get; init; }
        public double? CentreYMm { get; init; }
        public double? WidthMm { get; init; }
        public double? HeightMm { get; init; }
        public int? ViewScale { get; init; }
        public bool? CropActive { get; init; }

        /// <summary>True when the viewport outline sits inside the frame. Null when unplaced.</summary>
        public bool? FitsFrame { get; init; }

        public string? Warning { get; init; }
    }

    /// <summary>True when the placement actually produced a viewport.</summary>
    public static bool WasPlaced(Placement placement) => placement.Success;

    /// <summary>Counts the placements that produced a viewport lying outside the frame.</summary>
    public static int CountOutsideFrame(System.Collections.Generic.IEnumerable<Placement> placements)
    {
        var outside = 0;
        foreach (var p in placements)
            if (p.Success && p.FitsFrame == false) outside++;
        return outside;
    }

    /// <summary>
    /// Places <paramref name="viewId"/> at the centre of <paramref name="frame"/> and reports
    /// whether the viewport actually fits inside it. Shared by batch_create_sheets and
    /// place_viewport so a sheet built by either tool lands the same way.
    /// Must be called inside an open transaction.
    /// </summary>
    public static Placement PlaceCentred(Document doc, ViewSheet sheet, ElementId viewId, Frame frame)
    {
        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, viewId))
        {
            return new Placement
            {
                ViewId = ToolHelpers.GetElementIdValue(viewId),
                Success = false,
                Reason = "Revit refuses this view on this sheet: it is already placed on another sheet, "
                       + "or it is a view type that cannot be placed as a viewport."
            };
        }

        var viewport = Viewport.Create(doc, sheet.Id, viewId, frame.CentreFeet);

        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        var haveOutline = false;
        try
        {
            var outline = viewport.GetBoxOutline();
            minX = outline.MinimumPoint.X * MmPerFoot;
            minY = outline.MinimumPoint.Y * MmPerFoot;
            maxX = outline.MaximumPoint.X * MmPerFoot;
            maxY = outline.MaximumPoint.Y * MmPerFoot;
            haveOutline = true;
        }
        catch
        {
            // Outline is unavailable on some viewport states; the placement still stands.
        }

        var fits = !haveOutline || frame.Contains(minX, minY, maxX, maxY);
        var view = doc.GetElement(viewId) as View;

        return new Placement
        {
            ViewId = ToolHelpers.GetElementIdValue(viewId),
            ViewportId = ToolHelpers.GetElementIdValue(viewport.Id),
            Success = true,
            ViewName = view?.Name,
            CentreXMm = System.Math.Round(frame.CentreXMm, 1),
            CentreYMm = System.Math.Round(frame.CentreYMm, 1),
            WidthMm = haveOutline ? System.Math.Round(maxX - minX, 1) : null,
            HeightMm = haveOutline ? System.Math.Round(maxY - minY, 1) : null,
            ViewScale = view?.Scale,
            CropActive = view?.CropBoxActive,
            // Null, not false, when there is nothing to judge against: no viewport outline,
            // or no measurable frame.
            FitsFrame = (haveOutline && frame.IsKnown) ? fits : null,
            Warning = fits
                ? null
                : $"The viewport spans {maxX - minX:F0} x {maxY - minY:F0} mm, larger than the frame "
                  + $"({frame.WidthMm:F0} x {frame.HeightMm:F0} mm): part of the drawing falls outside "
                  + "the border. Crop the view first — paper size = crop size / view scale."
        };
    }
}
