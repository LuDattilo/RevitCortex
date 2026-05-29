using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Lists all schedules (if no scheduleId) or retrieves headers/rows for a specific schedule.
/// </summary>
public class GetScheduleDataTool : ICortexTool
{
    public string Name => "get_schedule_data";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Lists all schedules (if no scheduleId) or retrieves headers/rows for a specific schedule.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active document in session");

        var scheduleId = input["scheduleId"]?.Value<long>() ?? 0;
        var maxRows    = input["maxRows"]?.Value<int>() ?? 500;

        try
        {
            if (scheduleId <= 0)
                return ListAllSchedules(doc);

            return GetScheduleRows(doc, scheduleId, maxRows);
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
#if REVIT2024_OR_GREATER
                id = s.Id.Value,
#else
                id = (long)s.Id.IntegerValue,
#endif
                name     = s.Name,
                category = s.Definition.CategoryId != ElementId.InvalidElementId
#if REVIT2024_OR_GREATER
                    ? ((BuiltInCategory)s.Definition.CategoryId.Value).ToString()
#else
                    ? ((BuiltInCategory)s.Definition.CategoryId.IntegerValue).ToString()
#endif
                    : "None"
            })
            .ToList();

        return CortexResult<object>.Ok(new
        {
            scheduleCount = schedules.Count,
            schedules
        });
    }

    private static CortexResult<object> GetScheduleRows(Document doc, long scheduleId, int maxRows)
    {
#if REVIT2024_OR_GREATER
        var elem = doc.GetElement(new ElementId(scheduleId));
#else
        var elem = doc.GetElement(new ElementId((int)scheduleId));
#endif
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

        // Available fields
        var availableFields = definition.GetSchedulableFields()
            .Select(f => new
            {
                name      = f.GetName(doc),
                fieldType = f.FieldType.ToString(),
#if REVIT2024_OR_GREATER
                parameterId = f.ParameterId.Value
#else
                parameterId = (long)f.ParameterId.IntegerValue
#endif
            })
            .ToList();

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
            availableFields
        });
    }
}
