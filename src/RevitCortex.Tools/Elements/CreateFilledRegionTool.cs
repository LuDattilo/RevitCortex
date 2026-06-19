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
/// Creates a filled region from boundary points in the specified view.
/// </summary>
public class CreateFilledRegionTool : ICortexTool
{
    public string Name => "create_filled_region";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a filled region from boundary points in the specified view, optionally with holes (inner loops).";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var boundaryPoints = input["boundaryPoints"] as JArray;
        if (boundaryPoints == null || boundaryPoints.Count < 3)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "boundaryPoints array with minimum 3 points is required");

        var viewIdLong = input["viewId"]?.Value<long>() ?? -1;
        var typeName = input["filledRegionTypeName"]?.Value<string>();

        try
        {
            // Resolve view
            View? view;
            if (viewIdLong > 0)
            {
#if REVIT2024_OR_GREATER
                view = doc.GetElement(new ElementId(viewIdLong)) as View;
#else
                view = doc.GetElement(new ElementId((int)viewIdLong)) as View;
#endif
            }
            else
            {
                view = doc.ActiveView;
            }

            if (view == null)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Could not resolve view");

            // Resolve type
            var regionType = !string.IsNullOrEmpty(typeName)
                ? new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>()
                    .FirstOrDefault(t => t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
                : null;
            regionType ??= new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>().FirstOrDefault();

            if (regionType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    "No filled region types available");

            // Build boundary curve loop
            var points = boundaryPoints.Select(p => new XYZ(
                p["x"]!.Value<double>() / MmPerFoot,
                p["y"]!.Value<double>() / MmPerFoot,
                0)).ToList();

            var loop = new CurveLoop();
            for (int i = 0; i < points.Count; i++)
                loop.Append(Line.CreateBound(points[i], points[(i + 1) % points.Count]));

            var loops = new List<CurveLoop> { loop };
            var warnings = new List<string>();
            var holes = input["holes"] as JArray;
            if (holes != null)
            {
                int holeIndex = 0;
                foreach (var hole in holes.OfType<JArray>())
                {
                    holeIndex++;
                    if (hole.Count < 3) { warnings.Add($"Hole {holeIndex} skipped: needs at least 3 points"); continue; }
                    try
                    {
                        var hpts = hole.Select(p => new XYZ(
                            p["x"]!.Value<double>() / MmPerFoot,
                            p["y"]!.Value<double>() / MmPerFoot, 0)).ToList();
                        var hloop = new CurveLoop();
                        for (int i = 0; i < hpts.Count; i++)
                            hloop.Append(Line.CreateBound(hpts[i], hpts[(i + 1) % hpts.Count]));
                        loops.Add(hloop);
                    }
                    catch (Exception ex) { warnings.Add($"Hole {holeIndex} skipped: {ex.Message}"); }
                }
            }

            using var tx = new Transaction(doc, "RevitCortex: Create Filled Region");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            var region = FilledRegion.Create(doc, regionType.Id, view.Id, loops);
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new
            {
                filledRegionId = ToolHelpers.GetElementIdValue(region.Id),
                typeName = regionType.Name,
                viewName = view.Name,
                holeCount = loops.Count - 1,
                warnings
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to create filled region: {ex.Message}");
        }
    }
}
