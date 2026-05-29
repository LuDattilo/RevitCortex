using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Annotations;

/// <summary>
/// Creates one or more dimension annotations between points or element references.
/// </summary>
public class CreateDimensionsTool : ICortexTool
{
    public string Name => "create_dimensions";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates one or more dimension annotations between points or element references.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var dimensions = input["dimensions"] as JArray;
        if (dimensions == null || dimensions.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "dimensions array is required",
                suggestion: "Provide {\"dimensions\": [{\"startPoint\": {\"x\":0,\"y\":0,\"z\":0}, \"endPoint\": {\"x\":1000,\"y\":0,\"z\":0}}]}");

        var createdIds = new List<long>();
        var warnings = new List<string>();

        using var tx = new Transaction(doc, "RevitCortex: Create Dimensions");
        tx.Start();

        try
        {
            foreach (var dimSpec in dimensions)
            {
                try
                {
                    CreateSingleDimension(doc, (JObject)dimSpec, createdIds, warnings);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to create dimension: {ex.Message}");
                }
            }
            tx.Commit();
        }
        catch
        {
            if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
            throw;
        }

        return CortexResult<object>.Ok(new
        {
            createdCount = createdIds.Count,
            createdDimensionIds = createdIds,
            warnings
        });
    }

    private static void CreateSingleDimension(Document doc, JObject spec, List<long> createdIds, List<string> warnings)
    {
        // Resolve view
        var viewId = spec["viewId"]?.Value<long>() ?? -1;
        View? view;
        if (viewId > 0)
        {
#if REVIT2024_OR_GREATER
            view = doc.GetElement(new ElementId(viewId)) as View;
#else
            view = doc.GetElement(new ElementId((int)viewId)) as View;
#endif
        }
        else
        {
            view = doc.ActiveView;
        }

        if (view == null)
        {
            warnings.Add("Could not resolve target view");
            return;
        }

        var elementIds = spec["elementIds"] as JArray;
        var startPt = spec["startPoint"];
        var endPt = spec["endPoint"];

        if (elementIds != null && elementIds.Count >= 2)
        {
            CreateDimensionBetweenElements(doc, view, elementIds, spec, createdIds, warnings);
        }
        else if (startPt != null && endPt != null)
        {
            CreateDimensionBetweenPoints(doc, view, startPt, endPt, spec, createdIds, warnings);
        }
        else
        {
            warnings.Add("Provide either elementIds (2+) or startPoint/endPoint");
        }
    }

    private static void CreateDimensionBetweenElements(
        Document doc, View view, JArray elementIds, JObject spec,
        List<long> createdIds, List<string> warnings)
    {
        var refs = new ReferenceArray();
        XYZ? firstCenter = null;
        XYZ? lastCenter = null;

        foreach (var idToken in elementIds)
        {
            var eid = idToken.Value<long>();
#if REVIT2024_OR_GREATER
            var elem = doc.GetElement(new ElementId(eid));
#else
            var elem = doc.GetElement(new ElementId((int)eid));
#endif
            if (elem == null)
            {
                warnings.Add($"Element {eid} not found, skipping");
                continue;
            }

            var reference = GetBestReference(elem, view);
            if (reference == null)
            {
                warnings.Add($"Cannot find dimensionable reference for element {eid}");
                continue;
            }

            refs.Append(reference);

            var center = GetElementCenter(elem);
            if (firstCenter == null) firstCenter = center;
            lastCenter = center;
        }

        if (refs.Size < 2)
        {
            warnings.Add("Need at least 2 valid element references for a dimension");
            return;
        }

        // Build dimension line
        var linePointToken = spec["linePoint"];
        XYZ linePoint;
        if (linePointToken != null)
        {
            linePoint = ParseXYZ(linePointToken);
        }
        else
        {
            // Offset from midpoint
            var mid = (firstCenter! + lastCenter!) / 2.0;
            linePoint = mid + view.UpDirection * (3.0 / MmPerFoot * 1000); // 3 feet offset
        }

        var dimLine = Line.CreateBound(firstCenter!, lastCenter!);
        // Project dimension line through the offset point
        var dir = (lastCenter! - firstCenter!).Normalize();
        var projectedStart = linePoint - dir * 1000;
        var projectedEnd = linePoint + dir * 1000;
        try
        {
            dimLine = Line.CreateBound(projectedStart, projectedEnd);
        }
        catch
        {
            // fallback: use element centers line
            dimLine = Line.CreateBound(firstCenter!, lastCenter!);
        }

        var dim = doc.Create.NewDimension(view, dimLine, refs);
        if (dim != null)
        {
            createdIds.Add(ToolHelpers.GetElementIdValue(dim.Id));

            // Apply dimension type if specified
            var dimensionStyleId = spec["dimensionStyleId"]?.Value<long>() ?? -1;
            if (dimensionStyleId > 0)
            {
#if REVIT2024_OR_GREATER
                var styleElem = doc.GetElement(new ElementId(dimensionStyleId));
#else
                var styleElem = doc.GetElement(new ElementId((int)dimensionStyleId));
#endif
                if (styleElem is DimensionType dt)
                    dim.DimensionType = dt;
            }
        }
    }

    private static void CreateDimensionBetweenPoints(
        Document doc, View view, JToken startPtToken, JToken endPtToken, JObject spec,
        List<long> createdIds, List<string> warnings)
    {
        var p0 = ParseXYZ(startPtToken);
        var p1 = ParseXYZ(endPtToken);

        if (p0.IsAlmostEqualTo(p1))
        {
            warnings.Add("Start and end points are identical");
            return;
        }

        // For point-to-point dimensions we need detail lines as references
        var detailLine1 = doc.Create.NewDetailCurve(view, Line.CreateBound(p0, p0 + XYZ.BasisZ * 0.01));
        var detailLine2 = doc.Create.NewDetailCurve(view, Line.CreateBound(p1, p1 + XYZ.BasisZ * 0.01));

        var refs = new ReferenceArray();
        refs.Append(detailLine1.GeometryCurve.Reference);
        refs.Append(detailLine2.GeometryCurve.Reference);

        var linePointToken = spec["linePoint"];
        XYZ linePoint = linePointToken != null
            ? ParseXYZ(linePointToken)
            : (p0 + p1) / 2.0 + view.UpDirection * (2.0 / MmPerFoot * 1000);

        var dimLine = Line.CreateBound(p0, p1);
        var dim = doc.Create.NewDimension(view, dimLine, refs);
        if (dim != null)
        {
            createdIds.Add(ToolHelpers.GetElementIdValue(dim.Id));

            // Apply dimension type if specified (parity with element-mode branch)
            var dimensionStyleId = spec["dimensionStyleId"]?.Value<long>() ?? -1;
            if (dimensionStyleId > 0)
            {
#if REVIT2024_OR_GREATER
                var styleElem = doc.GetElement(new ElementId(dimensionStyleId));
#else
                var styleElem = doc.GetElement(new ElementId((int)dimensionStyleId));
#endif
                if (styleElem is DimensionType dt)
                    dim.DimensionType = dt;
            }
        }
    }

    private static Reference? GetBestReference(Element elem, View view)
    {
        var options = new Options { View = view, ComputeReferences = true };
        var geom = elem.get_Geometry(options);
        if (geom == null) return null;

        foreach (var obj in geom)
        {
            if (obj is Solid solid)
            {
                foreach (Face face in solid.Faces)
                {
                    if (face.Reference != null)
                        return face.Reference;
                }
            }
            else if (obj is Line line && line.Reference != null)
            {
                return line.Reference;
            }
            else if (obj is GeometryInstance gi)
            {
                foreach (var innerObj in gi.GetInstanceGeometry())
                {
                    if (innerObj is Solid innerSolid)
                    {
                        foreach (Face face in innerSolid.Faces)
                        {
                            if (face.Reference != null)
                                return face.Reference;
                        }
                    }
                }
            }
        }
        return null;
    }

    private static XYZ GetElementCenter(Element elem)
    {
        var bb = elem.get_BoundingBox(null);
        if (bb != null)
            return (bb.Min + bb.Max) / 2.0;
        var loc = elem.Location;
        if (loc is LocationPoint lp) return lp.Point;
        if (loc is LocationCurve lc) return (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2.0;
        return XYZ.Zero;
    }

    private static XYZ ParseXYZ(JToken token)
    {
        var x = token["x"]?.Value<double>() ?? 0;
        var y = token["y"]?.Value<double>() ?? 0;
        var z = token["z"]?.Value<double>() ?? 0;
        return new XYZ(x / MmPerFoot, y / MmPerFoot, z / MmPerFoot);
    }
}
