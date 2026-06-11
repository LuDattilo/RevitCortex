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
/// Reconstructs floors from IFC-imported DirectShape elements using Floor.Create.
/// Extracts the bottom face footprint as a CurveLoop for the floor profile.
/// </summary>
public class IfcRebuildFloorsTool : ICortexTool
{
    public string Name => "ifc_rebuild_floors";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Rebuild native Revit floors from IFC-imported DirectShape elements";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var floorTypeIdRaw = input["floorTypeId"]?.Value<long>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

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
            candidates = IfcGeometryHelper.GetDirectShapes(doc!, BuiltInCategory.OST_Floors);
        }

        FloorType? floorType = null;
        if (floorTypeIdRaw.HasValue)
        {
            floorType = doc!.GetElement(ToolHelpers.ToElementId(floorTypeIdRaw.Value)) as FloorType;
            if (floorType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"FloorType {floorTypeIdRaw.Value} not found");
        }
        else
        {
            // Use the first available floor type
            floorType = new FilteredElementCollector(doc!)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault();
        }

        if (floorType == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No FloorType available in the document");

        var results = new List<object>();
        int rebuilt = 0, skipped = 0;

        if (!dryRun)
        {
            if (!session.RequestConfirmation("rebuild floors", candidates.Count))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");
        }

        // One TransactionGroup per invocation: the N per-element commits collapse
        // into a single undo step, and a mid-run failure can no longer leave a
        // fragmented undo stack behind.
        using TransactionGroup? txGroup = dryRun ? null : new TransactionGroup(doc!, "RevitCortex: Rebuild Floors");
        txGroup?.Start();

        foreach (var ds in candidates)
        {
            var solids = IfcGeometryHelper.GetSolids(ds);
            if (solids.Count == 0)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "No solid geometry found",
                });
                continue;
            }

            var footprint = IfcGeometryHelper.ExtractBottomFootprint(solids[0]);
            if (footprint == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "skipped",
                    reason = "Could not extract floor footprint",
                });
                continue;
            }

            var bb = ds.get_BoundingBox(null);
            var level = IfcGeometryHelper.FindNearestLevel(doc!, bb?.Min.Z ?? 0);
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
                var areaSqM = footprint.GetExactLength() > 0
                    ? Math.Round(ComputeLoopArea(footprint) * MmPerFoot * MmPerFoot / 1e6, 2)
                    : 0;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "would_rebuild",
                    floorTypeName = floorType.Name,
                    levelName = level.Name,
                    areaSqM,
                });
                continue;
            }

            try
            {
                using var tx = new Transaction(doc!, "RevitCortex: Rebuild Floor");
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();

                var curveLoops = new List<CurveLoop> { footprint };
                var newFloor = Floor.Create(doc!, curveLoops, floorType.Id, level.Id);

                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException(
                        "Revit rolled back the transaction: " + TransactionFailureHandling.Describe(txFailures));

                rebuilt++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "rebuilt",
                    newElementId = ToolHelpers.GetElementIdValue(newFloor.Id),
                    floorTypeName = floorType.Name,
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

        if (txGroup != null && txGroup.GetStatus() == TransactionStatus.Started)
            txGroup.Assimilate();

        return CortexResult<object>.Ok(new
        {
            dryRun,
            totalCandidates = candidates.Count,
            rebuilt,
            skipped,
            results,
        });
    }

    private static double ComputeLoopArea(CurveLoop loop)
    {
        // Approximate area using shoelace formula on projected XY points
        var points = new List<XYZ>();
        foreach (var curve in loop)
        {
            points.Add(curve.GetEndPoint(0));
        }
        if (points.Count < 3) return 0;

        double area = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var j = (i + 1) % points.Count;
            area += points[i].X * points[j].Y;
            area -= points[j].X * points[i].Y;
        }
        return Math.Abs(area) / 2.0;
    }
}
