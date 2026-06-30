using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using CoreSettings = RevitCortex.Core.Security.CortexSettings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RevitCortex.Plugin.UI;

public partial class ToolsSettingsPage : Page
{
    private readonly List<CategoryGroup> _allGroups = new();
    private readonly ObservableCollection<CategoryGroup> _filteredGroups = new();

    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex", "settings.json");

    public ToolsSettingsPage()
    {
        InitializeComponent();
        CategoryList.ItemsSource = _filteredGroups;

        SearchBox.TextChanged += (s, e) =>
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        };

        LoadTools();

        // Reflect the persisted master switch for custom C# execution.
        CodeExecToggle.IsChecked = CoreSettings.Load().EnableCodeExecution;
    }

    private void LoadTools()
    {
        _allGroups.Clear();

        var router = RevitCortexApp.Instance?.Router;
        if (router == null)
        {
            _allGroups.Add(new CategoryGroup
            {
                CategoryName = "No tools loaded",
                Tools = new ObservableCollection<ToolRowItem>
                {
                    new() { ToolName = "(start Revit with a project)", Description = "Open a project to see available tools" }
                }
            });
            ApplyFilter();
            return;
        }

        var toolInfos = router.GetAllToolInfo();

        // Group tools by category
        var grouped = toolInfos
            .GroupBy(t => t.Category)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var categoryGroup = new CategoryGroup
            {
                CategoryName = group.Key,
                IsExpanded = false
            };

            foreach (var (name, category, description, enabled) in group)
            {
                var item = new ToolRowItem
                {
                    ToolName = name,
                    Description = description,
                    IsEnabled = enabled
                };
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ToolRowItem.IsEnabled))
                    {
                        categoryGroup.OnPropertyChanged(nameof(CategoryGroup.EnabledCount));
                        UpdateCount();
                    }
                };
                categoryGroup.Tools.Add(item);
            }

            _allGroups.Add(categoryGroup);
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = SearchBox?.Text?.Trim().ToLowerInvariant() ?? "";
        _filteredGroups.Clear();

        if (string.IsNullOrEmpty(query))
        {
            foreach (var group in _allGroups)
                _filteredGroups.Add(group);
        }
        else
        {
            foreach (var group in _allGroups)
            {
                var matchingTools = group.Tools.Where(t =>
                    t.ToolName.ToLowerInvariant().Contains(query) ||
                    t.Description.ToLowerInvariant().Contains(query) ||
                    group.CategoryName.ToLowerInvariant().Contains(query))
                    .ToList();

                if (matchingTools.Count > 0)
                {
                    var filtered = new CategoryGroup
                    {
                        CategoryName = group.CategoryName,
                        IsExpanded = true
                    };
                    foreach (var tool in matchingTools)
                        filtered.Tools.Add(tool);
                    _filteredGroups.Add(filtered);
                }
            }
        }

        UpdateCount();
    }

    private void UpdateCount()
    {
        int enabled = _allGroups.SelectMany(g => g.Tools).Count(t => t.IsEnabled);
        int total = _allGroups.SelectMany(g => g.Tools).Count();
        ToolCountText.Text = $"{enabled} / {total} enabled";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void SelectAll_Click(object sender, MouseButtonEventArgs e)
    {
        CategoryList.ItemsSource = null;
        foreach (var tool in _allGroups.SelectMany(g => g.Tools))
            tool.IsEnabled = true;
        CategoryList.ItemsSource = _filteredGroups;
        UpdateCount();
    }

    private void DeselectAll_Click(object sender, MouseButtonEventArgs e)
    {
        CategoryList.ItemsSource = null;
        foreach (var tool in _allGroups.SelectMany(g => g.Tools))
            tool.IsEnabled = false;
        CategoryList.ItemsSource = _filteredGroups;
        UpdateCount();
    }

    private void ExpandAll_Click(object sender, MouseButtonEventArgs e)
    {
        CategoryList.ItemsSource = null;
        foreach (var group in _filteredGroups)
            group.IsExpanded = true;
        CategoryList.ItemsSource = _filteredGroups;
    }

    private void CollapseAll_Click(object sender, MouseButtonEventArgs e)
    {
        CategoryList.ItemsSource = null;
        foreach (var group in _filteredGroups)
            group.IsExpanded = false;
        CategoryList.ItemsSource = _filteredGroups;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var disabledTools = _allGroups
                .SelectMany(g => g.Tools)
                .Where(t => !t.IsEnabled)
                .Select(t => t.ToolName)
                .ToArray();

            JObject settings;
            if (File.Exists(SettingsFilePath))
            {
                settings = JObject.Parse(File.ReadAllText(SettingsFilePath));
            }
            else
            {
                settings = new JObject();
            }

            settings["DisabledTools"] = new JArray(disabledTools);

            string dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SettingsFilePath, settings.ToString(Formatting.Indented));

            RevitCortexApp.Instance?.Router?.SetDisabledTools(disabledTools);

            // Persist the master switch for send_code_to_revit (merge-write preserves
            // DisabledTools just written above and any other keys).
            bool codeExec = CodeExecToggle.IsChecked == true;
            CoreSettings.SetEnableCodeExecution(codeExec);

            MessageBox.Show(
                $"Saved. {disabledTools.Length} tools disabled. " +
                $"send_code_to_revit: {(codeExec ? "ENABLED (last resort)" : "disabled")}.",
                "Tools", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public class CategoryGroup : INotifyPropertyChanged
{
    private bool _isExpanded = true;

    public string CategoryName { get; set; } = "";
    public ObservableCollection<ToolRowItem> Tools { get; set; } = new();

    public int EnabledCount => Tools.Count(t => t.IsEnabled);
    public int TotalCount => Tools.Count;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    public void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ToolRowItem : INotifyPropertyChanged
{
    private bool _isEnabled;

    public string ToolName { get; set; } = "";
    public string Description { get; set; } = "";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
