using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Measures the distance between two elements or explicit points (in mm).
/// Supports center_to_center, closest_points, and bounding_box measure types.
/// Mirrors the fork's MeasureBetweenElementsEventHandler logic.
/// </summary>
[ToolSafety(true, false)]
public class MeasureBetweenElementsTool : ICortexTool
{
    public string Name => "measure_between_elements";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Measures the distance between two elements or explicit points (in mm). Supports center_to_center, closest_points, and bounding_box measure types. Mirrors the fork's MeasureBetweenElementsEventHandler logic.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var elementId1 = input["elementId1"]?.Value<long?>() ?? 0;
        var elementId2 = input["elementId2"]?.Value<long?>() ?? 0;
        var point1Token = input["point1"];
        var point2Token = input["point2"];
        var measureType = input["measureType"]?.Value<string>() ?? "center_to_center";

        // Parse optional explicit points ({x,y,z} in mm)
        double[]? rawPoint1 = ParsePoint(point1Token);
        double[]? rawPoint2 = ParsePoint(point2Token);

        // Require at least two references (mix of element IDs and/or explicit points)
        bool hasRef1 = elementId1 > 0 || rawPoint1 != null;
        bool hasRef2 = elementId2 > 0 || rawPoint2 != null;

        if (!hasRef1 || !hasRef2)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "Must provide two references. Each can be an elementId or an explicit point {x, y, z} (mm).",
                suggestion: "Example: {\"elementId1\": 123, \"elementId2\": 456} or {\"point1\": {\"x\":0,\"y\":0,\"z\":0}, \"elementId2\": 456}");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        try
        {
            XYZ p1, p2;
            if (measureType.Equals("closest_points", StringComparison.OrdinalIgnoreCase)
                && elementId1 > 0 && elementId2 > 0)
            {
                var e1 = doc.GetElement(ToElementId(elementId1));
                var e2 = doc.GetElement(ToElementId(elementId2));
                if (e1 == null) throw new ArgumentException($"Element {elementId1} not found");
                if (e2 == null) throw new ArgumentException($"Element {elementId2} not found");
                ClosestPoints(e1, e2, out p1, out p2);
            }
            else
            {
                p1 = ResolvePoint(doc, elementId1, rawPoint1, measureType);
                p2 = ResolvePoint(doc, elementId2, rawPoint2, measureType);
            }

            double distanceFeet = p1.DistanceTo(p2);
            double distanceMm   = distanceFeet * 304.8;
            double dx = Math.Abs(p2.X - p1.X) * 304.8;
            double dy = Math.Abs(p2.Y - p1.Y) * 304.8;
            double dz = Math.Abs(p2.Z - p1.Z) * 304.8;

            return CortexResult<object>.Ok(new
            {
                message      = $"Distance: {distanceMm:F1} mm ({distanceMm / 1000:F3} m)",
                distance     = Math.Round(distanceMm, 1),
                distanceMeters = Math.Round(distanceMm / 1000, 3),
                deltaX = Math.Round(dx, 1),
                deltaY = Math.Round(dy, 1),
                deltaZ = Math.Round(dz, 1),
                point1 = FormatPoint(p1),
                point2 = FormatPoint(p2),
                measureType
            });
        }
        catch (ArgumentException ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, ex.Message);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Measure failed: {ex.Message}");
        }
    }

    private static XYZ ResolvePoint(Document doc, long elementId, double[]? point, string measureType)
    {
        // Explicit point takes priority (mm → feet)
        if (point != null && point.Length >= 3)
            return new XYZ(point[0] / 304.8, point[1] / 304.8, point[2] / 304.8);

        if (elementId > 0)
        {
            var element = doc.GetElement(ToElementId(elementId));
            if (element == null)
                throw new ArgumentException($"Element {elementId} not found");

            // bounding_box always uses bounding box center
            var bb = element.get_BoundingBox(null);
            if (bb != null)
                return (bb.Min + bb.Max) / 2.0;

            // Fallback for elements without bounding box
            if (element.Location is LocationPoint lp)
                return lp.Point;
            if (element.Location is LocationCurve lc)
                return lc.Curve.Evaluate(0.5, true);

            throw new ArgumentException($"Element {elementId} has no measurable geometry");
        }

        throw new ArgumentException("No valid point reference provided (neither elementId > 0 nor explicit point)");
    }

    /// <summary>
    /// Computes the closest points between two elements using their axis-aligned
    /// bounding boxes (deterministic, available on all targets). For each axis the
    /// nearest in-range coordinate is chosen, giving the closest points of the two AABBs.
    /// Falls back to centroid/location when an element has no bounding box.
    /// </summary>
    private static void ClosestPoints(Element e1, Element e2, out XYZ p1, out XYZ p2)
    {
        var bb1 = e1.get_BoundingBox(null);
        var bb2 = e2.get_BoundingBox(null);
        if (bb1 == null || bb2 == null)
        {
            p1 = CenterOf(e1);
            p2 = CenterOf(e2);
            return;
        }

        p1 = new XYZ(
            NearestOnInterval(bb1.Min.X, bb1.Max.X, bb2.Min.X, bb2.Max.X),
            NearestOnInterval(bb1.Min.Y, bb1.Max.Y, bb2.Min.Y, bb2.Max.Y),
            NearestOnInterval(bb1.Min.Z, bb1.Max.Z, bb2.Min.Z, bb2.Max.Z));
        p2 = new XYZ(
            NearestOnInterval(bb2.Min.X, bb2.Max.X, bb1.Min.X, bb1.Max.X),
            NearestOnInterval(bb2.Min.Y, bb2.Max.Y, bb1.Min.Y, bb1.Max.Y),
            NearestOnInterval(bb2.Min.Z, bb2.Max.Z, bb1.Min.Z, bb1.Max.Z));
    }

    // Nearest point on [aMin,aMax] to the midpoint of interval [bMin,bMax] on one axis.
    private static double NearestOnInterval(double aMin, double aMax, double bMin, double bMax)
    {
        var target = (bMin + bMax) / 2.0;
        if (target < aMin) return aMin;
        if (target > aMax) return aMax;
        return target;
    }

    private static XYZ CenterOf(Element e)
    {
        var bb = e.get_BoundingBox(null);
        if (bb != null) return (bb.Min + bb.Max) / 2.0;
        if (e.Location is LocationPoint lp) return lp.Point;
        if (e.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
        throw new ArgumentException("Element has no measurable geometry");
    }

    private static double[]? ParsePoint(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return null;

        try
        {
            var x = token["x"]?.Value<double>() ?? 0.0;
            var y = token["y"]?.Value<double>() ?? 0.0;
            var z = token["z"]?.Value<double>() ?? 0.0;
            return new[] { x, y, z };
        }
        catch
        {
            return null;
        }
    }

    private static object FormatPoint(XYZ p) => new
    {
        x = Math.Round(p.X * 304.8, 1),
        y = Math.Round(p.Y * 304.8, 1),
        z = Math.Round(p.Z * 304.8, 1)
    };

    private static ElementId ToElementId(long id)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(id);
#else
        return new ElementId((int)id);
#endif
    }
}
