using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// MSAL public-client authentication for Power BI REST APIs.
/// Uses device-code flow so we never need to embed a browser inside Revit.
/// Tokens are persisted to <c>%LOCALAPPDATA%\.revitcortex\msal_cache.bin</c>
/// and protected with DPAPI (per-user, per-machine).
/// </summary>
public class PowerBiAuthService
{
    /// <summary>Power BI delegated scope — read/write semantic models.</summary>
    public static readonly string[] DefaultScopes = new[]
    {
        "https://analysis.windows.net/powerbi/api/Dataset.ReadWrite.All",
        "https://analysis.windows.net/powerbi/api/Workspace.Read.All",
        "https://analysis.windows.net/powerbi/api/Report.Read.All"
    };

    private static readonly string CacheFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ".revitcortex", "msal_cache.bin");

    private readonly IPublicClientApplication _app;

    public PowerBiAuthService(PowerBiSettings settings)
    {
        var authority = string.IsNullOrEmpty(settings.TenantId)
            ? "https://login.microsoftonline.com/organizations"
            : $"https://login.microsoftonline.com/{settings.TenantId}";

        _app = PublicClientApplicationBuilder
            .Create(settings.ClientId)
            .WithAuthority(authority)
            .WithRedirectUri("http://localhost") // unused with device code, MSAL still wants one
            .Build();

        _app.UserTokenCache.SetBeforeAccess(BeforeAccess);
        _app.UserTokenCache.SetAfterAccess(AfterAccess);
    }

    /// <summary>
    /// Returns true if a cached account is present and a silent token can be acquired.
    /// Does NOT trigger interactive UI; use <see cref="SignInWithDeviceCodeAsync"/> for that.
    /// </summary>
    public async Task<AuthState> TryAcquireSilentAsync(CancellationToken ct = default)
    {
        // ConfigureAwait(false) throughout so MSAL continuations never try to
        // marshal back to the Revit/WPF SynchronizationContext — that would
        // deadlock when the caller blocks with .GetAwaiter().GetResult().
        var accounts = (await _app.GetAccountsAsync().ConfigureAwait(false)).ToList();
        if (accounts.Count == 0) return AuthState.NotSignedIn();

        var first = accounts[0];
        try
        {
            // First attempt: standard silent acquire (uses cache if token still valid)
            var result = await _app.AcquireTokenSilent(DefaultScopes, first)
                .ExecuteAsync(ct).ConfigureAwait(false);

            // If token expires within 5 minutes, proactively refresh via refresh token
            if (result.ExpiresOn - DateTimeOffset.UtcNow < TimeSpan.FromMinutes(5))
            {
                try
                {
                    result = await _app.AcquireTokenSilent(DefaultScopes, first)
                        .WithForceRefresh(true)
                        .ExecuteAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    // Proactive refresh failed — use the still-valid cached token
                }
            }

            return AuthState.SignedIn(result);
        }
        catch (MsalUiRequiredException)
        {
            // Cache is present but token expired and refresh token no longer valid
            return AuthState.NotSignedIn();
        }
        catch (MsalServiceException ex)
        {
            // Transient Microsoft Identity service error (503, DNS failure, proxy).
            // Surface as AuthState.Error so callers can show a network-error hint
            // instead of an unhandled exception stack trace.
            return AuthState.Error(ex);
        }
    }

    /// <summary>
    /// Initiates device-code flow. The <paramref name="onCodeReceived"/> callback
    /// gets the verification URL + user code as soon as Microsoft Identity issues
    /// them — show those to the user. Completes when the user finishes signing in
    /// (or the flow times out).
    /// </summary>
    public async Task<AuthState> SignInWithDeviceCodeAsync(
        Action<DeviceCodeResult> onCodeReceived,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _app
                .AcquireTokenWithDeviceCode(DefaultScopes, dcr =>
                {
                    onCodeReceived(dcr);
                    return Task.CompletedTask;
                })
                .ExecuteAsync(ct).ConfigureAwait(false);
            return AuthState.SignedIn(result);
        }
        catch (Exception ex)
        {
            return AuthState.Error(ex);
        }
    }

    public async Task SignOutAsync()
    {
        var accounts = await _app.GetAccountsAsync().ConfigureAwait(false);
        foreach (var a in accounts)
            await _app.RemoveAsync(a).ConfigureAwait(false);
        try { if (File.Exists(CacheFilePath)) File.Delete(CacheFilePath); } catch { }
    }

    // ─── DPAPI-protected token cache ────────────────────────────────────

    private static void BeforeAccess(TokenCacheNotificationArgs args)
    {
        try
        {
            if (!File.Exists(CacheFilePath)) return;
            var encrypted = File.ReadAllBytes(CacheFilePath);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            args.TokenCache.DeserializeMsalV3(decrypted);
        }
        catch
        {
            // Corrupt cache — ignore, next acquisition will rebuild it
        }
    }

    private static void AfterAccess(TokenCacheNotificationArgs args)
    {
        if (!args.HasStateChanged) return;
        try
        {
            var dir = Path.GetDirectoryName(CacheFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var serialized = args.TokenCache.SerializeMsalV3();
            var encrypted = ProtectedData.Protect(serialized, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(CacheFilePath, encrypted);
        }
        catch
        {
            // Best effort — if the cache can't be persisted the user will just
            // need to re-authenticate next time, which is recoverable.
        }
    }
}

/// <summary>
/// Result envelope for any auth call. Lightweight value type so callers
/// don't need to handle exceptions outside the service boundary.
/// </summary>
public class AuthState
{
    public bool IsSignedIn { get; }
    public string? AccessToken { get; }
    public string? Username { get; }
    public string? TenantId { get; }
    public DateTimeOffset? ExpiresOn { get; }
    public string? ErrorMessage { get; }

    private AuthState(bool ok, string? token, string? user, string? tenant,
        DateTimeOffset? expires, string? err)
    {
        IsSignedIn = ok;
        AccessToken = token;
        Username = user;
        TenantId = tenant;
        ExpiresOn = expires;
        ErrorMessage = err;
    }

    public static AuthState SignedIn(AuthenticationResult r) =>
        new(true, r.AccessToken, r.Account?.Username, r.TenantId, r.ExpiresOn, null);

    public static AuthState NotSignedIn() => new(false, null, null, null, null, null);

    public static AuthState Error(Exception ex) =>
        new(false, null, null, null, null, $"{ex.GetType().Name}: {ex.Message}");
}
