using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Returns an element's REAL solid geometry extents (bounding box, centroid, volume,
/// face/edge counts) in mm and MODEL coordinates.
///
/// Unlike <c>element.get_BoundingBox(null)</c>, which returns the element bounding box,
/// this reflects the actual solid AFTER joins and cuts. A beam cut by columns/slabs has
/// a solid smaller and shifted relative to its element bounding box — placing rebar from
/// the element bounding box lands it partly in empty space ("Rebar is placed completely
/// outside of its host"). This tool exposes the armable solid so callers can position
/// rebar/elements inside the host's physical body. Read-only.
/// </summary>
public class GetElementSolidGeometryTool : ICortexTool
{
    public string Name => "get_element_solid_geometry";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Get an element's REAL solid geometry (bounding box, centroid, volume m3, face/edge counts AND inferred cross-section shape) in mm and model coordinates. Unlike get_BoundingBox this reflects the actual solid AFTER joins and cuts, and tells you the section SHAPE: 'rectangular', 'non_rectangular_polygonal' (T/L/I/U/channel — bbox lies, the real outline is concave), 'circular', or 'circular_or_tapered'. Reports capVertexCount and fillRatio so you can tell a box (4 verts, fill≈1.0) from a T/L (more verts and/or low fill). Essential for placing rebar correctly: a 613x613 bbox can be a Ø610 circular pile, and a T-beam's bbox includes empty space above the web — corner bars/rectangular ties placed from the bbox fall outside the concrete. Always use this, not the bounding box, when positioning rebar inside a host.";

    private const double MmPerFoot = 304.8;
    private const double Ft3ToM3 = 0.0283168;
    private const double MinVolumeFt3 = 1e-6;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var elementId = input["elementId"]?.Value<long?>() ?? 0;
        var maxSolids = input["maxSolids"]?.Value<int?>() ?? 20;
        if (maxSolids < 1) maxSolids = 1;

        if (elementId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "A positive elementId is required.",
                suggestion: "Example: {\"elementId\": 606873, \"maxSolids\": 20}");

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var element = doc.GetElement(ToElementId(elementId));
        if (element == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Element {elementId} does not exist in the active document",
                suggestion: "Check the element ID or ensure the correct document is open");

