using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Sheets;

/// <summary>
/// Aligns viewports across sheets by placement position or model coordinates.
/// </summary>
public class AlignViewportsTool : ICortexTool
{
    public string Name => "align_viewports";
    public string Category => "Sheets";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Aligns viewports across sheets by placement position or model coordinates.";
    private const double MmPerFoot = 304.8;

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
#if REVIT2024_OR_GREATER
            var sourceVp = doc.GetElement(new ElementId(sourceViewportId)) as Viewport;
#else
            var sourceVp = doc.GetElement(new ElementId((int)sourceViewportId)) as Viewport;
#endif
            if (sourceVp == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Source viewport not found");

            var sourceCenter = sourceVp.GetBoxCenter();
            var results = new List<object>();

            using var tx = new Transaction(doc, "RevitCortex: Align Viewports");
            tx.Start();

            foreach (var tid in targetViewportIds)
            {
#if REVIT2024_OR_GREATER
                var targetVp = doc.GetElement(new ElementId(tid)) as Viewport;
#else
                var targetVp = doc.GetElement(new ElementId((int)tid)) as Viewport;
#endif
                if (targetVp == null)
                {
                    results.Add(new { viewportId = tid, success = false, reason = "Viewport not found" });
                    continue;
                }

                try
                {
                    targetVp.SetBoxCenter(sourceCenter);
                    results.Add(new { viewportId = tid, success = true });
                }
                catch (Exception ex)
                {
                    results.Add(new { viewportId = tid, success = false, reason = ex.Message });
                }
            }

            tx.Commit();

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
