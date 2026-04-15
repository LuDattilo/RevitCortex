using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevitCortex.Plugin.UI;

public partial class GeneralSettingsPage : Page
{
    private const int DefaultPort = 8080;
    private const string DefaultLogLevel = "Info";
    private const string DefaultModel = "claude-sonnet-4-6";
    private int _originalPort;

    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex", "settings.json");

    public GeneralSettingsPage()
    {
        InitializeComponent();
        LoadSettings();
        LoadVersionInfo();
        RefreshConnectionStatus();
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
                    SetComboText(ModelComboBox, settings.Model ?? DefaultModel);
                    ReadOnlyCheckBox.IsChecked = settings.ReadOnlyMode;
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
        SetComboText(ModelComboBox, DefaultModel);
    }

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

    private static void SetComboText(ComboBox combo, string value)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Content?.ToString() == value)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.Text = value;
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
        string model = ModelComboBox.Text?.Trim();
        if (string.IsNullOrEmpty(model)) model = DefaultModel;

        try
        {
            var settings = new CortexSettings
            {
                Port = port,
                LogLevel = logLevel,
                Model = model,
                ReadOnlyMode = ReadOnlyCheckBox.IsChecked == true
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
}

internal class CortexSettings
{
    public int Port { get; set; } = 8080;
    public string? LogLevel { get; set; } = "Info";
    public string? Model { get; set; } = "claude-sonnet-4-6";
    public bool ReadOnlyMode { get; set; }
}
