using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// Three-step wizard for configuring a Power BI export. Inspired by SheetLink's
/// dual-pane parameter selection but with extra features SheetLink lacks:
/// per-parameter coverage %, group filter, scope toggle (whole/view/selection)
/// honoured at export time, profile import from network folder.
/// </summary>
public partial class PowerBiExportWindow : Window
{
    private readonly Document _doc;
    private readonly ParameterDiscoveryService _discovery = new();

    // Step 1
    private readonly ObservableCollection<CategoryRow> _allCategories = new();
    private readonly ObservableCollection<CategoryRow> _filteredModelCategories = new();
    private readonly ObservableCollection<CategoryRow> _filteredAnnotationCategories = new();
    private readonly ObservableCollection<CategoryRow> _filteredAnalyticalCategories = new();
    private readonly ObservableCollection<CategoryRow> _filteredOtherCategories = new();
    private readonly ObservableCollection<ScheduleRow> _allSchedules = new();
    private readonly ObservableCollection<ScheduleRow> _filteredSchedules = new();

    // Step 2 — dual-pane
    private readonly List<ParameterRow> _allParameters = new();           // master list
    private readonly ObservableCollection<ParameterRow> _availableParams = new();
    private readonly ObservableCollection<ParameterRow> _selectedParams = new();

    private int _currentStep = 1;
    private CancellationTokenSource? _discoveryCts;

    /// <summary>
    /// Optional explicit column mapping. When non-empty it overrides the
    /// dual-pane parameter selection at export time.
    /// </summary>
    private List<ColumnMapping> _columnMappings = new();

    private enum SourceMode { Categories, Schedules }
    private enum Scope { WholeModel, ActiveView, Selection }

    private SourceMode _mode = SourceMode.Categories;
    private Scope _scope = Scope.WholeModel;

    public PowerBiExportWindow(Document doc)
    {
        InitializeComponent();
        _doc = doc;
        CategoryDataGrid.ItemsSource = _filteredModelCategories;
        AnnotationDataGrid.ItemsSource = _filteredAnnotationCategories;
        AnalyticalDataGrid.ItemsSource = _filteredAnalyticalCategories;
        OtherDataGrid.ItemsSource = _filteredOtherCategories;
        ScheduleDataGrid.ItemsSource = _filteredSchedules;
        AvailableParamsGrid.ItemsSource = _availableParams;
        SelectedParamsGrid.ItemsSource = _selectedParams;
        Loaded += OnLoaded;
        Closing += (_, _) => _discoveryCts?.Cancel();
    }

