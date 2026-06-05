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

    private static volatile DownloadState _state = DownloadState.Idle;
    public static DownloadState State => _state;

    private static volatile string? _downloadError;
    public static string? DownloadError => _downloadError;

    private static volatile string? _extractedPath;
    public static string? ExtractedPath => _extractedPath;

    private static readonly object _progressLock = new();
    private static (long Received, long Total) _downloadProgress;
    public static (long Received, long Total) DownloadProgress
    {
        get { lock (_progressLock) return _downloadProgress; }
    }

    private static CancellationTokenSource? _cts;
    private static readonly string TempZipPath =
        Path.Combine(Path.GetTempPath(), "revitcortex-update", "latest.zip");
    private static readonly string TempExtractPath =
        Path.Combine(Path.GetTempPath(), "revitcortex-update", "extracted");

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>
    /// Fired on a background thread when the check completes and an update is available.
    /// Subscribers must marshal to the UI thread themselves.
    /// </summary>
    public static event Action? UpdateAvailable;

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
        var sha256 = (string?)obj["sha256"];

        if (string.IsNullOrWhiteSpace(versionStr) ||
            string.IsNullOrWhiteSpace(downloadUrl))
        {
            return; // malformed manifest — silently ignore
        }

        if (!Version.TryParse(versionStr, out var remoteVer)) return;

        // Security (C5): refuse to advertise an update whose download URL is not on a
        // trusted host. A poisoned manifest could otherwise aim the elevated installer
        // at an attacker-controlled server.
        if (!IsTrustedDownloadUrl(downloadUrl))
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Update manifest rejected: untrusted downloadUrl '{downloadUrl}'.");
            return;
        }

        Latest = new UpdateInfo(
            remoteVer,
            downloadUrl!,
            changelog ?? string.Empty,
            remoteVer > CurrentVersion,
            string.IsNullOrWhiteSpace(sha256) ? null : sha256!.Trim());

        if (Latest.HasUpdate)
            UpdateAvailable?.Invoke();
    }

    /// <summary>
    /// Begins downloading the update zip in the background.
    /// State transitions: Idle → Downloading → Ready (or Error).
    /// Safe to call only when State == Idle and Latest.HasUpdate == true.
    /// </summary>
    public static void StartDownloadAsync()
    {
        if (_state != DownloadState.Idle || Latest?.HasUpdate != true) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _state = DownloadState.Downloading;
        _downloadError = null;
        lock (_progressLock) { _downloadProgress = (0, 0); }

        var url = Latest.DownloadUrl;
        // Defense in depth (C5): Latest is only set after IsTrustedDownloadUrl in CheckAsync,
        // but re-validate before pulling bytes that will be run elevated.
        if (!IsTrustedDownloadUrl(url))
        {
            _state = DownloadState.Error;
            _downloadError = "Refusing to download update from an untrusted URL.";
            return;
        }
        var ct = _cts.Token;

        Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<(long, long)>(p => { lock (_progressLock) { _downloadProgress = p; } });
                var result = await UpdateDownloader.DownloadAsync(url, TempZipPath, progress, ct);

                if (!result.Success)
                {
                    _state = DownloadState.Error;
                    _downloadError = result.ErrorMessage;
                    return;
                }

                // Security (C4): if the trusted manifest published a SHA-256, the downloaded
                // artifact must match it before we extract and run the elevated installer.
                var expectedHash = Latest?.Sha256;
                if (!string.IsNullOrWhiteSpace(expectedHash) && !Sha256Matches(TempZipPath, expectedHash))
                {
                    _state = DownloadState.Error;
                    _downloadError = "Update aborted: downloaded file failed its SHA-256 integrity check.";
                    try { File.Delete(TempZipPath); } catch { /* best effort cleanup */ }
                    return;
                }

                var extractedPath = await UpdateDownloader.ExtractAsync(TempZipPath, TempExtractPath, ct);
                _extractedPath = extractedPath;
                _state = DownloadState.Ready;
            }
            catch (OperationCanceledException)
            {
                // CancelDownload() already set _state = Idle on the caller thread
            }
            catch (Exception ex)
            {
                _state = DownloadState.Error;
                _downloadError = ex.Message;
                System.Diagnostics.Trace.WriteLine($"[RevitCortex] Download failed: {ex.Message}");
            }
        }, ct);
    }

    /// <summary>Cancels an in-progress download, returning State to Idle.</summary>
    public static void CancelDownload()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _state = DownloadState.Idle;
        lock (_progressLock) { _downloadProgress = (0, 0); }
    }

    /// <summary>
    /// Launches install.ps1 from ExtractedPath with RunAs (UAC prompt).
    /// State transitions: Ready → Installing.
    /// </summary>
    /// <returns>
    /// true if the installer process was actually started (caller may then close Revit);
    /// false if it could not launch (UAC denied, missing script, wrong state) — callers
    /// MUST NOT shut Revit down on false (H1).
    /// </returns>
    public static bool LaunchInstaller()
    {
        if (_state != DownloadState.Ready || string.IsNullOrEmpty(_extractedPath)) return false;

        var script = Path.Combine(_extractedPath, "install.ps1");
        if (!File.Exists(script))
        {
            _state = DownloadState.Error;
            _downloadError = $"install.ps1 not found in {_extractedPath}";
            return false;
        }

        try
        {
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{script}\"",
                Verb = "runas",
                UseShellExecute = true,
            });
            if (proc == null)
            {
                _downloadError = "Installer process could not be started.";
                return false;
            }
            _state = DownloadState.Installing;
            return true;
        }
        catch (Exception ex)
        {
            // UAC denied or process launch failed — stay in Ready so user can retry
            _downloadError = $"Installer launch failed: {ex.Message}";
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] LaunchInstaller failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Resets download state to Idle (for "Retry" after error).</summary>
    public static void ResetDownload()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _state = DownloadState.Idle;
        _downloadError = null;
        lock (_progressLock) { _downloadProgress = (0, 0); }
        _extractedPath = null;
    }

    // --- Security helpers (ultrareview 2026-06-04, criticals C4/C5) ---

    // Release artifacts are hosted on GitHub (releases repo) or OneDrive/SharePoint
    // (the manifest historically used 1drv.ms). Extend this list if the host changes.
    private static readonly string[] AllowedDownloadHostSuffixes =
    {
        "githubusercontent.com",
        "github.com",
        "1drv.ms",
        "onedrive.live.com",
        "sharepoint.com",
    };

    /// <summary>
    /// True only for an HTTPS URL whose host is a known release-hosting domain.
    /// Fails closed: an unrecognized host is rejected so a poisoned manifest cannot
    /// point the elevated installer at an attacker-controlled server (C5).
    /// </summary>
    public static bool IsTrustedDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri is null) return false;
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return false;

        var host = uri.Host;
        foreach (var suffix in AllowedDownloadHostSuffixes)
        {
            if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True if the file at <paramref name="filePath"/> matches the given SHA-256 hex
    /// digest (case-insensitive). Used to verify download integrity against the hash
    /// published in the trusted manifest before the elevated installer runs (C4).
    /// </summary>
    public static bool Sha256Matches(string filePath, string? expectedHex)
    {
        if (string.IsNullOrWhiteSpace(expectedHex)) return false;
        if (!File.Exists(filePath)) return false;

        string actual;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (var stream = File.OpenRead(filePath))
        {
            var hash = sha.ComputeHash(stream);
            actual = BitConverter.ToString(hash).Replace("-", string.Empty);
        }

        var expected = expectedHex!.Trim().Replace(" ", string.Empty);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}

public class UpdateInfo
{
    public Version RemoteVersion { get; }
    public string DownloadUrl { get; }
    public string Changelog { get; }
    public bool HasUpdate { get; }
    public string? Sha256 { get; }

    public UpdateInfo(Version remoteVersion, string downloadUrl, string changelog, bool hasUpdate, string? sha256 = null)
    {
        RemoteVersion = remoteVersion;
        DownloadUrl = downloadUrl;
        Changelog = changelog;
        HasUpdate = hasUpdate;
        Sha256 = sha256;
    }
}
