using System.Windows;
using System.Windows.Controls;

namespace RevitCortex.Plugin.UI;

public partial class SettingsWindow : Window
{
    private readonly GeneralSettingsPage _generalPage;
    private readonly ToolsSettingsPage _toolsPage;
    private bool _isInitialized;

    public SettingsWindow()
    {
        InitializeComponent();

        _generalPage = new GeneralSettingsPage();
        _toolsPage = new ToolsSettingsPage();

        ContentFrame.Navigate(_generalPage);
        _isInitialized = true;
    }

    private void NavListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;

        if (NavListBox.SelectedItem == GeneralItem)
            ContentFrame.Navigate(_generalPage);
        else if (NavListBox.SelectedItem == ToolsItem)
            ContentFrame.Navigate(_toolsPage);
    }
}
