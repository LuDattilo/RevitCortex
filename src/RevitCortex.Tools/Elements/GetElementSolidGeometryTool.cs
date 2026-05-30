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
    public string Description => "Get an element's REAL solid geometry extents (bounding box, centroid, volume m3, face/edge counts) in mm and model coordinates. Unlike get_BoundingBox this reflects the actual solid AFTER joins and cuts — essential for placing rebar/elements inside hosts cut by columns or slabs, where the element bounding box is larger than the armable solid.";

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
