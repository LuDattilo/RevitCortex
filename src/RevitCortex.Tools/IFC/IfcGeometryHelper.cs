using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Shared geometry utilities for IFC reconstruction tools.
/// Extracts geometry from DirectShape elements, matches levels, detects geometry types.
/// </summary>
public static class IfcGeometryHelper
{
    private const double MmPerFoot = 304.8;

    /// <summary>
    /// Extract all non-degenerate Solids from an element's geometry.
    /// Handles both top-level solids and nested GeometryInstance solids.
    /// </summary>
    public static List<Solid> GetSolids(Element element)
    {
        var solids = new List<Solid>();
        var opts = new Options { DetailLevel = ViewDetailLevel.Fine };
        var geom = element.get_Geometry(opts);
        if (geom == null) return solids;

        foreach (var obj in geom)
        {
            if (obj is Solid solid && solid.Faces.Size > 0 && solid.Volume > 1e-9)
            {
                solids.Add(solid);
            }
            else if (obj is GeometryInstance inst)
            {
                foreach (var instObj in inst.GetInstanceGeometry())
                {
                    if (instObj is Solid s && s.Faces.Size > 0 && s.Volume > 1e-9)
                        solids.Add(s);
                }
            }
        }
        return solids;
    }

    /// <summary>
    /// Get the combined bounding box of an element in mm.
    /// </summary>
    public static (XYZ min, XYZ max)? GetBoundingBoxMm(Element element)
    {
        var bb = element.get_BoundingBox(null);
        if (bb == null) return null;
        return (
            new XYZ(bb.Min.X * MmPerFoot, bb.Min.Y * MmPerFoot, bb.Min.Z * MmPerFoot),
            new XYZ(bb.Max.X * MmPerFoot, bb.Max.Y * MmPerFoot, bb.Max.Z * MmPerFoot)
        );
    }

    /// <summary>
    /// Compute total volume in cubic meters from all solids of an element.
    /// </summary>
    public static double GetVolumeCubicMeters(Element element)
    {
        var solids = GetSolids(element);
        double totalFt3 = solids.Sum(s => s.Volume);
        return totalFt3 * 0.0283168; // ft³ to m³
    }

    /// <summary>
    /// Find the nearest level at or below a given elevation (in feet).
    /// </summary>
    public static Level? FindNearestLevel(Document doc, double elevationFeet)
    {
        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderByDescending(l => l.Elevation)
            .ToList();

        foreach (var level in levels)
        {
            if (level.Elevation <= elevationFeet + 0.01) // small tolerance
                return level;
        }
        return levels.LastOrDefault(); // lowest level as fallback
    }

