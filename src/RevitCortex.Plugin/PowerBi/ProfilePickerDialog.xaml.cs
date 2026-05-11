using System.Collections.Generic;
using System.Windows;

namespace RevitCortex.Plugin.PowerBi;

public partial class ProfilePickerDialog : Window
{
    private readonly List<PowerBiExportProfile> _profiles;
    public PowerBiExportProfile? SelectedProfile { get; private set; }

    public ProfilePickerDialog(List<PowerBiExportProfile> profiles)
    {
        InitializeComponent();
        _profiles = profiles;
        ProfileList.ItemsSource = _profiles;
        if (_profiles.Count > 0) ProfileList.SelectedIndex = 0;
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        SelectedProfile = ProfileList.SelectedItem as PowerBiExportProfile;
        DialogResult = SelectedProfile != null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is PowerBiExportProfile p)
        {
            ProfileStore.Delete(p.Name);
            _profiles.Remove(p);
            ProfileList.Items.Refresh();
        }
    }
}
