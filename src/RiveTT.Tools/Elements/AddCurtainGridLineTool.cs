using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>
/// Adds one grid line to an EXISTING curtain wall/system's grid — the wall itself is already
/// creatable via create_line_based_element with a curtain wall type. Split out of the former
/// manage_curtain_grid so this write, add_curtain_mullions, and get_curtain_grid_info each
/// carry only the parameters their own operation needs.
/// </summary>
[ToolSafety(false, false)]
public class AddCurtainGridLineTool : ICortexTool
{
    public string Name => "add_curtain_grid_line";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Adds a grid line to an existing curtain wall/system's grid. hostElementId, direction "
        + "(u|v), and offsetMm (distance along the host's own axis, in mm) are required.";

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

        var direction = (input["direction"]?.Value<string>() ?? "").ToLowerInvariant();
        var offsetMm = input["offsetMm"]?.Value<double?>();
        if ((direction != "u" && direction != "v") || offsetMm == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "direction (u|v) and offsetMm are required");

        // The API takes a full XYZ, but only the component along the grid's own axis
        // matters — Revit resolves the line against the host's own geometry from that.
        var offsetFt = offsetMm.Value / MmPerFoot;
        var point = direction == "u"
            ? new XYZ(0, 0, offsetFt)
            : new XYZ(offsetFt, 0, 0);

        using var tx = new Transaction(doc, "RiveTT: Add Curtain Grid Line");
        var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
        tx.Start();

        try
        {
            grid.AddGridLine(direction == "u", point, false);
        }
        catch (Exception ex)
        {
            tx.RollBack();
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"AddGridLine failed: {ex.Message}",
                suggestion: "Revit's own AddGridLine cannot add a line exactly at the host's start or end edge.");
        }

        if (tx.Commit() != TransactionStatus.Committed)
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}");

        return CurtainGridAccess.DescribeGrid(grid);
    }
}
