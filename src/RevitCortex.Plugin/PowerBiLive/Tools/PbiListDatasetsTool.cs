using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Lists push datasets in a Power BI workspace.
/// Read-only: safe in RevitCortex read-only mode.
///
/// Inputs:
///   workspaceId (string, required): Power BI group/workspace GUID.
/// </summary>
public class PbiListDatasetsTool : ICortexTool
{
    public string Name => "pbi_list_datasets";
    public string Category => "PowerBI";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Lists datasets in a Power BI workspace. Useful for finding an existing RevitCortex dataset before creating or publishing.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var workspaceId = input["workspaceId"]?.Value<string>();
        if (string.IsNullOrWhiteSpace(workspaceId))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "workspaceId is required.",
                suggestion: "Use pbi_list_workspaces to obtain a workspace id.");

        var settings = PowerBiSettings.Load();
        var auth = new PowerBiAuthService(settings);

        var state = PowerBiToolHelper.RunWithoutContext(() => auth.TryAcquireSilentAsync());
        if (!state.IsSignedIn || string.IsNullOrEmpty(state.AccessToken))
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Not signed in to Power BI.",
                suggestion: "Run pbi_check_auth with signIn=true.");

        try
        {
            using var client = new PowerBiServiceClient(state.AccessToken!);
            var datasets = PowerBiToolHelper.RunWithoutContext(() => client.ListDatasetsAsync(workspaceId));

            return CortexResult<object>.Ok(new
            {
                workspaceId,
                datasetCount = datasets.Count,
                datasets = datasets.Select(d => new
                {
                    id = d.Id,
                    name = d.Name,
                    configuredBy = d.ConfiguredBy,
                    isRefreshable = d.IsRefreshable,
                    createdAt = d.CreatedDate
                }).ToList(),
                tip = "Use the dataset id with pbi_publish_elements. To create a new dataset, call pbi_create_dataset."
            });
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 401)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Power BI token expired or invalid.",
                suggestion: "Run pbi_check_auth(signIn=false) to verify token, or signIn=true to refresh.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 403)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                $"Insufficient permissions for workspace '{workspaceId}'. Power BI API returned 403.",
                suggestion: "Verify the workspace ID and that your account has at least Member role.");
        }
        catch (PowerBiApiException ex) when (ex.StatusCode == 404)
        {
            return CortexResult<object>.Fail(CortexErrorCode.ElementNotFound,
                $"Workspace '{workspaceId}' not found.",
                suggestion: "Use pbi_list_workspaces to see available workspace IDs.");
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Dataset listing failed: {ex.Message}");
        }
    }
}
