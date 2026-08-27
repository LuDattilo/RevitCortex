using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Annotations;

/// <summary>
/// Creates one or more dimension annotations between points or element references.
/// </summary>
[ToolSafety(false, false)]
public class CreateDimensionsTool : ICortexTool
{
    public string Name => "create_dimensions";
    public string Category => "Annotations";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates linear dimension annotations between elementIds (2+) or startPoint/endPoint. (Radial/diameter/angular dimensions are not available: the Revit API exposes them only via the Family editor's FamilyItemFactory, not in a project document.)";

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

        using var tx = new Transaction(doc, "RiveTT: Create Dimensions");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
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
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
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
            view = doc.GetElement(new ElementId(viewId)) as View;
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
        // Two passes on purpose. The measurement direction is only known once every
        // element centre is known, and the reference to pick on each element DEPENDS on
        // that direction: dimensioning two parallel walls means taking the faces that
        // face each other, not whichever face the geometry iterator yields first.
        //
        // The single-pass version took `the first face with a Reference` on each element.
        // Between two walls 7000 mm apart that measured two arbitrary, often
        // perpendicular faces, and Revit answered with a degenerate segment — a constant
        // -MmPerFoot mm (-1 ft) per segment, identical whatever the real gap.
        var resolved = new List<(long Id, Element Element, XYZ Centre)>();
        foreach (var idToken in elementIds)
        {
            var eid = idToken.Value<long>();
            var elem = doc.GetElement(ToolHelpers.ToElementId(eid));
            if (elem == null)
            {
                warnings.Add($"Element {eid} not found, skipping");
                continue;
            }
            resolved.Add((eid, elem, GetElementCenter(elem)));
        }

        if (resolved.Count < 2)
        {
            warnings.Add("Need at least 2 valid elements for a dimension "
                       + $"({resolved.Count} of {elementIds.Count} resolved)");
            return;
        }

        var firstCenter = resolved[0].Centre;
        var lastCenter = resolved[^1].Centre;
        var span = lastCenter - firstCenter;

        if (span.GetLength() < 1e-6)
        {
            warnings.Add("The elements share the same centre, so there is no direction to "
                       + "measure along. Dimension not created.");
            return;
        }
        var dir = span.Normalize();

        var refs = new ReferenceArray();
        for (var i = 0; i < resolved.Count; i++)
        {
            var (eid, elem, centre) = resolved[i];

            // Direction toward the OTHER elements, computed per element — not the
            // single shared `dir`. GetBestReference used to score by |dot|, which
            // cannot tell "facing the other element" from "facing away from it":
            // for two parallel walls this consistently kept picking the same
            // relative side of each (e.g. both walls' "+X face"), which measures
            // centre-to-centre instead of face-to-face. See P2.5 in
            // PLAN_CORRECTION.md.
            var othersCentre = resolved.Where((_, j) => j != i)
                .Aggregate(XYZ.Zero, (acc, r) => acc + r.Centre) / (resolved.Count - 1);
            var toOthers = othersCentre - centre;
            var faceDirection = toOthers.GetLength() > 1e-9 ? toOthers.Normalize() : dir;

            var reference = GetBestReference(elem, view, faceDirection);
            if (reference == null)
            {
                warnings.Add($"Cannot find dimensionable reference for element {eid}");
                continue;
            }
            refs.Append(reference);
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
            // Offset the dimension line clear of the elements. 2000 mm, converted once:
            // the previous expression was 3.0 / MmPerFoot * 1000 with a comment claiming
            // "3 feet offset", and actually produced 9.84 feet.
            var mid = (firstCenter + lastCenter) / 2.0;
            linePoint = mid + view.UpDirection * (2000.0 / MmPerFoot);
        }

        // A bound line along the measurement direction, through the offset point, long
        // enough to cover the span with a margin. It used to extend `dir * 1000` each way
        // — Revit works in FEET, so that was a 610 m dimension line.
        var half = span.GetLength() / 2.0 + (500.0 / MmPerFoot);
        Line dimLine;
        try
        {
            dimLine = Line.CreateBound(linePoint - dir * half, linePoint + dir * half);
        }
        catch
        {
            // fallback: use element centers line
            dimLine = Line.CreateBound(firstCenter, lastCenter);
        }

        var dim = doc.Create.NewDimension(view, dimLine, refs);
        if (dim != null)
        {
            createdIds.Add(ToolHelpers.GetElementIdValue(dim.Id));

            // Apply dimension type if specified
            var dimensionStyleId = spec["dimensionStyleId"]?.Value<long>() ?? -1;
            if (dimensionStyleId > 0)
            {
                var styleElem = doc.GetElement(new ElementId(dimensionStyleId));
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

        // A detail curve must lie IN the view's plane, so both anchors are projected onto
        // it first. Whatever z the caller passes is therefore irrelevant, which is worth
        // knowing: trying z=0 and then the level elevation both failed before, because
        // the z was never the problem.
        var normal = view.ViewDirection;
        p0 -= normal * normal.DotProduct(p0 - view.Origin);
        p1 -= normal * normal.DotProduct(p1 - view.Origin);

        // The anchor tick must ALSO lie in the view plane. It used to run along
        // XYZ.BasisZ, which is perpendicular to a plan view's plane — Revit rejected
        // every point-to-point dimension with "Curve must be in the plane", at any z.
        // UpDirection is in-plane by construction, and perpendicular to a horizontal
        // measurement, which is where a witness line belongs.
        var tick = view.UpDirection.Multiply(10.0 / MmPerFoot);

        var detailLine1 = doc.Create.NewDetailCurve(view, Line.CreateBound(p0, p0 + tick));
        var detailLine2 = doc.Create.NewDetailCurve(view, Line.CreateBound(p1, p1 + tick));

        // The two anchor detail lines are only useful while the dimension that
        // references them exists. Without cleanup they accumulate as invisible
        // orphans whenever NewDimension returns null OR throws (e.g. invalid refs,
        // or a failure while applying the dimension style afterwards). Track them
        // and remove any that the created dimension did not consume in a finally.
        bool dimensionCreated = false;
        try
        {
            var refs = new ReferenceArray();
            refs.Append(detailLine1.GeometryCurve.Reference);
            refs.Append(detailLine2.GeometryCurve.Reference);

            var linePointToken = spec["linePoint"];
            XYZ linePoint = linePointToken != null
                ? ParseXYZ(linePointToken)
                // 2000 mm, converted once. The old expression read 2.0 / MmPerFoot * 1000,
                // which is 6.56 feet, not the 2 it looked like.
                : (p0 + p1) / 2.0 + view.UpDirection * (2000.0 / MmPerFoot);

            var dimLine = Line.CreateBound(p0, p1);
            var dim = doc.Create.NewDimension(view, dimLine, refs);
            if (dim != null)
            {
                dimensionCreated = true;
                createdIds.Add(ToolHelpers.GetElementIdValue(dim.Id));

                // Apply dimension type if specified (parity with element-mode branch)
                var dimensionStyleId = spec["dimensionStyleId"]?.Value<long>() ?? -1;
                if (dimensionStyleId > 0)
                {
                    var styleElem = doc.GetElement(new ElementId(dimensionStyleId));
                    if (styleElem is DimensionType dt)
                        dim.DimensionType = dt;
                }
            }
            else
            {
                warnings.Add("NewDimension returned null; anchor detail lines were removed");
            }
        }
        finally
        {
            // If no dimension references the anchor lines, delete them so a failed
            // (null-return or thrown) attempt does not leave orphan detail lines.
            if (!dimensionCreated)
            {
                doc.Delete(detailLine1.Id);
                doc.Delete(detailLine2.Id);
            }
        }
    }

    /// <summary>
    /// The reference to dimension this element by: the planar face whose OUTWARD
    /// normal points most toward <paramref name="direction"/> (the other
    /// element(s) being dimensioned to).
    ///
    /// A linear dimension only means something between faces that face each
    /// other. Taking the first face the geometry iterator happens to yield gave
    /// two arbitrary, frequently perpendicular faces, and Revit produced a
    /// degenerate segment — a constant -1 ft whatever the real distance.
    ///
    /// The score is the SIGNED dot product, not its absolute value: for a face
    /// whose normal points AWAY from the other element (dot near -1), abs() rated
    /// it as good as the correct near-side face (dot near +1), and ties broke on
    /// geometry-iterator order — which, for two similarly-oriented walls, landed
    /// on the SAME relative side of both, measuring centre-to-centre instead of
    /// face-to-face. See P2.5 in PLAN_CORRECTION.md.
    /// </summary>
    private static Reference? GetBestReference(Element elem, View view, XYZ direction)
    {
        var options = new Options { View = view, ComputeReferences = true };
        var geom = elem.get_Geometry(options);
        if (geom == null) return null;

        Reference? best = null;
        var bestScore = -1.0;
        Reference? fallback = null;

        void Consider(Solid solid)
        {
            foreach (Face face in solid.Faces)
            {
                if (face.Reference == null) continue;
                fallback ??= face.Reference;

                // Only a planar face has a single meaningful normal; a curved one cannot
                // be dimensioned to reliably.
                if (face is not PlanarFace planar) continue;

                var score = planar.FaceNormal.DotProduct(direction);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = face.Reference;
                }
            }
        }

        foreach (var obj in geom)
        {
            if (obj is Solid solid)
            {
                Consider(solid);
            }
            else if (obj is Line line && line.Reference != null)
            {
                fallback ??= line.Reference;
            }
            else if (obj is GeometryInstance gi)
            {
                foreach (var innerObj in gi.GetInstanceGeometry())
                    if (innerObj is Solid innerSolid)
                        Consider(innerSolid);
            }
        }

        // A face roughly perpendicular to the measurement is not worth returning as if it
        // were a choice; below ~30 degrees off-axis the dimension is meaningless anyway.
        return bestScore > 0.5 ? best : (best ?? fallback);
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
