# Auto-Update Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the manual "open browser → download zip → run script" update flow with an in-plugin download + RunAs launcher, reducing user action to click + UAC approval.

**Architecture:** `UpdateDownloader` (new) handles HTTP streaming and zip extraction. `UpdateChecker` (modified) adds a 6-state download machine (`Idle→Downloading→Ready→Installing→Done→Error`). `GeneralSettingsPage` (modified) renders the correct banner UI per state and manages a 250ms refresh timer during download.

**Tech Stack:** C# (.NET 8 / net48 multi-target), WPF/XAML, `System.Net.Http.HttpClient`, `System.IO.Compression.ZipFile`, `System.Diagnostics.Process`, XUnit

---

## File Map

| Action | Path | Responsibility |
|---|---|---|
| **CREATE** | `src/RevitCortex.Plugin/Updates/UpdateDownloader.cs` | HTTP streaming download + zip extraction |
| **MODIFY** | `src/RevitCortex.Plugin/Updates/UpdateChecker.cs` | Add `DownloadState` enum + download lifecycle methods |
| **MODIFY** | `src/RevitCortex.Plugin/UI/GeneralSettingsPage.xaml` | Add `ProgressBar`, `UpdateProgressText`, update button name |
| **MODIFY** | `src/RevitCortex.Plugin/UI/GeneralSettingsPage.xaml.cs` | State-aware banner rendering, download timer, action dispatch |
| **CREATE** | `src/RevitCortex.Tests/Updates/UpdateDownloaderTests.cs` | Unit tests for `UpdateDownloader` |

---

## Task 1: `UpdateDownloader` — test first

**Files:**
- Create: `src/RevitCortex.Tests/Updates/UpdateDownloaderTests.cs`
- Create: `src/RevitCortex.Plugin/Updates/UpdateDownloader.cs`

- [ ] **Step 1: Create the test file**

Create `src/RevitCortex.Tests/Updates/UpdateDownloaderTests.cs`:

```csharp
using RevitCortex.Plugin.Updates;
using System.IO.Compression;
using System.Net;
using System.Text;
using Xunit;

namespace RevitCortex.Tests.Updates;

public class UpdateDownloaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"rctest_{Guid.NewGuid():N}");

    public UpdateDownloaderTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    // Builds a valid zip byte array in memory (single entry "install.ps1")
    private static byte[] MakeFakeZip()
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("install.ps1");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("# fake installer");
        }
        return ms.ToArray();
    }

    private static HttpClient MakeClient(byte[] responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHttpHandler(responseBody, status);
        return new HttpClient(handler);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsSuccess_WhenResponseIsValid()
    {
        var zipBytes = MakeFakeZip();
        var client = MakeClient(zipBytes);
        var destPath = Path.Combine(_tempDir, "update.zip");
        var progressEvents = new List<(long, long)>();
        var progress = new Progress<(long, long)>(p => progressEvents.Add(p));

        var result = await UpdateDownloader.DownloadAsync(
            "http://fake/latest.zip", destPath, progress, CancellationToken.None, client);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.True(File.Exists(destPath));
        Assert.True(new FileInfo(destPath).Length > 0);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsFailure_WhenServerReturns404()
    {
        var client = MakeClient(Array.Empty<byte>(), HttpStatusCode.NotFound);
        var destPath = Path.Combine(_tempDir, "update.zip");

        var result = await UpdateDownloader.DownloadAsync(
            "http://fake/latest.zip", destPath, null, CancellationToken.None, client);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.False(File.Exists(destPath));
    }

    [Fact]
    public async Task DownloadAsync_ReturnsFailure_WhenCancelled()
    {
        var zipBytes = MakeFakeZip();
        var client = MakeClient(zipBytes);
        var destPath = Path.Combine(_tempDir, "update.zip");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await UpdateDownloader.DownloadAsync(
            "http://fake/latest.zip", destPath, null, cts.Token, client);

        Assert.False(result.Success);
        Assert.False(File.Exists(destPath));
    }

    [Fact]
    public async Task ExtractAsync_ExtractsFilesToDestination()
    {
        var zipBytes = MakeFakeZip();
        var zipPath = Path.Combine(_tempDir, "update.zip");
        await File.WriteAllBytesAsync(zipPath, zipBytes);
        var extractDir = Path.Combine(_tempDir, "extracted");

        var outPath = await UpdateDownloader.ExtractAsync(zipPath, extractDir, CancellationToken.None);

        Assert.Equal(extractDir, outPath);
        Assert.True(File.Exists(Path.Combine(extractDir, "install.ps1")));
    }

    [Fact]
    public async Task ExtractAsync_ThrowsOnEmptyZip()
    {
        var zipPath = Path.Combine(_tempDir, "empty.zip");
        await File.WriteAllBytesAsync(zipPath, Array.Empty<byte>());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdateDownloader.ExtractAsync(zipPath, _tempDir, CancellationToken.None));
    }

    // Minimal fake HTTP handler
    private class FakeHttpHandler(byte[] body, HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(body)
            };
            response.Content.Headers.ContentLength = body.Length;
            return Task.FromResult(response);
        }
    }
}
```

