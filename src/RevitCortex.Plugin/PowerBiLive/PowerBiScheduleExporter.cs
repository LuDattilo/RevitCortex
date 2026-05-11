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
/// regardless of number of columns or localization. Each schedule field becomes
/// a row per visible element with ScheduleId, ElementId, ColumnName,
/// ValueString, ValueNumber.
/// </summary>
public class PowerBiScheduleExporter
{
    private readonly string _schemaVersion = PowerBiDatasetSchema.CurrentVersion;

    /// <summary>
    /// Exports one or more schedules from the document into long-form rows.
    /// Backward-compatible wrapper around <see cref="ExportSchedulesDetailed"/>
    /// that returns only the rows (no diagnostics). Prefer the Detailed variant
    /// for new callers — silent skip is a footgun for data-integrity workflows.
    /// </summary>
    public List<Dictionary<string, object?>> ExportSchedules(
        Document doc,
        string exportRunId,
        DateTime exportedAtUtc,
        IEnumerable<long>? scheduleIds = null,
        int maxElementsPerSchedule = 5_000,
        CancellationToken ct = default)
    {
        return ExportSchedulesDetailed(doc, exportRunId, exportedAtUtc,
            scheduleIds, maxElementsPerSchedule, maxCellsPerSchedule: 0, ct).Rows;
    }

