using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Publishes Revit schedules to the Power BI Schedules table (long-form).
///
/// Threading contract: same as PbiPublishElementsTool.
///   Step 1: snapshot schedule data on the Revit main thread.
///   Step 2: HTTP publish on a background thread.
///
/// Inputs:
///   workspaceId     (string, required): Power BI group GUID.
///   datasetId       (string, optional): Existing dataset id.
///   datasetName     (string, optional): Used for lookup if datasetId omitted.
///   scheduleIds     (long[], optional): Specific schedule element ids to export.
///                   If omitted, all non-template schedules are exported.
///   mode            (string, optional): "replace" (default) or "append".
///   maxElementsPerSchedule (int, optional): Element cap per schedule. Default 5000.
///                   Alias: maxRowsPerSchedule (kept for backward compatibility,
///                   despite its misleading name — it always counted elements).
///   maxCellsPerSchedule (int, optional): Emitted-row cap per schedule. 0 means
///                   no cell cap. Use this to bound long-form row count when
///                   schedules have many fields (rows = elements * fields).
/// </summary>
public class PbiPublishSchedulesTool : ICortexTool
{
    public string Name => "pbi_publish_schedules";
    public string Category => "PowerBI";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Publishes Revit schedules to the Power BI Schedules table (long-form: one row per cell). " +
        "Supports replace and append modes.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // ── Inputs ────────────────────────────────────────────────────────────
        // workspaceId is required but can be resolved from ProjectBindings if omitted
        var workspaceId = input["workspaceId"]?.Value<string>();
        var datasetId = input["datasetId"]?.Value<string>();
        var datasetName = input["datasetName"]?.Value<string>();
        var mode = (input["mode"]?.Value<string>() ?? "replace").ToLowerInvariant();
        // maxElementsPerSchedule replaces the misleadingly-named maxRowsPerSchedule
        // (which actually limited elements, not emitted long-form rows). Both are
        // accepted; the new name wins if both are provided.
        var maxElementsPerSchedule = input["maxElementsPerSchedule"]?.Value<int>()
                                  ?? input["maxRowsPerSchedule"]?.Value<int>()
                                  ?? 5_000;
        // maxCellsPerSchedule caps the actual emitted rows per schedule.
        // 0 means no cell cap (only element cap applies).
        var maxCellsPerSchedule = input["maxCellsPerSchedule"]?.Value<int>() ?? 0;

        if (mode != "replace" && mode != "append")
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Invalid mode '{mode}'. Allowed values: replace, append.");

        // Parse optional scheduleIds filter
        List<long>? scheduleIds = null;
        var idsToken = input["scheduleIds"] as JArray;
        if (idsToken != null && idsToken.Count > 0)
        {
            scheduleIds = new List<long>();
            foreach (var t in idsToken)
            {
                try { scheduleIds.Add(t.Value<long>()); }
                catch { }
            }
        }

        // ── Authenticate ──────────────────────────────────────────────────────
        var settings = PowerBiSettings.Load();
        var writeCheck = PowerBiToolHelper.CheckExternalWritesAllowed(settings);
        if (writeCheck != null) return writeCheck;

        var auth = new PowerBiAuthService(settings);

