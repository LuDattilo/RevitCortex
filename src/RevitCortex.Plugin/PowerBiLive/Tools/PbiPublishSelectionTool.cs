using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Publishes the current Revit selection to the Power BI Selection table.
///
/// Threading contract: same as PbiPublishElementsTool.
///   Step 1: snapshot selected ElementIds on the Revit main thread.
///   Step 2: HTTP DELETE + POST on a background thread.
///
/// Inputs:
///   workspaceId   (string, optional): Power BI group GUID. Resolved from binding if omitted.
///   datasetId     (string, optional): Existing dataset id. Resolved from binding if omitted.
///   datasetName   (string, optional): Dataset name used for lookup. Default from binding or doc name.
///   clearIfEmpty  (bool, optional):   If true, DELETE existing rows even when nothing is selected.
///                                     Default: false (returns warning without touching PBI).
/// </summary>
public class PbiPublishSelectionTool : ICortexTool
{
    public string Name => "pbi_publish_selection";
    public string Category => "PowerBI";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description =>
        "Publishes the current Revit selection to the Power BI Selection table. " +
        "Each call replaces the previous snapshot (DELETE then POST). " +
        "workspaceId and datasetId can be omitted when a ProjectBinding exists.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // ── Inputs ────────────────────────────────────────────────────────────
        var workspaceId = input["workspaceId"]?.Value<string>();
        var datasetId   = input["datasetId"]?.Value<string>();
        var datasetName = input["datasetName"]?.Value<string>();
        var clearIfEmpty = input["clearIfEmpty"]?.Value<bool>() ?? false;

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

        // ── Snapshot selection on the main thread ─────────────────────────────
        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No active Revit document.");

        // Resolve workspaceId / datasetId / datasetName from ProjectBindings
        var docKey = ProjectDocumentKey.Compute(doc);
        var existingBinding = settings.GetBinding(docKey);

        if (string.IsNullOrWhiteSpace(workspaceId) && existingBinding != null)
            workspaceId = existingBinding.WorkspaceId;
        if (string.IsNullOrWhiteSpace(datasetId) && existingBinding != null)
            datasetId = existingBinding.DatasetId;
        if (string.IsNullOrWhiteSpace(datasetName))
        {
            if (existingBinding != null && !string.IsNullOrWhiteSpace(existingBinding.DatasetName))
                datasetName = existingBinding.DatasetName;
            else
            {
                string projectName = "";
                try { projectName = doc.ProjectInformation?.Name ?? doc.Title ?? ""; } catch { }
                datasetName = string.IsNullOrWhiteSpace(projectName)
                    ? "RevitCortex Live - v1"
                    : $"RevitCortex Live - {projectName} - v1";
            }
        }

