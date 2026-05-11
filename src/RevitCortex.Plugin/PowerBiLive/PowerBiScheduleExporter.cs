using System;
using System.Collections.Generic;
using System.Threading;
using Autodesk.Revit.DB;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// Exports Revit ViewSchedules to detached DTOs (no Revit object references)
/// that can be safely passed to a background thread for HTTP publishing.
///
/// MUST be called on the Revit main thread. Returns plain dictionaries in
/// long-form (one row per cell) matching the Schedules table schema in
/// PowerBiDatasetSchema.
///
/// Long-form rationale: a single generic schema works for every schedule
/// regardless of number of columns or localization. Each cell becomes a row
/// with ScheduleId, ScheduleName, RowIndex, ColumnName, ValueString,
/// ValueNumber.
/// </summary>
public class PowerBiScheduleExporter
{
    private readonly string _schemaVersion = PowerBiDatasetSchema.CurrentVersion;

    /// <summary>
    /// Exports one or more schedules from the document into long-form rows.
    /// If scheduleIds is null or empty, exports all non-template schedules.
    /// </summary>
    public List<Dictionary<string, object?>> ExportSchedules(
        Document doc,
        string exportRunId,
        DateTime exportedAtUtc,
        IEnumerable<long>? scheduleIds = null,
        int maxRowsPerSchedule = 5_000,
        CancellationToken ct = default)
    {
        string projectId = SafeString(() => doc.ProjectInformation?.UniqueId) ?? "";
        string documentGuid = GetDocumentGuid(doc);

        // Build lookup set of requested ids (empty = all)
        var idFilter = new HashSet<long>();
        if (scheduleIds != null)
            foreach (var id in scheduleIds)
                idFilter.Add(id);

        var collector = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule));
        var rows = new List<Dictionary<string, object?>>();

        foreach (ViewSchedule sch in collector)
        {
            ct.ThrowIfCancellationRequested();
            if (sch == null) continue;

            try
            {
                if (sch.IsTemplate) continue;

                bool isRev = false;
                try { isRev = sch.IsTitleblockRevisionSchedule; } catch { }
                if (isRev) continue;

                long schId = GetElementIdValue(sch.Id);

                if (idFilter.Count > 0 && !idFilter.Contains(schId)) continue;

                var scheduleRows = ExportSingleSchedule(
                    doc, sch, schId, exportRunId, exportedAtUtc,
                    projectId, documentGuid, maxRowsPerSchedule, ct);

                rows.AddRange(scheduleRows);
            }
            catch
            {
                // Skip schedules that fail — never break the whole export
            }
        }

        return rows;
    }

    private List<Dictionary<string, object?>> ExportSingleSchedule(
        Document doc,
        ViewSchedule sch,
        long schId,
        string exportRunId,
        DateTime exportedAtUtc,
        string projectId,
        string documentGuid,
        int maxRowsPerSchedule,
        CancellationToken ct)
    {
        var rows = new List<Dictionary<string, object?>>();
        string scheduleName = SafeString(() => sch.Name) ?? "";
        string exportedAtStr = exportedAtUtc.ToString("o");

        // Get column names from schedule definition
        var columnNames = new List<string>();
        try
        {
            var def = sch.Definition;
            int fieldCount = def?.GetFieldCount() ?? 0;
            for (int i = 0; i < fieldCount; i++)
            {
                try
                {
                    var field = def!.GetField(i);
                    columnNames.Add(field?.ColumnHeading ?? $"Column{i}");
                }
                catch
                {
                    columnNames.Add($"Column{i}");
                }
            }
        }
        catch { }

        if (columnNames.Count == 0) return rows;

        // Get table data
        TableData? tableData = null;
        TableSectionData? body = null;
        try
        {
            tableData = sch.GetTableData();
            body = tableData?.GetSectionData(SectionType.Body);
        }
        catch { return rows; }

        if (body == null) return rows;

        int totalRows = body.NumberOfRows;
        // Row 0 is the header in Revit schedules — start from row 1
        int startRow = 1;
        int rowCount = 0;

        for (int r = startRow; r < totalRows; r++)
        {
            ct.ThrowIfCancellationRequested();
            if (rowCount >= maxRowsPerSchedule) break;

            int dataRowIndex = r - startRow; // 0-based data row index

            for (int c = 0; c < columnNames.Count; c++)
            {
                string cellValue = "";
                try
                {
                    cellValue = body.GetCellText(r, c) ?? "";
                }
                catch { }

                string columnName = columnNames[c];
                double? valueNumber = null;
                if (double.TryParse(cellValue,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double parsed))
                {
                    valueNumber = parsed;
                }

                rows.Add(new Dictionary<string, object?>
                {
                    ["_SchemaVersion"] = _schemaVersion,
                    ["ExportRunId"]    = exportRunId,
                    ["ExportedAtUtc"]  = exportedAtStr,
                    ["ProjectId"]      = projectId,
                    ["DocumentGuid"]   = documentGuid,
                    ["ScheduleId"]     = schId,
                    ["ScheduleName"]   = scheduleName,
                    ["RowIndex"]       = (long)dataRowIndex,
                    ["ColumnName"]     = columnName,
                    ["ValueString"]    = cellValue,
                    ["ValueNumber"]    = valueNumber ?? 0.0
                });
            }

            rowCount++;
        }

        return rows;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static long GetElementIdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    private static string? SafeString(Func<string?> fn)
    {
        try { return fn(); }
        catch { return null; }
    }

    private static string GetDocumentGuid(Document doc)
    {
        try
        {
            var cloudPath = doc.GetCloudModelPath();
            if (cloudPath != null)
                return cloudPath.ToString();
        }
        catch { }
        try { return doc.PathName ?? ""; }
        catch { return ""; }
    }
}
