using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Views;

/// <summary>
/// Creates a 3D section box from selected elements' combined bounding box.
/// </summary>
[ToolSafety(false, false)]
public class SectionBoxFromSelectionTool : ICortexTool
{
    public string Name => "section_box_from_selection";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a 3D section box from selected elements' combined bounding box.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var elementIds = input["elementIds"]?.ToObject<List<long>>() ?? new List<long>();
        var offsetMm = input["offset"]?.Value<double>() ?? 1000;
        var duplicateView = input["duplicateView"]?.Value<bool>() ?? true;
        var viewName = input["viewName"]?.Value<string>();

        if (elementIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "elementIds array is required");

        try
        {
            // Compute combined bounding box
            XYZ? minPt = null, maxPt = null;
            foreach (var eid in elementIds)
            {
#if REVIT2024_OR_GREATER
                var elem = doc.GetElement(new ElementId(eid));
#else
                var elem = doc.GetElement(new ElementId((int)eid));
#endif
                if (elem == null) continue;
                var bb = elem.get_BoundingBox(null);
                if (bb == null) continue;

                minPt = minPt == null ? bb.Min : new XYZ(
                    Math.Min(minPt.X, bb.Min.X), Math.Min(minPt.Y, bb.Min.Y), Math.Min(minPt.Z, bb.Min.Z));
                maxPt = maxPt == null ? bb.Max : new XYZ(
                    Math.Max(maxPt.X, bb.Max.X), Math.Max(maxPt.Y, bb.Max.Y), Math.Max(maxPt.Z, bb.Max.Z));
            }

            if (minPt == null || maxPt == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No valid bounding boxes found");

            var offset = offsetMm / MmPerFoot;
            var sectionBox = new BoundingBoxXYZ
            {
                Min = new XYZ(minPt.X - offset, minPt.Y - offset, minPt.Z - offset),
                Max = new XYZ(maxPt.X + offset, maxPt.Y + offset, maxPt.Z + offset)
            };

            using var tx = new Transaction(doc, "RevitCortex: Section Box From Selection");
            tx.Start();

            View3D targetView;
            if (duplicateView)
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                    .FirstOrDefault(v => v.ViewFamily == ViewFamily.ThreeDimensional);
                if (vft == null)
                    return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "No 3D view family type");

                targetView = View3D.CreateIsometric(doc, vft.Id);
                targetView.Name = viewName ?? $"SectionBox_{DateTime.Now:HHmmss}";
            }
            else
            {
                targetView = doc.ActiveView as View3D
                    ?? new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                        .FirstOrDefault(v => !v.IsTemplate)!;
            }

            targetView.SetSectionBox(sectionBox);
            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                viewId = ToolHelpers.GetElementIdValue(targetView.Id),
                viewName = targetView.Name,
                elementCount = elementIds.Count
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
