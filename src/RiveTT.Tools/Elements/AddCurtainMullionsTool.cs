using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Adds mullions to an EXISTING curtain wall/system's grid lines. Split out of the former
/// manage_curtain_grid: AddMullions lives on CurtainGridLine, not CurtainGrid, and works one
/// segment curve at a time (verified signature: AddMullions(Curve, MullionType, bool
/// oneSegmentOnly)) — there is no bulk "add to this whole grid" call, which is a different
/// shape of operation from a plain grid-line insert or a read.
/// </summary>
[ToolSafety(false, false)]
public class AddCurtainMullionsTool : ICortexTool
{
    public string Name => "add_curtain_mullions";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Adds mullions to an existing curtain wall/system's grid lines. hostElementId and "
        + "mullionTypeId are required; applies to every ungridded segment unless gridLineIds narrows it.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var hostIdLong = input["hostElementId"]?.Value<long?>() ?? 0;
        if (hostIdLong <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "hostElementId is required");

        var host = doc.GetElement(ToolHelpers.ToElementId(hostIdLong));
        var grid = CurtainGridAccess.GetCurtainGrid(host);
        if (grid == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"hostElementId {hostIdLong} has no curtain grid (not a curtain wall/system/roof, or its type has no automatic grid)");

        var mullionTypeIdLong = input["mullionTypeId"]?.Value<long?>() ?? 0;
        if (mullionTypeIdLong <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "mullionTypeId is required",
                suggestion: "List OST_CurtainWallMullions types with list_system_types(category: \"OST_CurtainWallMullions\").");

        var mullionType = doc.GetElement(ToolHelpers.ToElementId(mullionTypeIdLong)) as MullionType;
        if (mullionType == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, $"{mullionTypeIdLong} is not a MullionType");

        var explicitIds = input["gridLineIds"]?.ToObject<List<long>>();
        var targetLineIds = explicitIds != null && explicitIds.Count > 0
            ? explicitIds.Select(ToolHelpers.ToElementId)
            : grid.GetUGridLineIds().Concat(grid.GetVGridLineIds());

        using var tx = new Transaction(doc, "RiveTT: Add Curtain Mullions");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        var addedCount = 0;
        var warnings = new List<string>();
        foreach (var lineId in targetLineIds)
        {
            if (doc.GetElement(lineId) is not CurtainGridLine gridLine)
            {
                warnings.Add($"{ToolHelpers.GetElementIdValue(lineId)} is not a curtain grid line");
                continue;
            }

            foreach (Curve segment in gridLine.AllSegmentCurves)
            {
                try
                {
                    // oneSegmentOnly=true: only THIS curve, never every segment that
                    // happens to share the same shape elsewhere on the grid.
                    gridLine.AddMullions(segment, mullionType, true);
                    addedCount++;
                }
                catch (Exception ex)
                {
                    // A segment that already has a mullion is a no-op for Revit, not a
                    // failure — but a genuine geometry rejection should still surface.
                    warnings.Add($"Segment on grid line {ToolHelpers.GetElementIdValue(lineId)}: {ex.Message}");
                }
            }
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        var info = CurtainGridAccess.DescribeGrid(grid);
        return CortexResult<object>.Ok(new { addedSegmentCount = addedCount, warnings, gridInfo = info.Data });
    }
}
