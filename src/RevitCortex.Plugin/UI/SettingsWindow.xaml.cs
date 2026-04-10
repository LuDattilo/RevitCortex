using System.Windows;
using System.Windows.Controls;

namespace RevitCortex.Plugin.UI;

public partial class SettingsWindow : Window
{
    private readonly GeneralSettingsPage _generalPage;
    private readonly ApiKeySettingsPage _apiKeyPage;
    private readonly ToolsSettingsPage _toolsPage;
    private readonly PricingSettingsPage _pricingPage;
    private readonly UsageReportPage _usagePage;
    private bool _isInitialized;

    public SettingsWindow()
    {
        InitializeComponent();

        _generalPage = new GeneralSettingsPage();
        _apiKeyPage = new ApiKeySettingsPage();
        _toolsPage = new ToolsSettingsPage();
        _pricingPage = new PricingSettingsPage();
        _usagePage = new UsageReportPage();

        ContentFrame.Navigate(_generalPage);
        _isInitialized = true;
    }

    private void NavListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;

        if (NavListBox.SelectedItem == GeneralItem)
            ContentFrame.Navigate(_generalPage);
        else if (NavListBox.SelectedItem == ApiKeyItem)
            ContentFrame.Navigate(_apiKeyPage);
        else if (NavListBox.SelectedItem == ToolsItem)
            ContentFrame.Navigate(_toolsPage);
        else if (NavListBox.SelectedItem == PricingItem)
            ContentFrame.Navigate(_pricingPage);
        else if (NavListBox.SelectedItem == UsageItem)
            ContentFrame.Navigate(_usagePage);
    }
}
