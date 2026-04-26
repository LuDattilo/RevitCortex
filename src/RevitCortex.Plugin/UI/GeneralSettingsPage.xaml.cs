using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Plugin.Commands;
using RevitCortex.Plugin.Updates;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;

namespace RevitCortex.Plugin.UI;

public partial class GeneralSettingsPage : Page
{
    private const int DefaultPort = 8080;
    private const string DefaultLogLevel = "Info";
    private const int DefaultKeepCount = 10;
    private DispatcherTimer? _saveFeedbackTimer;
    private int _originalPort;
    private DispatcherTimer? _downloadTimer;

    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex", "settings.json");

    public GeneralSettingsPage()
    {
        InitializeComponent();
        ApplyLocalizedStrings();
        LoadSettings();
        LoadVersionInfo();
        RefreshConnectionStatus();
        RefreshUpdateBanner();
        // The update check runs once at plugin startup on a background thread;
        // when the user opens Settings it may or may not have completed yet.
        // Re-check every second for ~10 s to catch the late reply, then stop.
        StartUpdateBannerPolling();

        // Subscribe to real-time server state changes so the status banner
        // updates immediately when the user clicks Cortex Switch.
        if (RevitCortexApp.Instance != null)
            RevitCortexApp.Instance.ServiceStateChanged += OnServiceStateChanged;

        Unloaded += (_, _) =>
        {
            if (RevitCortexApp.Instance != null)
                RevitCortexApp.Instance.ServiceStateChanged -= OnServiceStateChanged;
        };
    }

    private void OnServiceStateChanged()
    {
        // The event may fire from the Revit main thread or a background thread.
        // Dispatcher.Invoke ensures we update WPF controls on the UI thread.
        Dispatcher.Invoke(RefreshConnectionStatus);
    }

    private void ApplyLocalizedStrings()
    {
        SupportReportsTitle.Text = Localization.T("support.settings.title");
        SupportReportsSubtitle.Text = Localization.T("support.settings.subtitle");
        OpenReportsFolderButton.Content = Localization.T("support.settings.open_folder");
        DeleteAllReportsButton.Content = Localization.T("support.settings.delete_now");
    }

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
                UpdateDetail.Text = "⚠ Revit verrà chiuso automaticamente — salva il lavoro prima di continuare.";
                UpdateProgressGrid.Visibility = Visibility.Collapsed;
                SetActionButton("Installa e chiudi Revit", "#388E3C", "#2E7D32", isEnabled: true);
                UpdateManualButton.Visibility = Visibility.Collapsed;
                StopDownloadTimer();
                break;

