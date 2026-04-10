using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Exports a schedule view to CSV/TSV format or returns data as structured JSON.
/// </summary>
public class ExportScheduleTool : ICortexTool
{
    public string Name => "export_schedule";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var scheduleId = input["scheduleId"]?.Value<long>() ?? 0;
        var exportPath = input["exportPath"]?.Value<string>();
        var delimiter = input["delimiter"]?.Value<string>() ?? "Tab";
        var includeHeaders = input["includeHeaders"]?.Value<bool>() ?? true;

        if (scheduleId <= 0)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "scheduleId is required");

        try
        {
#if REVIT2024_OR_GREATER
            var schedule = doc.GetElement(new ElementId(scheduleId)) as ViewSchedule;
#else
            var schedule = doc.GetElement(new ElementId((int)scheduleId)) as ViewSchedule;
#endif
            if (schedule == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Schedule not found");

            var tableData = schedule.GetTableData();
            var sectionData = tableData.GetSectionData(SectionType.Body);
            var headerData = tableData.GetSectionData(SectionType.Header);

            var rows = new List<List<string>>();

            // Headers
            if (includeHeaders && sectionData.NumberOfRows > 0)
            {
                var headerRow = new List<string>();
                for (int col = 0; col < sectionData.NumberOfColumns; col++)
                    headerRow.Add(schedule.GetCellText(SectionType.Body, 0, col));
                rows.Add(headerRow);
            }

            // Data rows
            int startRow = includeHeaders ? 1 : 0;
            for (int row = startRow; row < sectionData.NumberOfRows; row++)
            {
                var dataRow = new List<string>();
                for (int col = 0; col < sectionData.NumberOfColumns; col++)
                    dataRow.Add(schedule.GetCellText(SectionType.Body, row, col));
                rows.Add(dataRow);
            }

            // Export to file if path provided
            if (!string.IsNullOrEmpty(exportPath))
            {
                var sep = delimiter switch
                {
                    "Comma" => ",",
                    "Semicolon" => ";",
                    "Space" => " ",
                    _ => "\t"
                };

                var sb = new StringBuilder();
                foreach (var row in rows)
                    sb.AppendLine(string.Join(sep, row));

                File.WriteAllText(exportPath, sb.ToString(), Encoding.UTF8);
                return CortexResult<object>.Ok(new
                {
                    scheduleName = schedule.Name,
                    exportedTo = exportPath,
                    rowCount = rows.Count,
                    columnCount = sectionData.NumberOfColumns
                });
            }

            return CortexResult<object>.Ok(new
            {
                scheduleName = schedule.Name,
                rowCount = rows.Count,
                columnCount = sectionData.NumberOfColumns,
                data = rows
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }
}