- [ ] **Step 2: Run the tests — expect compile error (UpdateDownloader does not exist yet)**

```
cd C:\Users\luigi.dattilo\Documents\RevitCortex
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj --configuration "Debug R26" --filter "UpdateDownloaderTests" 2>&1
```

Expected: build error `CS0246: The type or namespace name 'UpdateDownloader' could not be found`

- [ ] **Step 3: Create `UpdateDownloader.cs`**

Create `src/RevitCortex.Plugin/Updates/UpdateDownloader.cs`:

```csharp
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RevitCortex.Plugin.Updates;

public static class UpdateDownloader
{
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Downloads the zip at <paramref name="url"/> to <paramref name="destZipPath"/>,
    /// reporting (bytesReceived, totalBytes) via <paramref name="progress"/>.
    /// Pass a custom <paramref name="httpClient"/> for testing; production passes null.
    /// </summary>
    public static async Task<UpdateDownloadResult> DownloadAsync(
        string url,
        string destZipPath,
        IProgress<(long Received, long Total)>? progress,
        CancellationToken ct,
        HttpClient? httpClient = null)
    {
        bool ownsClient = httpClient is null;
        HttpClient http = httpClient ?? new HttpClient { Timeout = DownloadTimeout };

        try
        {
            ct.ThrowIfCancellationRequested();

            var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
                return Fail($"Server returned {(int)response.StatusCode} {response.ReasonPhrase}");

            long total = response.Content.Headers.ContentLength ?? -1;
            long received = 0;

            string? dir = Path.GetDirectoryName(destZipPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var file = new FileStream(destZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await file.WriteAsync(buffer, 0, read, ct);
                received += read;
                progress?.Report((received, total));
            }

            return new UpdateDownloadResult(true, null, destZipPath);
        }
        catch (OperationCanceledException)
        {
            TryDelete(destZipPath);
            return Fail("Download cancelled");
        }
        catch (Exception ex)
        {
            TryDelete(destZipPath);
            return Fail(ex.Message);
        }
        finally
        {
            if (ownsClient) http.Dispose();
        }
    }

    /// <summary>
    /// Extracts <paramref name="zipPath"/> into <paramref name="destDir"/>.
    /// Throws <see cref="InvalidDataException"/> if the zip is empty or corrupt.
    /// Returns <paramref name="destDir"/> on success.
    /// </summary>
    public static async Task<string> ExtractAsync(string zipPath, string destDir, CancellationToken ct)
    {
        // Validate before extracting
        var info = new FileInfo(zipPath);
        if (!info.Exists || info.Length == 0)
            throw new InvalidDataException($"Zip file is missing or empty: {zipPath}");

        // ZipFile.OpenRead throws InvalidDataException on corrupt zips
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            if (zip.Entries.Count == 0)
                throw new InvalidDataException("Zip file contains no entries");
        }

        ct.ThrowIfCancellationRequested();

        if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        Directory.CreateDirectory(destDir);

        // Run synchronous extraction on thread pool to avoid blocking UI thread
        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, destDir), ct);

        return destDir;
    }

    private static UpdateDownloadResult Fail(string msg) => new(false, msg, null);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

public record UpdateDownloadResult(bool Success, string? ErrorMessage, string? ZipPath);
```

- [ ] **Step 4: Run tests — expect all 5 to pass**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj --configuration "Debug R26" --filter "UpdateDownloaderTests" 2>&1
```

Expected:
```
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5
```

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Plugin/Updates/UpdateDownloader.cs src/RevitCortex.Tests/Updates/UpdateDownloaderTests.cs
git commit -m "feat(update): add UpdateDownloader with progress streaming and zip extraction"
```

---

## Task 2: Extend `UpdateChecker` with download state machine

**Files:**
- Modify: `src/RevitCortex.Plugin/Updates/UpdateChecker.cs`

- [ ] **Step 1: Add `DownloadState` enum and static properties after the existing `Latest` property**

