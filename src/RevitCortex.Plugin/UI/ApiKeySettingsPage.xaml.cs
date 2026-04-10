using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RevitCortex.Plugin.UI;

public partial class ApiKeySettingsPage : Page
{
    private bool _isPasswordVisible;
    private string _currentApiKey = string.Empty;

    public ApiKeySettingsPage()
    {
        InitializeComponent();
        DetectCurrentApiKey();
        EnvVarRadio.IsChecked = true;
    }

    private void DetectCurrentApiKey()
    {
        string? envKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        string? fileKey = null;
        string filePath = GetApiKeyFilePath();

        if (File.Exists(filePath))
        {
            try
            {
                fileKey = File.ReadAllText(filePath).Trim();
                if (string.IsNullOrEmpty(fileKey)) fileKey = null;
            }
            catch { fileKey = null; }
        }

        if (!string.IsNullOrEmpty(envKey))
        {
            StatusText.Text = "Configured";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            StatusSourceText.Text = "(from environment variable)";
            EnvVarRadio.IsChecked = true;
        }
        else if (!string.IsNullOrEmpty(fileKey))
        {
            StatusText.Text = "Configured";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            StatusSourceText.Text = "(from file)";
            FileRadio.IsChecked = true;
        }
        else
        {
            StatusText.Text = "Not configured";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
            StatusSourceText.Text = string.Empty;
        }
    }

    private static string GetApiKeyFilePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "api_key.txt");
    }

    private void ToggleVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;
        if (_isPasswordVisible)
        {
            ApiKeyTextBox.Text = _currentApiKey;
            ApiKeyPasswordBox.Visibility = Visibility.Collapsed;
            ApiKeyTextBox.Visibility = Visibility.Visible;
            ToggleVisibilityButton.Content = "Hide";
        }
        else
        {
            ApiKeyPasswordBox.Password = _currentApiKey;
            ApiKeyTextBox.Visibility = Visibility.Collapsed;
            ApiKeyPasswordBox.Visibility = Visibility.Visible;
            ToggleVisibilityButton.Content = "Show";
        }
    }

    private void ApiKeyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _currentApiKey = ApiKeyPasswordBox.Password;

    private void ApiKeyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        => _currentApiKey = ApiKeyTextBox.Text;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string apiKey = _currentApiKey?.Trim() ?? "";
        if (string.IsNullOrEmpty(apiKey))
        {
            MessageBox.Show("Please enter an API key.", "Missing API Key",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (EnvVarRadio.IsChecked == true)
            {
                Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", apiKey, EnvironmentVariableTarget.User);
                MessageBox.Show(
                    "API key saved to environment variable ANTHROPIC_API_KEY.\nRestart Revit for changes to take effect.",
                    "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (FileRadio.IsChecked == true)
            {
                string filePath = GetApiKeyFilePath();
                string? dir = Path.GetDirectoryName(filePath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(filePath, apiKey);
                MessageBox.Show(
                    $"API key saved to {filePath}.\nRestart Revit for changes to take effect.",
                    "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            DetectCurrentApiKey();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save API key: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
