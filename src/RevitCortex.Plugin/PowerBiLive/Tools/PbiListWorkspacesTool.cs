using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Lists Power BI workspaces (groups) the signed-in user can access.
/// Read-only (works in RevitCortex read-only mode).
/// </summary>
public class PbiListWorkspacesTool : ICortexTool
{
    public string Name => "pbi_list_workspaces";
    public string Category => "PowerBI";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Lists Power BI workspaces (groups) accessible to the signed-in user.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var settings = PowerBiSettings.Load();
        var auth = new PowerBiAuthService(settings);

        var state = RunWithoutContext(() => auth.TryAcquireSilentAsync());
        if (!state.IsSignedIn || string.IsNullOrEmpty(state.AccessToken))
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Not signed in to Power BI.",
                suggestion: "Run pbi_check_auth with signIn=true to start device-code flow.");
        }

        try
        {
            using var client = new PowerBiServiceClient(state.AccessToken!);
            var workspaces = RunWithoutContext(() => client.ListWorkspacesAsync());

            return CortexResult<object>.Ok(new
            {
                signedInAs = state.Username,
                workspaceCount = workspaces.Count,
                workspaces = workspaces.Select(w => new
                {
                    id = w.Id,
                    name = w.Name,
                    type = w.Type,
                    state = w.State,
                    premium = w.IsOnDedicatedCapacity
                }).ToList(),
                tip = "Save the chosen workspace id with pbi_set_default_workspace (Phase 1)."
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Workspace listing failed: {ex.Message}");
        }
    }

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
