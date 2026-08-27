using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;

namespace RiveTT.Tools.Project;

/// <summary>
/// Lists all schedules (if no scheduleId) or retrieves headers/rows for a specific schedule.
/// </summary>
[ToolSafety(true, false)]
public class GetScheduleDataTool : ICortexTool
{
    public string Name => "get_schedule_data";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists all schedules (if no scheduleId) or retrieves headers/rows for a specific schedule. availableFields is NOT returned unless includeAvailableFields=true: on a real project it lists several hundred schedulable parameters and dwarfs the rows you asked for.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var scheduleId = input["scheduleId"]?.Value<long>() ?? 0;
        var maxRows    = input["maxRows"]?.Value<int>() ?? 500;
        // availableFields is independent of maxRows and ran into hundreds of entries,
        // so a 10-row request still blew past the client's output limit. Opt-in now;
        // list_schedulable_fields is the dedicated tool for it.
        var includeAvailableFields = input["includeAvailableFields"]?.Value<bool>() ?? false;

        try
        {
            if (scheduleId <= 0)
                return ListAllSchedules(doc);

            return GetScheduleRows(doc, scheduleId, maxRows, includeAvailableFields);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Failed to get schedule data: {ex.Message}");
        }
    }

    private static CortexResult<object> ListAllSchedules(Document doc)
    {
        var schedules = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(s => !s.IsTitleblockRevisionSchedule)
            .Select(s => new
            {
                id = s.Id.Value,
                name     = s.Name,
                category = s.Definition.CategoryId != ElementId.InvalidElementId
                    ? ((BuiltInCategory)s.Definition.CategoryId.Value).ToString()
                    : "None"
            })
            .ToList();

        return CortexResult<object>.Ok(new
        {
            scheduleCount = schedules.Count,
            schedules
        });
    }

    private static CortexResult<object> GetScheduleRows(
        Document doc, long scheduleId, int maxRows, bool includeAvailableFields)
    {
        var elem = doc.GetElement(new ElementId(scheduleId));
        if (elem is not ViewSchedule schedule)
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Schedule with ID {scheduleId} not found",
                suggestion: "Call get_schedule_data with no scheduleId to list all schedules");

        var definition = schedule.Definition;

        // Column headers
        var headers = new List<string>();
        for (int i = 0; i < definition.GetFieldCount(); i++)
        {
            var field = definition.GetField(i);
            headers.Add(field.GetName());
        }

        // Table rows
        var tableData = schedule.GetTableData();
        var bodySection = tableData.GetSectionData(SectionType.Body);
        int rowCount = bodySection.NumberOfRows;
        int colCount = bodySection.NumberOfColumns;

        var rows = new List<List<string>>();
        int startRow = bodySection.FirstRowNumber;
        for (int r = startRow; r < rowCount && rows.Count < maxRows; r++)
        {
            var row = new List<string>();
            for (int c = 0; c < colCount; c++)
            {
                try { row.Add(schedule.GetCellText(SectionType.Body, r, c)); }
                catch { row.Add(""); }
            }
            rows.Add(row);
        }

        // Available fields — only when asked for.
        var schedulableFields = definition.GetSchedulableFields();
        var availableFields = includeAvailableFields
            ? schedulableFields
                .Select(f => new
                {
                    name      = f.GetName(doc),
                    fieldType = f.FieldType.ToString(),
                    parameterId = f.ParameterId.Value
                })
                .Cast<object>()
                .ToList()
            : null;

        return CortexResult<object>.Ok(new
        {
            scheduleId,
            scheduleName    = schedule.Name,
            headers,
            columnHeaders   = headers,
            rows,
            fieldCount      = headers.Count,
            rowCount,
            returnedRows    = rows.Count,
            truncated       = rowCount - bodySection.FirstRowNumber > rows.Count,
            availableFieldCount = schedulableFields.Count,
            availableFields
        });
    }
}
