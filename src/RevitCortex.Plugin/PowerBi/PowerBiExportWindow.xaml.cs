using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
    private readonly ObservableCollection<ScheduleFieldRow> _scheduleFields = new();

    // Step 2 — dual-pane
    private readonly List<ParameterRow> _allParameters = new();           // master list
    private readonly ObservableCollection<ParameterRow> _availableParams = new();
    private readonly ObservableCollection<ParameterRow> _selectedParams = new();

    private int _currentStep = 1;
    private CancellationTokenSource? _discoveryCts;
    private DispatcherTimer? _paramLoadTimer;

    // Schema mapping (Step 3 Advanced section). Bound to ColumnTypesGrid.
    private readonly ObservableCollection<ColumnTypeMapping> _columnTypes = new();

    private enum SchemaMode { Auto, Suggested, Custom }
    private SchemaMode _schemaMode = SchemaMode.Auto;

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
        ScheduleFieldsGrid.ItemsSource = _scheduleFields;
        AvailableParamsGrid.ItemsSource = _availableParams;
        SelectedParamsGrid.ItemsSource = _selectedParams;
        ColumnTypesGrid.ItemsSource = _columnTypes;
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

    private void SourceScope_Changed(object sender, RoutedEventArgs e)
    {
        // Fired during XAML parsing — guard until all referenced fields exist.
        if (ScopeWholeRadio == null || ScopeViewRadio == null || ScopeSelectionRadio == null) return;
        if (ModeSchedulesRadio == null) return;
        if (CategoryTableBorder == null || ScheduleTableBorder == null) return;
        if (ParamFilterPanel == null) return;

        if (ModeSchedulesRadio.IsChecked == true)
        {
            _mode = SourceMode.Schedules;
            _scope = Scope.WholeModel;
            CategoryTableBorder.Visibility = System.Windows.Visibility.Collapsed;
            ScheduleTableBorder.Visibility = System.Windows.Visibility.Visible;
            ParamFilterPanel.Visibility = System.Windows.Visibility.Collapsed;
        }
        else
        {
            _mode = SourceMode.Categories;
            if (ScopeViewRadio.IsChecked == true) _scope = Scope.ActiveView;
            else if (ScopeSelectionRadio.IsChecked == true) _scope = Scope.Selection;
            else _scope = Scope.WholeModel;
            CategoryTableBorder.Visibility = System.Windows.Visibility.Visible;
            ScheduleTableBorder.Visibility = System.Windows.Visibility.Collapsed;
            ParamFilterPanel.Visibility = System.Windows.Visibility.Visible;
            if (_allCategories.Count > 0 || _allSchedules.Count > 0)
                RefreshScopeFilter();
        }
    }

    private void RefreshScopeFilter()
    {
        if (_scope == Scope.WholeModel)
        {
            foreach (var c in _allCategories) c.InScope = true;
            foreach (var s in _allSchedules) s.InScope = true;
        }
        else
        {
            // Single pass: collect all category ElementId integers present in scope.
            // BuildSelectionCollector now returns null when the user's selection
            // is empty — in that case we skip iteration entirely so presentIds
            // stays empty and every category gets InScope=false (correct: there
            // are zero elements selected, so no category is "in scope").
            var presentIds = new HashSet<int>();
            FilteredElementCollector? col = null;
            try
            {
                col = _scope == Scope.ActiveView
                    ? new FilteredElementCollector(_doc, _doc.ActiveView.Id)
                    : BuildSelectionCollector();
                if (col != null)
                {
                    foreach (var elem in col.WhereElementIsNotElementType())
                    {
                        if (elem.Category?.Id is { } catId)
#if REVIT2024_OR_GREATER
                            presentIds.Add((int)catId.Value);
#else
                            presentIds.Add(catId.IntegerValue);
#endif
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"RefreshScopeFilter collector failed: {ex.Message}");
            }

            // Surface "empty selection" to the user as a status message —
            // otherwise the list just goes blank with no explanation.
            if (_scope == Scope.Selection && col == null)
                SetStatus("Selezione corrente in Revit: nessun elemento selezionato. Seleziona elementi nel modello e ri-clicca 'Selezione corrente'.");

            foreach (var c in _allCategories)
            {
                c.InScope = Enum.TryParse<BuiltInCategory>(c.OstCode, out var bic)
                    && presentIds.Contains((int)bic);
            }

            foreach (var s in _allSchedules)
            {
                try
                {
#if REVIT2024_OR_GREATER
                    var schId = new ElementId(s.ScheduleId);
#else
                    var schId = new ElementId((int)s.ScheduleId);
#endif
                    if (_doc.GetElement(schId) is ViewSchedule view)
                    {
                        var catId = view.Definition.CategoryId;
                        s.InScope = catId == null || catId == ElementId.InvalidElementId
                            || presentIds.Contains(
#if REVIT2024_OR_GREATER
                                (int)catId.Value
#else
                                catId.IntegerValue
#endif
                            );
                    }
                    else s.InScope = true;
                }
                catch { s.InScope = true; }
            }
        }

        if (_mode == SourceMode.Categories) ApplyCategoryFilter();
        else ApplyScheduleFilter();
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
                if (_selectedParams.Count == 0) { SetStatus("Seleziona almeno un parametro prima di procedere."); return; }
                GoToStep3();
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
            if (_selectedParams.Count == 0)
            {
                SetStatus("Seleziona almeno un parametro prima di procedere.");
                return;
            }
            GoToStep3();
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == 3)
            GoToStep(1);
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

        Step2Label.Visibility = System.Windows.Visibility.Collapsed;
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
        if (_mode == SourceMode.Schedules)
        {
            // Server writes one schedule_<Name>.csv per selected schedule and ignores
            // the user-supplied filename. Hide the misleading FileName/Overwrite controls
            // and show the actual list of files that will be produced.
            FileNameLabel.Visibility = System.Windows.Visibility.Collapsed;
            FileNameBox.Visibility = System.Windows.Visibility.Collapsed;
            OverwriteBox.Visibility = System.Windows.Visibility.Collapsed;
            ScheduleFilesPanel.Visibility = System.Windows.Visibility.Visible;
            SchemaMappingExpander.Visibility = System.Windows.Visibility.Collapsed;
            FileNameBox.Text = "";
            PopulateScheduleFilesList();
        }
        else
        {
            FileNameLabel.Visibility = System.Windows.Visibility.Visible;
            FileNameBox.Visibility = System.Windows.Visibility.Visible;
            OverwriteBox.Visibility = System.Windows.Visibility.Visible;
            ScheduleFilesPanel.Visibility = System.Windows.Visibility.Collapsed;
            SchemaMappingExpander.Visibility = System.Windows.Visibility.Visible;
            if (string.IsNullOrWhiteSpace(FileNameBox.Text))
                FileNameBox.Text = SuggestFileName();
            // Auto-populate / sync the schema-mapping grid with the current
            // column set. Even in Auto mode this gives the user visibility
            // into what columns the CSV will have; non-auto types entered
            // previously are preserved for still-existing columns.
            SyncColumnTypesWithSelection();
        }
        RefreshPreview();
    }

    // ───────────────────────── Schema mapping (Step 3 Advanced) ─────────────────────────

    private void SchemaMode_Changed(object sender, RoutedEventArgs e)
    {
        if (SchemaModeAutoRadio == null || SchemaModeSuggestedRadio == null || SchemaModeCustomRadio == null) return;
        if (ColumnTypesGrid == null) return;

        if (SchemaModeSuggestedRadio.IsChecked == true) _schemaMode = SchemaMode.Suggested;
        else if (SchemaModeCustomRadio.IsChecked == true) _schemaMode = SchemaMode.Custom;
        else _schemaMode = SchemaMode.Auto;

        // In Auto we lock the grid (nothing to map). In Suggested/Custom we open it for edit.
        ColumnTypesGrid.IsEnabled = _schemaMode != SchemaMode.Auto;

        // Suggested implicitly auto-populates if the grid is empty.
        if (_schemaMode == SchemaMode.Suggested && _columnTypes.Count == 0)
            ApplySuggestedTypes();
    }

    private void SuggestTypes_Click(object sender, RoutedEventArgs e)
    {
        // Force Suggested mode and rebuild from current selection — overwrites
        // whatever the user had typed in Custom (intentional: it's a Suggest button).
        SchemaModeSuggestedRadio.IsChecked = true;
        ApplySuggestedTypes();
    }

    /// <summary>
    /// Rebuilds <see cref="_columnTypes"/> from the current categories+params
    /// selection, applying heuristics: built-in fields are typed by convention,
    /// instance/type params by Revit <see cref="StorageType"/> + name patterns.
    /// </summary>
    private void ApplySuggestedTypes()
    {
        _columnTypes.Clear();
        if (_mode != SourceMode.Categories) return;

        // All built-in columns the CSV always writes. Keep the type by convention.
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "ElementId",     PbiType = "int"  });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "UniqueId",      PbiType = "text" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "Category",      PbiType = "text" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "Family",        PbiType = "text" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "Type",          PbiType = "text" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "DocumentTitle", PbiType = "text" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "DocumentPath",  PbiType = "text" });
        _columnTypes.Add(new ColumnTypeMapping { ColumnName = "EpisodeId",     PbiType = "text" });

        // Skip params that duplicate built-in column names — they are filtered
        // server-side at CSV write time (the built-in column already carries
        // the value), so emitting a mapping entry for them would be a ghost.
        var builtIns = BuiltInColumnNames();
        foreach (var p in _selectedParams)
        {
            if (string.IsNullOrEmpty(p.Name)) continue;
            if (builtIns.Contains(p.Name)) continue;
            var colName = p.Scope == "Type" ? $"[Type] {p.Name}" : p.Name;
            _columnTypes.Add(new ColumnTypeMapping
            {
                ColumnName = colName,
                PbiType = InferPbiTypeForParam(p.Name, p.GroupName)
            });
        }

        ColumnTypesGrid?.Items.Refresh();
    }

    /// <summary>
    /// Single source of truth for the built-in column header set. Used by both
    /// preview dedup logic and schema-mapping generation so they always agree.
    /// Mirrors the server-side header in <c>PushToPowerBiTool</c> (v2 schema).
    /// </summary>
    private static HashSet<string> BuiltInColumnNames() => new HashSet<string>(
        new[] { "ElementId", "UniqueId", "Category", "Family", "Type", "DocumentTitle", "DocumentPath", "EpisodeId" },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the full ordered list of CSV column headers that would be
    /// emitted for the current Categories-mode selection. Same logic as the
    /// server-side discovery + dedup, so the in-UI Schema Mapping grid stays
    /// consistent with the real CSV output.
    /// </summary>
    private List<string> ComputeCurrentColumnNames()
    {
        var cols = new List<string>
        {
            "ElementId", "UniqueId", "Category", "Family", "Type", "DocumentTitle", "DocumentPath", "EpisodeId"
        };
        if (_mode != SourceMode.Categories) return cols;

        var builtIns = BuiltInColumnNames();
        foreach (var p in _selectedParams)
        {
            if (string.IsNullOrEmpty(p.Name)) continue;
            if (builtIns.Contains(p.Name)) continue;
            cols.Add(p.Scope == "Type" ? $"[Type] {p.Name}" : p.Name);
        }
        return cols;
    }

    /// <summary>
    /// Synchronizes the Schema-Mapping grid with the current column selection
    /// while PRESERVING any non-auto types the user (or the Suggested heuristic)
    /// has already set on still-existing columns. This is what makes "Auto"
    /// mode informative — the grid shows the actual columns of the upcoming
    /// CSV with <c>auto</c> as the default type — and what lets the user
    /// freely tweak categories/params without losing their typed columns.
    /// </summary>
    private void SyncColumnTypesWithSelection()
    {
        var desired = ComputeCurrentColumnNames();
        var existing = _columnTypes.ToDictionary(c => c.ColumnName, c => c, StringComparer.OrdinalIgnoreCase);

        _columnTypes.Clear();
        foreach (var col in desired)
        {
            if (existing.TryGetValue(col, out var prev))
            {
                _columnTypes.Add(prev); // preserve user/Suggested type
            }
            else
            {
                _columnTypes.Add(new ColumnTypeMapping { ColumnName = col, PbiType = "auto" });
            }
        }
        ColumnTypesGrid?.Items.Refresh();
    }

    /// <summary>
    /// Heuristics: parameter name + group → likely Power BI type. Conservative
    /// (defaults to <c>text</c> when nothing matches) to avoid wrong auto-typing.
    /// </summary>
    private static string InferPbiTypeForParam(string name, string? groupName)
    {
        var n = name?.ToLowerInvariant() ?? "";
        var g = groupName?.ToLowerInvariant() ?? "";

        // ElementId-like
        if (n.EndsWith(" id") || n.EndsWith("_id") || n == "id") return "int";

        // Booleans
        if (n.StartsWith("is ") || n.StartsWith("has ") || n.StartsWith("can ")) return "bool";

        // Currency / cost
        if (n.Contains("cost") || n.Contains("price") || n.Contains("importo")
            || n.Contains("total") || n.Contains("subtotal") || n.Contains("amount"))
            return "fixed";

        // Percentages
        if (n.Contains("percent") || n.Contains("%") || g.Contains("percent"))
            return "percent";

        // Dates (heuristic — only obvious patterns to avoid false positives)
        if (n.EndsWith(" date") || n.EndsWith("_date") || n.Contains("data") && (n.Contains("creazione") || n.Contains("modifica")))
            return "date";

        // Numeric dimensions / quantities — group-based hint
        if (g.Contains("dimension") || g.Contains("constraint") || g.Contains("graphic")
            || n.Contains("length") || n.Contains("lunghezza")
            || n.Contains("area") || n.Contains("volume")
            || n.Contains("width") || n.Contains("height") || n.Contains("depth")
            || n.Contains("altezza") || n.Contains("larghezza") || n.Contains("profondità")
            || n.Contains("angle") || n.Contains("angolo")
            || n.Contains("count") || n.Contains("number") || n.Contains("numero"))
            return "number";

        return "text";
    }

    /// <summary>
    /// Maps the radio-button enum back to the wire string consumed by the server.
    /// </summary>
    private string SchemaModeWireValue() => _schemaMode switch
    {
        SchemaMode.Suggested => "Suggested",
        SchemaMode.Custom => "Custom",
        _ => "Auto"
    };

    private void PopulateScheduleFilesList()
    {
        var selected = _allSchedules.Where(s => s.IsSelected).ToList();
        var files = selected
            .Select(s => $"schedule_{SanitizeFileName(s.Name)}.csv")
            .ToList();
        ScheduleFilesList.ItemsSource = files;
        ScheduleFilesHeader.Text = files.Count == 1
            ? "File CSV che verrà scritto:"
            : $"File CSV che verranno scritti ({files.Count}, uno per schedule):";
    }

    private string SuggestFileName()
    {
        if (_mode == SourceMode.Schedules)
        {
            var sel = _allSchedules.Where(s => s.IsSelected).ToList();
            if (sel.Count == 1) return SanitizeFileName(sel[0].Name) + ".csv";
            if (sel.Count > 1) return SanitizeFileName(sel[0].Name) + "_e_altri.csv";
        }
        var title = Path.GetFileNameWithoutExtension(_doc?.Title ?? "");
        return string.IsNullOrWhiteSpace(title) ? "elements.csv" : SanitizeFileName(title) + ".csv";
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }

    // ───────────────────────── Filters ─────────────────────────

    private void ApplyCategoryFilter()
    {
        _filteredModelCategories.Clear();
        _filteredAnnotationCategories.Clear();
        _filteredAnalyticalCategories.Clear();
        _filteredOtherCategories.Clear();

        foreach (var c in _allCategories.Where(c => c.InScope))
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
        _filteredSchedules.Clear();
        foreach (var s in _allSchedules.Where(s => s.InScope)) _filteredSchedules.Add(s);

        if (SchedulesHeader != null)
        {
            int sel = _allSchedules.Count(s => s.IsSelected);
            SchedulesHeader.Text = sel > 0
                ? $"Schedule ({_filteredSchedules.Count} · {sel}✓)"
                : $"Schedule ({_filteredSchedules.Count})";
        }
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
        if (_mode != SourceMode.Categories) return;
        var selected = _allCategories.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0) return;
        LoadParametersInline(selected);
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

    // ───────────────────────── Inline parameter loading (3-pane mode) ─────────────────────────

    private void ScheduleParamLoad()
    {
        if (_paramLoadTimer == null)
        {
            _paramLoadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _paramLoadTimer.Tick += (_, _) =>
            {
                _paramLoadTimer.Stop();
                var selected = _allCategories.Where(c => c.IsSelected).ToList();
                if (selected.Count > 0)
                    LoadParametersInline(selected);
                else
                {
                    _allParameters.Clear();
                    _availableParams.Clear();
                    _selectedParams.Clear();
                    UpdatePaneHeaders();
                    SetStatus("Seleziona una o più categorie per vedere i parametri.");
                }
            };
        }
        _paramLoadTimer.Stop();
        _paramLoadTimer.Start();
    }

    private void LoadParametersInline(List<CategoryRow> selectedCategories)
    {
        SetStatus("Sto analizzando i parametri…");
        var ostCodes = selectedCategories.Select(c => c.OstCode).ToList();
        bool includeType = IncludeTypeParametersBox.IsChecked == true;
        try
        {
            var parameters = _discovery.DiscoverParameters(_doc, ostCodes, includeType, sampleSize: 200);
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
            var catLabel = selectedCategories.Count == 1
                ? selectedCategories[0].DisplayName
                : $"{selectedCategories.Count} categorie";
            SetStatus($"{parameters.Count} parametri per {catLabel}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Errore discovery parametri: {ex.GetType().Name}: {ex.Message}");
        }
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
            ScheduleParamLoad();
        }
        else
        {
            foreach (var s in _filteredSchedules) s.IsSelected = true;
            ApplyScheduleFilter();
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
            ScheduleParamLoad();
        }
        else
        {
            foreach (var s in _filteredSchedules) s.IsSelected = false;
            ApplyScheduleFilter();
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
            ctx.IsSelected = !ctx.IsSelected;
            UpdateCategoryTabHeaders();
            ScheduleParamLoad();
        }
    }

    private void ScheduleRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && row.DataContext is ScheduleRow ctx)
        {
            ctx.IsSelected = !ctx.IsSelected;
            ApplyScheduleFilter();
            RefreshScheduleFields(ctx);
        }
    }

    private void CategoryDataGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is CategoryRow ctx)
        {
            ctx.IsSelected = true;
            UpdateCategoryTabHeaders();
            var selected = _allCategories.Where(c => c.IsSelected).ToList();
            if (selected.Count > 0) LoadParametersInline(selected);
        }
    }

    private void ScheduleDataGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Double-click should NOT navigate to Step 3. The two single clicks that compose
        // a double-click already toggle IsSelected twice (net: back to start). We just
        // force-set IsSelected = true to ensure the row ends up selected, and refresh
        // the preview pane so the right column list reflects this schedule.
        if (ScheduleDataGrid.SelectedItem is ScheduleRow ctx)
        {
            ctx.IsSelected = true;
            ApplyScheduleFilter();
            RefreshScheduleFields(ctx);
        }
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

        if (profile.UseSchedules)
        {
            ModeSchedulesRadio.IsChecked = true;
        }
        else
        {
            switch (profile.ScopeMode)
            {
                case "ActiveView": ScopeViewRadio.IsChecked = true; break;
                case "Selection": ScopeSelectionRadio.IsChecked = true; break;
                default: ScopeWholeRadio.IsChecked = true; break;
            }
        }

        IncludeTypeParametersBox.IsChecked = profile.IncludeTypeParameters;
        if (!string.IsNullOrEmpty(profile.OutputFolder)) OutputFolderBox.Text = profile.OutputFolder;
        if (!string.IsNullOrEmpty(profile.FileName)) FileNameBox.Text = profile.FileName;
        OverwriteBox.IsChecked = profile.OverwriteFile;
        AutoExportBox.IsChecked = profile.AutoExportOnSave;
        TriggerRefreshBox.IsChecked = profile.TriggerPbiRefresh;
        RefreshWorkspaceIdBox.Text = profile.RefreshWorkspaceId ?? "";
        RefreshDatasetIdBox.Text = profile.RefreshDatasetId ?? "";
        RefreshConfigPanel.Visibility = profile.TriggerPbiRefresh
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        // Restore schema mapping
        _columnTypes.Clear();
        foreach (var ct in profile.ColumnTypes ?? new List<ColumnTypeMapping>())
            _columnTypes.Add(new ColumnTypeMapping { ColumnName = ct.ColumnName, PbiType = ct.PbiType, Format = ct.Format });
        switch ((profile.SchemaMappingMode ?? "Auto").ToLowerInvariant())
        {
            case "suggested": SchemaModeSuggestedRadio.IsChecked = true; break;
            case "custom":    SchemaModeCustomRadio.IsChecked = true; break;
            default:          SchemaModeAutoRadio.IsChecked = true; break;
        }

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

    /// <summary>
    /// Opens the current Output folder in Windows Explorer. Companion to the
    /// "Sfoglia…" button: Sfoglia changes the selection (folder picker, shows
    /// folders only by Windows design); this one shows the actual file content
    /// of whatever is currently in the box — useful for verifying that a
    /// previous export produced the expected CSVs.
    /// </summary>
    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = OutputFolderBox?.Text?.Trim();
            if (string.IsNullOrEmpty(dir))
            {
                SetStatus("Imposta prima una cartella di output.");
                return;
            }
            // Create on demand: the user might have typed a path that doesn't
            // exist yet (e.g. they're planning ahead before exporting). Avoids
            // an Explorer error popup and matches "Apri profili" semantics.
            Directory.CreateDirectory(dir!);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch (Exception ex)
        {
            SetStatus($"Apertura cartella output fallita: {ex.Message}");
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
            TriggerPbiRefresh = TriggerRefreshBox.IsChecked == true,
            RefreshWorkspaceId = string.IsNullOrWhiteSpace(RefreshWorkspaceIdBox.Text) ? null : RefreshWorkspaceIdBox.Text.Trim(),
            RefreshDatasetId = string.IsNullOrWhiteSpace(RefreshDatasetIdBox.Text) ? null : RefreshDatasetIdBox.Text.Trim(),
            ScopeMode = _scope.ToString(),
            SchemaMappingMode = SchemaModeWireValue(),
            ColumnTypes = _columnTypes.Select(c => new ColumnTypeMapping
            {
                ColumnName = c.ColumnName,
                PbiType = c.PbiType,
                Format = c.Format
            }).ToList()
        };
    }

    // ───────────────────────── PBI Refresh ─────────────────────────

    private void TriggerRefreshBox_Changed(object sender, RoutedEventArgs e)
    {
        if (RefreshConfigPanel == null) return;
        RefreshConfigPanel.Visibility = TriggerRefreshBox.IsChecked == true
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    private async System.Threading.Tasks.Task FirePbiRefreshAsync(string workspaceId, string datasetId)
    {
        try
        {
            SetStatus("Trigger refresh Power BI…");
            var settings = RevitCortex.Plugin.PowerBiLive.PowerBiSettings.Load();
            var auth = new RevitCortex.Plugin.PowerBiLive.PowerBiAuthService(settings);
            var authState = await auth.TryAcquireSilentAsync();
            if (!authState.IsSignedIn || string.IsNullOrEmpty(authState.AccessToken))
            {
                SetStatus("Export completato. Refresh non triggerato: non sei autenticato a Power BI (esegui pbi_check_auth).");
                return;
            }
            using var client = new RevitCortex.Plugin.PowerBiLive.PowerBiServiceClient(authState.AccessToken!);
            var requestId = await client.TriggerRefreshAsync(workspaceId, datasetId);
            var tip = string.IsNullOrEmpty(requestId)
                ? "Refresh in coda (Pro — nessun requestId)."
                : $"Refresh avviato (requestId: {requestId}).";
            SetStatus($"Export completato. {tip} La dashboard sarà aggiornata tra ~30s.");
        }
        catch (Exception ex)
        {
            SetStatus($"Export completato. Refresh fallito: {ex.Message}");
            DebugLog($"FirePbiRefreshAsync EXCEPTION: {ex}");
        }
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

        // Built-in columns and well-known sidecar names that the CSV writes
        // unconditionally. If the user selected a parameter with one of these
        // names (e.g. "Family" or "Type"), we silently skip it for the preview
        // and CSV — the built-in column already carries the same / closely
        // related value, and DataTable would throw on a duplicate column name.
        var builtIns = BuiltInColumnNames();

        var dedupInst = new List<string>();
        var dedupType = new List<string>();
        int skipped = 0;
        foreach (var n in instParams)
        {
            if (string.IsNullOrEmpty(n)) continue;
            if (builtIns.Contains(n)) { skipped++; continue; }
            dedupInst.Add(n);
        }
        foreach (var n in typeParams)
        {
            if (string.IsNullOrEmpty(n)) continue;
            // Type params are prefixed with "[Type] " so collisions with built-ins
            // are basically impossible, but guard anyway.
            if (builtIns.Contains(n)) { skipped++; continue; }
            dedupType.Add(n);
        }

        var headers = new List<string> { "ElementId", "Category", "Family", "Type" };
        headers.AddRange(dedupInst);
        if (IncludeTypeParametersBox.IsChecked == true)
            headers.AddRange(dedupType.Select(n => "[Type] " + n));

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
            foreach (var pn in dedupInst)
            {
                var p = elem.LookupParameter(pn);
                row[col++] = p != null ? GetParamDisplay(p) : "";
            }
            if (IncludeTypeParametersBox.IsChecked == true)
            {
                foreach (var pn in dedupType)
                {
                    var p = typeElem?.LookupParameter(pn);
                    row[col++] = p != null ? GetParamDisplay(p) : "";
                }
            }
            rows.Rows.Add(row);
        }

        PreviewGrid.ItemsSource = rows.DefaultView;

        int totalRows = CountElementsForScope(ostCodes);
        var totalParams = dedupInst.Count + (IncludeTypeParametersBox.IsChecked == true ? dedupType.Count : 0);
        var skipNote = skipped > 0
            ? $" · {skipped} parametr{(skipped == 1 ? "o" : "i")} duplicat{(skipped == 1 ? "o" : "i")} con colonne built-in (saltati)"
            : "";
        SummaryText.Text = $"Modalita Categorie · {ostCodes.Count} categorie · {totalParams} parametri · ~{totalRows} elementi · ambito {_scope}{skipNote}";
    }

    private List<Element> CollectElementsForScope(List<string> ostCodes, int take)
    {
        var elems = new List<Element>();
        foreach (var ost in ostCodes)
        {
            if (elems.Count >= take) break;
            if (!Enum.TryParse<BuiltInCategory>(ost, out var bic)) continue;
            FilteredElementCollector? col;
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
            if (col == null) continue;  // Selection mode with empty selection — nothing to preview
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
            FilteredElementCollector? col;
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
            if (col == null) continue;  // Selection mode with empty selection — count stays 0
            total += col.OfCategory(bic).WhereElementIsNotElementType().GetElementCount();
        }
        return total;
    }

    /// <summary>
    /// Returns a collector over the user's current Revit selection, or
    /// <c>null</c> when the selection is empty. Callers must treat null as
    /// "no elements" — NEVER fall back to whole-model, which was the previous
    /// bug that made "Selezione corrente" show every category when nothing
    /// was actually selected.
    /// </summary>
    private FilteredElementCollector? BuildSelectionCollector()
    {
        var ui = RevitCortexApp.Instance?.UiApplication;
        var sel = ui?.ActiveUIDocument?.Selection.GetElementIds();
        if (sel != null && sel.Count > 0)
            return new FilteredElementCollector(_doc, sel);
        return null;
    }

    private void RefreshScheduleFields(ScheduleRow? target = null)
    {
        _scheduleFields.Clear();
        target ??= ScheduleDataGrid?.SelectedItem as ScheduleRow
                ?? _allSchedules.FirstOrDefault(s => s.IsSelected);

        if (target == null)
        {
            if (ScheduleFieldsHeader != null)
                ScheduleFieldsHeader.Text = "Colonne (clicca una schedule)";
            return;
        }

        try
        {
#if REVIT2024_OR_GREATER
            var schId = new ElementId(target.ScheduleId);
#else
            var schId = new ElementId((int)target.ScheduleId);
#endif
            if (_doc.GetElement(schId) is not ViewSchedule view)
            {
                if (ScheduleFieldsHeader != null)
                    ScheduleFieldsHeader.Text = "Colonne (schedule non trovata)";
                return;
            }

            var def = view.Definition;
            int count = def.GetFieldCount();
            for (int i = 0; i < count; i++)
            {
                var field = def.GetField(i);
                if (field.IsHidden) continue;
                var header = string.IsNullOrWhiteSpace(field.ColumnHeading)
                    ? field.GetName()
                    : field.ColumnHeading;
                var scope = field.FieldType == ScheduleFieldType.ElementType ? "Type" : "Instance";
                bool isRO = field.FieldType != ScheduleFieldType.Instance
                         && field.FieldType != ScheduleFieldType.ElementType;
                _scheduleFields.Add(new ScheduleFieldRow(header, scope, isRO));
            }

            if (ScheduleFieldsHeader != null)
                ScheduleFieldsHeader.Text = $"Colonne — {target.Name} ({_scheduleFields.Count})";
        }
        catch (Exception ex)
        {
            DebugLog($"RefreshScheduleFields failed: {ex.Message}");
            if (ScheduleFieldsHeader != null)
                ScheduleFieldsHeader.Text = "Colonne (errore lettura)";
        }
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
                // Build columns from the schedule's field definition (authoritative,
                // and matches the CSV export path). Then read body cells defensively
                // by trying each (row,col) pair via the body section size.
                var def = view.Definition;
                int fieldCount = def?.GetFieldCount() ?? 0;
                var visibleFieldIndices = new List<int>();
                for (int i = 0; i < fieldCount; i++)
                {
                    try
                    {
                        var f = def!.GetField(i);
                        if (f == null || f.IsHidden) continue;
                        var heading = string.IsNullOrWhiteSpace(f.ColumnHeading)
                            ? (f.GetName() ?? $"Col{i + 1}")
                            : f.ColumnHeading;
                        rows.Columns.Add(MakeUniqueColumnName(rows, heading));
                        visibleFieldIndices.Add(i);
                    }
                    catch { /* skip malformed field */ }
                }

                if (rows.Columns.Count == 0)
                {
                    SetStatus("Anteprima schedule: nessuna colonna visibile.");
                }
                else
                {
                    var body = view.GetTableData().GetSectionData(SectionType.Body);
                    int bodyCols = body?.NumberOfColumns ?? 0;
                    int rowCount = Math.Min(body?.NumberOfRows ?? 0, 6);
                    // Body cell indices align with visible-field order (Revit collapses hidden fields).
                    int previewCols = Math.Min(rows.Columns.Count, bodyCols);
                    for (int r = 1; r < rowCount; r++)
                    {
                        var row = rows.NewRow();
                        for (int c = 0; c < previewCols; c++)
                        {
                            try { row[c] = view.GetCellText(SectionType.Body, r, c); }
                            catch { row[c] = ""; }
                        }
                        rows.Rows.Add(row);
                    }
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

            // Schema mapping (opt-in): only forwarded when the user picked something
            // other than Auto. Server-side, this triggers _Raw companion columns and
            // a sidecar .pq file with explicit Table.TransformColumnTypes.
            if (!string.Equals(profile.SchemaMappingMode, "Auto", StringComparison.OrdinalIgnoreCase)
                && profile.ColumnTypes != null && profile.ColumnTypes.Count > 0)
            {
                input["schemaMappingMode"] = profile.SchemaMappingMode;
                input["columnTypes"] = JArray.FromObject(profile.ColumnTypes);
            }

            DebugLog($"Export_Click: invoking push_to_powerbi with input keys: {string.Join(",", input.Properties().Select(p => p.Name))}");
            var result = router.Route("push_to_powerbi", input);
            DebugLog($"Export_Click: result Success={result?.Success} Error={result?.Error?.Code}");

            if (RegisterProtocolBox.IsChecked == true)
            {
                try { ProtocolHandlerRegistrar.Register(); }
                catch (Exception regEx) { DebugLog($"ProtocolHandler register failed: {regEx.Message}"); }
            }
            if (AutoExportBox.IsChecked == true) AutoExportHook.Enable(profile, _doc);
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

            // Modal feedback so the user gets a clear "done" signal beyond the
            // status bar. "Apri cartella" deep-links to Explorer in the output
            // folder for quick verification.
            ShowExportSuccessDialog(count, path);

            // Fire PBI refresh in background (non-blocking — doesn't hold Revit main thread).
            if (profile.TriggerPbiRefresh
                && !string.IsNullOrWhiteSpace(profile.RefreshWorkspaceId)
                && !string.IsNullOrWhiteSpace(profile.RefreshDatasetId))
            {
                _ = FirePbiRefreshAsync(profile.RefreshWorkspaceId!, profile.RefreshDatasetId!);
            }
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

    /// <summary>
    /// Modal success TaskDialog shown after a completed export. Tells the user
    /// what was written and offers a deep-link to the output folder. Falls
    /// back to a silent no-op if Revit's TaskDialog can't be created (e.g.
    /// from unit tests). Path may point to a CSV file or, in schedule mode
    /// with multiple files, the parent folder — both are handled.
    /// </summary>
    private void ShowExportSuccessDialog(string? count, string? path)
    {
        try
        {
            string folder = "";
            if (!string.IsNullOrEmpty(path))
            {
                folder = Directory.Exists(path)
                    ? path!
                    : Path.GetDirectoryName(path) ?? "";
            }
            if (string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(OutputFolderBox?.Text))
                folder = OutputFolderBox.Text;

            string title, instruction, content;
            if (_mode == SourceMode.Schedules)
            {
                var n = _allSchedules.Count(s => s.IsSelected);
                title = "Export Power BI completato";
                instruction = $"Esportate {n} schedule";
                content = $"File generati in: {folder}";
            }
            else
            {
                title = "Export Power BI completato";
                instruction = $"Esportate {count ?? "?"} righe";
                content = $"File: {path}";
            }

            var td = new Autodesk.Revit.UI.TaskDialog(title)
            {
                MainInstruction = instruction,
                MainContent = content,
                CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Close,
                DefaultButton = Autodesk.Revit.UI.TaskDialogResult.Close
            };
            td.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink1,
                "Apri cartella", "Apri Esplora risorse nella cartella di output");

            var r = td.Show();
            if (r == Autodesk.Revit.UI.TaskDialogResult.CommandLink1 && !string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                try { System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\""); }
                catch (Exception ex) { DebugLog($"Explorer open failed: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            DebugLog($"ShowExportSuccessDialog failed (non-fatal): {ex.Message}");
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
        public bool InScope { get; set; } = true;
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
        public bool InScope { get; set; } = true;
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

    public class ScheduleFieldRow
    {
        public string Header { get; }
        public string Scope { get; }
        public bool IsReadOnly { get; }
        public ScheduleFieldRow(string header, string scope, bool isReadOnly)
        {
            Header = header;
            Scope = scope;
            IsReadOnly = isReadOnly;
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
