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

        // Get schedule fields from schedule definition. Fields without a
        // parameter id are kept with blank values so the published shape still
        // reflects the schedule definition.
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
                catch
                {
                    fields.Add(new ScheduleExportField($"Column{i}", ElementId.InvalidElementId));
                }
            }
        }
        catch { }

        if (fields.Count == 0) return rows;

        IList<Element> elements;
        try
        {
            elements = new FilteredElementCollector(doc, sch.Id)
                .WhereElementIsNotElementType()
                .ToElements();
        }
        catch { return rows; }

        int rowCount = 0;
        foreach (var elem in elements)
        {
            ct.ThrowIfCancellationRequested();
            if (rowCount >= maxRowsPerSchedule) break;
            if (elem == null) continue;

            long elementId = GetElementIdValue(elem.Id);
            string uniqueId = SafeString(() => elem.UniqueId) ?? "";

            foreach (var field in fields)
            {
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
                    ["RowIndex"]       = (long)rowCount,
                    ["ColumnName"]     = field.ColumnName,
                    ["ValueString"]    = value.ValueString,
                    ["ValueNumber"]    = value.ValueNumber ?? 0.0
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