Open `src/RevitCortex.Plugin/Updates/UpdateChecker.cs`. After line 37 (`public static UpdateInfo? Latest ...`), add:

```csharp
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
```

- [ ] **Step 2: Add `StartDownloadAsync()`, `CancelDownload()`, and `LaunchInstaller()` methods**

Add these methods to the `UpdateChecker` class (before the closing `}`):

```csharp
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
```

- [ ] **Step 3: Verify the project builds**

```
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj --configuration "Debug R26" 2>&1
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Run full test suite — all 84+ tests still pass**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj --configuration "Debug R26" 2>&1
```

Expected: `Failed: 0, Passed: 89+`

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Plugin/Updates/UpdateChecker.cs
git commit -m "feat(update): add DownloadState machine to UpdateChecker (StartDownloadAsync, CancelDownload, LaunchInstaller)"
```

---

## Task 3: Update `GeneralSettingsPage.xaml` — add progress bar and rename button

**Files:**
- Modify: `src/RevitCortex.Plugin/UI/GeneralSettingsPage.xaml`

- [ ] **Step 1: Replace the UpdateBanner `<Border>` content**

Open `src/RevitCortex.Plugin/UI/GeneralSettingsPage.xaml`.

Find the `UpdateBanner` Border (currently lines 51–68). Replace the entire `<Grid>` inside it with:

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
    </Grid.ColumnDefinitions>
    <StackPanel Grid.Column="0" VerticalAlignment="Center">
        <TextBlock x:Name="UpdateTitle" FontSize="13" FontWeight="SemiBold" Foreground="#333"/>
        <TextBlock x:Name="UpdateDetail" FontSize="11" Foreground="#666"
                   Margin="0,2,0,0" TextWrapping="Wrap"/>
        <!-- Progress bar: visible only while downloading -->
        <Grid x:Name="UpdateProgressGrid" Visibility="Collapsed" Margin="0,6,0,0">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>
            <ProgressBar x:Name="UpdateProgress" Grid.Row="0"
                         Height="5" Minimum="0" Maximum="100" Value="0"
                         Background="#E0E0E0" Foreground="#1976D2"/>
            <TextBlock x:Name="UpdateProgressText" Grid.Row="1"
                       FontSize="10" Foreground="#888" Margin="0,3,0,0"/>
        </Grid>
    </StackPanel>
    <!-- Action buttons — only one visible at a time (except ERROR shows both) -->
    <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="12,0,0,0">
        <Button x:Name="UpdateActionButton"
                Content="Download &amp; Install"
                Padding="14,6" FontSize="13"
                Background="#FFB300" Foreground="White" BorderBrush="#FF8F00"
                Click="UpdateAction_Click" Cursor="Hand"/>
        <!-- Secondary button: visible only in ERROR state, to the right of "Riprova" -->
        <Button x:Name="UpdateManualButton"
                Content="Scarica manualmente"
                Padding="10,6" Margin="8,0,0,0" FontSize="12"
                Background="#9E9E9E" Foreground="White" BorderBrush="#757575"
                Click="UpdateManual_Click" Cursor="Hand" Visibility="Collapsed"/>
    </StackPanel>
</Grid>
```

> **Note:** The existing `UpdateDownloadButton` is replaced by `UpdateActionButton`. Remove any leftover reference to `UpdateDownloadButton` in the XAML file.

- [ ] **Step 2: Verify XAML compiles (build the plugin project)**

```
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj --configuration "Debug R26" 2>&1
```

