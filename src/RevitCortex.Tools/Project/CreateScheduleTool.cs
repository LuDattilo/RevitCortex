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
/// Creates a schedule view with specified category, fields, filters, and sort options.
/// </summary>
public class CreateScheduleTool : ICortexTool
{
    public string Name => "create_schedule";
    public string Category => "Project";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Creates a schedule view with specified category, fields, filters, and sort options.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var categoryName = input["categoryName"]?.Value<string>() ?? "";
        var scheduleName = input["name"]?.Value<string>() ?? "New Schedule";
        var scheduleType = input["scheduleType"]?.Value<string>() ?? "regular";
        var fields = input["fields"] as JArray;

        try
        {
            // Resolve category
            var catId = ElementId.InvalidElementId;
            if (!string.IsNullOrEmpty(categoryName))
            {
                var resolvedId = CategoryResolver.ResolveToId(doc, categoryName);
                if (resolvedId != null) catId = resolvedId;
            }

            using var tx = new Transaction(doc, "RevitCortex: Create Schedule");
            tx.Start();

            ViewSchedule schedule;
            switch (scheduleType.ToLowerInvariant().Replace(" ", "").Replace("_", ""))
            {
                case "materialtakeoff":
                    schedule = ViewSchedule.CreateMaterialTakeoff(doc, catId);
                    break;
                case "keyschedule":
                    schedule = ViewSchedule.CreateKeySchedule(doc, catId);
                    break;
                case "sheetlist":
                    schedule = ViewSchedule.CreateSheetList(doc);
                    break;
                case "viewlist":
                    schedule = ViewSchedule.CreateViewList(doc);
                    break;
                default:
                    schedule = ViewSchedule.CreateSchedule(doc, catId);
                    break;
            }

            // Handle name uniqueness
            try { schedule.Name = scheduleName; }
            catch
            {
                for (int i = 1; i <= 99; i++)
                {
                    try { schedule.Name = $"{scheduleName} ({i})"; break; }
                    catch { /* try next */ }
                }
            }

            // Add fields. Track both added and skipped so callers know exactly which
            // requested fields didn't apply (instead of silently dropping them and leaving
            // the LLM to hallucinate reasons why — see audit log analysis 2026-05-15).
            var addedFields = new List<string>();
            var skippedFields = new List<object>();
            List<string>? schedulableNames = null;
            if (fields != null)
            {
                var schedulableFields = schedule.Definition.GetSchedulableFields();
                schedulableNames = schedulableFields
                    .Select(f => f.GetName(doc))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
                foreach (var fieldSpec in fields)
                {
                    var paramName = fieldSpec["parameterName"]?.Value<string>();
                    if (string.IsNullOrEmpty(paramName))
                    {
                        skippedFields.Add(new { parameterName = (string?)null, reason = "EmptyName" });
                        continue;
                    }

                    var sf = schedulableFields.FirstOrDefault(f =>
                        f.GetName(doc).Equals(paramName, StringComparison.OrdinalIgnoreCase));
                    if (sf != null)
                    {
                        var field = schedule.Definition.AddField(sf);
                        var heading = fieldSpec["heading"]?.Value<string>();
                        if (!string.IsNullOrEmpty(heading))
                            field.ColumnHeading = heading;
                        var isHidden = fieldSpec["isHidden"]?.Value<bool>() ?? false;
                        field.IsHidden = isHidden;
                        addedFields.Add(paramName!);
                    }
                    else
                    {
                        // Find up to 3 closest matches (case-insensitive substring) so the
                        // caller gets actionable hints instead of guessing localization issues.
                        var hints = schedulableNames
                            .Where(n => n.IndexOf(paramName!, StringComparison.OrdinalIgnoreCase) >= 0
                                     || paramName!.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                            .Take(3)
                            .ToList();
                        skippedFields.Add(new
                        {
                            parameterName = paramName,
                            reason = "NotSchedulableForCategory",
                            suggestions = hints
                        });
                    }
                }
            }

            tx.Commit();

            return CortexResult<object>.Ok(new
            {
                scheduleId = ToolHelpers.GetElementIdValue(schedule.Id),
                scheduleName = schedule.Name,
                scheduleType,
                addedFieldCount = addedFields.Count,
                addedFields,
                skippedFieldCount = skippedFields.Count,
                skippedFields,
                // Include the full schedulable name list only when at least one field was
                // skipped — keeps the response compact on the happy path.
                schedulableFieldNames = skippedFields.Count > 0 ? schedulableNames : null
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Failed to create schedule: {ex.Message}");
        }
    }
}
