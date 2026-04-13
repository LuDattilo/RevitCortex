using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.IFC;

/// <summary>
/// Reconstructs walls from IFC-imported DirectShape elements using Wall.Create.
/// Extracts wall profile (base line + height + thickness) and finds matching WallType.
/// </summary>
public class IfcRebuildWallsTool : ICortexTool
{
    public string Name => "ifc_rebuild_walls";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Rebuild native Revit walls from IFC-imported DirectShape elements";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var wallTypeIdRaw = input["wallTypeId"]?.Value<long>();
        var structural = input["structural"]?.Value<bool>() ?? false;
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        // Get candidates
        List<DirectShape> candidates;
        if (elementIds != null && elementIds.Length > 0)
        {
            candidates = elementIds
                .Select(id => doc!.GetElement(ToolHelpers.ToElementId(id)) as DirectShape)
                .Where(ds => ds != null)
                .ToList()!;
        }
        else
        {
            candidates = IfcGeometryHelper.GetDirectShapes(doc!, BuiltInCategory.OST_Walls);
        }

        // Find wall type
        WallType? wallType = null;
        if (wallTypeIdRaw.HasValue)
        {
            wallType = doc!.GetElement(ToolHelpers.ToElementId(wallTypeIdRaw.Value)) as WallType;
            if (wallType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"WallType {wallTypeIdRaw.Value} not found");
        }

        var allWallTypes = new FilteredElementCollector(doc!)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .ToList();

        var results = new List<object>();
        int rebuilt = 0, skipped = 0;

        if (!dryRun)
        {
            if (!session.RequestConfirmation("rebuild walls", candidates.Count))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");
        }

        foreach (var ds in candidates)
        {
            var profile = IfcGeometryHelper.ExtractWallProfile(ds);
            if (profile == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "Could not extract wall profile",
                });
                continue;
            }

            // Find matching wall type by thickness
            var useWallType = wallType ?? FindClosestWallType(allWallTypes, profile.Thickness);
            if (useWallType == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "No matching WallType found for thickness " +
                             Math.Round(profile.Thickness * MmPerFoot, 0) + "mm",
                });
                continue;
            }

            var level = IfcGeometryHelper.FindNearestLevel(doc!, profile.BaseElevation);
            if (level == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "No level found",
                });
                continue;
            }

            if (dryRun)
            {
                rebuilt++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "would_rebuild",
                    wallTypeName = useWallType.Name,
                    levelName = level.Name,
                    lengthMm = Math.Round(profile.StartPoint.DistanceTo(profile.EndPoint) * MmPerFoot, 0),
                    heightMm = Math.Round(profile.Height * MmPerFoot, 0),
                    thicknessMm = Math.Round(profile.Thickness * MmPerFoot, 0),
                });
                continue;
            }

            // Actually rebuild
            try
            {
                using var tx = new Transaction(doc!, "RevitCortex: Rebuild Wall");
                tx.Start();

                var baseLine = Line.CreateBound(profile.StartPoint, profile.EndPoint);
                var offset = profile.BaseElevation - level.Elevation;

                var newWall = Wall.Create(doc!, baseLine, useWallType.Id, level.Id,
                    profile.Height, offset, false, structural);

                tx.Commit();

                rebuilt++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "rebuilt",
                    newElementId = ToolHelpers.GetElementIdValue(newWall.Id),
                    wallTypeName = useWallType.Name,
                    levelName = level.Name,
                });
            }
            catch (Exception ex)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "failed",
                    reason = ex.Message,
                });
            }
        }

        return CortexResult<object>.Ok(new
        {
            dryRun,
            totalCandidates = candidates.Count,
            rebuilt,
            skipped,
            results,
        });
    }

    private static WallType? FindClosestWallType(List<WallType> types, double thicknessFeet)
    {
        WallType? best = null;
        double bestDelta = double.MaxValue;

        foreach (var wt in types)
        {
            try
            {
                var width = wt.Width; // in feet
                var delta = Math.Abs(width - thicknessFeet);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = wt;
                }
            }
            catch { /* some types may not have Width */ }
        }

        // Only accept if within 50mm tolerance
        return bestDelta * MmPerFoot < 50 ? best : null;
    }
}