Expected: `Build succeeded. 0 Error(s)` (will likely error if `UpdateDownloadButton` is still referenced in `.xaml.cs` — fix that in Task 4)

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Plugin/UI/GeneralSettingsPage.xaml
git commit -m "feat(update): add ProgressBar and state-aware action button to UpdateBanner"
```

---

## Task 4: Update `GeneralSettingsPage.xaml.cs` — state rendering and download timer

**Files:**
- Modify: `src/RevitCortex.Plugin/UI/GeneralSettingsPage.xaml.cs`

- [ ] **Step 1: Add `_downloadTimer` field and remove the old `UpdateDownloadButton` reference**

At the top of the class, add the timer field alongside `_originalPort`:

```csharp
private int _originalPort;
private DispatcherTimer? _downloadTimer;
```

Remove the line in `ApplyLocalizedStrings()` that references `UpdateDownloadButton`:
```csharp
// DELETE this line:
UpdateDownloadButton.Content = Localization.T("update.download_button");
```

- [ ] **Step 2: Replace `RefreshUpdateBanner()` with state-aware version**

Delete the current `RefreshUpdateBanner()` method and replace it with:

```csharp
private void RefreshUpdateBanner()
{
    var info = UpdateChecker.Latest;
    if (info?.HasUpdate != true)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
        StopDownloadTimer();
        return;
    }

    UpdateBanner.Visibility = Visibility.Visible;

    switch (UpdateChecker.State)
    {
        case UpdateChecker.DownloadState.Idle:
            UpdateTitle.Text = $"RevitCortex {info.RemoteVersion} disponibile";
            UpdateDetail.Text = $"Sei sulla {UpdateChecker.CurrentVersion} — {info.Changelog}";
            UpdateProgressGrid.Visibility = Visibility.Collapsed;
            SetActionButton("Download & Install", "#FFB300", "#FF8F00", isEnabled: true);
            UpdateManualButton.Visibility = Visibility.Collapsed;
            StopDownloadTimer();
            break;

        case UpdateChecker.DownloadState.Downloading:
            var (recv, total) = UpdateChecker.DownloadProgress;
            string progress = total > 0
                ? $"{recv / 1_048_576.0:F0} / {total / 1_048_576.0:F0} MB"
                : $"{recv / 1_048_576.0:F0} MB scaricati…";
            double pct = total > 0 ? recv * 100.0 / total : 0;
            UpdateTitle.Text = $"Download in corso… {progress}";
            UpdateDetail.Text = string.Empty;
            UpdateProgress.Value = pct;
            UpdateProgressText.Text = progress;
            UpdateProgressGrid.Visibility = Visibility.Visible;
            SetActionButton("Annulla", "#9E9E9E", "#757575", isEnabled: true);
            UpdateManualButton.Visibility = Visibility.Collapsed;
            StartDownloadTimer();
            break;

        case UpdateChecker.DownloadState.Ready:
            UpdateTitle.Text = "Pronto per l'installazione";
            UpdateDetail.Text = "Si aprirà il prompt di amministratore — approva per installare";
            UpdateProgressGrid.Visibility = Visibility.Collapsed;
            SetActionButton("Install ora", "#388E3C", "#2E7D32", isEnabled: true);
            UpdateManualButton.Visibility = Visibility.Collapsed;
            StopDownloadTimer();
            break;

        case UpdateChecker.DownloadState.Installing:
            UpdateTitle.Text = "Installazione avviata";
            UpdateDetail.Text = "Riavvia Revit per completare l'aggiornamento";
            UpdateProgressGrid.Visibility = Visibility.Collapsed;
            SetActionButton("Chiudi Revit", "#00796B", "#004D40", isEnabled: true);
            UpdateManualButton.Visibility = Visibility.Collapsed;
            StopDownloadTimer();
            break;

        case UpdateChecker.DownloadState.Done:
            UpdateBanner.Visibility = Visibility.Collapsed;
            StopDownloadTimer();
            break;

        case UpdateChecker.DownloadState.Error:
            UpdateTitle.Text = "Download fallito";
            UpdateDetail.Text = UpdateChecker.DownloadError ?? "Errore sconosciuto";
            UpdateProgressGrid.Visibility = Visibility.Collapsed;
            SetActionButton("Riprova", "#E53935", "#B71C1C", isEnabled: true);
            UpdateManualButton.Visibility = Visibility.Visible;
            StopDownloadTimer();
            break;
    }
}

private void SetActionButton(string label, string bg, string border, bool isEnabled)
{
    UpdateActionButton.Content = label;
    UpdateActionButton.Background = new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString(bg));
    UpdateActionButton.BorderBrush = new SolidColorBrush(
        (Color)ColorConverter.ConvertFromString(border));
    UpdateActionButton.IsEnabled = isEnabled;
}

private void StartDownloadTimer()
{
    if (_downloadTimer?.IsEnabled == true) return;
    _downloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
    _downloadTimer.Tick += (_, _) => RefreshUpdateBanner();
    _downloadTimer.Start();
}

