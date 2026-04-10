using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace RevitCortex.Plugin.UI;

public partial class GeneralSettingsPage : Page
{
    private const int DefaultPort = 8080;
    private const string DefaultLogLevel = "Info";
    private const string DefaultModel = "claude-sonnet-4-6";

    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex", "settings.json");

    public GeneralSettingsPage()
    {
        InitializeComponent();
        LoadSettings();
        LoadVersionInfo();
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
                    PortTextBox.Text = settings.Port.ToString();
                    SetComboSelection(LogLevelComboBox, settings.LogLevel ?? DefaultLogLevel);
                    SetComboText(ModelComboBox, settings.Model ?? DefaultModel);
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
                Model = model
            };

            string dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(SettingsFilePath,
                JsonConvert.SerializeObject(settings, Formatting.Indented));

            MessageBox.Show("Settings saved. Restart Revit for port changes.",
                "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
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
}
