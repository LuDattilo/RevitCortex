using System;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Triggers an on-demand refresh of an Import-mode Power BI dataset.
///
/// Designed for the "Cortex writes CSV to OneDrive → user publishes .pbix → refresh
/// on push" flow: after a PushToPowerBi export, the user clicks (or this is wired
/// automatically) to make Power BI Service re-import the new CSVs.
///
/// This is independent from the PbiPublish* tools, which target a real-time Push
/// Dataset with a fixed v1.1 EAV schema. The refresh target is the Import dataset
/// the user manually published from their .pbix, whose source is the OneDrive
/// folder Cortex writes to.
///
/// Inputs:
///   workspaceId   (string, required): Power BI group GUID hosting the dataset.
///   datasetId     (string, required): GUID of the Import dataset to refresh.
///   notifyOnFailure (bool, optional, default false): email the dataset owner
///                  on refresh failure. Requires the workspace to have a valid
///                  dataset owner.
///
/// Returns:
///   { requestId, status, pollUrl } — requestId is empty on Pro (no Enhanced
///   Refresh). status will be "InProgress" immediately after the trigger;
///   callers can poll GetRefreshStatusAsync if they need the final state.
///
/// Quota:
///   Pro: 8 refreshes/day per dataset. Premium / PPU: 48/day. Hitting the
///   quota returns HTTP 400 with code RefreshOverLimit.
/// </summary>
[ToolSafety(false, false)]
public class PbiTriggerRefreshTool : ICortexTool
{
    public string Name => "pbi_trigger_refresh";
    public string Category => "PowerBI";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description =>
        "Triggers an on-demand refresh of an Import-mode Power BI dataset " +
        "(e.g. the .pbix published from a RevitCortex CSV folder). Use this " +
        "after PushToPowerBi to make the cloud workspace pick up the new data.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var workspaceId = input["workspaceId"]?.Value<string>();
        var datasetId   = input["datasetId"]?.Value<string>();
        var notifyOnFailure = input["notifyOnFailure"]?.Value<bool>() ?? false;

        if (string.IsNullOrWhiteSpace(workspaceId))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "workspaceId is required.",
                suggestion: "Pass the PBI workspace GUID. Find it in the workspace URL on app.powerbi.com.");

        if (string.IsNullOrWhiteSpace(datasetId))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "datasetId is required.",
                suggestion: "Pass the dataset GUID. Use pbi_list_datasets(workspaceId) to enumerate.");

        // Same auth pattern as the publish tools — silent acquire via cached MSAL token.
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
                suggestion: "Run pbi_check_auth(signIn=true) to refresh credentials.");
        }

        if (!authState.IsSignedIn || string.IsNullOrEmpty(authState.AccessToken))
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Not signed in to Power BI.",
                suggestion: "Run pbi_check_auth with signIn=true.");

        string requestId;
        string lastStatus;
        try
        {
            requestId = PowerBiToolHelper.RunWithoutContext(async () =>
            {
                using var client = new PowerBiServiceClient(authState.AccessToken!);
                return await client.TriggerRefreshAsync(workspaceId!, datasetId!, notifyOnFailure);
            });

            // Best-effort: query the last refresh status so the caller sees
            // something useful immediately. On Pro this is the only way to
            // verify the trigger landed (no requestId returned).
            lastStatus = PowerBiToolHelper.RunWithoutContext(async () =>
            {
                using var client = new PowerBiServiceClient(authState.AccessToken!);
                return await client.GetLastRefreshStatusAsync(workspaceId!, datasetId!);
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Refresh trigger failed: {ex.Message}",
                suggestion: "Verify the workspace and dataset GUIDs, and that the dataset is refreshable (Import mode).");
        }

        return CortexResult<object>.Ok(new
        {
            workspaceId,
            datasetId,
            requestId = string.IsNullOrEmpty(requestId) ? null : requestId,
            status = lastStatus,
            pollUrl = string.IsNullOrEmpty(requestId)
                ? null
                : $"https://api.powerbi.com/v1.0/myorg/groups/{workspaceId}/datasets/{datasetId}/refreshes/{requestId}",
            tip = string.IsNullOrEmpty(requestId)
                ? "Pro workspace: refresh queued but no requestId. Check status via pbi_get_last_refresh_status or in PBI Service."
                : "Premium / Enhanced Refresh: poll pollUrl or use pbi_get_refresh_status with this requestId."
        });
    }
}
