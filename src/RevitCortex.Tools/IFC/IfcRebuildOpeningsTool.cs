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
/// Cuts openings in rebuilt walls and floors based on IFC opening elements.
/// Uses Document.Create.NewOpening for rectangular wall openings and
/// curve-based floor/roof openings.
/// </summary>
public class IfcRebuildOpeningsTool : ICortexTool
{
    public string Name => "ifc_rebuild_openings";
    public string Category => "IFC";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Cut openings in rebuilt walls/floors based on IFC opening elements";

    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (error != null) return error;

        var elementIds = input["elementIds"]?.ToObject<long[]>();
        var hostElementIds = input["hostElementIds"]?.ToObject<long[]>();
        var dryRun = input["dryRun"]?.Value<bool>() ?? true;

        // Find opening DirectShapes
        List<DirectShape> openingCandidates;
        if (elementIds != null && elementIds.Length > 0)
        {
            openingCandidates = elementIds
                .Select(id => doc!.GetElement(ToolHelpers.ToElementId(id)) as DirectShape)
                .Where(ds => ds != null)
                .ToList()!;
        }
        else
        {
            // IFC openings are typically in GenericModel or a void category
            openingCandidates = IfcGeometryHelper.GetDirectShapes(doc!)
                .Where(ds =>
                {
                    var ifcType = IfcGeometryHelper.GetIfcParameter(ds, "IfcExportAs")
                               ?? IfcGeometryHelper.GetIfcParameter(ds, "IfcType") ?? "";
                    return ifcType.IndexOf("Opening", StringComparison.OrdinalIgnoreCase) >= 0
                        || ifcType.IndexOf("IfcOpeningElement", StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .ToList();
        }

        // Find host elements (rebuilt walls/floors)
        List<Element> hosts;
        if (hostElementIds != null && hostElementIds.Length > 0)
        {
            hosts = hostElementIds
                .Select(id => doc!.GetElement(ToolHelpers.ToElementId(id)))
                .Where(e => e != null)
                .ToList()!;
        }
        else
        {
            var walls = new FilteredElementCollector(doc!)
                .OfClass(typeof(Wall)).Cast<Element>();
            var floors = new FilteredElementCollector(doc!)
                .OfClass(typeof(Floor)).Cast<Element>();
            hosts = walls.Concat(floors).ToList();
        }

        var results = new List<object>();
        int created = 0, skipped = 0;

        if (!dryRun)
        {
            if (!session.RequestConfirmation("rebuild openings", openingCandidates.Count))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");
        }

        // One TransactionGroup per invocation: the N per-element commits collapse
        // into a single undo step, and a mid-run failure can no longer leave a
        // fragmented undo stack behind.
        using TransactionGroup? txGroup = dryRun ? null : new TransactionGroup(doc!, "RevitCortex: Rebuild Openings");
        txGroup?.Start();

        foreach (var openingDs in openingCandidates)
        {
            var bb = openingDs.get_BoundingBox(null);
            if (bb == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(openingDs.Id),
                    status = "skipped",
                    reason = "No bounding box",
                });
                continue;
            }

            // Find the host that contains this opening (bounding box overlap)
            var hostElement = FindContainingHost(hosts, bb);
            if (hostElement == null)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(openingDs.Id),
                    status = "skipped",
                    reason = "No matching host element found",
                });
                continue;
            }

            if (dryRun)
            {
                created++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(openingDs.Id),
                    hostElementId = ToolHelpers.GetElementIdValue(hostElement.Id),
                    hostType = hostElement is Wall ? "wall" : "floor",
                    status = "would_create",
                    widthMm = Math.Round((bb.Max.X - bb.Min.X) * MmPerFoot, 0),
                    heightMm = Math.Round((bb.Max.Z - bb.Min.Z) * MmPerFoot, 0),
                });
                continue;
            }

            try
            {
                using var tx = new Transaction(doc!, "RevitCortex: Create Opening");
                var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                tx.Start();

                if (hostElement is Wall wall)
                {
                    doc!.Create.NewOpening(wall, bb.Min, bb.Max);
                }
                else
                {
                    // Floor/roof opening using curve profile
                    var curveArray = new CurveArray();
                    curveArray.Append(Line.CreateBound(
                        new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z),
                        new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z)));
                    curveArray.Append(Line.CreateBound(
                        new XYZ(bb.Max.X, bb.Min.Y, bb.Min.Z),
                        new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z)));
                    curveArray.Append(Line.CreateBound(
                        new XYZ(bb.Max.X, bb.Max.Y, bb.Min.Z),
                        new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z)));
                    curveArray.Append(Line.CreateBound(
                        new XYZ(bb.Min.X, bb.Max.Y, bb.Min.Z),
                        new XYZ(bb.Min.X, bb.Min.Y, bb.Min.Z)));

                    doc!.Create.NewOpening(hostElement, curveArray, true);
                }

                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException(
                        "Revit rolled back the transaction: " + TransactionFailureHandling.Describe(txFailures));

                created++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(openingDs.Id),
                    hostElementId = ToolHelpers.GetElementIdValue(hostElement.Id),
                    status = "created",
                });
            }
            catch (Exception ex)
            {
                skipped++;
                results.Add(new
                {
                    sourceElementId = ToolHelpers.GetElementIdValue(openingDs.Id),
                    status = "failed",
                    reason = ex.Message,
                });
            }
        }

        if (txGroup != null && txGroup.GetStatus() == TransactionStatus.Started)
            txGroup.Assimilate();

        return CortexResult<object>.Ok(new { dryRun, totalCandidates = openingCandidates.Count, created, skipped, results });
    }

    private static Element? FindContainingHost(List<Element> hosts, BoundingBoxXYZ openingBb)
    {
        var openingCenter = new XYZ(
            (openingBb.Min.X + openingBb.Max.X) / 2,
            (openingBb.Min.Y + openingBb.Max.Y) / 2,
            (openingBb.Min.Z + openingBb.Max.Z) / 2);

        foreach (var host in hosts)
        {
            var hostBb = host.get_BoundingBox(null);
            if (hostBb == null) continue;

            // Check if opening center is within the host bounding box (with tolerance)
            double tol = 0.5; // ~150mm tolerance
            if (openingCenter.X >= hostBb.Min.X - tol && openingCenter.X <= hostBb.Max.X + tol &&
                openingCenter.Y >= hostBb.Min.Y - tol && openingCenter.Y <= hostBb.Max.Y + tol &&
                openingCenter.Z >= hostBb.Min.Z - tol && openingCenter.Z <= hostBb.Max.Z + tol)
            {
                return host;
            }
        }
        return null;
    }
}
