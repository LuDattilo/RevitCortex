using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Elements;

/// <summary>Uses Revit 2027's native hosted-wall API for lining and façade walls.</summary>
[ToolSafety(false, false)]
public sealed class SetWallHostTool : ICortexTool
{

    public string Name => "set_wall_host";
    public string Category => "Architecture";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Associate a wall with a host wall using the Revit 2027 hosted-wall API, or clear that association.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var document = ToolHelpers.GetDocument(session);
        var wallId = input["wallId"]?.Value<long>() ?? -1;
        var hostWallId = input["hostWallId"]?.Value<long>() ?? -1;
        var offsetMm = input["offsetFromHost"]?.Value<double?>() ?? 0;
        if (document == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");
        var wall = document.GetElement(ToolHelpers.ToElementId(wallId)) as Wall;
        if (wall == null)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, $"Wall {wallId} was not found");

#if REVIT2027_OR_GREATER
        if (ToolHelpers.GetDryRun(input))
            return CortexResult<object>.Ok(new
            {
                dryRun = true,
                wallId,
                currentHostWallId = ToolHelpers.GetElementIdValue(wall.GetHostWallId()),
                requestedHostWallId = hostWallId,
                requestedOffsetFromHostMm = offsetMm
            });

        try
        {
            using var transaction = new Transaction(document, "RiveTT: Set Wall Host");
            var failures = TransactionFailureHandling.FromInput(transaction, input);
            transaction.Start();
            var hostId = hostWallId > 0 ? ToolHelpers.ToElementId(hostWallId) : ElementId.InvalidElementId;
            wall.SetHostWallId(hostId);
            var offset = wall.get_Parameter(BuiltInParameter.WALL_OFFSET_FROM_HOST);
            if (offset != null && !offset.IsReadOnly)
                offset.Set(offsetMm / MmPerFoot);
            if (transaction.Commit() != TransactionStatus.Committed)
                return TransactionFailureHandling.ToFailure(failures,
                    "Wall hosting was rolled back", "Verify that both walls support the hosted-wall relationship.");
            return CortexResult<object>.Ok(new
            {
                wallId,
                hostWallId = ToolHelpers.GetElementIdValue(wall.GetHostWallId()),
                offsetFromHost = offsetMm
            });
        }
        catch (Exception exception)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Could not set wall host: {exception.Message}");
        }
#else
        // Wall.GetHostWallId/SetHostWallId (walls hosted on walls) is a Revit 2027
        // API with no equivalent in 2026: report unsupported rather than fail to
        // compile or throw a MissingMethodException at runtime.
        return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
            "set_wall_host requires Revit 2027 or newer: walls hosted on walls do not exist in this Revit version.",
            suggestion: "Open the model in Revit 2027+ to use this feature.");
#endif
    }
}