    /// <summary>
    /// Find level by name (case-insensitive).
    /// </summary>
    public static Level? FindLevelByName(Document doc, string name)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Detect geometry type of an element based on its solids.
    /// Returns: "extrusion", "sweep", "brep", "mesh", or "unknown".
    /// </summary>
    public static string DetectGeometryType(Element element)
    {
        var solids = GetSolids(element);
        if (solids.Count == 0)
        {
            // Check for mesh-only geometry
            var opts = new Options { DetailLevel = ViewDetailLevel.Fine };
            var geom = element.get_Geometry(opts);
            if (geom != null && geom.Any(g => g is Mesh))
                return "mesh";
            return "unknown";
        }

        // Simple heuristic: count planar vs curved faces
        int planarFaces = 0;
        int curvedFaces = 0;
        foreach (var solid in solids)
        {
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace) planarFaces++;
                else curvedFaces++;
            }
        }

        if (curvedFaces == 0 && planarFaces <= 8)
            return "extrusion"; // box-like, likely extruded profile
        if (curvedFaces > 0 && curvedFaces <= planarFaces)
            return "sweep"; // has curved surfaces but mostly planar
        return "brep"; // complex boundary representation
    }

    /// <summary>
    /// Extract the bottom face footprint of a solid as a CurveLoop.
    /// Returns null if no clear bottom planar face is found.
    /// </summary>
    public static CurveLoop? ExtractBottomFootprint(Solid solid)
    {
        PlanarFace? bottomFace = null;
        double lowestZ = double.MaxValue;

        foreach (Face face in solid.Faces)
        {
            if (face is PlanarFace pf)
            {
                // A bottom face has a normal pointing down (negative Z)
                if (pf.FaceNormal.Z < -0.9)
                {
                    var origin = pf.Origin;
                    if (origin.Z < lowestZ)
                    {
                        lowestZ = origin.Z;
                        bottomFace = pf;
                    }
                }
            }
        }

        if (bottomFace == null) return null;

        // Get the outer edge loop of the bottom face
        var edgeLoops = bottomFace.GetEdgesAsCurveLoops();
        return edgeLoops.Count > 0 ? edgeLoops[0] : null;
    }

    /// <summary>
    /// Try to extract a wall-like linear profile: base line + height + thickness.
    /// Returns null if the element doesn't look like a wall.
    /// </summary>
    public static WallProfile? ExtractWallProfile(Element element)
    {
        var bb = element.get_BoundingBox(null);
        if (bb == null) return null;

        double dx = bb.Max.X - bb.Min.X;
        double dy = bb.Max.Y - bb.Min.Y;
        double dz = bb.Max.Z - bb.Min.Z;

        // A wall has one dimension much larger than the other horizontal one
        double length, thickness;
        XYZ startPt, endPt;

        if (dx > dy * 3)
        {
            // Wall runs along X
            length = dx;
            thickness = dy;
            double midY = (bb.Min.Y + bb.Max.Y) / 2;
            startPt = new XYZ(bb.Min.X, midY, bb.Min.Z);
            endPt = new XYZ(bb.Max.X, midY, bb.Min.Z);
        }
        else if (dy > dx * 3)
        {
            // Wall runs along Y
            length = dy;
            thickness = dx;
            double midX = (bb.Min.X + bb.Max.X) / 2;
            startPt = new XYZ(midX, bb.Min.Y, bb.Min.Z);
            endPt = new XYZ(midX, bb.Max.Y, bb.Min.Z);
        }
        else
        {
            return null; // Not wall-like proportions
        }

        // Minimum wall proportions: length > 300mm, thickness 50-1000mm, height > 500mm
        double lengthMm = length * MmPerFoot;
        double thickMm = thickness * MmPerFoot;
        double heightMm = dz * MmPerFoot;
        if (lengthMm < 300 || thickMm < 50 || thickMm > 1000 || heightMm < 500)
            return null;

        return new WallProfile
        {
            StartPoint = startPt,
            EndPoint = endPt,
            Height = dz,
            Thickness = thickness,
            BaseElevation = bb.Min.Z,
        };
    }

    /// <summary>
    /// Try to extract a column-like profile: center point + height.
    /// </summary>
    public static ColumnProfile? ExtractColumnProfile(Element element)
    {
        var bb = element.get_BoundingBox(null);
        if (bb == null) return null;

        double dx = bb.Max.X - bb.Min.X;
        double dy = bb.Max.Y - bb.Min.Y;
        double dz = bb.Max.Z - bb.Min.Z;

        double maxHorizontal = Math.Max(dx, dy);

        // Column: height >> horizontal dimensions, roughly square cross-section
        double heightMm = dz * MmPerFoot;
        if (heightMm < 1000 || dz < maxHorizontal * 2)
            return null; // Not column-like

        return new ColumnProfile
        {
            CenterPoint = new XYZ(
                (bb.Min.X + bb.Max.X) / 2,
                (bb.Min.Y + bb.Max.Y) / 2,
                bb.Min.Z),
            Height = dz,
            BaseElevation = bb.Min.Z,
            CrossSectionWidth = dx,
            CrossSectionDepth = dy,
        };
    }

    /// <summary>
    /// Try to extract a beam-like profile: start point, end point, cross-section.
    /// A beam is horizontal with length >> cross-section dimensions.
    /// </summary>
    public static BeamProfile? ExtractBeamProfile(Element element)
    {
        var bb = element.get_BoundingBox(null);
        if (bb == null) return null;

        double dx = bb.Max.X - bb.Min.X;
        double dy = bb.Max.Y - bb.Min.Y;
        double dz = bb.Max.Z - bb.Min.Z;

        // Find the longest horizontal dimension
        double maxHoriz = Math.Max(dx, dy);
        double minHoriz = Math.Min(dx, dy);

        // Beam: longest horizontal dim >> the other two dims
        if (maxHoriz < minHoriz * 2.5 || maxHoriz < dz * 2.5)
            return null;

        XYZ startPt, endPt;
        double midZ = (bb.Min.Z + bb.Max.Z) / 2;

        if (dx >= dy)
        {
            double midY = (bb.Min.Y + bb.Max.Y) / 2;
            startPt = new XYZ(bb.Min.X, midY, midZ);
            endPt = new XYZ(bb.Max.X, midY, midZ);
        }
        else
        {
            double midX = (bb.Min.X + bb.Max.X) / 2;
            startPt = new XYZ(midX, bb.Min.Y, midZ);
            endPt = new XYZ(midX, bb.Max.Y, midZ);
        }

        return new BeamProfile
        {
            StartPoint = startPt,
            EndPoint = endPt,
            Elevation = midZ,
            CrossSectionWidth = Math.Min(dx, dy),
            CrossSectionDepth = dz,
        };
    }

    /// <summary>
    /// Get all DirectShape elements in the document, optionally filtered by category.
    /// </summary>
    public static List<DirectShape> GetDirectShapes(Document doc, BuiltInCategory? category = null)
    {
        var collector = new FilteredElementCollector(doc).OfClass(typeof(DirectShape));
        if (category.HasValue)
            collector = collector.OfCategory(category.Value);
        return collector.Cast<DirectShape>().ToList();
    }

    /// <summary>
    /// Get a specific IFC parameter value from an element by name.
    /// IFC imports store IFC properties as instance parameters.
    /// </summary>
    public static string? GetIfcParameter(Element element, string paramName)
    {
        var param = element.LookupParameter(paramName);
        if (param == null || !param.HasValue) return null;
        return param.StorageType == StorageType.String
            ? param.AsString()
            : param.AsValueString();
    }

    // ── Profile data classes ──

    public class WallProfile
    {
        public XYZ StartPoint { get; set; } = XYZ.Zero;
        public XYZ EndPoint { get; set; } = XYZ.Zero;
        public double Height { get; set; }
        public double Thickness { get; set; }
        public double BaseElevation { get; set; }
    }

    public class ColumnProfile
    {
        public XYZ CenterPoint { get; set; } = XYZ.Zero;
        public double Height { get; set; }
        public double BaseElevation { get; set; }
        public double CrossSectionWidth { get; set; }
        public double CrossSectionDepth { get; set; }
    }

    public class BeamProfile
    {
        public XYZ StartPoint { get; set; } = XYZ.Zero;
        public XYZ EndPoint { get; set; } = XYZ.Zero;
        public double Elevation { get; set; }
        public double CrossSectionWidth { get; set; }
        public double CrossSectionDepth { get; set; }
    }
}
