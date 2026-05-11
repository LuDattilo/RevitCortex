using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Creates a new push dataset in a Power BI workspace using the RevitCortex
/// fixed schema. If a dataset with the same name already exists, returns its
/// id instead of creating a duplicate (idempotent on name).
///
/// Inputs:
///   workspaceId  (string, required): Power BI group GUID.
///   datasetName  (string, optional): Override default name.
///                Default: "RevitCortex Live - {ProjectName} - v1",
///                or "RevitCortex Live - v1" when no document is open.
///   tables       (string[], optional): Which tables to include.
///                Allowed: "Metadata", "Elements", "Schedules",
///                "ElementParameters", "Selection".
///                Default: ["Metadata", "Elements", "Selection"].
/// </summary>
public class PbiCreateDatasetTool : ICortexTool
{
    private static readonly string[] DefaultTables =
    {
        PowerBiDatasetSchema.TableMetadata,
        PowerBiDatasetSchema.TableElements,
        PowerBiDatasetSchema.TableSelection
    };

    public string Name => "pbi_create_dataset";
    public string Category => "PowerBI";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description =>
        "Creates a RevitCortex push dataset in Power BI. Idempotent: if a dataset with the same name exists, returns its id without creating a duplicate.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var workspaceId = input["workspaceId"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(workspaceId))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "workspaceId is required.",
                suggestion: "Use pbi_list_workspaces to enumerate available workspaces.");

        // Resolve dataset name
        var datasetName = input["datasetName"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(datasetName))
        {
            string projectName = "";
            try
            {
                var doc = session.Store.Get<object>("activeDocument") as Document;
                if (doc != null)
                    projectName = doc.ProjectInformation?.Name ?? doc.Title ?? "";
            }
            catch { }
            datasetName = string.IsNullOrWhiteSpace(projectName)
                ? "RevitCortex Live - v1"
                : $"RevitCortex Live - {projectName} - v1";
        }

        // Resolve tables
        var tableNames = new List<string>();
        var tablesToken = input["tables"] as JArray;
        if (tablesToken != null && tablesToken.Count > 0)
        {
            foreach (var t in tablesToken)
            {
                var name = t.Value<string>();
                if (!string.IsNullOrEmpty(name))
                    tableNames.Add(name);
            }
        }
        else
        {
            tableNames.AddRange(DefaultTables);
        }

        var settings = PowerBiSettings.Load();
        var auth = new PowerBiAuthService(settings);

        var state = RunWithoutContext(() => auth.TryAcquireSilentAsync());
        if (!state.IsSignedIn || string.IsNullOrEmpty(state.AccessToken))
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Not signed in to Power BI.",
                suggestion: "Run pbi_check_auth with signIn=true.");

        try
        {
            using var client = new PowerBiServiceClient(state.AccessToken!);

            // Idempotency: check if the dataset already exists
            var existing = RunWithoutContext(() => client.GetDatasetByNameAsync(workspaceId, datasetName));
            if (existing != null)
            {
                return CortexResult<object>.Ok(new
                {
                    workspaceId,
                    datasetId = existing.Id,
                    datasetName = existing.Name,
                    created = false,
                    schemaVersion = PowerBiDatasetSchema.CurrentVersion,
                    message = $"Dataset '{datasetName}' already exists — returning existing id.",
                    tip = "Use pbi_publish_elements to push data into this dataset."
                });
            }

            // Create new dataset
            var body = PowerBiDatasetSchema.BuildCreateDatasetBody(datasetName, tableNames);
            var newId = RunWithoutContext(() => client.CreatePushDatasetAsync(workspaceId, body));

            return CortexResult<object>.Ok(new
            {
                workspaceId,
                datasetId = newId,
                datasetName,
                created = true,
                tables = tableNames,
                schemaVersion = PowerBiDatasetSchema.CurrentVersion,
                tip = "Use pbi_publish_elements to push model data. Use pbi_publish_elements with mode='replace' for a full refresh."
            });
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 400)
        {
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Power BI rejected the dataset schema: {ex.Message}",
                suggestion: "Check that the table names are valid (Metadata, Elements, Schedules, ElementParameters, Selection).");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 401)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Power BI token expired or invalid.",
                suggestion: "Run pbi_check_auth(signIn=false) to verify, or signIn=true to refresh.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 403)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                $"Insufficient permissions in workspace '{workspaceId}'. Creating datasets requires at least Contributor role.",
                suggestion: "Ask the workspace admin to grant Contributor or Admin role to your account.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 404)
        {
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Workspace '{workspaceId}' not found.",
                suggestion: "Use pbi_list_workspaces to see available workspace IDs.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 409)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Conflict creating dataset '{datasetName}': {ex.Message}",
                suggestion: "A dataset with this name or schema may already exist with a different configuration. Use pbi_list_datasets to inspect.");
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Dataset creation failed: {ex.Message}");
        }
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
