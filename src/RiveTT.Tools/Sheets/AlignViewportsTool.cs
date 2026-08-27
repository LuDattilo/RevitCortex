using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;
using static RiveTT.Tools.Utilities.LengthUnits;

namespace RiveTT.Tools.Sheets;

/// <summary>
/// Aligns viewports across sheets by placement position or model coordinates.
/// </summary>
[ToolSafety(false, false)]
public class AlignViewportsTool : ICortexTool
{
    public string Name => "align_viewports";
    public string Category => "Sheets";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Aligns viewports across sheets. alignMode 'placement' matches box centers; 'model' matches the box outline min-corner so equal-scale views of the same model region line up.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var sourceViewportId = input["sourceViewportId"]?.Value<long>() ?? 0;
        var targetViewportIds = input["targetViewportIds"]?.ToObject<List<long>>() ?? new List<long>();
        var alignMode = input["alignMode"]?.Value<string>() ?? "placement";

        if (sourceViewportId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "sourceViewportId is required");
        if (targetViewportIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "targetViewportIds array is required");

        try
        {
            var sourceVp = doc.GetElement(new ElementId(sourceViewportId)) as Viewport;
            if (sourceVp == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Source viewport not found");

            var useModel = alignMode.Equals("model", StringComparison.OrdinalIgnoreCase);
            var sourceCenter = sourceVp.GetBoxCenter();
            var sourceAnchor = useModel ? sourceVp.GetBoxOutline().MinimumPoint : sourceCenter;
            var results = new List<object>();

            using var tx = new Transaction(doc, "RiveTT: Align Viewports");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            foreach (var tid in targetViewportIds)
            {
                var targetVp = doc.GetElement(new ElementId(tid)) as Viewport;
                if (targetVp == null)
                {
                    results.Add(new { viewportId = tid, success = false, reason = "Viewport not found" });
                    continue;
                }

                try
                {
                    if (useModel)
                    {
                        var delta = sourceAnchor - targetVp.GetBoxOutline().MinimumPoint;
                        targetVp.SetBoxCenter(targetVp.GetBoxCenter() + delta);
                    }
                    else
                    {
                        targetVp.SetBoxCenter(sourceAnchor);
                    }
                    results.Add(new { viewportId = tid, success = true });
                }
                catch (Exception ex)
                {
                    results.Add(new { viewportId = tid, success = false, reason = ex.Message });
                }
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new
            {
                alignedCount = results.Count(r => ((dynamic)r).success),
                alignMode,
                sourcePosition = new { x = sourceCenter.X * MmPerFoot, y = sourceCenter.Y * MmPerFoot },
                results
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