        try
        {
            var solids = CollectSolids(element);

            if (solids.Count == 0)
                return CortexResult<object>.Ok(new
                {
                    message = $"Element {elementId} has no solid geometry (annotation, line-based, or empty element).",
                    elementId,
                    elementName = SafeName(element),
                    category = element.Category?.Name,
                    solidCount = 0,
                    solidsReturned = 0,
                });

            // Aggregate accumulators (model-space mm), over ALL qualifying solids.
            double aggMinX = double.MaxValue, aggMinY = double.MaxValue, aggMinZ = double.MaxValue;
            double aggMaxX = double.MinValue, aggMaxY = double.MinValue, aggMaxZ = double.MinValue;
            double totalVolumeFt3 = 0;
            int totalFaces = 0, totalEdges = 0;

            var solidRows = new List<object>();
            for (int i = 0; i < solids.Count; i++)
            {
                var solid = solids[i];
                var bb = solid.GetBoundingBox();
                // GetBoundingBox() is in the solid's LOCAL space — map corners to model space.
                var min = bb.Transform.OfPoint(bb.Min);
                var max = bb.Transform.OfPoint(bb.Max);

                // The transform can swap which corner is numerically min/max per axis; normalize.
                double minX = Math.Min(min.X, max.X), maxX = Math.Max(min.X, max.X);
                double minY = Math.Min(min.Y, max.Y), maxY = Math.Max(min.Y, max.Y);
                double minZ = Math.Min(min.Z, max.Z), maxZ = Math.Max(min.Z, max.Z);

                aggMinX = Math.Min(aggMinX, minX); aggMaxX = Math.Max(aggMaxX, maxX);
                aggMinY = Math.Min(aggMinY, minY); aggMaxY = Math.Max(aggMaxY, maxY);
                aggMinZ = Math.Min(aggMinZ, minZ); aggMaxZ = Math.Max(aggMaxZ, maxZ);

                totalVolumeFt3 += solid.Volume;
                int faces = solid.Faces.Size;
                int edges = solid.Edges.Size;
                totalFaces += faces;
                totalEdges += edges;

                if (solidRows.Count < maxSolids)
                {
                    var centroid = solid.ComputeCentroid();
                    solidRows.Add(new
                    {
                        index = i,
                        boundingBox = new
                        {
                            min = PointMm(minX, minY, minZ),
                            max = PointMm(maxX, maxY, maxZ),
                            sizeX = Math.Round((maxX - minX) * MmPerFoot, 1),
                            sizeY = Math.Round((maxY - minY) * MmPerFoot, 1),
                            sizeZ = Math.Round((maxZ - minZ) * MmPerFoot, 1),
                        },
                        centroid = PointMm(centroid.X, centroid.Y, centroid.Z),
                        volume = Math.Round(solid.Volume * Ft3ToM3, 6),
                        faceCount = faces,
                        edgeCount = edges,
                        // Section shape matters for rebar layout: a 613x613 bbox can be a Ø610
                        // CIRCLE (corner bars/rect ties would fall outside). bbox gives extents only.
                        section = DetectSection(solid),
                    });
                }
            }

            return CortexResult<object>.Ok(new
            {
                elementId,
                elementName = SafeName(element),
                category = element.Category?.Name,
                solidCount = solids.Count,
                solidsReturned = solidRows.Count,
                truncated = solidRows.Count < solids.Count,
                aggregate = new
                {
                    boundingBox = new
                    {
                        min = PointMm(aggMinX, aggMinY, aggMinZ),
                        max = PointMm(aggMaxX, aggMaxY, aggMaxZ),
                        sizeX = Math.Round((aggMaxX - aggMinX) * MmPerFoot, 1),
                        sizeY = Math.Round((aggMaxY - aggMinY) * MmPerFoot, 1),
                        sizeZ = Math.Round((aggMaxZ - aggMinZ) * MmPerFoot, 1),
                    },
                    volume = Math.Round(totalVolumeFt3 * Ft3ToM3, 6),
                    faceCount = totalFaces,
                    edgeCount = totalEdges,
                },
                solids = solidRows,
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to read solid geometry of element {elementId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Classify a solid's cross-section so callers don't assume "rectangular" from the bbox.
    ///
    /// The cross-section CAP is the planar face of SMALLEST area (the two end caps of an extruded
    /// member are the smallest planar faces; the long lateral faces are larger). We read the cap's
    /// OUTER loop: a rectangular box cap has exactly 4 vertices and fills its own 2D bbox
    /// (fillRatio≈1.0). A T/L/I/U/channel profile is still all-planar but its cap has MORE than 4
    /// vertices and/or fills only part of its 2D bbox — so it must NOT be called "rectangular".
    /// A round/tapered pile has cylindrical/other lateral faces. Returns a small dictionary;
    /// never throws (geometry queries are wrapped).
    /// </summary>
    private static object DetectSection(Solid solid)
    {
        try
        {
            int planar = 0, cylindrical = 0, other = 0;
            PlanarFace? cap = null;
            double capMinArea = double.MaxValue;
            foreach (Face f in solid.Faces)
            {
                if (f is PlanarFace pf)
                {
                    planar++;
                    // The cross-section is the SMALLEST planar face (end cap), not the largest
                    // (a long lateral face). Reading the largest face was the bug that made a
                    // T-beam's flank look "rectangular".
                    if (f.Area > 0 && f.Area < capMinArea) { capMinArea = f.Area; cap = pf; }
                }
                else if (f is CylindricalFace) cylindrical++;
                else other++;
            }

            int capVertexCount = cap != null ? CountOuterLoopVertices(cap) : 0;
            double? fillRatio = cap != null ? CapFillRatio(cap) : (double?)null;
            string shape = ClassifyShape(planar, cylindrical, other, capVertexCount, fillRatio);

            return SectionResult(shape, planar, cylindrical, other, CapAreaM2(capMinArea), capVertexCount, fillRatio);
        }
        catch (Exception ex)
        {
            return new { shape = "unknown", error = ex.Message };
        }
    }

    /// <summary>
    /// Pure cross-section classifier (no Revit types, so it is unit-testable without a Revit host).
    /// Decides the shape label from face counts and the cap's outer-loop vertex count + fill ratio.
    /// A curved lateral face wins (round/tapered pile). Otherwise a 4-vertex cap that fills its 2D
    /// bbox is a box; a cap with more vertices and/or a low fill ratio is a T/L/I/U/channel.
    /// </summary>
    public static string ClassifyShape(int planar, int cylindrical, int other, int capVertexCount, double? fillRatio)
    {
        if (cylindrical > 0 && planar <= 3) return "circular";
        if (other > 0 && planar <= 3) return "circular_or_tapered";
        if (capVertexCount == 4 && (fillRatio == null || fillRatio.Value >= 0.95)) return "rectangular";
        if (capVertexCount >= 4 && cylindrical == 0 && other == 0) return "non_rectangular_polygonal";
        if (capVertexCount > 0) return "non_rectangular_polygonal";
        return "complex";
    }

    private static object SectionResult(string shape, int planar, int cylindrical, int other,
        double? capAreaM2, int? capVertexCount, double? fillRatio) => new
        {
            shape,
            planarFaces = planar,
            cylindricalFaces = cylindrical,
            otherFaces = other,
            capAreaM2,
            capVertexCount,
            fillRatio,
            note = "shape from the smallest planar face (the cross-section cap). 'rectangular' = 4-vertex cap filling its bbox; 'non_rectangular_polygonal' = T/L/I/U/channel — bars/ties must follow the real outline (get the cap vertex loop), NOT the bbox; 'circular'/'circular_or_tapered' = place bars on a bolt-circle and approximate ties with an inscribed polygon.",
        };

    private static double? CapAreaM2(double areaFt2) =>
        areaFt2 > 0 && areaFt2 < double.MaxValue
            ? Math.Round(areaFt2 * MmPerFoot * MmPerFoot / 1_000_000.0, 4)
            : (double?)null;

    /// <summary>Count vertices on a planar face's longest (outer) edge loop.</summary>
    private static int CountOuterLoopVertices(PlanarFace cap)
    {
        try
        {
            var loops = cap.EdgeLoops;
            int best = 0;
            for (int i = 0; i < loops.Size; i++)
            {
                int n = 0;
                foreach (Edge _ in loops.get_Item(i)) n++;
                if (n > best) best = n;   // outer loop has the most edges; holes are smaller
            }
            return best;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Ratio of the cap's true area to the area of its 2D bounding rectangle (in the face's UV
    /// frame). ≈1.0 for a filled rectangle; markedly less for an L/T/U whose outline is concave.
    /// </summary>
    private static double? CapFillRatio(PlanarFace cap)
    {
        try
        {
            var bb = cap.GetBoundingBox();   // UV bbox of the face's parametric domain
            double du = bb.Max.U - bb.Min.U;
            double dv = bb.Max.V - bb.Min.V;
            double rect = du * dv;
            if (rect <= 1e-9) return null;
            return Math.Round(cap.Area / rect, 3);
        }
        catch { return null; }
    }

    /// <summary>
    /// Collect all non-degenerate Solids (Volume &gt; 1e-6 ft³) from an element's Fine
    /// geometry, unwrapping nested <see cref="GeometryInstance"/> solids.
    /// </summary>
    private static List<Solid> CollectSolids(Element element)
    {
        var solids = new List<Solid>();
        var opts = new Options { DetailLevel = ViewDetailLevel.Fine };
        var geom = element.get_Geometry(opts);
        if (geom == null) return solids;

        foreach (var obj in geom)
        {
            if (obj is Solid solid && solid.Faces.Size > 0 && solid.Volume > MinVolumeFt3)
            {
                solids.Add(solid);
            }
            else if (obj is GeometryInstance inst)
            {
                foreach (var instObj in inst.GetInstanceGeometry())
                {
                    if (instObj is Solid s && s.Faces.Size > 0 && s.Volume > MinVolumeFt3)
                        solids.Add(s);
                }
            }
        }
        return solids;
    }

    private static object PointMm(double xFt, double yFt, double zFt) => new
    {
        x = Math.Round(xFt * MmPerFoot, 1),
        y = Math.Round(yFt * MmPerFoot, 1),
        z = Math.Round(zFt * MmPerFoot, 1),
    };

    private static string? SafeName(Element element)
    {
        try { return element.Name; }
        catch { return null; }
    }

    private static ElementId ToElementId(long id)
    {
#if REVIT2024_OR_GREATER
        return new ElementId(id);
#else
        return new ElementId((int)id);
#endif
    }
}
