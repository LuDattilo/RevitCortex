using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

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

            using var tx = new Transaction(doc, "RevitCortex: Create Filled Region");
            tx.Start();
            var region = FilledRegion.Create(doc, regionType.Id, view.Id, new List<CurveLoop> { loop });
            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                filledRegionId = GetIdLong(region.Id),
                typeName = regionType.Name,
                viewName = view.Name
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to create filled region: {ex.Message}");
        }
    }

    private static long GetIdLong(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }
}
