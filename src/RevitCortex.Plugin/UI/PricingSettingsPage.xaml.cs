using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace RevitCortex.Plugin.UI;

public partial class PricingSettingsPage : Page
{
    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex", "settings.json");

    public PricingSettingsPage()
    {
        InitializeComponent();
        LoadPricing();
    }

    private void LoadPricing()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JObject.Parse(json);
                var pricing = settings["tokenPricing"] as JObject;
                if (pricing != null)
                {
                    LoadModel(pricing, "claude-sonnet-4-6", SonnetInputPrice, SonnetOutputPrice);
                    LoadModel(pricing, "claude-haiku-4-5", HaikuInputPrice, HaikuOutputPrice);
                    LoadModel(pricing, "claude-opus-4-6", OpusInputPrice, OpusOutputPrice);
                    return;
                }
            }
        }
        catch { }

        SetDefaults();
    }

    private static void LoadModel(JObject pricing, string model, TextBox inputBox, TextBox outputBox)
    {
        var m = pricing[model] as JObject;
        if (m != null)
        {
            inputBox.Text = (m["inputPerMTok"]?.Value<double>() ?? 0).ToString("F2");
            outputBox.Text = (m["outputPerMTok"]?.Value<double>() ?? 0).ToString("F2");
        }
    }

    private void SetDefaults()
    {
        SonnetInputPrice.Text = "3.00";
        SonnetOutputPrice.Text = "15.00";
        HaikuInputPrice.Text = "0.80";
        HaikuOutputPrice.Text = "4.00";
        OpusInputPrice.Text = "15.00";
        OpusOutputPrice.Text = "75.00";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParsePrice(SonnetInputPrice, "Sonnet Input", out double sonnetIn)) return;
        if (!TryParsePrice(SonnetOutputPrice, "Sonnet Output", out double sonnetOut)) return;
        if (!TryParsePrice(HaikuInputPrice, "Haiku Input", out double haikuIn)) return;
        if (!TryParsePrice(HaikuOutputPrice, "Haiku Output", out double haikuOut)) return;
        if (!TryParsePrice(OpusInputPrice, "Opus Input", out double opusIn)) return;
        if (!TryParsePrice(OpusOutputPrice, "Opus Output", out double opusOut)) return;

        try
        {
            string dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            JObject settings;
            if (File.Exists(SettingsFilePath))
                settings = JObject.Parse(File.ReadAllText(SettingsFilePath));
            else
                settings = new JObject();

            settings["tokenPricing"] = new JObject
            {
                ["claude-sonnet-4-6"] = new JObject { ["inputPerMTok"] = sonnetIn, ["outputPerMTok"] = sonnetOut },
                ["claude-haiku-4-5"] = new JObject { ["inputPerMTok"] = haikuIn, ["outputPerMTok"] = haikuOut },
                ["claude-opus-4-6"] = new JObject { ["inputPerMTok"] = opusIn, ["outputPerMTok"] = opusOut },
            };

            File.WriteAllText(SettingsFilePath, settings.ToString());

            MessageBox.Show("Pricing saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool TryParsePrice(TextBox box, string label, out double value)
    {
        if (double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0)
            return true;

        MessageBox.Show($"Invalid price for {label}. Enter a non-negative number.",
            "Invalid Price", MessageBoxButton.OK, MessageBoxImage.Warning);
        value = 0;
        return false;
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e) => SetDefaults();
}
