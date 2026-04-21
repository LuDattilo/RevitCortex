using Newtonsoft.Json.Linq;
using System;
using System.IO;
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

    public enum DownloadState { Idle, Downloading, Ready, Installing, Done, Error }

    public static DownloadState State { get; private set; } = DownloadState.Idle;
    public static string? DownloadError { get; private set; }
    public static string? ExtractedPath { get; private set; }
    public static (long Received, long Total) DownloadProgress { get; private set; }

    private static CancellationTokenSource? _cts;
    private static readonly string TempZipPath =
        Path.Combine(Path.GetTempPath(), "revitcortex-update", "latest.zip");
    private static readonly string TempExtractPath =
        Path.Combine(Path.GetTempPath(), "revitcortex-update", "extracted");

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

    /// <summary>
    /// Begins downloading the update zip in the background.
    /// State transitions: Idle → Downloading → Ready (or Error).
    /// Safe to call only when State == Idle and Latest.HasUpdate == true.
    /// </summary>
    public static void StartDownloadAsync()
    {
        if (State != DownloadState.Idle || Latest?.HasUpdate != true) return;

        _cts = new CancellationTokenSource();
        State = DownloadState.Downloading;
        DownloadError = null;
        DownloadProgress = (0, 0);

        var url = Latest.DownloadUrl;
        var ct = _cts.Token;

        Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<(long, long)>(p => DownloadProgress = p);
                var result = await UpdateDownloader.DownloadAsync(url, TempZipPath, progress, ct);

                if (!result.Success)
                {
                    State = DownloadState.Error;
                    DownloadError = result.ErrorMessage;
                    return;
                }

                var extractedPath = await UpdateDownloader.ExtractAsync(TempZipPath, TempExtractPath, ct);
                ExtractedPath = extractedPath;
                State = DownloadState.Ready;
            }
            catch (OperationCanceledException)
            {
                State = DownloadState.Idle;
            }
            catch (Exception ex)
            {
                State = DownloadState.Error;
                DownloadError = ex.Message;
                System.Diagnostics.Trace.WriteLine($"[RevitCortex] Download failed: {ex.Message}");
            }
        }, ct);
    }

    /// <summary>Cancels an in-progress download, returning State to Idle.</summary>
    public static void CancelDownload()
    {
        _cts?.Cancel();
        _cts = null;
        State = DownloadState.Idle;
        DownloadProgress = (0, 0);
    }

    /// <summary>
    /// Launches install.ps1 from ExtractedPath with RunAs (UAC prompt).
    /// State transitions: Ready → Installing.
    /// </summary>
    public static void LaunchInstaller()
    {
        if (State != DownloadState.Ready || string.IsNullOrEmpty(ExtractedPath)) return;

        var script = Path.Combine(ExtractedPath, "install.ps1");
        if (!File.Exists(script))
        {
            State = DownloadState.Error;
            DownloadError = $"install.ps1 not found in {ExtractedPath}";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{script}\"",
                Verb = "runas",
                UseShellExecute = true,
            });
            State = DownloadState.Installing;
        }
        catch (Exception ex)
        {
            // UAC denied or process launch failed — stay in Ready so user can retry
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] LaunchInstaller failed: {ex.Message}");
        }
    }

    /// <summary>Resets download state to Idle (for "Retry" after error).</summary>
    public static void ResetDownload()
    {
        _cts?.Cancel();
        _cts = null;
        State = DownloadState.Idle;
        DownloadError = null;
        DownloadProgress = (0, 0);
        ExtractedPath = null;
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