    private static readonly string DebugLogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".revitcortex", "powerbi_debug.log");

    private static void DebugLog(string msg)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(DebugLogPath)!;
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(DebugLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch { /* never throw from logger */ }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Revit DB API must run on the main thread. The wizard is opened from
        // an IExternalCommand so we ARE on the main thread here — call directly.
        DebugLog("=== OnLoaded START ===");
        DebugLog($"Document: title='{_doc?.Title}' isFamily={_doc?.IsFamilyDocument} pathName='{_doc?.PathName}'");
        SetStatus("Sto analizzando il modello…");
        int catCount = -1, schCount = -1;
        try
        {
            try { OutputFolderBox.Text = SuggestDefaultOutputFolder(); } catch (Exception ofex) { DebugLog($"OutputFolder set failed: {ofex.Message}"); }

            DebugLog("Calling DiscoverCategories…");
            List<CategoryInfo> categories = new();
            try
            {
                categories = _discovery.DiscoverCategories(_doc, CancellationToken.None);
                catCount = categories.Count;
                DebugLog($"DiscoverCategories OK: count={catCount}");
                if (catCount > 0)
                {
                    var sample = string.Join(", ", categories.Take(5).Select(c => $"{c.OstCode}({c.InstanceCount})"));
                    DebugLog($"  sample: {sample}");
                }
            }
            catch (Exception ex)
            {
                DebugLog($"DiscoverCategories FAILED: {ex.GetType().Name}: {ex.Message}");
                DebugLog($"  StackTrace: {ex.StackTrace}");
                SetStatus($"Discovery categorie fallita: {ex.GetType().Name}: {ex.Message}");
                ShowErrorDialog("Discovery categorie fallita", ex);
                return;
            }

            DebugLog("Calling DiscoverSchedules…");
            List<ScheduleInfo> schedules = new();
            try
            {
                schedules = _discovery.DiscoverSchedules(_doc, CancellationToken.None);
                schCount = schedules.Count;
                DebugLog($"DiscoverSchedules OK: count={schCount}");
            }
            catch (Exception ex)
            {
                DebugLog($"DiscoverSchedules FAILED (non-blocking): {ex.GetType().Name}: {ex.Message}");
            }

            DebugLog($"Populating ObservableCollections (cat={catCount}, sch={schCount})…");
            _allCategories.Clear();
            foreach (var cat in categories) _allCategories.Add(new CategoryRow(cat));
            _allSchedules.Clear();
            foreach (var sch in schedules) _allSchedules.Add(new ScheduleRow(sch));

            DebugLog("Calling ApplyCategoryFilter / ApplyScheduleFilter…");
            ApplyCategoryFilter();
            ApplyScheduleFilter();
            DebugLog($"After filter: model={_filteredModelCategories.Count}, anno={_filteredAnnotationCategories.Count}, anal={_filteredAnalyticalCategories.Count}, other={_filteredOtherCategories.Count}, schedules={_filteredSchedules.Count}");

            if (catCount == 0)
                SetStatus("Nessuna categoria con istanze trovata nel modello attivo.");
            else
                SetStatus($"{catCount} categorie · {schCount} schedule · seleziona la sorgente.");
            DebugLog("=== OnLoaded END (success) ===");
        }
        catch (Exception ex)
        {
            DebugLog($"OnLoaded OUTER CATCH: {ex.GetType().Name}: {ex.Message}");
            DebugLog($"  StackTrace: {ex.StackTrace}");
            SetStatus($"Errore inatteso ({ex.GetType().Name}): {ex.Message}");
            ShowErrorDialog("Errore inatteso in OnLoaded", ex);
        }
    }

    private static void ShowErrorDialog(string title, Exception ex)
    {
        try
        {
            var td = new Autodesk.Revit.UI.TaskDialog($"Power BI Export — {title}")
            {
                MainInstruction = title,
                MainContent = $"{ex.GetType().Name}: {ex.Message}",
                ExpandedContent = ex.StackTrace ?? "",
                CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Close
            };
            td.Show();
        }
        catch { /* Never let logging swallow the original error path */ }
    }

    // ───────────────────────── Source mode + scope ─────────────────────────

    private void SourceMode_Changed(object sender, RoutedEventArgs e)
    {
        // Fired during XAML parsing for IsChecked="True" — guard until ALL referenced fields exist.
        if (ModeCategoriesRadio == null || ModeSchedulesRadio == null) return;
        if (CategoryTableBorder == null || ScheduleTableBorder == null) return;
        if (CategoryFilter == null) return;
        if (ModeSchedulesRadio.IsChecked == true)
        {
            _mode = SourceMode.Schedules;
            CategoryTableBorder.Visibility = System.Windows.Visibility.Collapsed;
            ScheduleTableBorder.Visibility = System.Windows.Visibility.Visible;
            CategoryFilter.ToolTip = "Filtra per nome schedule o categoria";
        }
        else
        {
            _mode = SourceMode.Categories;
            CategoryTableBorder.Visibility = System.Windows.Visibility.Visible;
            ScheduleTableBorder.Visibility = System.Windows.Visibility.Collapsed;
            CategoryFilter.ToolTip = "Filtra per nome o codice OST";
        }
    }

    private void Scope_Changed(object sender, RoutedEventArgs e)
    {
        // During InitializeComponent() the IsChecked="True" attribute on the
        // first radio fires Checked before the other two fields exist. Guard.
        if (ScopeWholeRadio == null || ScopeViewRadio == null || ScopeSelectionRadio == null) return;
        if (ScopeViewRadio.IsChecked == true) _scope = Scope.ActiveView;
        else if (ScopeSelectionRadio.IsChecked == true) _scope = Scope.Selection;
        else _scope = Scope.WholeModel;
    }

    // ───────────────────────── Step navigation ─────────────────────────

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == 1)
        {
            if (_mode == SourceMode.Categories)
            {
                var selected = _allCategories.Where(c => c.IsSelected).ToList();
                if (selected.Count == 0) { SetStatus("Seleziona almeno una categoria."); return; }
                GoToStep2(selected);
            }
            else
            {
                var selected = _allSchedules.Where(s => s.IsSelected).ToList();
                if (selected.Count == 0) { SetStatus("Seleziona almeno una schedule."); return; }
                _allParameters.Clear();
                _availableParams.Clear();
                _selectedParams.Clear();
                GoToStep3();
            }
        }
        else if (_currentStep == 2)
        {
            GoToStep3();
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == 3)
            GoToStep(_mode == SourceMode.Schedules ? 1 : 2);
        else if (_currentStep == 2)
            GoToStep(1);
    }

    private void GoToStep(int step)
    {
        _currentStep = step;
        Step1Panel.Visibility = step == 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        Step2Panel.Visibility = step == 2 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        Step3Panel.Visibility = step == 3 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        Step1Label.FontWeight = step == 1 ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
        Step2Label.FontWeight = step == 2 ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
        Step3Label.FontWeight = step == 3 ? System.Windows.FontWeights.SemiBold : System.Windows.FontWeights.Normal;
        var grey = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0x9E));
        Step1Label.Foreground = step == 1 ? System.Windows.Media.Brushes.Black : grey;
        Step2Label.Foreground = step == 2 ? System.Windows.Media.Brushes.Black : grey;
        Step3Label.Foreground = step == 3 ? System.Windows.Media.Brushes.Black : grey;

        BackBtn.IsEnabled = step > 1;
        NextBtn.Visibility = step < 3 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ExportBtn.Visibility = step == 3 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        Step2Label.Visibility = _mode == SourceMode.Schedules ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    }

    private void GoToStep2(List<CategoryRow> selectedCategories)
    {
        GoToStep(2);
        SetStatus("Sto analizzando i parametri…");

        var ostCodes = selectedCategories.Select(c => c.OstCode).ToList();
        bool includeType = IncludeTypeParametersBox.IsChecked == true;

        try
        {
            // Revit DB API requires the main thread; call inline.
            var parameters = _discovery.DiscoverParameters(_doc, ostCodes, includeType, sampleSize: 200);

            // Preserve user's previous "Selected" choices when reloading
            var previouslySelected = _selectedParams.Select(p => (p.Name, p.Scope)).ToHashSet();

            _allParameters.Clear();
            foreach (var p in parameters) _allParameters.Add(new ParameterRow(p));

            _selectedParams.Clear();
            _availableParams.Clear();
            foreach (var p in _allParameters)
            {
                if (previouslySelected.Contains((p.Name, p.Scope)))
                    _selectedParams.Add(p);
                else
                    _availableParams.Add(p);
            }
            ApplyParameterFilter();
            UpdateSelectedOrderIndices();
            UpdatePaneHeaders();

            SetStatus($"{parameters.Count} parametri scoperti. Sposta a destra quelli da esportare.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore discovery parametri: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void GoToStep3()
    {
        GoToStep(3);
        RefreshPreview();
    }

    // ───────────────────────── Filters ─────────────────────────

    private void CategoryFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_mode == SourceMode.Categories) ApplyCategoryFilter();
        else ApplyScheduleFilter();
    }

    private void ApplyCategoryFilter()
    {
        var q = (CategoryFilter?.Text ?? "").Trim();
        _filteredModelCategories.Clear();
        _filteredAnnotationCategories.Clear();
        _filteredAnalyticalCategories.Clear();
        _filteredOtherCategories.Clear();

        IEnumerable<CategoryRow> source = _allCategories;
        if (!string.IsNullOrEmpty(q))
            source = source.Where(c =>
                c.DisplayName.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                c.OstCode.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);

        foreach (var c in source)
        {
            switch (c.CategoryType)
            {
                case "Model": _filteredModelCategories.Add(c); break;
                case "Annotation": _filteredAnnotationCategories.Add(c); break;
                case "Analytical": _filteredAnalyticalCategories.Add(c); break;
                default: _filteredOtherCategories.Add(c); break;
            }
        }

        UpdateCategoryTabHeaders();
    }

    private void UpdateCategoryTabHeaders()
    {
        try
        {
            int totalModelSelected = _allCategories.Count(c => c.IsSelected && c.CategoryType == "Model");
            int totalAnnoSelected = _allCategories.Count(c => c.IsSelected && c.CategoryType == "Annotation");
            int totalAnalSelected = _allCategories.Count(c => c.IsSelected && c.CategoryType == "Analytical");
            int totalOtherSelected = _allCategories.Count(c =>
                c.IsSelected && c.CategoryType != "Model" && c.CategoryType != "Annotation" && c.CategoryType != "Analytical");

            ModelCategoriesTab.Header = $"Model Categories ({_filteredModelCategories.Count}{(totalModelSelected > 0 ? $" · {totalModelSelected}✓" : "")})";
            AnnotationCategoriesTab.Header = $"Annotation Categories ({_filteredAnnotationCategories.Count}{(totalAnnoSelected > 0 ? $" · {totalAnnoSelected}✓" : "")})";
            AnalyticalCategoriesTab.Header = $"Analytical Model Categories ({_filteredAnalyticalCategories.Count}{(totalAnalSelected > 0 ? $" · {totalAnalSelected}✓" : "")})";

            // "Altre" tab: show only when non-empty (should not happen after Internal filtering,
            // but kept as a safety net for unknown category types).
            if (_filteredOtherCategories.Count > 0)
            {
                OtherCategoriesTab.Visibility = System.Windows.Visibility.Visible;
                OtherCategoriesTab.Header = $"Altre ({_filteredOtherCategories.Count}{(totalOtherSelected > 0 ? $" · {totalOtherSelected}✓" : "")})";
            }
            else
            {
                OtherCategoriesTab.Visibility = System.Windows.Visibility.Collapsed;
            }
        }
        catch { /* tab elements may be null during initialize */ }
    }

    private void CategoryTab_Changed(object sender, SelectionChangedEventArgs e) { /* purely informational */ }

    private void ApplyScheduleFilter()
    {
        var q = (CategoryFilter?.Text ?? "").Trim();
        _filteredSchedules.Clear();
        IEnumerable<ScheduleRow> source = _allSchedules;
        if (!string.IsNullOrEmpty(q))
            source = source.Where(s =>
                s.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                s.CategoryName.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
        foreach (var s in source) _filteredSchedules.Add(s);
    }

    private void ParameterFilter_TextChanged(object sender, TextChangedEventArgs e) => ApplyParameterFilter();

    private void ApplyParameterFilter()
    {
        var q = (ParameterFilter?.Text ?? "").Trim();
        bool hideEmpty = HideEmptyBox?.IsChecked == true;
        _availableParams.Clear();
        var selectedKeys = _selectedParams.Select(p => (p.Name, p.Scope)).ToHashSet();
        IEnumerable<ParameterRow> source = _allParameters.Where(p => !selectedKeys.Contains((p.Name, p.Scope)));
        if (hideEmpty) source = source.Where(p => p.CoveragePercent > 0);
        if (!string.IsNullOrEmpty(q))
            source = source.Where(p =>
                p.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                p.GroupName.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0);
        foreach (var p in source) _availableParams.Add(p);
        UpdatePaneHeaders();
    }

    private void HideEmpty_Changed(object sender, RoutedEventArgs e) => ApplyParameterFilter();

    private void IncludeTypeParameters_Changed(object sender, RoutedEventArgs e)
    {
        if (_currentStep != 2) return;
        var selected = _allCategories.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0) return;
        GoToStep2(selected);
    }

    private void UpdatePaneHeaders()
    {
        if (AvailableHeader != null)
            AvailableHeader.Text = $"Disponibili ({_availableParams.Count})";
        if (SelectedHeader != null)
            SelectedHeader.Text = $"Selezionati ({_selectedParams.Count})";
    }

    private void UpdateSelectedOrderIndices()
    {
        for (int i = 0; i < _selectedParams.Count; i++)
            _selectedParams[i].OrderIndex = i + 1;
        SelectedParamsGrid.Items.Refresh();
    }

    // ───────────────────────── Dual-pane transfer buttons ─────────────────────────

    private void MoveToSelected_Click(object sender, RoutedEventArgs e)
    {
        var rows = AvailableParamsGrid.SelectedItems.Cast<ParameterRow>().ToList();
        foreach (var r in rows) MoveOneToSelected(r);
        UpdateSelectedOrderIndices();
        ApplyParameterFilter();
    }

    private void MoveAllToSelected_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in _availableParams.ToList()) MoveOneToSelected(r);
        UpdateSelectedOrderIndices();
        ApplyParameterFilter();
    }

    private void MoveToAvailable_Click(object sender, RoutedEventArgs e)
    {
        var rows = SelectedParamsGrid.SelectedItems.Cast<ParameterRow>().ToList();
        foreach (var r in rows) _selectedParams.Remove(r);
        UpdateSelectedOrderIndices();
        ApplyParameterFilter();
    }

    private void MoveAllToAvailable_Click(object sender, RoutedEventArgs e)
    {
        _selectedParams.Clear();
        UpdateSelectedOrderIndices();
        ApplyParameterFilter();
    }

    private void MoveSelectedUp_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedParamsGrid.SelectedItem as ParameterRow;
        if (sel == null) return;
        int idx = _selectedParams.IndexOf(sel);
        if (idx <= 0) return;
        _selectedParams.Move(idx, idx - 1);
        UpdateSelectedOrderIndices();
        SelectedParamsGrid.SelectedItem = sel;
    }

    private void MoveSelectedDown_Click(object sender, RoutedEventArgs e)
    {
        var sel = SelectedParamsGrid.SelectedItem as ParameterRow;
        if (sel == null) return;
        int idx = _selectedParams.IndexOf(sel);
        if (idx < 0 || idx >= _selectedParams.Count - 1) return;
        _selectedParams.Move(idx, idx + 1);
        UpdateSelectedOrderIndices();
        SelectedParamsGrid.SelectedItem = sel;
    }

    private void AvailableParams_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AvailableParamsGrid.SelectedItem is ParameterRow row)
        {
            MoveOneToSelected(row);
            UpdateSelectedOrderIndices();
            ApplyParameterFilter();
        }
    }

    private void SelectedParams_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SelectedParamsGrid.SelectedItem is ParameterRow row)
        {
            _selectedParams.Remove(row);
            UpdateSelectedOrderIndices();
            ApplyParameterFilter();
        }
    }

    private void MoveOneToSelected(ParameterRow row)
    {
        if (!_selectedParams.Contains(row))
            _selectedParams.Add(row);
    }

    // ───────────────────────── Categories/schedules row click ─────────────────────────

    private void SelectAllCategories_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == SourceMode.Categories)
        {
            var (rows, grid) = GetActiveCategoryView();
            foreach (var c in rows) c.IsSelected = true;
            grid?.Items.Refresh();
            UpdateCategoryTabHeaders();
        }
        else
        {
            foreach (var s in _filteredSchedules) s.IsSelected = true;
            ScheduleDataGrid.Items.Refresh();
        }
    }

    private void SelectNoneCategories_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == SourceMode.Categories)
        {
            var (rows, grid) = GetActiveCategoryView();
            foreach (var c in rows) c.IsSelected = false;
            grid?.Items.Refresh();
            UpdateCategoryTabHeaders();
        }
        else
        {
            foreach (var s in _filteredSchedules) s.IsSelected = false;
            ScheduleDataGrid.Items.Refresh();
        }
    }

    /// <summary>Returns the rows + DataGrid of the currently selected category tab.</summary>
    private (IEnumerable<CategoryRow> rows, DataGrid? grid) GetActiveCategoryView()
    {
        var item = CategoryTabControl?.SelectedItem;
        if (item == AnnotationCategoriesTab) return (_filteredAnnotationCategories, AnnotationDataGrid);
        if (item == AnalyticalCategoriesTab) return (_filteredAnalyticalCategories, AnalyticalDataGrid);
        if (item == OtherCategoriesTab) return (_filteredOtherCategories, OtherDataGrid);
        return (_filteredModelCategories, CategoryDataGrid);
    }

    private void CategoryRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && row.DataContext is CategoryRow ctx)
        {
            if (e.OriginalSource is System.Windows.DependencyObject src)
            {
                var cell = FindAncestor<DataGridCell>(src);
                if (cell != null && cell.Column?.DisplayIndex == 0) return;
            }
            ctx.IsSelected = !ctx.IsSelected;
            UpdateCategoryTabHeaders();
        }
    }

    private void ScheduleRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && row.DataContext is ScheduleRow ctx)
        {
            if (e.OriginalSource is System.Windows.DependencyObject src)
            {
                var cell = FindAncestor<DataGridCell>(src);
                if (cell != null && cell.Column?.DisplayIndex == 0) return;
            }
            ctx.IsSelected = !ctx.IsSelected;
        }
    }

    private void CategoryDataGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is CategoryRow ctx)
        {
            ctx.IsSelected = true;
            UpdateCategoryTabHeaders();
            Next_Click(sender, e);
        }
    }

    private void ScheduleDataGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ScheduleDataGrid.SelectedItem is ScheduleRow ctx)
        {
            ctx.IsSelected = true;
            Next_Click(sender, e);
        }
    }

    private static T? FindAncestor<T>(System.Windows.DependencyObject d) where T : System.Windows.DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    // ───────────────────────── Profiles ─────────────────────────

    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        var profiles = ProfileStore.LoadAll();
        if (profiles.Count == 0)
        {
            SetStatus("Nessun profilo salvato. Configura una selezione e salvala con 'Salva profilo'.");
            return;
        }
        var dlg = new ProfilePickerDialog(profiles) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.SelectedProfile == null) return;
        ApplyProfile(dlg.SelectedProfile);
    }

    private void ApplyProfile(PowerBiExportProfile profile)
    {
        var catSet = new HashSet<string>(profile.Categories, StringComparer.OrdinalIgnoreCase);
        foreach (var c in _allCategories) c.IsSelected = catSet.Contains(c.OstCode);
        CategoryDataGrid.Items.Refresh();
        AnnotationDataGrid.Items.Refresh();
        AnalyticalDataGrid.Items.Refresh();
        OtherDataGrid.Items.Refresh();
        UpdateCategoryTabHeaders();

        var schSet = new HashSet<long>(profile.ScheduleIds);
        foreach (var s in _allSchedules) s.IsSelected = schSet.Contains(s.ScheduleId);
        ScheduleDataGrid.Items.Refresh();

        if (profile.UseSchedules) ModeSchedulesRadio.IsChecked = true;
        else ModeCategoriesRadio.IsChecked = true;

        IncludeTypeParametersBox.IsChecked = profile.IncludeTypeParameters;
        if (!string.IsNullOrEmpty(profile.OutputFolder)) OutputFolderBox.Text = profile.OutputFolder;
        if (!string.IsNullOrEmpty(profile.FileName)) FileNameBox.Text = profile.FileName;
        OverwriteBox.IsChecked = profile.OverwriteFile;
        AutoExportBox.IsChecked = profile.AutoExportOnSave;
        _columnMappings = profile.ColumnMappings?.ToList() ?? new List<ColumnMapping>();

        // Note: parameters are re-applied when user enters step 2 (preserved via _selectedParams)
        SetStatus($"Profilo '{profile.Name}' caricato.");
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ProfileNameDialog { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.ProfileName)) return;

        var profile = BuildCurrentProfile(dlg.ProfileName);
        try
        {
            ProfileStore.Save(profile);
            SetStatus($"Profilo '{profile.Name}' salvato in {ProfileStore.GetProfilesDirectory()}");
        }
        catch (Exception ex)
        {
            SetStatus($"Salvataggio fallito: {ex.Message}");
        }
    }

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Importa profilo da file (.json)",
            Filter = "Profili RevitCortex (*.json)|*.json",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var profile = Newtonsoft.Json.JsonConvert.DeserializeObject<PowerBiExportProfile>(json);
            if (profile == null || string.IsNullOrWhiteSpace(profile.Name))
            {
                SetStatus("File non valido o privo di nome profilo.");
                return;
            }
            ProfileStore.Save(profile);
            ApplyProfile(profile);
            SetStatus($"Profilo '{profile.Name}' importato e applicato.");
        }
        catch (Exception ex)
        {
            SetStatus($"Import fallito: {ex.Message}");
        }
    }

    private void ApplyFromCsv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Seleziona il CSV da riapplicare al modello",
            Filter = "CSV (*.csv)|*.csv|Tutti i file (*.*)|*.*",
            CheckFileExists = true
        };
        if (!string.IsNullOrEmpty(OutputFolderBox?.Text) && Directory.Exists(OutputFolderBox.Text))
            dlg.InitialDirectory = OutputFolderBox.Text;
        if (dlg.ShowDialog() != true) return;

        var router = RevitCortexApp.Instance?.Router;
        if (router == null)
        {
            SetStatus("Router RevitCortex non disponibile.");
            return;
        }

        // Step 1: dryRun preview
        SetStatus("Anteprima riapplicazione (dryRun)…");
        var preview = router.Route("import_from_powerbi", new JObject
        {
            ["filePath"] = dlg.FileName,
            ["dryRun"] = true
        });
        if (preview == null || !preview.Success)
        {
            var msg = preview?.Error?.Message ?? "errore sconosciuto";
            SetStatus($"Anteprima fallita: {msg}");
            return;
        }

        var p = JObject.FromObject(preview.Data!);
        int rows = p["rows"]?.Value<int>() ?? 0;
        int writableCols = p["writableColumns"]?.Value<int>() ?? 0;
        int wouldUpdate = p["updatedCount"]?.Value<int>() ?? 0;

        // Confirm with user before committing
        var td = new Autodesk.Revit.UI.TaskDialog("Applica modifiche da CSV")
        {
            MainInstruction = $"Verranno aggiornati ~{wouldUpdate} valori di parametro",
            MainContent = $"File: {dlg.FileName}\n" +
                          $"Righe: {rows} · Colonne scrivibili: {writableCols}\n\n" +
                          "Procedere con la scrittura?",
            CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Yes | Autodesk.Revit.UI.TaskDialogCommonButtons.Cancel,
            DefaultButton = Autodesk.Revit.UI.TaskDialogResult.Yes
        };
        if (td.Show() != Autodesk.Revit.UI.TaskDialogResult.Yes)
        {
            SetStatus("Riapplicazione annullata.");
            return;
        }

        // Step 2: real write
        SetStatus("Scrivo i parametri…");
        var commit = router.Route("import_from_powerbi", new JObject
        {
            ["filePath"] = dlg.FileName,
            ["dryRun"] = false
        });
        if (commit == null || !commit.Success)
        {
            var msg = commit?.Error?.Message ?? "errore sconosciuto";
            SetStatus($"Scrittura fallita: {msg}");
            return;
        }

        var c = JObject.FromObject(commit.Data!);
        int updated = c["updatedCount"]?.Value<int>() ?? 0;
        int missing = c["missingElements"]?.Value<int>() ?? 0;
        int paramNF = c["parameterNotFound"]?.Value<int>() ?? 0;
        int readOnly = c["readOnlyHits"]?.Value<int>() ?? 0;
        int errors = c["errors"]?.Value<int>() ?? 0;
        SetStatus($"Riapplicato: {updated} aggiornati · {missing} elementi mancanti · {paramNF} param non trovati · {readOnly} read-only · {errors} errori.");
    }

    private void OpenProfilesFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = ProfileStore.GetProfilesDirectory();
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch (Exception ex)
        {
            SetStatus($"Apertura cartella fallita: {ex.Message}");
        }
    }

    private PowerBiExportProfile BuildCurrentProfile(string name)
    {
        return new PowerBiExportProfile
        {
            Name = name,
            UseSchedules = _mode == SourceMode.Schedules,
            Categories = _allCategories.Where(c => c.IsSelected).Select(c => c.OstCode).ToList(),
            ScheduleIds = _allSchedules.Where(s => s.IsSelected).Select(s => s.ScheduleId).ToList(),
            InstanceParameters = _selectedParams.Where(p => p.Scope == "Instance").Select(p => p.Name).ToList(),
            TypeParameters = _selectedParams.Where(p => p.Scope == "Type").Select(p => p.Name).ToList(),
            IncludeTypeParameters = IncludeTypeParametersBox.IsChecked == true,
            OutputFolder = OutputFolderBox.Text,
            FileName = FileNameBox.Text,
            OverwriteFile = OverwriteBox.IsChecked == true,
            AutoExportOnSave = AutoExportBox.IsChecked == true,
            ScopeMode = _scope.ToString(),
            ColumnMappings = _columnMappings.ToList()
        };
    }

    // ───────────────────────── Output ─────────────────────────

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Seleziona la cartella di output (premi Apri sulla cartella)",
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Seleziona-questa-cartella",
            Filter = "Cartella|*.this-directory"
        };
        if (!string.IsNullOrEmpty(OutputFolderBox.Text) && Directory.Exists(OutputFolderBox.Text))
            dlg.InitialDirectory = OutputFolderBox.Text;
        if (dlg.ShowDialog() == true)
        {
            var folder = Path.GetDirectoryName(dlg.FileName);
            if (!string.IsNullOrEmpty(folder)) OutputFolderBox.Text = folder!;
            RefreshPreview();
        }
    }

    private void EditMappings_Click(object sender, RoutedEventArgs e)
    {
        // If user hasn't customized yet, seed from the current selection
        if (_columnMappings.Count == 0)
        {
            _columnMappings = BuildDefaultMappingsFromSelection();
        }

        var dlg = new MappingEditorDialog(_columnMappings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _columnMappings = dlg.Result;
            SetStatus($"Mapping aggiornato: {_columnMappings.Count} colonne personalizzate.");
            RefreshPreview();
        }
    }

    /// <summary>
    /// Builds a starter mapping list from the current selection: 4 built-in
    /// fields followed by all selected parameters in their chosen order.
    /// </summary>
    private List<ColumnMapping> BuildDefaultMappingsFromSelection()
    {
        var result = new List<ColumnMapping>
        {
            new() { Source = "field", FieldName = "ElementId", Header = "ElementId" },
            new() { Source = "field", FieldName = "Category",  Header = "Category"  },
            new() { Source = "field", FieldName = "Family",    Header = "Family"    },
            new() { Source = "field", FieldName = "Type",      Header = "Type"      }
        };
        foreach (var p in _selectedParams)
        {
            result.Add(new ColumnMapping
            {
                Source = "param",
                ParameterName = p.Name,
                Scope = p.Scope,
                Header = p.Scope == "Type" ? $"[Type] {p.Name}" : p.Name
            });
        }
        return result;
    }

    private void RefreshPreview()
    {
        try
        {
            if (_mode == SourceMode.Schedules) RefreshSchedulePreview();
            else RefreshCategoryPreview();
        }
        catch (Exception ex)
        {
            SetStatus($"Errore preview: {ex.Message}");
        }
    }

    private void RefreshCategoryPreview()
    {
        var instParams = _selectedParams.Where(p => p.Scope == "Instance").Select(p => p.Name).ToList();
        var typeParams = _selectedParams.Where(p => p.Scope == "Type").Select(p => p.Name).ToList();

        var headers = new List<string> { "ElementId", "Category", "Family", "Type" };
        headers.AddRange(instParams);
        if (IncludeTypeParametersBox.IsChecked == true)
            headers.AddRange(typeParams.Select(n => "[Type] " + n));

        var rows = new System.Data.DataTable();
        foreach (var h in headers) rows.Columns.Add(h);

        var ostCodes = _allCategories.Where(c => c.IsSelected).Select(c => c.OstCode).ToList();
        var elements = CollectElementsForScope(ostCodes, take: 5);
        foreach (var elem in elements)
        {
            var row = rows.NewRow();
            row[0] = GetIdValue(elem.Id);
            row[1] = elem.Category?.Name ?? "";
            var typeId = elem.GetTypeId();
            var typeElem = typeId != ElementId.InvalidElementId ? _doc.GetElement(typeId) : null;
            row[2] = (typeElem as ElementType)?.FamilyName ?? "";
            row[3] = (typeElem as ElementType)?.Name ?? "";

            int col = 4;
            foreach (var pn in instParams)
            {
                var p = elem.LookupParameter(pn);
                row[col++] = p != null ? GetParamDisplay(p) : "";
            }
            if (IncludeTypeParametersBox.IsChecked == true)
            {
                foreach (var pn in typeParams)
                {
                    var p = typeElem?.LookupParameter(pn);
                    row[col++] = p != null ? GetParamDisplay(p) : "";
                }
            }
            rows.Rows.Add(row);
        }

        PreviewGrid.ItemsSource = rows.DefaultView;

        int totalRows = CountElementsForScope(ostCodes);
        SummaryText.Text = $"Modalita Categorie · {ostCodes.Count} categorie · {instParams.Count + (IncludeTypeParametersBox.IsChecked == true ? typeParams.Count : 0)} parametri · ~{totalRows} elementi · ambito {_scope}";
    }

    private List<Element> CollectElementsForScope(List<string> ostCodes, int take)
    {
        var elems = new List<Element>();
        foreach (var ost in ostCodes)
        {
            if (elems.Count >= take) break;
            if (!Enum.TryParse<BuiltInCategory>(ost, out var bic)) continue;
            FilteredElementCollector col;
            try
            {
                col = _scope switch
                {
                    Scope.ActiveView => new FilteredElementCollector(_doc, _doc.ActiveView.Id),
                    Scope.Selection => BuildSelectionCollector(),
                    _ => new FilteredElementCollector(_doc)
                };
            }
            catch
            {
                col = new FilteredElementCollector(_doc);
            }
            elems.AddRange(col.OfCategory(bic).WhereElementIsNotElementType().Take(take - elems.Count));
        }
        return elems;
    }

    private int CountElementsForScope(List<string> ostCodes)
    {
        int total = 0;
        foreach (var ost in ostCodes)
        {
            if (!Enum.TryParse<BuiltInCategory>(ost, out var bic)) continue;
            FilteredElementCollector col;
            try
            {
                col = _scope switch
                {
                    Scope.ActiveView => new FilteredElementCollector(_doc, _doc.ActiveView.Id),
                    Scope.Selection => BuildSelectionCollector(),
                    _ => new FilteredElementCollector(_doc)
                };
            }
            catch
            {
                col = new FilteredElementCollector(_doc);
            }
            total += col.OfCategory(bic).WhereElementIsNotElementType().GetElementCount();
        }
        return total;
    }

    private FilteredElementCollector BuildSelectionCollector()
    {
        var ui = RevitCortexApp.Instance?.UiApplication;
        var sel = ui?.ActiveUIDocument?.Selection.GetElementIds();
        if (sel != null && sel.Count > 0)
            return new FilteredElementCollector(_doc, sel);
        return new FilteredElementCollector(_doc);
    }

    private void RefreshSchedulePreview()
    {
        var selected = _allSchedules.Where(s => s.IsSelected).ToList();
        if (selected.Count == 0)
        {
            PreviewGrid.ItemsSource = null;
            SummaryText.Text = "Nessuna schedule selezionata.";
            return;
        }

        var first = selected[0];
        var rows = new System.Data.DataTable();

#if REVIT2024_OR_GREATER
        var schId = new ElementId(first.ScheduleId);
#else
        var schId = new ElementId((int)first.ScheduleId);
#endif
        if (_doc.GetElement(schId) is ViewSchedule view)
        {
            try
            {
                var data = view.GetTableData();
                var body = data.GetSectionData(SectionType.Body);
                int colCount = body.NumberOfColumns;
                int rowCount = Math.Min(body.NumberOfRows, 6);

                for (int c = 0; c < colCount; c++)
                {
                    var headerText = view.GetCellText(SectionType.Header, 0, c);
                    if (string.IsNullOrEmpty(headerText)) headerText = $"Col{c + 1}";
                    rows.Columns.Add(MakeUniqueColumnName(rows, headerText));
                }
                for (int r = 1; r < rowCount; r++)
                {
                    var row = rows.NewRow();
                    for (int c = 0; c < colCount; c++)
                        row[c] = view.GetCellText(SectionType.Body, r, c);
                    rows.Rows.Add(row);
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Anteprima schedule fallita: {ex.Message}");
            }
        }

        PreviewGrid.ItemsSource = rows.DefaultView;
        var totalRows = selected.Sum(s => s.RowCount);
        SummaryText.Text = $"Modalita Schedule · {selected.Count} schedule · {totalRows} righe totali (anteprima dalla prima)";
    }

    private static string MakeUniqueColumnName(System.Data.DataTable dt, string baseName)
    {
        if (!dt.Columns.Contains(baseName)) return baseName;
        int i = 2;
        while (dt.Columns.Contains($"{baseName} ({i})")) i++;
        return $"{baseName} ({i})";
    }

    // ───────────────────────── Export ─────────────────────────

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var profile = BuildCurrentProfile("_lastExport");

        if (_mode == SourceMode.Categories && profile.Categories.Count == 0)
        {
            SetStatus("Devi selezionare almeno una categoria.");
            return;
        }
        if (_mode == SourceMode.Schedules && profile.ScheduleIds.Count == 0)
        {
            SetStatus("Devi selezionare almeno una schedule.");
            return;
        }

        SetStatus("Sto eseguendo l'export…");
        ExportBtn.IsEnabled = false;
        BackBtn.IsEnabled = false;

        try
        {
            ProfileStore.Save(profile);

            // Direct router invocation: we are already on the Revit main thread
            // and the router runs the tool synchronously. Avoids the TCP roundtrip
            // and the deadlock risk of awaiting a socket call while holding the UI thread.
            var router = RevitCortexApp.Instance?.Router;
            if (router == null)
            {
                SetStatus("Router RevitCortex non disponibile. Riavvia Revit.");
                return;
            }

            var input = new JObject
            {
                ["maxElements"] = profile.MaxElements,
                ["scopeMode"] = profile.ScopeMode
            };

            if (_scope == Scope.Selection)
            {
                var ui = RevitCortexApp.Instance?.UiApplication;
                var ids = ui?.ActiveUIDocument?.Selection.GetElementIds();
                if (ids != null && ids.Count > 0)
                    input["selectionIds"] = new JArray(ids.Select(GetIdValue).Cast<object>().ToArray());
            }
            else if (_scope == Scope.ActiveView)
            {
                input["activeViewId"] = GetIdValue(_doc.ActiveView.Id);
            }

            if (_mode == SourceMode.Schedules)
            {
                input["scheduleIds"] = new JArray(profile.ScheduleIds);
            }
            else
            {
                input["categories"] = new JArray(profile.Categories);
                input["includeTypeParameters"] = profile.IncludeTypeParameters;
                if (profile.InstanceParameters.Count > 0 || profile.TypeParameters.Count > 0)
                {
                    var allParams = profile.InstanceParameters.Concat(profile.TypeParameters);
                    input["parameterNames"] = new JArray(allParams);
                }
            }
            if (!string.IsNullOrEmpty(profile.OutputFolder)) input["outputFolder"] = profile.OutputFolder;
            if (!string.IsNullOrEmpty(profile.FileName))
            {
                var fileName = profile.OverwriteFile
                    ? profile.FileName
                    : InsertTimestamp(profile.FileName!);
                input["fileName"] = fileName;
            }

            // Pass user mappings (alias + formula) when the user customized them.
            if (profile.ColumnMappings != null && profile.ColumnMappings.Count > 0)
            {
                input["columnMappings"] = JArray.FromObject(profile.ColumnMappings);
            }

            DebugLog($"Export_Click: invoking push_to_powerbi with input keys: {string.Join(",", input.Properties().Select(p => p.Name))}");
            var result = router.Route("push_to_powerbi", input);
            DebugLog($"Export_Click: result Success={result?.Success} Error={result?.Error?.Code}");

            if (RegisterProtocolBox.IsChecked == true)
            {
                try { ProtocolHandlerRegistrar.Register(); }
                catch (Exception regEx) { DebugLog($"ProtocolHandler register failed: {regEx.Message}"); }
            }
            if (AutoExportBox.IsChecked == true) AutoExportHook.Enable(profile);
            else AutoExportHook.Disable();

            if (result == null)
            {
                SetStatus("Nessuna risposta dal router.");
                return;
            }

            if (!result.Success)
            {
                var msg = result.Error?.Message ?? "errore sconosciuto";
                var code = result.Error?.Code.ToString() ?? "Unknown";
                SetStatus($"Errore export ({code}): {msg}");
                ShowErrorDialog($"Export fallito ({code})", new Exception(msg));
                return;
            }

            // Extract file path / row count from the typed payload
            var data = result.Data;
            string? path = null;
            string? count = null;
            try
            {
                var json = JObject.FromObject(data!);
                path = json["filePath"]?.ToString() ?? json["outputFolder"]?.ToString();
                count = json["elementCount"]?.ToString() ?? json["rowCount"]?.ToString();
            }
            catch { /* fall through */ }

            SetStatus($"Export completato: {count ?? "?"} righe → {path ?? "(percorso sconosciuto)"}");
        }
        catch (Exception ex)
        {
            DebugLog($"Export_Click EXCEPTION: {ex}");
            SetStatus($"Errore: {ex.GetType().Name}: {ex.Message}");
            ShowErrorDialog("Export — eccezione inattesa", ex);
        }
        finally
        {
            ExportBtn.IsEnabled = true;
            BackBtn.IsEnabled = true;
        }
    }

    // ───────────────────────── Helpers ─────────────────────────

    private static string SuggestDefaultOutputFolder()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[]
        {
            Path.Combine(userProfile, "OneDrive - GPA Ingegneria Srl"),
            Path.Combine(userProfile, "OneDrive - GPA Partners"),
            Path.Combine(userProfile, "OneDrive")
        })
        {
            if (Directory.Exists(candidate))
                return Path.Combine(candidate, "RevitCortex");
        }
        return Path.Combine(userProfile, "Documents", "RevitCortex");
    }

    private static string InsertTimestamp(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
    }

    private static string GetParamDisplay(Parameter p)
    {
        if (!p.HasValue) return "";
        return p.StorageType switch
        {
            StorageType.String => p.AsString() ?? "",
            StorageType.Integer => p.AsInteger().ToString(),
            StorageType.Double => p.AsValueString() ?? p.AsDouble().ToString("F4"),
            StorageType.ElementId => p.AsValueString() ?? p.AsElementId().ToString(),
            _ => ""
        };
    }

    private static long GetIdValue(ElementId id)
    {
#if REVIT2024_OR_GREATER
        return id.Value;
#else
        return id.IntegerValue;
#endif
    }

    private void SetStatus(string text) => StatusText.Text = text;

    // ───────────────────────── View-model rows ─────────────────────────

    // NOTE: must be public (or at least non-private) so WPF data-binding can
    // reflect the property accessors. Private nested types compile fine but
    // the binding engine silently fails to read DisplayName/OstCode/etc.,
    // leaving the DataGrid with rows that have no visible content.
    public class CategoryRow : ViewRow
    {
        public string DisplayName { get; }
        public string OstCode { get; }
        public int InstanceCount { get; }
        public string CategoryType { get; }
        public CategoryRow(CategoryInfo info)
        {
            DisplayName = info.DisplayName;
            OstCode = info.OstCode;
            InstanceCount = info.InstanceCount;
            CategoryType = info.CategoryType;
        }
    }

    public class ScheduleRow : ViewRow
    {
        public long ScheduleId { get; }
        public string Name { get; }
        public string CategoryName { get; }
        public int ColumnCount { get; }
        public int RowCount { get; }
        public ScheduleRow(ScheduleInfo info)
        {
            ScheduleId = info.ScheduleId;
            Name = info.Name;
            CategoryName = info.CategoryName;
            ColumnCount = info.ColumnCount;
            RowCount = info.RowCount;
        }
    }

    public class ParameterRow : ViewRow
    {
        public string Name { get; }
        public string Scope { get; }
        public string GroupName { get; }
        public int CoveragePercent { get; }
        public bool IsReadOnly { get; }
        public bool IsShared { get; }
        public int OrderIndex { get; set; }
        public ParameterRow(ParameterInfo info)
        {
            Name = info.Name;
            Scope = info.Scope;
            GroupName = info.GroupName;
            CoveragePercent = info.CoveragePercent;
            IsReadOnly = info.IsReadOnly;
            IsShared = info.IsShared;
        }
    }

    public abstract class ViewRow : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}