        AuthState authState;
        try { authState = PowerBiToolHelper.RunWithoutContext(() => auth.TryAcquireSilentAsync()); }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Silent token acquisition failed: {ex.Message}",
                suggestion: "Run pbi_check_auth(signIn=true) to refresh.");
        }

        if (!authState.IsSignedIn || string.IsNullOrEmpty(authState.AccessToken))
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Not signed in to Power BI.",
                suggestion: "Run pbi_check_auth with signIn=true.");

        // ── Snapshot schedules on the main thread ─────────────────────────────
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active Revit document.");

        // Resolve from ProjectBindings when not supplied by caller
        var docKey = ProjectDocumentKey.Compute(doc);
        var existingBinding = settings.GetBinding(docKey);
        var compatibleBinding = existingBinding != null &&
            existingBinding.SchemaVersion == PowerBiDatasetSchema.CurrentVersion
                ? existingBinding
                : null;

        if (string.IsNullOrWhiteSpace(workspaceId) && existingBinding != null)
            workspaceId = existingBinding.WorkspaceId;

        if (string.IsNullOrWhiteSpace(datasetId) && compatibleBinding != null)
            datasetId = compatibleBinding.DatasetId;

        if (string.IsNullOrWhiteSpace(datasetName))
        {
            if (compatibleBinding != null && !string.IsNullOrWhiteSpace(compatibleBinding.DatasetName))
            {
                datasetName = compatibleBinding.DatasetName;
            }
            else
            {
                string projectName = "";
                try { projectName = doc.ProjectInformation?.Name ?? doc.Title ?? ""; }
                catch { }
                datasetName = PowerBiDatasetSchema.BuildDefaultDatasetName(projectName);
            }
        }

        // Re-validate workspaceId after binding resolution
        if (string.IsNullOrWhiteSpace(workspaceId))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "workspaceId is required.",
                suggestion: "Use pbi_list_workspaces to enumerate available workspaces.");

        var exportRunId = Guid.NewGuid().ToString();
        var exportedAt = DateTime.UtcNow;

        var exporter = new PowerBiScheduleExporter();
        List<Dictionary<string, object?>> scheduleRows;
        int scheduleCount = 0;
        int rowsDeduped = 0;
        var warnings = new List<string>();
        var skippedSchedules = new List<object>();
        var skippedFields = new List<object>();
        var truncations = new List<object>();

        try
        {
            var detail = exporter.ExportSchedulesDetailed(
                doc, exportRunId, exportedAt,
                scheduleIds, maxElementsPerSchedule, maxCellsPerSchedule);
            scheduleRows = detail.Rows;

            // Diagnostics: surface skipped/truncated so the caller cannot mistake
            // a partial export for a complete one.
            foreach (var s in detail.SkippedSchedules)
            {
                skippedSchedules.Add(new { scheduleId = s.ScheduleId, scheduleName = s.ScheduleName, reason = s.Reason });
                warnings.Add($"Schedule '{s.ScheduleName}' (id={s.ScheduleId}) skipped: {s.Reason}");
            }
            foreach (var f in detail.SkippedFields)
            {
                skippedFields.Add(new { scheduleId = f.ScheduleId, scheduleName = f.ScheduleName, fieldName = f.FieldName, reason = f.Reason });
            }
            foreach (var t in detail.Truncations)
            {
                truncations.Add(new { scheduleId = t.ScheduleId, scheduleName = t.ScheduleName, reason = t.Reason });
                warnings.Add($"Schedule '{t.ScheduleName}' (id={t.ScheduleId}) truncated: {t.Reason}");
            }

            rowsDeduped = DeduplicateScheduleRows(scheduleRows);
            if (mode == "append")
            {
                warnings.Add(
                    "Append mode posts rows without clearing Power BI first; rerunning the same logical export can create duplicates across runs.");
            }

            // Count distinct schedules — use typed HashSet to avoid boxing equality surprises.
            var seen = new System.Collections.Generic.HashSet<long>();
            foreach (var r in scheduleRows)
            {
                if (r.TryGetValue("ScheduleId", out var sid) && sid is long schId)
                    seen.Add(schId);
            }
            scheduleCount = seen.Count;
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Schedule snapshot failed: {ex.Message}");
        }

        if (scheduleRows.Count == 0)
            return CortexResult<object>.Ok(new
            {
                success = true,
                workspaceId,
                datasetName,
                table = PowerBiDatasetSchema.TableSchedules,
                mode,
                exportRunId,
                scheduleCount = 0,
                rowCount = 0,
                batchCount = 0,
                durationMs = 0,
                warnings = new[] { "No schedules found matching the given filter." }
            });

        // ── HTTP publish on background thread ─────────────────────────────────
        var sw = Stopwatch.StartNew();

        try
        {
            var result = PowerBiToolHelper.RunWithoutContext(() => PublishAsync(
                authState.AccessToken!,
                workspaceId!,
                datasetId,
                datasetName,
                mode,
                scheduleRows,
                warnings));

            sw.Stop();

            // Save/update ProjectBinding
            try
            {
                string projectName = "";
                string documentGuid = "";
                try { projectName = doc.ProjectInformation?.Name ?? doc.Title ?? ""; } catch { }
                try { documentGuid = doc.ProjectInformation?.UniqueId ?? ""; } catch { }
                settings.SetBinding(docKey, new ProjectBinding
                {
                    WorkspaceId = workspaceId!,
                    DatasetId = result.DatasetId,
                    DatasetName = datasetName,
                    ProjectName = projectName,
                    DocumentGuid = documentGuid,
                    LastPathHash = ProjectDocumentKey.ComputePathHash(doc.PathName ?? ""),
                    SchemaVersion = PowerBiDatasetSchema.CurrentVersion
                });
            }
            catch { /* non-critical */ }

            return CortexResult<object>.Ok(new
            {
                success = true,
                workspaceId,
                datasetId = result.DatasetId,
                datasetName,
                table = PowerBiDatasetSchema.TableSchedules,
                mode,
                exportRunId,
                scheduleCount,
                rowCount = result.RowCount,
                rowsDeduped,
                batchCount = result.BatchCount,
                durationMs = sw.ElapsedMilliseconds,
                warnings,
                // Diagnostics — empty arrays when nothing was skipped/truncated.
                skippedSchedules,
                skippedFields,
                truncations
            });
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 401)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Power BI token expired during publish.",
                suggestion: "Run pbi_check_auth(signIn=true) to refresh.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 403)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                $"Insufficient permissions in workspace. Power BI API returned 403.",
                suggestion: "Ensure your account has at least Contributor role on the workspace.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 404)
        {
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Dataset or table not found: {ex.Message}",
                suggestion: "Run pbi_publish_elements first (creates dataset with all tables including Schedules), or pbi_create_dataset.");
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Schedule publish failed: {ex.Message}");
        }
    }

    // ─── Async publish logic ──────────────────────────────────────────────────

    private static int DeduplicateScheduleRows(List<Dictionary<string, object?>> rows)
    {
        var byKey = new Dictionary<string, Dictionary<string, object?>>();
        var order = new List<string>();

        foreach (var row in rows)
        {
            var key = BuildScheduleRowKey(row);
            if (!byKey.ContainsKey(key))
                order.Add(key);
            byKey[key] = row; // last write wins
        }

        int removed = rows.Count - byKey.Count;
        if (removed <= 0) return 0;

        rows.Clear();
        foreach (var key in order)
            rows.Add(byKey[key]);
        return removed;
    }

    private static string BuildScheduleRowKey(Dictionary<string, object?> row)
    {
        return GetKeyPart(row, "ScheduleId") + "|" +
               GetKeyPart(row, "ElementId") + "|" +
               GetKeyPart(row, "ColumnName");
    }

    private static string GetKeyPart(Dictionary<string, object?> row, string name)
    {
        object? value;
        return row.TryGetValue(name, out value) && value != null
            ? value.ToString() ?? ""
            : "";
    }

    private static async System.Threading.Tasks.Task<PublishSummary> PublishAsync(
        string accessToken,
        string workspaceId,
        string? datasetId,
        string datasetName,
        string mode,
        List<Dictionary<string, object?>> scheduleRows,
        List<string> warnings)
    {
        using var client = new PowerBiServiceClient(accessToken);

        // Resolve dataset id — lookup by name when not supplied, or validate
        // the cached binding id is still alive (stale binding after manual delete).
        if (string.IsNullOrEmpty(datasetId))
        {
            var existing = await client.GetDatasetByNameAsync(workspaceId, datasetName)
                .ConfigureAwait(false);
            if (existing == null)
                throw new InvalidOperationException(
                    $"Dataset '{datasetName}' not found. Run pbi_publish_elements first " +
                    "to auto-create the dataset with all tables including Schedules.");
            datasetId = existing.Id;
        }
        else
        {
            // Validate the cached id is still alive; fall back to name-lookup on stale binding
            try
            {
                var datasets = await client.ListDatasetsAsync(workspaceId).ConfigureAwait(false);
                bool found = false;
                foreach (var ds in datasets)
                    if (ds.Id == datasetId) { found = true; break; }

                if (!found)
                {
                    var existing = await client.GetDatasetByNameAsync(workspaceId, datasetName)
                        .ConfigureAwait(false);
                    if (existing == null)
                        throw new InvalidOperationException(
                            $"Dataset '{datasetName}' not found. Run pbi_publish_elements first " +
                            "to auto-create the dataset with all tables including Schedules.");
                    datasetId = existing.Id;
                }
            }
            catch (InvalidOperationException) { throw; }
            catch { /* proceed with cached id, real call will surface errors */ }
        }

        if (mode == "replace")
        {
            await client.DeleteRowsAsync(workspaceId, datasetId!,
                PowerBiDatasetSchema.TableSchedules).ConfigureAwait(false);
        }

        var asObjects = scheduleRows.ConvertAll(r => (object)r);
        var posted = await client.PostRowsAsync(
            workspaceId, datasetId!, PowerBiDatasetSchema.TableSchedules,
            asObjects).ConfigureAwait(false);

        int batchCount = (int)Math.Ceiling(scheduleRows.Count / 10_000.0);

        return new PublishSummary
        {
            DatasetId = datasetId!,
            RowCount = posted,
            BatchCount = batchCount
        };
    }

    private class PublishSummary
    {
        public string DatasetId { get; set; } = "";
        public int RowCount { get; set; }
        public int BatchCount { get; set; }
    }

}
