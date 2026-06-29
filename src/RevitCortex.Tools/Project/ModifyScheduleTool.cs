using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Project;

/// <summary>
/// Modifies an existing schedule: add/remove fields, set/clear filters, set/clear sorting, rename, display options.
/// </summary>
[ToolSafety(false, true)]
public class ModifyScheduleTool : ICortexTool
{
    public string Name => "modify_schedule";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Modifies an existing schedule: add/remove fields, set/clear filters, set/clear sorting, rename, display options.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var scheduleId = input["scheduleId"]?.Value<long>();
        var scheduleName = input["scheduleName"]?.Value<string>();
        var action = input["action"]?.Value<string>() ?? "add_field";

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
                schedule = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                    .FirstOrDefault(s => s.Name.Equals(scheduleName, StringComparison.OrdinalIgnoreCase));
            }

            if (schedule == null)
                return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound, "Schedule not found");

            var normalizedAction = action.ToLowerInvariant();
            if (normalizedAction == "set_sort")
                normalizedAction = "set_sorting";

            if (normalizedAction != "add_field" &&
                normalizedAction != "remove_field" &&
                normalizedAction != "set_sorting" &&
                normalizedAction != "clear_sorting" &&
                normalizedAction != "set_filter" &&
                normalizedAction != "clear_filter" &&
                normalizedAction != "rename")
            {
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    $"Unknown action: {action}",
                    suggestion: "Use: add_field, remove_field, set_sorting, clear_sorting, set_filter, clear_filter, rename");
            }

            if (!session.RequestConfirmation("modify schedule", 1, schedule.Name))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Operation cancelled by user");

            using var tx = new Transaction(doc, "RevitCortex: Modify Schedule");
            var txFailures = TransactionFailureHandling.SuppressWarnings(tx);
            tx.Start();

            var result = normalizedAction switch
            {
                "add_field" => AddFields(schedule, input),
                "remove_field" => RemoveFields(schedule, input),
                "set_sorting" => SetSorting(schedule, input),
                "clear_sorting" => ClearSorting(schedule),
                "set_filter" => SetFilter(schedule, input),
                "clear_filter" => ClearFilter(schedule),
                "rename" => RenameSchedule(schedule, input),
                _ => new object()
            };

            // H19: the sub-methods signal validation failures by returning an anonymous
            // object with an `error` property. Returning that inside CortexResult.Ok hid
            // the failure from callers (Ok envelope with a buried error). Detect it, roll
            // back, and surface a real Fail instead.
            var errorProp = result.GetType().GetProperty("error");
            if (errorProp?.GetValue(result) is string errMsg && !string.IsNullOrEmpty(errMsg))
            {
                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, errMsg);
            }

            if (tx.Commit() != TransactionStatus.Committed)
                return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                    $"Revit rolled back the transaction: {TransactionFailureHandling.Describe(txFailures)}",
                    suggestion: "Fix the reported model errors and retry.");
            return CortexResult<object>.Ok(result);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed: {ex.Message}");
        }
    }

    private static object AddFields(ViewSchedule schedule, JObject input)
    {
        var fieldNames = input["fieldNames"]?.ToObject<List<string>>() ?? new List<string>();
        var def = schedule.Definition;
        var schedulableFields = def.GetSchedulableFields();
        int added = 0;

        foreach (var name in fieldNames)
        {
            var field = schedulableFields.FirstOrDefault(f => f.GetName(schedule.Document).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (field != null)
            {
                def.AddField(field);
                added++;
            }
        }
        return new { action = "add_field", addedCount = added };
    }

    private static object RemoveFields(ViewSchedule schedule, JObject input)
    {
        var fieldNames = input["fieldNames"]?.ToObject<List<string>>() ?? new List<string>();
        var def = schedule.Definition;
        int removed = 0;

        for (int i = def.GetFieldCount() - 1; i >= 0; i--)
        {
            var field = def.GetField(i);
            if (fieldNames.Any(n => field.GetName().Equals(n, StringComparison.OrdinalIgnoreCase)))
            {
                def.RemoveField(i);
                removed++;
            }
        }
        return new { action = "remove_field", removedCount = removed };
    }

    private static object SetSorting(ViewSchedule schedule, JObject input)
    {
        var sortFields = input["sortFields"]?.ToObject<List<JObject>>() ?? new List<JObject>();
        var def = schedule.Definition;

        // Clear existing
        def.ClearSortGroupFields();

        int set = 0;
        foreach (var sf in sortFields)
        {
            var fieldName = sf["fieldName"]?.Value<string>();
            var sortOrder = sf["sortOrder"]?.Value<string>();
            if (sortOrder == null)
            {
                // Boolean alias {ascending: false} — the shape the wrapper documented
                // historically; without this, descending sort was unreachable.
                var ascending = sf["ascending"]?.Value<bool?>();
                sortOrder = ascending == false ? "descending" : "ascending";
            }
            if (string.IsNullOrEmpty(fieldName)) continue;

            for (int i = 0; i < def.GetFieldCount(); i++)
            {
                var field = def.GetField(i);
                if (field.GetName().Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    var sgf = new ScheduleSortGroupField(field.FieldId,
                        sortOrder.Equals("descending", StringComparison.OrdinalIgnoreCase)
                            ? ScheduleSortOrder.Descending : ScheduleSortOrder.Ascending);
                    def.AddSortGroupField(sgf);
                    set++;
                    break;
                }
            }
        }
        return new { action = "set_sorting", sortFieldCount = set };
    }

    private static object ClearSorting(ViewSchedule schedule)
    {
        schedule.Definition.ClearSortGroupFields();
        return new { action = "clear_sorting" };
    }

    private static object SetFilter(ViewSchedule schedule, JObject input)
    {
        var fieldName = input["filterField"]?.Value<string>() ?? input["fieldName"]?.Value<string>();
        var op = (input["filterType"]?.Value<string>() ?? input["operator"]?.Value<string>() ?? "equal")
            .ToLowerInvariant().Replace("_", "").Replace(" ", "");
        var valueToken = input["filterValue"] ?? input["value"];
        if (string.IsNullOrEmpty(fieldName))
            return new { error = "filterField required" };

        var def = schedule.Definition;
        ScheduleFieldId? fieldId = null;
        for (int i = 0; i < def.GetFieldCount(); i++)
        {
            var f = def.GetField(i);
            if (f.GetName().Equals(fieldName, StringComparison.OrdinalIgnoreCase))
            {
                fieldId = f.FieldId;
                break;
            }
        }
        if (fieldId == null)
            return new { error = $"Field '{fieldName}' not present in schedule. Add it first with add_field." };

        var filterType = op switch
        {
            "notequal" => ScheduleFilterType.NotEqual,
            "greater" or "greaterthan" => ScheduleFilterType.GreaterThan,
            "greaterorequal" or "greaterthanorequal" => ScheduleFilterType.GreaterThanOrEqual,
            "less" or "lessthan" => ScheduleFilterType.LessThan,
            "lessorequal" or "lessthanorequal" => ScheduleFilterType.LessThanOrEqual,
            "contains" => ScheduleFilterType.Contains,
            "notcontains" or "doesnotcontain" => ScheduleFilterType.NotContains,
            "beginswith" => ScheduleFilterType.BeginsWith,
            "endswith" => ScheduleFilterType.EndsWith,
            "hasvalue" => ScheduleFilterType.HasParameter,
            "hasnovalue" or "isempty" => ScheduleFilterType.HasNoValue,
            _ => ScheduleFilterType.Equal
        };

        ScheduleFilter filter;
        if (filterType == ScheduleFilterType.HasParameter || filterType == ScheduleFilterType.HasNoValue)
        {
            filter = new ScheduleFilter(fieldId, filterType);
        }
        else if (valueToken != null && valueToken.Type == JTokenType.Integer)
        {
            filter = new ScheduleFilter(fieldId, filterType, valueToken.Value<int>());
        }
        else if (valueToken != null && valueToken.Type == JTokenType.Float)
        {
            filter = new ScheduleFilter(fieldId, filterType, valueToken.Value<double>());
        }
        else
        {
            filter = new ScheduleFilter(fieldId, filterType, valueToken?.Value<string>() ?? "");
        }

        def.AddFilter(filter);
        return new { action = "set_filter", field = fieldName, filterType = filterType.ToString() };
    }

    private static object ClearFilter(ViewSchedule schedule)
    {
        var def = schedule.Definition;
        int count = def.GetFilterCount();
        def.ClearFilters();
        return new { action = "clear_filter", clearedCount = count };
    }

    private static object RenameSchedule(ViewSchedule schedule, JObject input)
    {
        var newName = input["newName"]?.Value<string>();
        if (string.IsNullOrEmpty(newName)) return new { error = "newName required" };
        var oldName = schedule.Name;
        schedule.Name = newName;
        return new { action = "rename", oldName, newName };
    }
}