    /// <summary>
    /// Exports schedules and returns rows together with a diagnostic envelope
    /// covering skipped schedules, skipped fields, truncations, and read errors.
    ///
    /// Caps:
    ///  - <paramref name="maxElementsPerSchedule"/> bounds the number of source
    ///    elements visited per schedule. Default 5000.
    ///  - <paramref name="maxCellsPerSchedule"/> bounds the number of long-form
    ///    rows emitted per schedule. 0 means no cell cap (only element cap).
    ///    Useful to guarantee the Push API row-budget per single schedule.
    ///
    /// Silent failures are NOT swallowed: the result envelope reports every
    /// schedule that was skipped and why, so callers can decide whether to fail
    /// the workflow or report partial success.
    /// </summary>
    public ScheduleExportResult ExportSchedulesDetailed(
        Document doc,
        string exportRunId,
        DateTime exportedAtUtc,
        IEnumerable<long>? scheduleIds = null,
        int maxElementsPerSchedule = 5_000,
        int maxCellsPerSchedule = 0,
        CancellationToken ct = default)
    {
        var result = new ScheduleExportResult();
        string projectId = SafeString(() => doc.ProjectInformation?.UniqueId) ?? "";
        string documentGuid = GetDocumentGuid(doc);

        var idFilter = new HashSet<long>();
        if (scheduleIds != null)
            foreach (var id in scheduleIds)
                idFilter.Add(id);

        var collector = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule));

        foreach (ViewSchedule sch in collector)
        {
            ct.ThrowIfCancellationRequested();
            if (sch == null) continue;

            long schId = 0;
            string scheduleName = "";
            try
            {
                if (sch.IsTemplate) continue;

                bool isRev = false;
                try { isRev = sch.IsTitleblockRevisionSchedule; } catch { }
                if (isRev) continue;

                schId = GetElementIdValue(sch.Id);
                scheduleName = SafeString(() => sch.Name) ?? "";

                if (idFilter.Count > 0 && !idFilter.Contains(schId)) continue;

                ExportSingleSchedule(
                    doc, sch, schId, scheduleName, exportRunId, exportedAtUtc,
                    projectId, documentGuid, maxElementsPerSchedule, maxCellsPerSchedule,
                    result, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Surface the failure instead of swallowing — callers need to
                // know which schedules were lost so they can retry or notify.
                result.SkippedSchedules.Add(new SkippedSchedule(
                    scheduleId: schId,
                    scheduleName: scheduleName,
                    reason: ex.GetType().Name + ": " + ex.Message));
            }
        }

        return result;
    }

    private void ExportSingleSchedule(
        Document doc,
        ViewSchedule sch,
        long schId,
        string scheduleName,
        string exportRunId,
        DateTime exportedAtUtc,
        string projectId,
        string documentGuid,
        int maxElementsPerSchedule,
        int maxCellsPerSchedule,
        ScheduleExportResult result,
        CancellationToken ct)
    {
        var rows = result.Rows;
        string exportedAtStr = exportedAtUtc.ToString("o");

        // Resolve fields once. Fields without a parameter id still produce a
        // column so the published shape reflects the schedule definition.
        var fields = new List<ScheduleExportField>();
        try
        {
            var def = sch.Definition;
            int fieldCount = def?.GetFieldCount() ?? 0;
            for (int i = 0; i < fieldCount; i++)
            {
                try
                {
                    var field = def!.GetField(i);
                    fields.Add(new ScheduleExportField(
                        field?.ColumnHeading ?? $"Column{i}",
                        field != null ? field.ParameterId : ElementId.InvalidElementId));
                }
                catch (Exception fex)
                {
                    fields.Add(new ScheduleExportField($"Column{i}", ElementId.InvalidElementId));
                    result.SkippedFields.Add(new SkippedField(schId, scheduleName, $"Column{i}",
                        $"{fex.GetType().Name}: {fex.Message}"));
                }
            }
        }
        catch (Exception ex)
        {
            result.SkippedSchedules.Add(new SkippedSchedule(schId, scheduleName,
                $"definition unreadable: {ex.GetType().Name}: {ex.Message}"));
            return;
        }

        if (fields.Count == 0)
        {
            result.SkippedSchedules.Add(new SkippedSchedule(schId, scheduleName,
                "schedule has no fields"));
            return;
        }

        IList<Element> elements;
        try
        {
            elements = new FilteredElementCollector(doc, sch.Id)
                .WhereElementIsNotElementType()
                .ToElements();
        }
        catch (Exception ex)
        {
            result.SkippedSchedules.Add(new SkippedSchedule(schId, scheduleName,
                $"element collector failed: {ex.GetType().Name}: {ex.Message}"));
            return;
        }

        int elementCount = 0;
        int cellsEmitted = 0;
        bool truncatedByElements = false;
        bool truncatedByCells = false;
        foreach (var elem in elements)
        {
            ct.ThrowIfCancellationRequested();
            if (elementCount >= maxElementsPerSchedule)
            {
                truncatedByElements = true;
                break;
            }
            if (elem == null) continue;

            long elementId = GetElementIdValue(elem.Id);
            string uniqueId = SafeString(() => elem.UniqueId) ?? "";

            foreach (var field in fields)
            {
                if (maxCellsPerSchedule > 0 && cellsEmitted >= maxCellsPerSchedule)
                {
                    truncatedByCells = true;
                    break;
                }

                var parameter = ResolveScheduleFieldParameter(doc, elem, field.ParameterId);
                var value = ReadParameterValue(doc, parameter);

                rows.Add(new Dictionary<string, object?>
                {
                    ["_SchemaVersion"] = _schemaVersion,
                    ["ExportRunId"]    = exportRunId,
                    ["ExportedAtUtc"]  = exportedAtStr,
                    ["ProjectId"]      = projectId,
                    ["DocumentGuid"]   = documentGuid,
                    ["ScheduleId"]     = schId,
                    ["ScheduleName"]   = scheduleName,
                    ["ElementId"]      = elementId,
                    ["UniqueId"]       = uniqueId,
                    ["RowIndex"]       = (long)elementCount,
                    ["ColumnName"]     = field.ColumnName,
                    ["ValueString"]    = value.ValueString,
                    // ValueNumber is null for non-numeric fields (text, levels,
                    // family/type names). 0.0 would corrupt SUM/AVG measures in
                    // Power BI; null is treated as BLANK and ignored by aggregates.
                    ["ValueNumber"]    = value.ValueNumber
                });
                cellsEmitted++;
            }
            if (truncatedByCells) break;

            elementCount++;
        }

        if (truncatedByElements)
            result.Truncations.Add(new Truncation(schId, scheduleName,
                $"reached maxElementsPerSchedule={maxElementsPerSchedule}; further elements skipped"));
        if (truncatedByCells)
            result.Truncations.Add(new Truncation(schId, scheduleName,
                $"reached maxCellsPerSchedule={maxCellsPerSchedule}; further cells skipped"));
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

    private static Parameter? ResolveScheduleFieldParameter(
        Document doc,
        Element elem,
        ElementId parameterId)
    {
        if (parameterId == null || parameterId == ElementId.InvalidElementId)
            return null;

        var parameter = FindParameterById(elem, parameterId);
        if (parameter != null) return parameter;

        try
        {
            var typeId = elem.GetTypeId();
            if (typeId != null && typeId != ElementId.InvalidElementId)
            {
                var typeElem = doc.GetElement(typeId);
                if (typeElem != null)
                    return FindParameterById(typeElem, parameterId);
            }
        }
        catch { }

        return null;
    }

    private static Parameter? FindParameterById(Element elem, ElementId parameterId)
    {
        try
        {
            var raw = GetElementIdValue(parameterId);
            if (raw < 0)
            {
                var builtin = (BuiltInParameter)(int)raw;
                var byBuiltin = elem.get_Parameter(builtin);
                if (byBuiltin != null) return byBuiltin;
            }

            foreach (Parameter p in elem.Parameters)
            {
                if (p != null && p.Id == parameterId)
                    return p;
            }
        }
        catch { }

        return null;
    }

    private static ParameterValue ReadParameterValue(Document doc, Parameter? parameter)
    {
        if (parameter == null)
            return new ParameterValue("", null);

        try
        {
            switch (parameter.StorageType)
            {
                case StorageType.Double:
                {
                    var number = parameter.AsDouble();
                    var text = parameter.AsValueString();
                    if (string.IsNullOrWhiteSpace(text))
                        text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return new ParameterValue(text ?? "", number);
                }
                case StorageType.Integer:
                {
                    var number = parameter.AsInteger();
                    var text = parameter.AsValueString();
                    if (string.IsNullOrWhiteSpace(text))
                        text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return new ParameterValue(text ?? "", number);
                }
                case StorageType.String:
                {
                    var text = parameter.AsString() ?? "";
                    double parsed;
                    double? number = double.TryParse(text,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out parsed)
                        ? parsed
                        : (double?)null;
                    return new ParameterValue(text, number);
                }
                case StorageType.ElementId:
                {
                    var id = parameter.AsElementId();
                    var referenced = SafeString(() => doc.GetElement(id)?.Name);
                    var text = referenced;
                    if (string.IsNullOrWhiteSpace(text))
                        text = parameter.AsValueString();
                    if (string.IsNullOrWhiteSpace(text))
                        text = GetElementIdValue(id).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return new ParameterValue(text ?? "", null);
                }
                default:
                    return new ParameterValue(parameter.AsValueString() ?? "", null);
            }
        }
        catch
        {
            return new ParameterValue("", null);
        }
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

    private class ScheduleExportField
    {
        public ScheduleExportField(string columnName, ElementId parameterId)
        {
            ColumnName = columnName;
            ParameterId = parameterId;
        }

        public string ColumnName { get; }
        public ElementId ParameterId { get; }
    }

    private class ParameterValue
    {
        public ParameterValue(string valueString, double? valueNumber)
        {
            ValueString = valueString;
            ValueNumber = valueNumber;
        }

        public string ValueString { get; }
        public double? ValueNumber { get; }
    }
}

/// <summary>
/// Detailed export result used by callers that need diagnostics (which schedules
/// were skipped, which fields failed to read, whether output was truncated by
/// caps). Silent skip is dangerous for data-integrity flows; this DTO exposes
/// every degradation explicitly so callers can surface it to the user.
/// </summary>
public sealed class ScheduleExportResult
{
    public List<Dictionary<string, object?>> Rows { get; } = new();
    public List<SkippedSchedule> SkippedSchedules { get; } = new();
    public List<SkippedField> SkippedFields { get; } = new();
    public List<Truncation> Truncations { get; } = new();

    /// <summary>True iff every requested schedule produced at least one row and no caps fired.</summary>
    public bool IsClean => SkippedSchedules.Count == 0
                        && SkippedFields.Count == 0
                        && Truncations.Count == 0;
}

public sealed class SkippedSchedule
{
    public long ScheduleId { get; }
    public string ScheduleName { get; }
    public string Reason { get; }
    public SkippedSchedule(long scheduleId, string scheduleName, string reason)
    {
        ScheduleId = scheduleId; ScheduleName = scheduleName; Reason = reason;
    }
}

public sealed class SkippedField
{
    public long ScheduleId { get; }
    public string ScheduleName { get; }
    public string FieldName { get; }
    public string Reason { get; }
    public SkippedField(long scheduleId, string scheduleName, string fieldName, string reason)
    {
        ScheduleId = scheduleId; ScheduleName = scheduleName; FieldName = fieldName; Reason = reason;
    }
}

public sealed class Truncation
{
    public long ScheduleId { get; }
    public string ScheduleName { get; }
    public string Reason { get; }
    public Truncation(long scheduleId, string scheduleName, string reason)
    {
        ScheduleId = scheduleId; ScheduleName = scheduleName; Reason = reason;
    }
}
