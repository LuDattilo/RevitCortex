using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Compares an original IFC DirectShape with its rebuilt native Revit element.
/// Reports volume difference, bounding box overlap, and geometric similarity.
/// </summary>
[ToolSafety(true, false)]
public class IfcCompareOriginalVsRebuiltTool : ICortexTool
{
    public string Name => "ifc_compare_original_vs_rebuilt";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Compare original IFC element with its rebuilt native Revit counterpart";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var originalId = input["originalElementId"]?.Value<long>() ?? 0;
        var rebuiltId = input["rebuiltElementId"]?.Value<long>() ?? 0;

        if (originalId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "originalElementId is required");
        if (rebuiltId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "rebuiltElementId is required");

        var original = doc!.GetElement(ToolHelpers.ToElementId(originalId));
        var rebuilt = doc!.GetElement(ToolHelpers.ToElementId(rebuiltId));

        if (original == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Original element {originalId} not found");
        if (rebuilt == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Rebuilt element {rebuiltId} not found");

        var origVolume = IfcGeometryHelper.GetVolumeCubicMeters(original);
        var rebuVolume = IfcGeometryHelper.GetVolumeCubicMeters(rebuilt);

        var origBb = original.get_BoundingBox(null);
        var rebuBb = rebuilt.get_BoundingBox(null);

        double volumeDiffPercent = origVolume > 0.0001
            ? Math.Round((rebuVolume - origVolume) / origVolume * 100, 2)
            : 0;

        double bbOverlap = 0;
        if (origBb != null && rebuBb != null)
            bbOverlap = Math.Round(ComputeBbOverlap(origBb, rebuBb) * 100, 1);

        var qualityScore = ComputeQualityScore(volumeDiffPercent, bbOverlap);

        return CortexResult<object>.Ok(new
        {
            original = new
            {
                elementId = originalId,
                category = original.Category?.Name ?? "Unknown",
                volumeM3 = Math.Round(origVolume, 4),
                boundingBox = origBb != null ? FormatBb(origBb) : null,
            },
            rebuilt = new
            {
                elementId = rebuiltId,
                category = rebuilt.Category?.Name ?? "Unknown",
                volumeM3 = Math.Round(rebuVolume, 4),
                boundingBox = rebuBb != null ? FormatBb(rebuBb) : null,
            },
            comparison = new
            {
                volumeDifferencePercent = volumeDiffPercent,
                boundingBoxOverlapPercent = bbOverlap,
                qualityScore,
                qualityRating = qualityScore >= 90 ? "excellent"
                    : qualityScore >= 70 ? "good"
                    : qualityScore >= 50 ? "fair"
                    : "poor",
            },
        });
    }

    private static double ComputeBbOverlap(BoundingBoxXYZ a, BoundingBoxXYZ b)
    {
        double overlapX = Math.Max(0, Math.Min(a.Max.X, b.Max.X) - Math.Max(a.Min.X, b.Min.X));
        double overlapY = Math.Max(0, Math.Min(a.Max.Y, b.Max.Y) - Math.Max(a.Min.Y, b.Min.Y));
        double overlapZ = Math.Max(0, Math.Min(a.Max.Z, b.Max.Z) - Math.Max(a.Min.Z, b.Min.Z));
        double overlapVol = overlapX * overlapY * overlapZ;

        double aVol = (a.Max.X - a.Min.X) * (a.Max.Y - a.Min.Y) * (a.Max.Z - a.Min.Z);
        double bVol = (b.Max.X - b.Min.X) * (b.Max.Y - b.Min.Y) * (b.Max.Z - b.Min.Z);
        double unionVol = aVol + bVol - overlapVol;

        return unionVol > 0 ? overlapVol / unionVol : 0;
    }

    private static double ComputeQualityScore(double volumeDiffPercent, double bbOverlap)
    {
        // Score from 0-100
        double volScore = Math.Max(0, 100 - Math.Abs(volumeDiffPercent) * 2);
        double bbScore = bbOverlap;
        return Math.Round((volScore + bbScore) / 2, 1);
    }

    private static object FormatBb(BoundingBoxXYZ bb)
    {
        return new
        {
            minMm = new { x = Math.Round(bb.Min.X * MmPerFoot, 0), y = Math.Round(bb.Min.Y * MmPerFoot, 0), z = Math.Round(bb.Min.Z * MmPerFoot, 0) },
            maxMm = new { x = Math.Round(bb.Max.X * MmPerFoot, 0), y = Math.Round(bb.Max.Y * MmPerFoot, 0), z = Math.Round(bb.Max.Z * MmPerFoot, 0) },
        };
    }
}
