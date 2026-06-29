using System;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.PowerBiLive.Tools;

/// <summary>
/// Revokes the cached Power BI token and clears the MSAL DPAPI cache.
/// After sign-out, pbi_check_auth(signIn=false) returns signedIn=false.
/// The user must call pbi_check_auth(signIn=true) to re-authenticate.
///
/// Security notes:
///   - Only the MSAL token cache is deleted (msal_cache.bin).
///   - No password or client secret is ever stored — nothing else to clear.
///   - The token is bound to the current Windows user (DPAPI CurrentUser).
///   - After sign-out an Azure AD admin can also revoke refresh tokens
///     server-side via the Entra ID portal.
/// </summary>
[ToolSafety(false, true)]
public class PbiSignOutTool : ICortexTool
{
    public string Name => "pbi_sign_out";
    public string Category => "PowerBI";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description =>
        "Signs out of Power BI by revoking all cached MSAL tokens. " +
        "After this call pbi_check_auth(signIn=false) returns signedIn=false. " +
        "Use pbi_check_auth(signIn=true) to re-authenticate.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var settings = PowerBiSettings.Load();
        var auth = new PowerBiAuthService(settings);

        // Check who is currently signed in before clearing
        string? username = null;
        try
        {
            var state = PowerBiToolHelper.RunWithoutContext(() => auth.TryAcquireSilentAsync());
            username = state.IsSignedIn ? state.Username : null;
        }
        catch { }

        try
        {
            PbiCheckAuthTool.ResetAuthFlow();
            PowerBiToolHelper.RunWithoutContext(async () =>
            {
                await auth.SignOutAsync().ConfigureAwait(false);
                return true;
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Sign-out failed: {ex.Message}");
        }

        return CortexResult<object>.Ok(new
        {
            signedOut = true,
            previousAccount = username ?? "(unknown)",
            message = username != null
                ? $"Signed out from {username}. MSAL token cache cleared."
                : "Token cache cleared (no active account found).",
            tip = "Use pbi_check_auth(signIn=true) to sign in again."
        });
    }
}