        if (string.IsNullOrWhiteSpace(workspaceId))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "workspaceId is required.",
                suggestion: "Use pbi_list_workspaces to enumerate available workspaces, or run pbi_publish_elements first to create a ProjectBinding.");

        // Read current selection
        List<Dictionary<string, object?>> selectionRows;
        string exportRunId;
        try
        {
            var uiDoc = new UIDocument(doc);
            var selectedIds = uiDoc.Selection.GetElementIds();

            if (selectedIds.Count == 0 && !clearIfEmpty)
            {
                return CortexResult<object>.Ok(new
                {
                    success = true,
                    workspaceId,
                    datasetName,
                    table = PowerBiDatasetSchema.TableSelection,
                    rowCount = 0,
                    warning = "Nothing selected in Revit. The Selection table was NOT cleared. Pass clearIfEmpty=true to explicitly clear it.",
                    tip = "Select elements in Revit first, then call pbi_publish_selection again."
                });
            }

            exportRunId = Guid.NewGuid().ToString();
            var exportedAt = DateTime.UtcNow;
            var docGuid = "";
            var projectId = docKey;
            try { docGuid = doc.ProjectInformation?.UniqueId ?? ""; } catch { }

            selectionRows = new List<Dictionary<string, object?>>(selectedIds.Count);
            foreach (var id in selectedIds)
            {
                long elementIdValue;
#if REVIT2024_OR_GREATER
                elementIdValue = id.Value;
#else
                elementIdValue = id.IntegerValue;
#endif
                string uniqueId = "";
                try
                {
                    var elem = doc.GetElement(id);
                    if (elem != null) uniqueId = elem.UniqueId ?? "";
                }
                catch { }

                selectionRows.Add(new Dictionary<string, object?>
                {
                    ["_SchemaVersion"] = PowerBiDatasetSchema.CurrentVersion,
                    ["UpdatedAtUtc"]   = exportedAt,
                    ["ProjectId"]      = projectId,
                    ["DocumentGuid"]   = docGuid,
                    ["ElementId"]      = elementIdValue,
                    ["UniqueId"]       = uniqueId,
                    ["SelectionSetId"] = exportRunId
                });
            }
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Selection snapshot failed: {ex.Message}");
        }

        // ── HTTP publish on background thread ─────────────────────────────────
        var sw = Stopwatch.StartNew();
        try
        {
            var result = PowerBiToolHelper.RunWithoutContext(() => PublishAsync(
                authState.AccessToken!,
                workspaceId!,
                datasetId,
                datasetName,
                selectionRows));

            sw.Stop();

            // Update binding
            try
            {
                string projectName = "";
                string documentGuid = "";
                try { projectName = doc.ProjectInformation?.Name ?? doc.Title ?? ""; } catch { }
                try { documentGuid = doc.ProjectInformation?.UniqueId ?? ""; } catch { }
                settings.SetBinding(docKey, new ProjectBinding
                {
                    WorkspaceId   = workspaceId!,
                    DatasetId     = result.DatasetId,
                    DatasetName   = datasetName,
                    ProjectName   = projectName,
                    DocumentGuid  = documentGuid,
                    LastPathHash  = ProjectDocumentKey.ComputePathHash(doc.PathName ?? ""),
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
                table = PowerBiDatasetSchema.TableSelection,
                exportRunId = selectionRows.Count > 0 ? exportRunId : null,
                rowCount = result.RowCount,
                batchCount = 0,
                durationMs = sw.ElapsedMilliseconds
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
                "Insufficient permissions in workspace. Power BI API returned 403.",
                suggestion: "Ensure your account has at least Contributor role on the workspace.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 404)
        {
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Dataset not found: {ex.Message}",
                suggestion: "Run pbi_publish_elements first to auto-create the dataset with all tables including Selection.");
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Selection publish failed: {ex.Message}");
        }
    }

    // ─── Async publish ────────────────────────────────────────────────────────

    private static async System.Threading.Tasks.Task<PublishSummary> PublishAsync(
        string accessToken,
        string workspaceId,
        string? datasetId,
        string datasetName,
        List<Dictionary<string, object?>> rows)
    {
        using var client = new PowerBiServiceClient(accessToken);

        // Resolve dataset id — with stale-binding detection
        if (string.IsNullOrEmpty(datasetId))
        {
            var existing = await client.GetDatasetByNameAsync(workspaceId, datasetName)
                .ConfigureAwait(false);
            if (existing == null)
                throw new InvalidOperationException(
                    $"Dataset '{datasetName}' not found. Run pbi_publish_elements first to create it.");
            datasetId = existing.Id;
        }
        else
        {
            // Validate cached id is still alive
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
                            $"Dataset '{datasetName}' not found. Run pbi_publish_elements first to create it.");
                    datasetId = existing.Id;
                }
            }
            catch (InvalidOperationException) { throw; }
            catch { /* proceed with cached id */ }
        }

        // DELETE existing rows, then POST new snapshot
        await client.DeleteRowsAsync(workspaceId, datasetId!,
            PowerBiDatasetSchema.TableSelection).ConfigureAwait(false);

        int posted = 0;
        if (rows.Count > 0)
        {
            var asObjects = rows.ConvertAll(r => (object)r);
            posted = await client.PostRowsAsync(
                workspaceId, datasetId!, PowerBiDatasetSchema.TableSelection,
                asObjects).ConfigureAwait(false);
        }

        return new PublishSummary { DatasetId = datasetId!, RowCount = posted };
    }

    private class PublishSummary
    {
        public string DatasetId { get; set; } = "";
        public int RowCount { get; set; }
    }
}
