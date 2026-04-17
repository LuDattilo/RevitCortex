using Newtonsoft.Json;
using RevitCortex.Plugin.Commands;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TaskDialogCommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons;
using TaskDialogResult = Autodesk.Revit.UI.TaskDialogResult;

namespace RevitCortex.Plugin.UI;

public partial class GeneralSettingsPage : Page
{
    private const int DefaultPort = 8080;
    private const string DefaultLogLevel = "Info";
    private const int DefaultKeepCount = 10;
    private int _originalPort;

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
    }

    private void ApplyLocalizedStrings()
    {
        SupportReportsTitle.Text = Localization.T("support.settings.title");
        SupportReportsSubtitle.Text = Localization.T("support.settings.subtitle");
        OpenReportsFolderButton.Content = Localization.T("support.settings.open_folder");
        DeleteAllReportsButton.Content = Localization.T("support.settings.delete_now");
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
            var settings = new CortexSettings
            {
                Port = port,
                LogLevel = logLevel,
                ReadOnlyMode = ReadOnlyCheckBox.IsChecked == true,
                SupportReportKeepCount = keep
            };

            string dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(SettingsFilePath,
                JsonConvert.SerializeObject(settings, Formatting.Indented));

            // Apply read-only mode immediately (no restart needed)
            if (RevitCortexApp.Instance?.Router != null)
                RevitCortexApp.Instance.Router.ReadOnlyMode = settings.ReadOnlyMode;

            if (port != _originalPort)
            {
                MessageBox.Show("Settings saved. Restart Revit for port changes.",
                    "Saved", MessageBoxButton.OK, MessageBoxImage.Warning);
                _originalPort = port;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