            case UpdateChecker.DownloadState.Installing:
                UpdateTitle.Text = "Installazione avviata — chiusura in corso…";
                UpdateDetail.Text = "Riavvia Revit al termine dell'installazione.";
                UpdateProgressGrid.Visibility = Visibility.Collapsed;
                SetActionButton("Installa e chiudi Revit", "#00796B", "#004D40", isEnabled: false);
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
        StopDownloadTimer(); // stop and null the old one before creating a new one
        _downloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _downloadTimer.Tick += (_, _) => RefreshUpdateBanner();
        _downloadTimer.Start();
    }

    private void StopDownloadTimer()
    {
        _downloadTimer?.Stop();
        _downloadTimer = null;
    }

    private void StartUpdateBannerPolling()
    {
        if (UpdateChecker.Latest != null) return; // check already completed

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        int ticks = 0;
        timer.Tick += (_, _) =>
        {
            ticks++;
            if (UpdateChecker.Latest != null || ticks >= 10)
            {
                timer.Stop();
                RefreshUpdateBanner();
            }
        };
        timer.Start();
    }

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
                // Close Revit after a short delay so the installer process has
                // time to start and enter its Assert-RevitClosed loop before
                // Revit exits. Without this the DLLs are locked and the install fails.
                var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                closeTimer.Tick += (_, _) =>
                {
                    closeTimer.Stop();
                    try { System.Windows.Application.Current?.Shutdown(); } catch { }
                };
                closeTimer.Start();
                break;

            case UpdateChecker.DownloadState.Installing:
                // Button is disabled in this state — unreachable in practice.
                break;

            case UpdateChecker.DownloadState.Done:
                // Banner is hidden in this state; click is unreachable.
                break;

            default:
                break; // Guard against future enum additions.
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

    private void RefreshConnectionStatus()
    {
        var app = RevitCortexApp.Instance;
        bool running = app?.IsServiceRunning ?? false;
        int port = app?.Port ?? 8080;

        if (running)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(46, 125, 50));   // green
            StatusBanner.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233));
            StatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(165, 214, 167));
            StatusTitle.Text = "Server running";
            StatusDetail.Text = $"Listening on localhost:{port} — ready for AI commands";
            PortBadgeText.Text = $"Port {port}";
            PortBadge.Background = new SolidColorBrush(Color.FromRgb(165, 214, 167));
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(158, 158, 158)); // gray
            StatusBanner.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
            StatusBanner.BorderBrush = new SolidColorBrush(Color.FromRgb(224, 224, 224));
            StatusTitle.Text = "Server stopped";
            StatusDetail.Text = "Click 'Cortex Switch' in the ribbon to start";
            PortBadgeText.Text = $"Port {port}";
            PortBadge.Background = new SolidColorBrush(Color.FromRgb(224, 224, 224));
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonConvert.DeserializeObject<CortexSettings>(json);
                if (settings != null)
                {
                    _originalPort = settings.Port;
                    PortTextBox.Text = settings.Port.ToString();
                    SetComboSelection(LogLevelComboBox, settings.LogLevel ?? DefaultLogLevel);
                    ReadOnlyCheckBox.IsChecked = settings.ReadOnlyMode;
                    KeepCountTextBox.Text = ClampKeepCount(settings.SupportReportKeepCount).ToString();
                    return;
                }
            }
        }
        catch { }

        SetDefaults();
    }

    private void LoadVersionInfo()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            PluginVersionText.Text = version?.ToString() ?? "Unknown";
        }
        catch { PluginVersionText.Text = "Unknown"; }

        try
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            string[] parts = assemblyPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string revitYear = "Unknown";
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Equals("Addins", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                {
                    revitYear = parts[i + 1];
                    break;
                }
            }
            RevitVersionText.Text = revitYear;
        }
        catch { RevitVersionText.Text = "Unknown"; }
    }

    private void SetDefaults()
    {
        _originalPort = DefaultPort;
        PortTextBox.Text = DefaultPort.ToString();
        SetComboSelection(LogLevelComboBox, DefaultLogLevel);
        KeepCountTextBox.Text = DefaultKeepCount.ToString();
    }

    private static int ClampKeepCount(int n) => n < 1 ? 1 : (n > 200 ? 200 : n);

    private static void SetComboSelection(ComboBox combo, string value)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Content?.ToString()?.Equals(value, StringComparison.OrdinalIgnoreCase) == true)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 1;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(PortTextBox.Text.Trim(), out int port) || port < 1 || port > 65535)
        {
            MessageBox.Show("Please enter a valid port number (1-65535).", "Invalid Port",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string logLevel = (LogLevelComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? DefaultLogLevel;

        if (!int.TryParse(KeepCountTextBox.Text.Trim(), out int keep)) keep = DefaultKeepCount;
        keep = ClampKeepCount(keep);
        KeepCountTextBox.Text = keep.ToString();

        try
        {
            // Merge-write: preserve keys managed by other pages (e.g. EnableCodeExecution,
            // DisabledTools) by loading the existing JSON first and updating only our fields.
            JObject settings = File.Exists(SettingsFilePath)
                ? JObject.Parse(File.ReadAllText(SettingsFilePath))
                : new JObject();

            settings["Port"] = port;
            settings["LogLevel"] = logLevel;
            settings["ReadOnlyMode"] = ReadOnlyCheckBox.IsChecked == true;
            settings["SupportReportKeepCount"] = keep;

            string dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(SettingsFilePath, settings.ToString(Formatting.Indented));

            // Apply read-only mode immediately (no restart needed)
            if (RevitCortexApp.Instance?.Router != null)
                RevitCortexApp.Instance.Router.ReadOnlyMode = ReadOnlyCheckBox.IsChecked == true;

            bool portChanged = port != _originalPort;
            if (portChanged)
            {
                ShowSaveFeedback("Saved \u2713  Restart Revit for port change", success: true, restartHint: true);
                _originalPort = port;
            }
            else
            {
                ShowSaveFeedback("Saved \u2713", success: true);
            }
        }
        catch (Exception ex)
        {
            ShowSaveFeedback($"Save failed: {ex.Message}", success: false);
        }
    }

    private void ShowSaveFeedback(string message, bool success, bool restartHint = false)
    {
        SaveFeedbackText.Text = message;
        SaveFeedbackText.Foreground = new SolidColorBrush(success
            ? Color.FromRgb(46, 125, 50)        // green
            : Color.FromRgb(198, 40, 40));      // red
        SaveFeedbackText.Visibility = Visibility.Visible;

        // Restart hint stays visible longer (4s) so the user can read it.
        var ttl = restartHint ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(2.5);

        _saveFeedbackTimer?.Stop();
        _saveFeedbackTimer = new DispatcherTimer { Interval = ttl };
        _saveFeedbackTimer.Tick += (_, _) =>
        {
            _saveFeedbackTimer?.Stop();
            _saveFeedbackTimer = null;
            SaveFeedbackText.Visibility = Visibility.Collapsed;
        };
        _saveFeedbackTimer.Start();
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e) => SetDefaults();

    private void OpenReportsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SendSupportReport.ReportsFolder);
            System.Diagnostics.Process.Start("explorer.exe", SendSupportReport.ReportsFolder);
        }
        catch (Exception ex)
        {
            TaskDialog.Show(Localization.T("support.title"),
                Localization.T("support.settings.open_folder_failed", ex.Message));
        }
    }

    private void DeleteAllReports_Click(object sender, RoutedEventArgs e)
    {
        var title = Localization.T("support.title");
        int count = SendSupportReport.CountReports();
        if (count == 0)
        {
            TaskDialog.Show(title, Localization.T("support.cleanup.none"));
            return;
        }

        long bytes = SendSupportReport.TotalReportsBytes();
        string size = FormatSize(bytes);
        var dialog = new TaskDialog(title)
        {
            MainInstruction = Localization.T("support.cleanup.confirm_title"),
            MainContent = Localization.T("support.cleanup.confirm_body", count, size),
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No,
        };
        if (dialog.Show() != TaskDialogResult.Yes) return;

        var (deleted, failed, _) = SendSupportReport.DeleteAllReports();
        if (failed == 0)
            TaskDialog.Show(title, Localization.T("support.cleanup.done", deleted));
        else
            TaskDialog.Show(title, Localization.T("support.cleanup.partial", deleted, failed));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}

internal class CortexSettings
{
    public int Port { get; set; } = 8080;
    public string? LogLevel { get; set; } = "Info";
    public bool ReadOnlyMode { get; set; }
    public int SupportReportKeepCount { get; set; } = 10;
}
