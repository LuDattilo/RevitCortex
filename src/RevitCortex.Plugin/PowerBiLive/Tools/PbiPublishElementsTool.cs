using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Publishes Revit model elements to a Power BI push dataset.
///
/// Phase 1 — Elements table only.
///
/// Threading contract:
///   Execute() is called on the Revit main thread by CortexRouter/Dispatcher.
///   Step 1: snapshot elements on the main thread (Revit API access).
///   Step 2: HTTP publish on a background thread (no Revit API).
///   Both steps block Execute() — the MCP tool is synchronous by design.
///   For very large models, users should set reasonable maxElements limits.
///
/// Inputs:
///   workspaceId   (string, required): Power BI group GUID.
///   datasetId     (string, optional): Use existing dataset. If omitted,
///                 the dataset is looked up by datasetName.
///   datasetName   (string, optional): Dataset name used for lookup/create.
///                 Default: "RevitCortex Live - {ProjectName} - v{schema}".
///   mode          (string, optional): "replace" (default), "append", "create".
///                 replace: DELETE rows then POST snapshot.
///                 append:  POST snapshot, keeping existing rows.
///                 create:  same as replace but errors if dataset missing.
///   scopeMode     (string, optional): "WholeModel" (default).
///                 Future: "CurrentView", "Selection".
///   categoryFilter (string[], optional): OST codes to include.
///                 If omitted, all model elements are exported.
///   maxElements   (int, optional): Hard limit on exported rows. Default 10000.
/// </summary>
public class PbiPublishElementsTool : ICortexTool
{
    public string Name => "pbi_publish_elements";
    public string Category => "PowerBI";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Publishes Revit model elements to a Power BI push dataset (Elements table). " +
        "Supports replace (full snapshot) and append (incremental) modes.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // ── Inputs ────────────────────────────────────────────────────────────
        // workspaceId is required but can be resolved from ProjectBindings if omitted.
        var workspaceId = input["workspaceId"]?.Value<string>();
        var datasetId = input["datasetId"]?.Value<string>();
        var datasetName = input["datasetName"]?.Value<string>();
        var mode = (input["mode"]?.Value<string>() ?? "replace").ToLowerInvariant();
        var maxElements = input["maxElements"]?.Value<int>() ?? 10_000;

        if (mode != "replace" && mode != "append" && mode != "create")
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Invalid mode '{mode}'. Allowed values: replace, append, create.");

