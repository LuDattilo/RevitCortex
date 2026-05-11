using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Reports the current Power BI authentication state. If a valid token is
/// cached, returns user info silently. If not, optionally starts a device-code
/// flow in the background and returns status immediately. The user completes
/// sign-in in the browser; a later silent check sees the DPAPI-backed MSAL cache.
///
/// Inputs:
///   signIn (bool, default false): if true and not already signed in, kick
///   off device-code flow without blocking Revit.
/// </summary>
public class PbiCheckAuthTool : ICortexTool
{
    private static readonly PowerBiAuthFlowState FlowState = new PowerBiAuthFlowState();

    public string Name => "pbi_check_auth";
    public string Category => "PowerBI";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Reports Power BI sign-in state. With signIn=true, starts a device-code flow inside Revit to authenticate the current Windows user.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var signIn = input["signIn"]?.Value<bool>() ?? false;
        var settings = PowerBiSettings.Load();
        var auth = new PowerBiAuthService(settings);

        // Step 1: silent attempt.
        // Run on a dedicated thread-pool thread with no SynchronizationContext so
        // MSAL continuations never try to marshal back to the Revit/WPF UI thread
        // (which would deadlock because this Execute() is blocking that same thread).
        AuthState state;
        try
        {
            state = RunWithoutContext(() => auth.TryAcquireSilentAsync());
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Silent acquisition crashed: {ex.GetType().Name}: {ex.Message}");
        }

        if (state.IsSignedIn)
        {
            return CortexResult<object>.Ok(BuildOkPayload(state, signedJustNow: false, settings));
        }

        if (!signIn)
        {
            var flow = FlowState.Snapshot();
            if (flow.IsRunning)
                return CortexResult<object>.Ok(BuildFlowPayload(flow, settings));

            return CortexResult<object>.Ok(new
            {
                signedIn = false,
                authFlowStatus = flow.Status.ToString(),
                lastError = flow.ErrorMessage,
                message = "Not signed in. Call again with signIn=true to start device-code flow.",
                clientId = settings.ClientId,
                tenantId = settings.TenantId
            });
        }

        // Step 2: device-code flow — fire-and-forget. Never wait for MSAL's
        // network call to return the device code on the Revit UI thread. The
        // first response may be "Starting"; the next status check returns the
        // code as soon as Microsoft Identity provides it.
        if (FlowState.TryBegin())
        {
            StartDeviceCodeFlow(auth);
        }

        return CortexResult<object>.Ok(BuildFlowPayload(FlowState.Snapshot(), settings));
    }

    private static void StartDeviceCodeFlow(PowerBiAuthService auth)
    {
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                var state = auth.SignInWithDeviceCodeAsync(dcr =>
                {
                    FlowState.SetDeviceCode(dcr.UserCode, dcr.VerificationUrl, dcr.ExpiresOn);
                    WriteDeviceCodeDiagnostic(dcr.UserCode, dcr.VerificationUrl);
                    OpenBrowser(dcr.VerificationUrl);
                }).ConfigureAwait(false).GetAwaiter().GetResult();

                if (state.IsSignedIn)
                {
                    FlowState.SetCompleted(state.Username);
                }
                else
                {
                    FlowState.SetFailed(state.ErrorMessage ?? "Power BI sign-in did not complete.");
                }
            }
            catch (Exception ex)
            {
                FlowState.SetFailed($"{ex.GetType().Name}: {ex.Message}");
            }
        });
        thread.IsBackground = true;
        thread.Start();
    }

    /// <summary>
    /// Runs an async factory on a dedicated background thread that starts with
    /// NO SynchronizationContext. This prevents MSAL's internal awaits from
    /// attempting to post continuations back to the Revit/WPF UI thread,
    /// which would deadlock because Execute() is called on that thread.
    /// </summary>
    private static T RunWithoutContext<T>(Func<System.Threading.Tasks.Task<T>> factory)
    {
        T result = default!;
        Exception? caught = null;

        var thread = new System.Threading.Thread(() =>
        {
            // Explicitly null the SynchronizationContext on this fresh thread.
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

    private static object BuildFlowPayload(PowerBiAuthFlowSnapshot flow, PowerBiSettings settings)
    {
        return new
        {
            signedIn = false,
            authFlowStatus = flow.Status.ToString(),
            awaitingLogin = flow.Status == PowerBiAuthFlowStatus.AwaitingUser,
            userCode = flow.UserCode,
            verificationUrl = flow.VerificationUrl,
            expiresOn = flow.ExpiresOn?.ToString("o"),
            lastError = flow.ErrorMessage,
            clientId = settings.ClientId,
            tenantId = settings.TenantId,
            message = BuildFlowMessage(flow),
            tip = "Complete login in the browser, then call pbi_check_auth(signIn=false) to confirm."
        };
    }

    private static string BuildFlowMessage(PowerBiAuthFlowSnapshot flow)
    {
        if (flow.Status == PowerBiAuthFlowStatus.AwaitingUser)
            return $"Open {flow.VerificationUrl} and enter code {flow.UserCode}.";

        if (flow.Status == PowerBiAuthFlowStatus.Starting)
            return "Power BI sign-in has started. Call pbi_check_auth(signIn=false) again in a few seconds to retrieve the device code.";

        if (flow.Status == PowerBiAuthFlowStatus.Failed)
            return flow.ErrorMessage ?? "Power BI sign-in failed.";

        if (flow.Status == PowerBiAuthFlowStatus.Completed)
            return "Power BI sign-in completed. Call pbi_check_auth(signIn=false) to verify the cached token.";

        return "Power BI sign-in is not started.";
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Non-fatal: caller still receives the URL and code.
        }
    }

    private static void WriteDeviceCodeDiagnostic(string userCode, string verificationUrl)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".revitcortex");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "pbi_device_code.txt"),
                $"{userCode}|{verificationUrl}");
        }
        catch
        {
            // Diagnostic only; auth state is kept in memory and MSAL owns token cache.
        }
    }

    private static object BuildOkPayload(AuthState state, bool signedJustNow, PowerBiSettings settings)
    {
        return new
        {
            signedIn = true,
            signedJustNow,
            username = state.Username,
            tenantId = state.TenantId,
            tokenExpiresOn = state.ExpiresOn?.ToString("o"),
            tokenLifetimeMinutes = state.ExpiresOn.HasValue
                ? (int)Math.Round((state.ExpiresOn.Value - DateTimeOffset.UtcNow).TotalMinutes)
                : (int?)null,
            clientId = settings.ClientId,
            allowExternalWrites = settings.AllowExternalWrites,
            tip = "Use pbi_list_workspaces next to enumerate available Power BI workspaces."
        };
    }

}
