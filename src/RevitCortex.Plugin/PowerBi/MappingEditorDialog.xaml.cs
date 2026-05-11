using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace RevitCortex.Plugin.PowerBi;

/// <summary>
/// Edits the user-defined column mapping. Each row represents one CSV column:
/// the source can be a parameter (with Instance/Type scope), a built-in field,
/// or a formula with {token} placeholders. Header is the CSV alias.
/// </summary>
public partial class MappingEditorDialog : Window
{
    private readonly ObservableCollection<MappingViewRow> _rows = new();

    public List<ColumnMapping> Result { get; private set; } = new();

    public MappingEditorDialog(IEnumerable<ColumnMapping> initial)
    {
        InitializeComponent();
        foreach (var m in initial) _rows.Add(MappingViewRow.From(m));
        Reindex();
        MappingsGrid.ItemsSource = _rows;
    }

    private void AddFormula_Click(object sender, RoutedEventArgs e)
    {
        _rows.Add(new MappingViewRow
        {
            Source = "formula",
            SourceText = "{Family} - {Type} ({Mark})",
            Header = "Etichetta"
        });
        Reindex();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        var sel = MappingsGrid.SelectedItem as MappingViewRow;
        if (sel == null) return;
        int idx = _rows.IndexOf(sel);
        if (idx <= 0) return;
        _rows.Move(idx, idx - 1);
        Reindex();
        MappingsGrid.SelectedItem = sel;
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        var sel = MappingsGrid.SelectedItem as MappingViewRow;
        if (sel == null) return;
        int idx = _rows.IndexOf(sel);
        if (idx < 0 || idx >= _rows.Count - 1) return;
        _rows.Move(idx, idx + 1);
        Reindex();
        MappingsGrid.SelectedItem = sel;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var sel = MappingsGrid.SelectedItem as MappingViewRow;
        if (sel == null) return;
        _rows.Remove(sel);
        Reindex();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result.Clear();
        foreach (var r in _rows) Result.Add(r.ToMapping());
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Reindex()
    {
        for (int i = 0; i < _rows.Count; i++) _rows[i].OrderIndex = i + 1;
        MappingsGrid?.Items.Refresh();
    }

    public class MappingViewRow : INotifyPropertyChanged
    {
        private string _source = "param";
        public string Source
        {
            get => _source;
            set { _source = value; OnPropertyChanged(nameof(Source)); }
        }

        /// <summary>
        /// Editable single-text column. Meaning depends on Source:
        ///   param   -> ParameterName
        ///   field   -> FieldName (ElementId | Category | Family | Type)
        ///   formula -> Formula string with {tokens}
        /// </summary>
        public string SourceText { get; set; } = "";

        public string Scope { get; set; } = "Instance";
        public string Header { get; set; } = "";
        public int OrderIndex { get; set; }

        public static MappingViewRow From(ColumnMapping m) => new()
        {
            Source = m.Source,
            SourceText = m.Source switch
            {
                "field" => m.FieldName,
                "formula" => m.Formula,
                _ => m.ParameterName
            },
            Scope = m.Scope,
            Header = m.Header
        };

        public ColumnMapping ToMapping()
        {
            var m = new ColumnMapping { Source = Source, Header = Header, Scope = Scope };
            switch (Source)
            {
                case "field": m.FieldName = SourceText; break;
                case "formula": m.Formula = SourceText; break;
                default: m.ParameterName = SourceText; break;
            }
            return m;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
