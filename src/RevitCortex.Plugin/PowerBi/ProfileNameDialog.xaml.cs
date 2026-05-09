using System.Windows;

namespace RevitCortex.Plugin.PowerBi;

public partial class ProfileNameDialog : Window
{
    public string ProfileName { get; private set; } = "";

    public ProfileNameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ProfileName = NameBox.Text.Trim();
        DialogResult = !string.IsNullOrEmpty(ProfileName);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