private void StopDownloadTimer()
{
    _downloadTimer?.Stop();
    _downloadTimer = null;
}
```

- [ ] **Step 3: Replace `UpdateDownload_Click` with `UpdateAction_Click` and add `UpdateManual_Click`**

Delete the existing `UpdateDownload_Click` method and replace with:

```csharp
private void UpdateAction_Click(object sender, RoutedEventArgs e)
{
    switch (UpdateChecker.State)
    {
        case UpdateChecker.DownloadState.Idle:
        case UpdateChecker.DownloadState.Error:
            UpdateChecker.ResetDownload();
            UpdateChecker.StartDownloadAsync();
            RefreshUpdateBanner();
            break;

        case UpdateChecker.DownloadState.Downloading:
            UpdateChecker.CancelDownload();
            RefreshUpdateBanner();
            break;

        case UpdateChecker.DownloadState.Ready:
            UpdateChecker.LaunchInstaller();
            RefreshUpdateBanner();
            break;

        case UpdateChecker.DownloadState.Installing:
            // "Chiudi Revit" button
            try { System.Windows.Application.Current?.Shutdown(); } catch { }
            break;
    }
}

private void UpdateManual_Click(object sender, RoutedEventArgs e)
{
    var info = UpdateChecker.Latest;
    if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl)) return;
    try
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = info.DownloadUrl,
            UseShellExecute = true,
        });
    }
    catch (Exception ex)
    {
        TaskDialog.Show(Localization.T("support.title"),
            Localization.T("update.open_browser_failed", ex.Message));
    }
}
```

- [ ] **Step 4: Build the plugin**

```
dotnet build src/RevitCortex.Plugin/RevitCortex.Plugin.csproj --configuration "Debug R26" 2>&1
```

Expected: `Build succeeded. 0 Error(s)`

If `ColorConverter` is unresolved, add `using System.Windows.Media;` at the top of the file (should already be present).

- [ ] **Step 5: Run full test suite**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj --configuration "Debug R26" 2>&1
```

Expected: `Failed: 0, Passed: 89+`

- [ ] **Step 6: Commit**

```bash
git add src/RevitCortex.Plugin/UI/GeneralSettingsPage.xaml.cs
git commit -m "feat(update): state-aware banner rendering, download timer, UpdateAction_Click dispatcher"
```

---

## Task 5: Build release and manual smoke test

**Files:** none (verification only)

- [ ] **Step 1: Build all Revit targets**

```
cd C:\Users\luigi.dattilo\Documents\RevitCortex
powershell -ExecutionPolicy Bypass -Command ".\build-release.ps1 -Version 1.0.7-dev" 2>&1
```

Expected: zip created at `C:\Users\luigi.dattilo\Documents\RevitCortex\RevitCortex-v1.0.7-dev.zip`

- [ ] **Step 2: Deploy to local Revit for manual test**

```
powershell -ExecutionPolicy Bypass -File deploy.ps1 -RevitVersion 2025 -Config Release
```

- [ ] **Step 3: Simulate update available**

Temporarily edit `UpdateChecker.cs`, change `ManifestUrl` to a local HTTP server or a test URL that returns a higher version. Alternatively, edit the `CheckAsync` method to force `Latest` with `HasUpdate = true` and a real `DownloadUrl` pointing to the existing `RevitCortex-v1.0.6.zip` on OneDrive.

Quick test shim — add this temporary override at the end of `CheckInBackground()`:

```csharp
// TEMP: force update banner for smoke test — remove before shipping
Latest = new UpdateInfo(
    new Version(99, 0, 0),
    "https://raw.githubusercontent.com/LuDattilo/revitcortex-releases/main/latest.json", // will 404 intentionally
    "Smoke test update",
    hasUpdate: true);
```

- [ ] **Step 4: Open Revit, open Settings → General — verify IDLE state**

Expected: amber banner "RevitCortex 99.0.0 disponibile" with "Download & Install" button

- [ ] **Step 5: Click "Download & Install" — verify DOWNLOADING state**

Expected: blue banner with progress bar; button becomes "Annulla"

- [ ] **Step 6: Let download fail (URL is invalid) — verify ERROR state**

Expected: red banner "Download fallito", "Riprova" and "Scarica manualmente" buttons

- [ ] **Step 7: Click "Riprova" — verify returns to DOWNLOADING**

Expected: blue banner reappears with fresh progress

- [ ] **Step 8: Click "Annulla" — verify IDLE state restored**

Expected: amber banner returns to original IDLE state

- [ ] **Step 9: Remove the smoke test shim from `UpdateChecker.cs`**

Remove the temporary `Latest = new UpdateInfo(...)` lines added in Step 3.

- [ ] **Step 10: Restore `ManifestUrl` if modified, run full test suite**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj --configuration "Debug R26" 2>&1
```

Expected: `Failed: 0`

- [ ] **Step 11: Final commit**

```bash
git add src/RevitCortex.Plugin/Updates/UpdateChecker.cs
git commit -m "test(update): verify all banner states pass smoke test — shim removed"
```
