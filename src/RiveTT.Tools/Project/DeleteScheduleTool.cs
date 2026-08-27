using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.Project;

/// <summary>
/// Deletes a Revit schedule by ID or name. Defaults to dryRun=true — see DeletionPreview
/// for why the old RequestConfirmation call was not a safety net.
/// </summary>
[ToolSafety(false, true)]
public class DeleteScheduleTool : ICortexTool
{
    public string Name => "delete_schedule";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Deletes a Revit schedule by ID or name. Defaults to dryRun=true: the preview names the schedule and "
        + "reports the cascade, including the viewports that placed it on sheets. Set dryRun=false to execute.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var scheduleId = input["scheduleId"]?.Value<long>();
        var scheduleName = input["scheduleName"]?.Value<string>();

        try
        {
            ViewSchedule? schedule = null;

            if (scheduleId.HasValue && scheduleId.Value > 0)
            {
                schedule = doc.GetElement(new ElementId(scheduleId.Value)) as ViewSchedule;
            }
            else if (!string.IsNullOrEmpty(scheduleName))
            {
                schedule = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(s => s.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase));
            }

            if (schedule == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                    "Schedule not found",
                    suggestion: "List the schedules with get_schedule_data or export_schedule, or pass "
                              + "scheduleId instead of scheduleName.");

            var name = schedule.Name;

            if (ToolHelpers.GetDryRun(input))
                return DeletionPreview.Build(doc, schedule.Id,
                    $"Schedule '{name}'",
                    new { scheduleId = ToolHelpers.GetElementIdValue(schedule.Id), scheduleName = name });

            using var tx = new Transaction(doc, "RiveTT: Delete Schedule");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            doc.Delete(schedule.Id);
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new { deleted = true, scheduleName = name });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to delete the schedule: {ex.Message}",
                suggestion: "Run again with dryRun=true to see what the deletion would cascade to.");
        }
    }
}