        // Category filter
        List<BuiltInCategory>? categoryFilter = null;
        var catToken = input["categoryFilter"] as JArray;
        if (catToken != null && catToken.Count > 0)
        {
            categoryFilter = new List<BuiltInCategory>();
            foreach (var t in catToken)
            {
                var code = t.Value<string>();
                if (!string.IsNullOrEmpty(code) &&
                    Enum.TryParse<BuiltInCategory>(code, out var bic))
                {
                    categoryFilter.Add(bic);
                }
            }
            if (categoryFilter.Count == 0)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "categoryFilter contained no valid OST codes.",
                    suggestion: "Use OST_ prefixed category codes, e.g. [\"OST_Walls\", \"OST_Doors\"].");
        }

        // ── Step 1: authenticate (still on main thread, but network-free via MSAL cache) ──
        var settings = PowerBiSettings.Load();
        var writeCheck = PowerBiToolHelper.CheckExternalWritesAllowed(settings);
        if (writeCheck != null) return writeCheck;

        var auth = new PowerBiAuthService(settings);

        AuthState authState;
        try
        {
            authState = PowerBiToolHelper.RunWithoutContext(() => auth.TryAcquireSilentAsync());
        }
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

        // ── Step 2: snapshot Revit elements on the main thread ───────────────
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active Revit document.");

        // Compute stable document key for ProjectBindings
        var docKey = ProjectDocumentKey.Compute(doc);
        var existingBinding = settings.GetBinding(docKey);
        var compatibleBinding = existingBinding != null &&
            existingBinding.SchemaVersion == PowerBiDatasetSchema.CurrentVersion
                ? existingBinding
                : null;

        // Resolve workspaceId from binding if not provided
        if (string.IsNullOrWhiteSpace(workspaceId) && existingBinding != null)
            workspaceId = existingBinding.WorkspaceId;

        // Resolve datasetId from binding if not provided
        if (string.IsNullOrWhiteSpace(datasetId) && compatibleBinding != null)
            datasetId = compatibleBinding.DatasetId;

        // Resolve dataset name
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

        // Re-validate workspaceId after binding resolution.
        if (string.IsNullOrWhiteSpace(workspaceId))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "workspaceId is required.",
                suggestion: "Use pbi_list_workspaces to enumerate available workspaces.");

        var exportRunId = Guid.NewGuid().ToString();
        var exportedAt = DateTime.UtcNow;

        var exporter = new PowerBiElementExporter();
        List<Dictionary<string, object?>> elementRows;
        List<Dictionary<string, object?>> metadataRows;
        try
        {
            elementRows = exporter.ExportElements(
                doc, exportRunId, exportedAt, categoryFilter, maxElements);
            metadataRows = PowerBiElementExporter.BuildMetadataRows(
                doc, exportRunId, exportedAt);
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Element snapshot failed: {ex.Message}");
        }

        // ── Step 3: HTTP publish on a background thread ───────────────────────
        // All Revit API calls are done. Pass only plain DTOs to the background thread.
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();

        try
        {
            var publishResult = PowerBiToolHelper.RunWithoutContext(() => PublishAsync(
                authState.AccessToken!,
                workspaceId!,
                datasetId,
                datasetName,
                mode,
                exportRunId,
                elementRows,
                metadataRows,
                warnings));

            sw.Stop();

            // Save/update ProjectBinding so future calls can omit workspaceId/datasetId
            try
            {
                string projectName = "";
                string documentGuid = "";
                try { projectName = doc.ProjectInformation?.Name ?? doc.Title ?? ""; } catch { }
                try { documentGuid = doc.ProjectInformation?.UniqueId ?? ""; } catch { }
                settings.SetBinding(docKey, new ProjectBinding
                {
                    WorkspaceId = workspaceId!,
                    DatasetId = publishResult.DatasetId,
                    DatasetName = datasetName,
                    ProjectName = projectName,
                    DocumentGuid = documentGuid,
                    LastPathHash = ProjectDocumentKey.ComputePathHash(doc.PathName ?? ""),
                    SchemaVersion = PowerBiDatasetSchema.CurrentVersion
                });
            }
            catch { /* non-critical — binding save failure should not fail the publish */ }

            return CortexResult<object>.Ok(new
            {
                success = true,
                workspaceId,
                datasetId = publishResult.DatasetId,
                datasetName,
                table = PowerBiDatasetSchema.TableElements,
                mode,
                exportRunId,
                rowCount = publishResult.RowCount,
                batchCount = publishResult.BatchCount,
                durationMs = sw.ElapsedMilliseconds,
                warnings
            });
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 401)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Power BI token expired during publish.",
                suggestion: "Run pbi_check_auth(signIn=false) or signIn=true, then retry.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 403)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                $"Insufficient permissions in workspace '{workspaceId}'. Power BI API returned 403.",
                suggestion: "Ensure your account has at least Contributor role on the workspace.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 404)
        {
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Dataset or workspace not found. API returned 404: {ex.Message}",
                suggestion: mode == "create"
                    ? "Use pbi_create_dataset to create the dataset first."
                    : "Use pbi_list_datasets to verify the dataset id, or omit datasetId to auto-resolve by name.");
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Publish failed: {ex.Message}");
        }
    }

    // ─── Async publish logic (runs on background thread) ─────────────────────

    private static async System.Threading.Tasks.Task<PublishSummary> PublishAsync(
        string accessToken,
        string workspaceId,
        string? datasetId,
        string datasetName,
        string mode,
        string exportRunId,
        List<Dictionary<string, object?>> elementRows,
        List<Dictionary<string, object?>> metadataRows,
        List<string> warnings)
    {
        using var client = new PowerBiServiceClient(accessToken);

        // Resolve dataset id — lookup by name when not supplied, or when the
        // cached binding id no longer exists (stale binding after manual delete).
        if (string.IsNullOrEmpty(datasetId))
        {
            datasetId = await ResolveOrCreateDatasetAsync(
                client, workspaceId, datasetName, mode, warnings).ConfigureAwait(false);
        }
        else
        {
            // Validate the cached id is still alive; fall back to name-lookup on 404
            try
            {
                // A lightweight probe: list datasets and see if our id is present
                var datasets = await client.ListDatasetsAsync(workspaceId).ConfigureAwait(false);
                bool found = false;
                foreach (var ds in datasets)
                    if (ds.Id == datasetId) { found = true; break; }

                if (!found)
                {
                    warnings.Add($"Cached dataset id '{datasetId}' no longer exists — resolving by name.");
                    datasetId = await ResolveOrCreateDatasetAsync(
                        client, workspaceId, datasetName, mode, warnings).ConfigureAwait(false);
                }
            }
            catch
            {
                // If we can't verify, proceed with the cached id and let the real call fail naturally
            }
        }

        int totalRows = 0;
        int batchCount = 0;

        // Delete existing rows when replacing
        if (mode == "replace")
        {
            await client.DeleteRowsAsync(workspaceId, datasetId!,
                PowerBiDatasetSchema.TableElements).ConfigureAwait(false);
        }

        // Post element rows
        if (elementRows.Count > 0)
        {
            var asObjects = elementRows.ConvertAll(r => (object)r);
            var posted = await client.PostRowsAsync(
                workspaceId, datasetId!, PowerBiDatasetSchema.TableElements,
                asObjects).ConfigureAwait(false);
            totalRows += posted;
            batchCount += (int)Math.Ceiling(elementRows.Count / 10_000.0);
        }

        // Always replace metadata (delete + post)
        await client.DeleteRowsAsync(workspaceId, datasetId!,
            PowerBiDatasetSchema.TableMetadata).ConfigureAwait(false);
        if (metadataRows.Count > 0)
        {
            var metaObjects = metadataRows.ConvertAll(r => (object)r);
            await client.PostRowsAsync(
                workspaceId, datasetId!, PowerBiDatasetSchema.TableMetadata,
                metaObjects).ConfigureAwait(false);
        }

        return new PublishSummary
        {
            DatasetId = datasetId!,
            RowCount = totalRows,
            BatchCount = batchCount
        };
    }

    private static async System.Threading.Tasks.Task<string> ResolveOrCreateDatasetAsync(
        PowerBiServiceClient client,
        string workspaceId,
        string datasetName,
        string mode,
        List<string> warnings)
    {
        var existing = await client.GetDatasetByNameAsync(workspaceId, datasetName)
            .ConfigureAwait(false);

        if (existing != null)
            return existing.Id;

        if (mode == "create" || mode == "replace")
        {
            var body = PowerBiDatasetSchema.BuildCreateDatasetBody(
                datasetName, PowerBiDatasetSchema.AllTables);
            var newId = await client.CreatePushDatasetAsync(workspaceId, body)
                .ConfigureAwait(false);
            warnings.Add($"Dataset '{datasetName}' did not exist — created automatically.");
            return newId;
        }

        throw new InvalidOperationException(
            $"Dataset '{datasetName}' not found. Cannot append to a non-existent dataset. " +
            "Use mode='replace' or 'create' to create it first.");
    }

    private class PublishSummary
    {
        public string DatasetId { get; set; } = "";
        public int RowCount { get; set; }
        public int BatchCount { get; set; }
    }

}
