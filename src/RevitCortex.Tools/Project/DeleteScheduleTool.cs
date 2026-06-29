using System;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Deletes a Revit schedule by ID or name.
/// </summary>
[ToolSafety(false, true)]
public class DeleteScheduleTool : ICortexTool
{
    public string Name => "delete_schedule";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Deletes a Revit schedule by ID or name.";
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

            if (!session.RequestConfirmation("delete schedule", 1))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            var name = schedule.Name;
            using var tx = new Transaction(doc, "RevitCortex: Delete Schedule");
            tx.Start();
            doc.Delete(schedule.Id);
            tx.Commit();

            return CortexResult<object>.Ok(new { deleted = true, scheduleName = name });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
