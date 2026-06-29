using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Views;

/// <summary>
/// Modifies the view range (top, cut plane, bottom, view depth) for one or more plan views.
/// </summary>
[ToolSafety(false, false)]
public class BatchModifyViewRangeTool : ICortexTool
{
    public string Name => "batch_modify_view_range";
    public string Category => "Views";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Modifies the view range (top, cut plane, bottom, view depth) for one or more plan views.";
    private const double MmPerFoot = 304.8;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var viewIds = input["viewIds"]?.ToObject<List<long>>() ?? new List<long>();
        var topOffsetMm = input["topOffset"]?.Value<double?>();
        var cutPlaneOffsetMm = input["cutPlaneOffset"]?.Value<double?>();
        var bottomOffsetMm = input["bottomOffset"]?.Value<double?>();
        var viewDepthOffsetMm = input["viewDepthOffset"]?.Value<double?>();

        if (viewIds.Count == 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "viewIds array is required");

        // H9: confirm before modifying view ranges across a set of views.
        if (!session.RequestConfirmation("modify view range for", viewIds.Count))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled,
                "Operation cancelled by user");

        try
        {
            var results = new List<object>();
            using var tx = new Transaction(doc, "RevitCortex: Modify View Range");
            tx.Start();

            foreach (var vid in viewIds)
            {
#if REVIT2024_OR_GREATER
                var view = doc.GetElement(new ElementId(vid)) as ViewPlan;
#else
                var view = doc.GetElement(new ElementId((int)vid)) as ViewPlan;
#endif
                if (view == null) continue;

                var vr = view.GetViewRange();
                if (topOffsetMm.HasValue)
                    vr.SetOffset(PlanViewPlane.TopClipPlane, topOffsetMm.Value / MmPerFoot);
                if (cutPlaneOffsetMm.HasValue)
                    vr.SetOffset(PlanViewPlane.CutPlane, cutPlaneOffsetMm.Value / MmPerFoot);
                if (bottomOffsetMm.HasValue)
                    vr.SetOffset(PlanViewPlane.BottomClipPlane, bottomOffsetMm.Value / MmPerFoot);
                if (viewDepthOffsetMm.HasValue)
                    vr.SetOffset(PlanViewPlane.ViewDepthPlane, viewDepthOffsetMm.Value / MmPerFoot);

                view.SetViewRange(vr);

                var updated = view.GetViewRange();
                results.Add(new
                {
                    viewId = vid,
                    viewName = view.Name,
                    topOffset = updated.GetOffset(PlanViewPlane.TopClipPlane) * MmPerFoot,
                    cutPlaneOffset = updated.GetOffset(PlanViewPlane.CutPlane) * MmPerFoot,
                    bottomOffset = updated.GetOffset(PlanViewPlane.BottomClipPlane) * MmPerFoot,
                    viewDepthOffset = updated.GetOffset(PlanViewPlane.ViewDepthPlane) * MmPerFoot
                });
            }

            tx.Commit();
            return CortexResult<object>.Ok(new { modifiedCount = results.Count, views = results });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
