using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RevitCortex.Plugin.Updates;

/// <summary>
/// Checks a remote manifest for a newer RevitCortex release.
/// The manifest is a small public JSON file hosted in a separate
/// metadata repository (the product repo itself is private, so we
/// cannot read it anonymously from the plugin).
///
/// Shape of latest.json:
/// {
///   "version": "1.0.3",
///   "downloadUrl": "https://1drv.ms/...",
///   "changelog": "..."
/// }
///
/// Check runs once at plugin startup on a background task with a
/// short timeout. Network failures are swallowed — we never block
/// Revit startup or surface a dialog.
/// </summary>
public static class UpdateChecker
{
    private const string ManifestUrl =
        "https://raw.githubusercontent.com/LuDattilo/revitcortex-releases/main/latest.json";

    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(5);

    // Cached result read by the Settings page. null = no check done
    // yet; .HasUpdate may be false if we're already on the latest.
    public static UpdateInfo? Latest { get; private set; }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>Fires the check in the background. Safe to call once at startup.</summary>
    public static void CheckInBackground()
    {
        Task.Run(async () =>
        {
            try { await CheckAsync(); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Update check failed: {ex.Message}");
            }
        });
    }

    private static async Task CheckAsync()
    {
        // Allow TLS 1.2 on older runtimes (net48 defaults to SSL3/TLS1.0,
        // raw.githubusercontent.com requires TLS 1.2+).
        try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
        catch { /* ignore on runtimes that don't expose the enum value */ }

        using var http = new HttpClient { Timeout = HttpTimeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"RevitCortex/{CurrentVersion} (+update-check)");

        var body = await http.GetStringAsync(ManifestUrl);
        var obj = JObject.Parse(body);

        var versionStr = (string?)obj["version"];
        var downloadUrl = (string?)obj["downloadUrl"];
        var changelog = (string?)obj["changelog"];

        if (string.IsNullOrWhiteSpace(versionStr) ||
            string.IsNullOrWhiteSpace(downloadUrl))
        {
            return; // malformed manifest — silently ignore
        }

        if (!Version.TryParse(versionStr, out var remoteVer)) return;

        Latest = new UpdateInfo(
            remoteVer,
            downloadUrl!,
            changelog ?? string.Empty,
            remoteVer > CurrentVersion);
    }
}

public class UpdateInfo
{
    public Version RemoteVersion { get; }
    public string DownloadUrl { get; }
    public string Changelog { get; }
    public bool HasUpdate { get; }

    public UpdateInfo(Version remoteVersion, string downloadUrl, string changelog, bool hasUpdate)
    {
        RemoteVersion = remoteVersion;
        DownloadUrl = downloadUrl;
        Changelog = changelog;
        HasUpdate = hasUpdate;
    }
}
