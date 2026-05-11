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
///   maxRowsPerSchedule (int, optional): Row cap per schedule. Default 5000.
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
        var workspaceId = input["workspaceId"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(workspaceId))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "workspaceId is required.",
                suggestion: "Use pbi_list_workspaces to enumerate available workspaces.");

        var datasetId = input["datasetId"]?.Value<string>();
        var datasetName = input["datasetName"]?.Value<string>();
        var mode = (input["mode"]?.Value<string>() ?? "replace").ToLowerInvariant();
        var maxRowsPerSchedule = input["maxRowsPerSchedule"]?.Value<int>() ?? 5_000;

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
        var auth = new PowerBiAuthService(settings);

        AuthState authState;
        try { authState = RunWithoutContext(() => auth.TryAcquireSilentAsync()); }
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

        if (string.IsNullOrWhiteSpace(datasetName))
        {
            string projectName = "";
            try { projectName = doc.ProjectInformation?.Name ?? doc.Title ?? ""; }
            catch { }
            datasetName = string.IsNullOrWhiteSpace(projectName)
                ? "RevitCortex Live - v1"
                : $"RevitCortex Live - {projectName} - v1";
        }

        var exportRunId = Guid.NewGuid().ToString();
        var exportedAt = DateTime.UtcNow;

        var exporter = new PowerBiScheduleExporter();
        List<Dictionary<string, object?>> scheduleRows;
        int scheduleCount = 0;

        try
        {
            scheduleRows = exporter.ExportSchedules(
                doc, exportRunId, exportedAt,
                scheduleIds, maxRowsPerSchedule);

            // Count distinct schedules
            var seen = new System.Collections.Generic.HashSet<object?>();
            foreach (var r in scheduleRows)
            {
                if (r.TryGetValue("ScheduleId", out var sid))
                    seen.Add(sid);
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
        var warnings = new List<string>();

        try
        {
            var result = RunWithoutContext(() => PublishAsync(
                authState.AccessToken!,
                workspaceId!,
                datasetId,
                datasetName,
                mode,
                scheduleRows,
                warnings));

            sw.Stop();

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
                batchCount = result.BatchCount,
                durationMs = sw.ElapsedMilliseconds,
                warnings
            });
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 401)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Power BI token expired during publish.",
                suggestion: "Run pbi_check_auth(signIn=true) to refresh.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 404)
        {
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Dataset not found: {ex.Message}",
                suggestion: "Run pbi_create_dataset first, or check datasetId/datasetName.");
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Schedule publish failed: {ex.Message}");
        }
    }

    // ─── Async publish logic ──────────────────────────────────────────────────

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

        // Resolve dataset id
        if (string.IsNullOrEmpty(datasetId))
        {
            var existing = await client.GetDatasetByNameAsync(workspaceId, datasetName)
                .ConfigureAwait(false);
            if (existing == null)
                throw new InvalidOperationException(
                    $"Dataset '{datasetName}' not found. Create it first with pbi_create_dataset.");
            datasetId = existing.Id;
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

    // ─── Thread helper ────────────────────────────────────────────────────────

    private static T RunWithoutContext<T>(Func<System.Threading.Tasks.Task<T>> factory)
    {
        T result = default!;
        Exception? caught = null;

        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                result = factory().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (caught != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();

        return result;
    }
}
