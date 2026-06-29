using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Duplicates a Revit schedule by ID or name with a new name.
/// </summary>
[ToolSafety(false, false)]
public class DuplicateScheduleTool : ICortexTool
{
    public string Name => "duplicate_schedule";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Duplicates a Revit schedule by ID or name with a new name.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var scheduleId = input["scheduleId"]?.Value<long>();
        var scheduleName = input["scheduleName"]?.Value<string>();
        var newName = input["newName"]?.Value<string>();

        if (string.IsNullOrEmpty(newName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "newName is required");

        try
        {
            ViewSchedule? schedule = null;

            if (scheduleId.HasValue && scheduleId.Value > 0)
            {
#if REVIT2024_OR_GREATER
                schedule = doc.GetElement(new ElementId(scheduleId.Value)) as ViewSchedule;
#else
                schedule = doc.GetElement(new ElementId((int)scheduleId.Value)) as ViewSchedule;
#endif
            }
            else if (!string.IsNullOrEmpty(scheduleName))
            {
                schedule = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .Cast<ViewSchedule>()
                    .FirstOrDefault(s => s.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase));
            }

            if (schedule == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Schedule not found");

            using var tx = new Transaction(doc, "RevitCortex: Duplicate Schedule");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();
            var newId = schedule.Duplicate(ViewDuplicateOption.Duplicate);
            var newSchedule = doc.GetElement(newId) as ViewSchedule;
            if (newSchedule != null)
                newSchedule.Name = newName;
            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");

            return CortexResult<object>.Ok(new
            {
                originalName = schedule.Name,
                newName = newSchedule?.Name ?? newName,
                newScheduleId = ToolHelpers.GetElementIdValue(newId)
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
