using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Reports the current Power BI authentication state. If a valid token is
/// cached, returns user info silently. If not, optionally starts a device-code
/// flow: the user sees a code + URL inside Revit and completes sign-in in
/// their browser, then this method returns success.
///
/// Inputs:
///   signIn (bool, default false): if true and not already signed in, kick
///   off device-code flow and wait for the user to finish.
/// </summary>
public class PbiCheckAuthTool : ICortexTool
{
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

        // Step 1: silent attempt
        AuthState state;
        try
        {
            state = Task.Run(() => auth.TryAcquireSilentAsync()).GetAwaiter().GetResult();
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
            return CortexResult<object>.Ok(new
            {
                signedIn = false,
                message = "Not signed in. Call again with signIn=true to start device-code flow.",
                clientId = settings.ClientId,
                tenantId = settings.TenantId
            });
        }

        // Step 2: device-code flow with TaskDialog
        DeviceCodeInfo? capturedCode = null;
        try
        {
            state = Task.Run(() => auth.SignInWithDeviceCodeAsync(dcr =>
            {
                capturedCode = new DeviceCodeInfo
                {
                    Url = dcr.VerificationUrl,
                    Code = dcr.UserCode,
                    Message = dcr.Message
                };
                ShowDeviceCodeDialog(dcr);
            })).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Device-code flow crashed: {ex.GetType().Name}: {ex.Message}");
        }

        if (!state.IsSignedIn)
        {
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                state.ErrorMessage ?? "Sign-in did not complete.",
                suggestion: capturedCode != null
                    ? $"Open {capturedCode.Url} and enter code {capturedCode.Code}, then re-run."
                    : "Try again or check tenant/client settings.");
        }

        return CortexResult<object>.Ok(BuildOkPayload(state, signedJustNow: true, settings));
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

    private static void ShowDeviceCodeDialog(Microsoft.Identity.Client.DeviceCodeResult dcr)
    {
        // Run on the WPF UI thread; if no Revit dispatcher is attached just block synchronously.
        try
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke((System.Action)(() =>
            {
                try
                {
                    var td = new Autodesk.Revit.UI.TaskDialog("RevitCortex — Power BI Sign-in")
                    {
                        MainInstruction = "Apri il browser e accedi al tuo account Power BI",
                        MainContent =
                            $"1. Apri questo URL: {dcr.VerificationUrl}\n" +
                            $"2. Inserisci il codice: {dcr.UserCode}\n" +
                            $"3. Completa il login Microsoft\n\n" +
                            "La finestra si chiude automaticamente al termine.",
                        CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Cancel,
                        DefaultButton = Autodesk.Revit.UI.TaskDialogResult.Cancel
                    };
                    td.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink1,
                        "Apri il browser ora");
                    var r = td.Show();
                    if (r == Autodesk.Revit.UI.TaskDialogResult.CommandLink1)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dcr.VerificationUrl)
                            {
                                UseShellExecute = true
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }));
        }
        catch
        {
            // No UI dispatcher (e.g. headless run) — caller will see the code in firstErrors instead.
        }
    }

    private class DeviceCodeInfo
    {
        public string Url { get; set; } = "";
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
    }
}
