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
/// Reconstructs roofs from IFC-imported DirectShape elements using NewFootPrintRoof.
/// Extracts the bottom face footprint for the roof profile.
/// </summary>
[ToolSafety(false, false)]
public class IfcRebuildRoofsTool : ICortexTool
{
    public string Name => "ifc_rebuild_roofs";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Rebuild native Revit roofs from IFC-imported DirectShape elements";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var roofTypeIdRaw = input["roofTypeId"]?.Value<long>();
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
            candidates = IfcGeometryHelper.GetDirectShapes(doc!, BuiltInCategory.OST_Roofs);
        }

        RoofType? roofType = null;
        if (roofTypeIdRaw.HasValue)
        {
            roofType = doc!.GetElement(ToolHelpers.ToElementId(roofTypeIdRaw.Value)) as RoofType;
            if (roofType == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    $"RoofType {roofTypeIdRaw.Value} not found");
        }
        else
        {
            roofType = new FilteredElementCollector(doc!)
                .OfClass(typeof(RoofType))
                .Cast<RoofType>()
                .FirstOrDefault();
        }

        if (roofType == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No RoofType available in the document");

        var results = new List<object>();
        int rebuilt = 0, skipped = 0;

        if (!dryRun)
        {
            if (!session.RequestConfirmation("rebuild roofs", candidates.Count))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");
        }

        // One TransactionGroup per invocation: the N per-element commits collapse
        // into a single undo step, and a mid-run failure can no longer leave a
        // fragmented undo stack behind.
        using TransactionGroup? txGroup = dryRun ? null : new TransactionGroup(doc!, "RevitCortex: Rebuild Roofs");
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
                    reason = "Could not extract roof footprint",
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
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "would_rebuild",
                    roofTypeName = roofType.Name,
                    levelName = level.Name,
                });
                continue;
            }

            try
            {
                using var tx = new Transaction(doc!, "RevitCortex: Rebuild Roof");
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();

                // Convert CurveLoop to CurveArray for NewFootPrintRoof
                var curveArray = new CurveArray();
                foreach (var curve in footprint)
                    curveArray.Append(curve);

                var newRoof = doc!.Create.NewFootPrintRoof(
                    curveArray, level, roofType, out _);

                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException(
                        "Revit rolled back the transaction: " + TransactionFailureHandling.Describe(txFailures));

                rebuilt++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(ds.Id),
                    name = ds.Name,
                    status = "rebuilt",
                    newElementId = ToolHelpers.GetElementIdValue(newRoof?.Id),
                    roofTypeName = roofType.Name,
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
}
