# Power BI Export — Settings Panel Specification

**Status:** Implemented, ready for review/commit
**Date:** 2026-05-12
**Plugin version target:** 1.0.24
**Files:**
- `src/RevitCortex.Plugin/PowerBi/PowerBiExportWindow.xaml`
- `src/RevitCortex.Plugin/PowerBi/PowerBiExportWindow.xaml.cs`
- `src/RevitCortex.Plugin/PowerBi/ColumnTypeMapping.cs` (new — schema mapping POCO)
- `src/RevitCortex.Plugin/PowerBi/PowerBiExportProfile.cs` (profile fields)
- `src/RevitCortex.Plugin/PowerBi/ParameterDiscoveryService.cs` (consumer)
- `src/RevitCortex.Tools/Elements/PushToPowerBiTool.cs` (server-side export, downstream)
- `src/RevitCortex.Tools/LinkedFiles/GetCoordinationModelsTool.cs` (R26 build fix as side effect)

**Removed this session:**
- `MappingEditorDialog.xaml(.cs)` — old column-aliasing dialog, removed entirely (replaced by Schema Mapping below)
- `ColumnMapping.cs` — old alias/formula POCO, removed
- Old "Mappa colonne…" button + `EditMappings_Click` handler + helpers (`BuildDefaultMappingsFromSelection`, `BuildDefaultMappingsFromSchedules`)
- "Applica da CSV…" footer button + `ApplyFromCsv_Click` handler (server `import_from_powerbi` tool still available for AI clients via MCP)
- "Ambito:" label and `CategoryFilter` search box (UI simplification per user request)

---

## 1. Purpose

A user-facing wizard for configuring a Power BI CSV export from a Revit model. SheetLink-inspired UX, but folded into a single in-place pane to reduce step-count fatigue. Two source modes (Categories+Parameters, Schedules) live in the same window, switched via radio buttons. Output settings live in a separate Step 3.

The goal of the recent refactor (this session) was:

1. Merge what used to be `Step 1 = pick categories` + `Step 2 = pick parameters` into a single Step 1 with a 3-pane SheetLink-style layout (categories | available params | selected params).
2. Make a single click on a category/schedule row toggle the checkbox directly (no WPF two-click edit dance).
3. Make schedule mode honest about the fact that the server writes N files (one per schedule), not one.

## 2. Window layout

```
┌─────────────────────────────────────────────────────────────────────────────────────┐
│ Step indicator: ① Categorie         ③ Output                  (Step 2 hidden)      │
├─────────────────────────────────────────────────────────────────────────────────────┤
│                                                                                     │
│  STEP 1 — Sorgente dati                                                             │
│  ○ Tutto il modello   ○ Vista attiva   ○ Selezione corrente   | ○ Schedule          │
│                                                                                     │
│  [Tutte][Nessuna][Profili…]                 | [ param filter ] [□ Tipo] [□ Vuoti]   │
│                                                                                     │
│  ┌────────────┬─────────────────────┬─────┬─────────────────────┐                   │
│  │            │                     │  ▶  │                     │                   │
│  │  Tabs:     │  Available          │ ▶▶  │  Selected           │                   │
│  │  Model     │  parameters         │     │  parameters         │                   │
│  │  Annot.    │  (color-coded)      │  ◀  │  (ordered)          │                   │
│  │  Analyt.   │                     │ ◀◀  │                     │                   │
│  │  Altre*    │                     │     │                     │                   │
│  │            │                     │  ↑  │                     │                   │
│  │  ✓ Casew.  │  Width Family Cov.  │  ↓  │  # Param Group      │                   │
│  │  ☐ Doors   │  ◆ Length 87%       │     │  1 Family Generic   │                   │
│  │  ...       │  ...                │     │  2 Mark   Identity  │                   │
│  └────────────┴─────────────────────┴─────┴─────────────────────┘                   │
│       280px              *              auto             *                          │
│                                                                                     │
├─────────────────────────────────────────────────────────────────────────────────────┤
│ [Salva profilo…][Importa…][Apri cartella][Applica da CSV…] status text   [◀][Avanti▶]│
└─────────────────────────────────────────────────────────────────────────────────────┘
```

In **schedule mode** the middle/right param panes are replaced by a split pane:

```
┌────────────────────────────────┬────────────────────────────────┐
│  Schedule list                 │  Columns preview (clicked one) │
│  ✓ Abaco delle finiture        │  ▢ Numero        Instance      │
│  ☐ Abaco dei locali            │  ▢ Nome          Instance      │
│  ...                           │  ▢ Area          Read-only     │
└────────────────────────────────┴────────────────────────────────┘
```

Step 3 (Output) varies per mode — see §6.

## 3. State model

```csharp
private enum SourceMode { Categories, Schedules }
private enum Scope      { WholeModel, ActiveView, Selection }

private SourceMode _mode  = SourceMode.Categories;
private Scope      _scope = Scope.WholeModel;
private int        _currentStep = 1;
```

Both `_mode` and `_scope` are driven by a **single radio-button group** named `SourceScope`. The five radios are mutually exclusive:

- `ScopeWholeRadio` → `_mode=Categories, _scope=WholeModel`
- `ScopeViewRadio` → `_mode=Categories, _scope=ActiveView`
- `ScopeSelectionRadio` → `_mode=Categories, _scope=Selection`
- `ModeSchedulesRadio` → `_mode=Schedules, _scope=WholeModel`

Single `SourceScope_Changed` handler covers all four. Guards on null at the top because Checked fires during XAML init before all fields are bound.

## 4. Data stores

All five DataGrids are bound to `ObservableCollection<>` filtered views over master lists. The master lists are the only source of truth — the filtered views are derived.

| Master                                        | Filtered views                                                                                                                                                  | DataGrid                                                                                          |
|-----------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------|
| `_allCategories : ObservableCollection<CategoryRow>` | `_filteredModelCategories` <br> `_filteredAnnotationCategories` <br> `_filteredAnalyticalCategories` <br> `_filteredOtherCategories`                            | `CategoryDataGrid` <br> `AnnotationDataGrid` <br> `AnalyticalDataGrid` <br> `OtherDataGrid` |
| `_allSchedules : ObservableCollection<ScheduleRow>`  | `_filteredSchedules`                                                                                                                                            | `ScheduleDataGrid`                                                                                |
| n/a                                           | `_scheduleFields : ObservableCollection<ScheduleFieldRow>`                                                                                                      | `ScheduleFieldsGrid` (Step 1 right pane in schedule mode)                                         |
| `_allParameters : List<ParameterRow>`         | `_availableParams : ObservableCollection<ParameterRow>` <br> `_selectedParams : ObservableCollection<ParameterRow>`                                              | `AvailableParamsGrid` <br> `SelectedParamsGrid`                                                   |

**Critical invariant:** the filtered category collections (`_filteredModelCategories` et al.) hold the *same* `CategoryRow` instances that live in `_allCategories`. Toggling `IsSelected` on a row in any tab mutates the same underlying object that `ScheduleParamLoad` queries via `_allCategories.Where(c => c.IsSelected)`. This is what lets Annotation and Analytical tabs work with the same 3-pane flow as Model categories without any per-tab code.

`CategoryRow` and `ScheduleRow` carry an `InScope : bool` flag set by `RefreshScopeFilter()` (see §5).

## 5. User flow per mode

### 5.1 Categories mode (default)

1. User picks a scope radio (Whole/View/Selection). `SourceScope_Changed` fires:
   - Shows `CategoryTableBorder`, hides `ScheduleTableBorder`.
   - Shows `ParamFilterPanel` (the right half of the filter bar).
   - Calls `RefreshScopeFilter()` if the model has already been discovered.
2. `RefreshScopeFilter()` does **one** `FilteredElementCollector` pass to build a `HashSet<int>` of category IDs present in scope, then flags every `CategoryRow.InScope` and `ScheduleRow.InScope` in O(1). This is the perf-critical path: with hundreds of categories, doing N individual `.Any()` queries would be slow.
3. `ApplyCategoryFilter()` rebuilds the four `_filtered*Categories` collections from `_allCategories.Where(c => c.InScope)`. No text filter — the categories list is intentionally browsed by scroll + tab partitioning, not search (decision: with ~80 categories spread across 3 tabs, the search box added clutter without much benefit).
4. User clicks rows. Each click toggles `CategoryRow.IsSelected` (see §7 for the single-click mechanism).
5. After each toggle, `ScheduleParamLoad()` (re)starts a 300 ms `DispatcherTimer`. On tick, if anything is selected, calls `LoadParametersInline()` which:
   - Calls `_discovery.DiscoverParameters(doc, ostCodes, includeType, sampleSize: 200)`.
   - Preserves the user's previously-selected params (keyed by `(Name, Scope)`) so they stay in the right pane.
   - Sorts: Instance > Type, then Group, then Name.
   - Updates pane headers and status text.
6. User moves params from Available to Selected via arrows or double-click.
7. Click "Avanti" → validates `selected.Count > 0` AND `_selectedParams.Count > 0`, then `GoToStep3()`.

The 300 ms debounce matters: without it, "Tutte"/"Nessuna" buttons or rapid clicking would fire one synchronous `DiscoverParameters` call per row.

### 5.2 Schedules mode

1. User picks `ModeSchedulesRadio`. `SourceScope_Changed`:
   - Hides `CategoryTableBorder`, shows `ScheduleTableBorder`.
   - Hides `ParamFilterPanel` (params are derived from the schedule itself).
   - Forces `_scope = WholeModel` (schedules are not scope-filterable).
2. `ScheduleDataGrid` is populated from `_filteredSchedules`. Schedules with no instances in scope are hidden via the same `InScope` flag.
3. User clicks a schedule row. `ScheduleRow_Click`:
   - Toggles `IsSelected` (the multi-select state).
   - Calls `RefreshScheduleFields(ctx)` which populates the right pane with that schedule's columns, color-coded by `ScheduleFieldType`:
     - `Instance` → green
     - `ElementType` → yellow
     - `Count` / `CalculatedValue` / anything else → red (marked `IsReadOnly`)
4. Preview pane **always reflects the schedule just clicked**, not the union of all selected schedules. This is important because the server writes one CSV per schedule (see §6.2), so a unified preview would be misleading.
5. Click "Avanti" → validates `selected.Count > 0`, then `GoToStep3()`. No param selection step.

## 6. Step 3 — Output

### 6.1 Categories mode

- `OutputFolderBox` (visible always)
- `FileNameLabel` + `FileNameBox` — user-editable, pre-filled with `SuggestFileName()` (project name sanitized + `.csv`)
- `OverwriteBox` — checkbox, controls whether the server inserts a timestamp suffix
- `AutoExportBox`, `RegisterProtocolBox` — visible always
- `ScheduleFilesPanel` — collapsed
- `PreviewGrid` — first 5 rows assembled inline via `RefreshCategoryPreview()` using the `_selectedParams` and `_scope`
- `SummaryText` — `"Modalita Categorie · N categorie · M parametri · ~K elementi · ambito X"`
- **`SchemaMappingExpander` (Advanced section, see §6.3) — visible, collapsed by default**

### 6.2 Schedules mode

- `FileNameLabel`, `FileNameBox`, `OverwriteBox` — **collapsed**. Reason: the server-side `ExportSchedules()` ignores `fileName` and always writes `schedule_<SanitizedName>.csv` per schedule, always overwriting. Showing these controls was misleading.
- `ScheduleFilesPanel` — **visible**, with:
  - Header: `"File CSV che verrà scritto:"` (1 schedule) or `"File CSV che verranno scritti (N, uno per schedule):"`
  - `ScheduleFilesList`: `ItemsControl` with one `TextBlock` per filename
  - Italic note: filenames are auto-generated from schedule names and overwritten on each export
- `SchemaMappingExpander` — **collapsed**. Reason: schedules already carry their own column type info from Revit. Schema mapping only makes sense for the discovery-mode category export.
- `PreviewGrid` — shows first 5 rows from the **first** selected schedule (`_allSchedules.First(s => s.IsSelected)`) via `RefreshSchedulePreview()`. Columns are sourced from `ViewSchedule.Definition.GetField(i)` (not from `body.NumberOfColumns`) so the preview is robust against grouped/keyed/calculated schedules; each `GetCellText` call is wrapped in try/catch.
- `SummaryText` — `"Modalita Schedule · N schedule · M righe totali (anteprima dalla prima)"`
- **Double-click on a schedule row does NOT navigate to Step 3** (intentional change: the two single clicks that compose a double-click already toggle `IsSelected` twice; the handler force-sets `IsSelected = true` and refreshes the preview, but no navigation).

### 6.3 Advanced — Schema Mapping (opt-in, categories mode only)

`SchemaMappingExpander` is an `Expander` placed below the preview area. Collapsed by default, opens via single click on the header. Contents:

- **Mode radio group `SchemaMode`**: `Auto` (default — let Power BI infer), `Suggested` (RevitCortex pre-populates types from heuristics), `Custom` (user-edited).
- **Button "Suggerisci dai parametri"**: rebuilds `_columnTypes` from the current selection + heuristics regardless of current mode (also forces mode = Suggested).
- **`ColumnTypesGrid`** (bound to `ObservableCollection<ColumnTypeMapping> _columnTypes`): three columns — `Colonna` (readonly), `Tipo Power BI` (`DataGridComboBoxColumn` with values `auto|int|number|fixed|percent|bool|date|datetime|text|duration`), `Formato (opz.)` (free text). DataGrid is `IsEnabled=false` when mode is Auto.

**Heuristics (`InferPbiTypeForParam`)** in priority order:

| Pattern (lowercase) | Mapped type |
|---|---|
| ends with ` id` / `_id`, equals `id` | `int` |
| starts with `is `, `has `, `can ` | `bool` |
| contains `cost`, `price`, `importo`, `total`, `subtotal`, `amount` | `fixed` (Currency) |
| contains `percent`, `%`; group contains `percent` | `percent` |
| ends with ` date` / `_date`; or `data` + (`creazione`/`modifica`) | `date` |
| group contains `dimension`/`constraint`/`graphic`; name contains length/area/volume/width/height/depth/angle/count (en+it) | `number` |
| default | `text` |

Built-ins are always emitted: `ElementId → int`, `Category/Family/Type → text`.

### 6.4 Server-side schema mapping (`_Raw` + `.pq`)

When `schemaMappingMode != "Auto"` and `columnTypes.Count > 0`, the server-side `PushToPowerBiTool` does two things:

1. **Companion `_Raw` columns.** For each header column that the user mapped to a numeric type (`int`, `number`, `fixed`, `percent`), the CSV gets a sibling `<col>_Raw` containing `Parameter.AsDouble()` (or `AsInteger()`) formatted with `CultureInfo.InvariantCulture`. The display column keeps the locale-formatted "1500 mm²" string for use as labels. This pattern (option B in design) preserves human readability while giving PBI a clean number to aggregate.
2. **Sidecar `.pq` file.** `<csvName>.pq` is written next to the CSV containing a complete Power Query script: `Csv.Document` + `Table.PromoteHeaders` + `Table.TransformColumnTypes`. For numeric-typed display columns, the transform types them as `type text`; the `_Raw` companion is typed with the actual Power Query type (`Int64.Type`, `type number`, `Currency.Type`, `Percentage.Type`). Auto-typed columns are omitted from the transform list so PBI keeps inferring them.

User imports in PBI via "Get Data → Blank Query → Advanced Editor" and pastes the `.pq` content, or wires the dataset to it for scheduled refresh.

## 7. Single-click selection mechanism

The naive approach — `DataGridCheckBoxColumn` — produces the well-known WPF two-click problem: first click selects the row (just visually highlights it), second click enters edit mode and toggles the bound value. Users hated it.

**Solution**, applied uniformly to all five DataGrids:

```xml
<DataGridTemplateColumn Header="✓" Width="36" IsReadOnly="True">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
            <CheckBox IsChecked="{Binding IsSelected, Mode=OneWay}"
                      IsHitTestVisible="False"
                      HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
```

- The CheckBox is visual-only (`IsHitTestVisible="False"`). It doesn't process clicks itself.
- Clicks anywhere on the row bubble up to `DataGridRow.MouseLeftButtonUp` → `CategoryRow_Click` / `ScheduleRow_Click`.
- The handler always toggles `IsSelected`. The binding is `Mode=OneWay` so when the underlying flag changes, the visual updates; no reverse path needed.

This is why the row handlers are now down to 4 lines each:

```csharp
private void CategoryRow_Click(object sender, MouseButtonEventArgs e)
{
    if (sender is DataGridRow row && row.DataContext is CategoryRow ctx)
    {
        ctx.IsSelected = !ctx.IsSelected;
        UpdateCategoryTabHeaders();
        ScheduleParamLoad();
    }
}
```

The old `FindAncestor<DataGridCell>` lookup + column-index check was removed because it's no longer needed.

## 8. Export contract (wire format)

`Export_Click` builds a `JObject` and dispatches synchronously to the in-process router (no TCP round-trip; we're already on the Revit main thread and the tool runs to completion before returning):

```json
{
  "scopeMode": "WholeModel|ActiveView|Selection",
  "maxElements": 10000,

  // ─ Scope-specific ─
  "selectionIds": [123, 456, ...],     // only when Selection
  "activeViewId": 789,                  // only when ActiveView

  // ─ Categories mode ─
  "categories": ["OST_Doors", ...],
  "includeTypeParameters": true,
  "parameterNames": ["Mark", "Family Name", "Type Name", ...],

  // ─ Schedules mode ─
  "scheduleIds": [12345, 67890, ...],

  // ─ Output ─
  "outputFolder": "C:\\Users\\...\\RevitCortex\\<DocName>",
  "fileName": "<project>.csv",          // categories only; ignored in schedule mode

  // ─ Schema mapping (opt-in) ─
  "schemaMappingMode": "Suggested|Custom",   // omitted when "Auto"
  "columnTypes": [                            // omitted when "Auto" or empty
    { "ColumnName": "ElementId", "PbiType": "int", "Format": null },
    { "ColumnName": "Area",      "PbiType": "number" },
    { "ColumnName": "Mark",      "PbiType": "text" }
  ]
}
```

The router dispatches to `push_to_powerbi` (defined in `RevitCortex.Tools/Elements/PushToPowerBiTool.cs`).

The legacy `columnMappings` field (header aliasing + `{Token}` formulas) is no longer sent by the UI but `PushToPowerBiTool` still accepts it for AI/MCP clients that want to script complex column shaping. The two features are independent — `columnMappings` reshapes the column set; `schemaMappingMode + columnTypes` types it for Power BI.

## 9. Server-side export (downstream of this panel)

`PushToPowerBiTool.Execute()` branches:

- If `scheduleIds.Count > 0` → `ExportSchedules()`:
  - Iterates each schedule ID.
  - Resolves the `ViewSchedule`, enumerates its visible (non-hidden) fields.
  - Per row: `ElementId` (forced first column) + one cell per visible field, looking up the parameter via `BuiltInParameter` (when `ParameterId` is a built-in) or `LookupParameter(heading)` (fallback for shared params).
  - Writes `schedule_<SanitizedName>.csv` per schedule. Always overwrites. Writes a single `last_refresh.json` sidecar.
  - **Does NOT use:** `fileName`, `columnMappings`, `categories`, `parameterNames`.
- Otherwise → element export:
  - Resolves `categories[]` via `CategoryResolver.ResolveToId()`.
  - Collects elements scoped by `scopeMode` + `selectionIds`/`activeViewId`.
  - Discovers parameter columns from a 100-element sample.
  - When `columnMappings` is set, uses explicit mapping mode (with `ElementId` force-prepended unless the user already mapped it). Otherwise discovery mode: `ElementId, Category, Family, Type, <instance params>, [Type] <type params>`.
  - Writes one CSV. Respects `fileName` + overwrite (timestamp inserted if `overwriteFile == false`).

## 10. Known limitations / open TODOs

1. **No batch parameter discovery for "Tutte" + many categories.** Clicking "Tutte" on Model Categories with 50+ categories triggers a single (post-debounce) `DiscoverParameters` call that samples up to 200 elements per category. For very large models this can take 1–3 seconds. Acceptable for now; could be made async with cancellation.
2. **Step 2 is a vestigial empty `Grid`.** `<Grid x:Name="Step2Panel" Visibility="Collapsed"/>` is kept only because `GoToStep(int)` still has a `step == 2` branch. The branch is dead in the new flow. Could be removed but it's harmless.
3. **`GoToStep2(List<CategoryRow>)` is dead code.** Was used by old wizard; no callers after this refactor. Safe to delete.
4. **OST_Areas appears in Model Categories tab.** It's classified as Model by Revit's `CategoryType` enum even though semantically it's a spatial element. We follow Revit's classification — same as V/G overrides. Not a bug per se, but noted in case the user is surprised.
5. **Date heuristic is conservative.** `InferPbiTypeForParam` only matches `*_date` / `* date` and Italian `data + creazione/modifica`. Won't catch many real-world date params (e.g. `Last Modified`, `CreatedAt`); user must switch to Custom mode and pick `date` manually. Conservative on purpose — false-positives in date typing break PBI import worse than text.
6. **Power Query `.pq` embeds an absolute CSV path.** When the user moves the output folder, the `.pq` breaks. Workaround: regenerate or edit the path manually. Future: emit a `Folder.Files` variant that takes the folder as a parameter.
7. **`columnMappings` (legacy alias/formula) still accepted by server.** UI no longer sends it but `PushToPowerBiTool` still supports it for MCP clients. Intentional — leaves the door open for an LLM to script arbitrary column shaping without a UI.

## 11. Files touched in this session

**Added:**
- `src/RevitCortex.Plugin/PowerBi/ColumnTypeMapping.cs` — POCO `{ ColumnName, PbiType, Format }` for the Schema Mapping pipeline.
- `docs/powerbi-export-settings-panel.md` — this document.

**Removed:**
- `src/RevitCortex.Plugin/PowerBi/MappingEditorDialog.xaml` + `.xaml.cs` — old column-aliasing dialog.
- `src/RevitCortex.Plugin/PowerBi/ColumnMapping.cs` — old alias/formula POCO.

**Modified:**
- `PowerBiExportWindow.xaml` — restructured Step 1 into 3-pane; merged scope + mode into one radio group; new Step 3 schedule-files panel; new Step 3 `SchemaMappingExpander`; converted all checkbox columns to template columns with `IsHitTestVisible="False"`; window widened to 1200px; Step 2 panel emptied; Step 2 indicator hidden; removed "Ambito:" label, `CategoryFilter` search box, "Applica da CSV…" footer button, "Mappa colonne…" button.
- `PowerBiExportWindow.xaml.cs` —
  - Added `using System.Windows.Threading;` and `DispatcherTimer _paramLoadTimer`.
  - New `ScheduleParamLoad()` (300 ms debounce) + `LoadParametersInline(List<CategoryRow>)`.
  - New `_columnTypes` observable + `SchemaMode_Changed` + `SuggestTypes_Click` + `ApplySuggestedTypes` + `InferPbiTypeForParam` (heuristics) + `SchemaModeWireValue`.
  - `SourceScope_Changed` now toggles `ParamFilterPanel.Visibility`; null guards rewritten for the removed `CategoryFilter`.
  - `GoToStep3()` branches on `_mode` to show/hide form rows vs. `ScheduleFilesPanel` and to hide `SchemaMappingExpander` in schedule mode.
  - `PopulateScheduleFilesList()` builds the file-list shown in Step 3 schedule mode.
  - `Next_Click` step 1 categories now validates `_selectedParams.Count > 0` and goes straight to Step 3.
  - `Back_Click` from Step 3 always goes to Step 1.
  - `CategoryRow_Click` / `ScheduleRow_Click` — single-click toggle; no column-index check; old `FindAncestor<>` helper removed.
  - `ScheduleDataGrid_DoubleClick` — no longer calls `Next_Click`; just force-sets `IsSelected = true` + `ApplyScheduleFilter()` + `RefreshScheduleFields()`.
  - `SelectAllCategories_Click` / `SelectNoneCategories_Click` / `IncludeTypeParameters_Changed` / `CategoryDataGrid_DoubleClick` — all updated to trigger param loading.
  - `ApplyCategoryFilter` / `ApplyScheduleFilter` — dropped the text-filter codepath.
  - `RefreshSchedulePreview` — rewritten to use `view.Definition.GetFieldCount()` + visible fields, with per-cell try/catch.
  - `ApplyProfile` / `BuildCurrentProfile` — restore/save the new `SchemaMappingMode` + `ColumnTypes` fields.
  - `Export_Click` — wire `schemaMappingMode` + `columnTypes` when non-Auto; removed the `columnMappings` send.
  - Removed: `EditMappings_Click`, `BuildDefaultMappingsFromSelection`, `BuildDefaultMappingsFromSchedules`, `ApplyFromCsv_Click`, `CategoryFilter_TextChanged`, `FindAncestor<T>`, `_columnMappings` field.
- `PowerBiExportProfile.cs` — added `SchemaMappingMode` + `ColumnTypes`; removed `ColumnMappings`.
- `src/RevitCortex.Tools/Elements/PushToPowerBiTool.cs` —
  - `WriteCsvAtomic` (`.tmp` → rename) replaces direct `File.WriteAllText` for both element and schedule CSVs.
  - `GetParamDisplayValue` numeric fallbacks now use `CultureInfo.InvariantCulture`.
  - New `GetParamRawValue` for `_Raw` companion columns (invariant culture).
  - New `ParseColumnTypes`, `IsNumericMapped`, `MapToPowerQueryType`, `WritePowerQuerySidecar`, `EscapeForPq`.
  - Discovery mode header/row generation now emits `<col>_Raw` companions for numeric-typed columns and writes a sibling `.pq` file when `schemaMappingMode != "Auto"`.
- `src/RevitCortex.Tools/LinkedFiles/GetCoordinationModelsTool.cs` — preprocessor guards changed from `REVIT2026_OR_GREATER` to `REVIT2027_OR_GREATER` (the API was added in R27, not R26). Unblocks R26 Tools build.

## 12. Manual test checklist (for the reviewer)

Run in Revit 2026 with a non-trivial model loaded.

### Categories mode

- [ ] Open the Power BI Export wizard. Header shows "① Categorie" and "③ Output" (no Step 2 label).
- [ ] Default radio is "Tutto il modello". The three category tabs (Model / Annotation / Analytical) show counts in their headers.
- [ ] Click a category row (e.g. Casework, Doors). **Single click** must put the check on. The middle pane fills with parameters after ~300 ms.
- [ ] Click another category. Parameters reload, but anything you already moved to "Selezionati" stays put.
- [ ] Switch to "Vista attiva". The category list collapses to only categories with elements in the active view. Same for "Selezione corrente" (use a real Revit selection first).
- [ ] Use "Tutte" / "Nessuna". The DataGrid checkboxes flip in a single visual update; the param list reloads once after the debounce.
- [ ] Toggle "Includi parametri di tipo". The param list reloads with `[Type] *` entries.
- [ ] Toggle "Nascondi sempre vuoti". The 0% coverage params disappear from Available.
- [ ] Move a few params to "Selezionati" via ▶, ▶▶, double-click. Use ↑/↓ to reorder.
- [ ] Click "Avanti" with 0 params selected → status message asks for a param. With ≥1 selected → goes to Step 3.
- [ ] In Step 3: filename pre-filled, Sovrascrivi visible, "Mappa colonne…" visible, preview grid populated.
- [ ] Click "Indietro". Should return to Step 1 with all category and param selections preserved.

### Annotation Categories

- [ ] Same flow as Model Categories but on the Annotation tab. Door Tags / Room Tags etc. should expose tag-specific params (e.g. shared params used by tag labels).
- [ ] Mixed selection (Doors from Model + Door Tags from Annotation) should merge parameters into the Available list. Verify this is the desired behavior — SheetLink shows the same merge.

### Schedules mode

- [ ] Click the "Schedule" radio (rightmost). Layout swaps to schedule list + columns preview. ParamFilterPanel hides.
- [ ] Click a schedule. Right pane fills with that schedule's columns, color-coded.
- [ ] Click a different schedule. Preview updates. The previous one stays checked (multi-select).
- [ ] Single-click toggle: the checkbox must flip on first click, like in category tabs.
- [ ] **Double-click a schedule. Must stay on Step 1** (no auto-navigation to Output). Selection stays on.
- [ ] Pick a schedule that previously caused "column number invalid" (grouped/keyed schedules). The preview must populate without errors (cells may be empty but no toast).
- [ ] Select 2-3 schedules → "Avanti".
- [ ] Step 3: FileName / Overwrite / SchemaMappingExpander all hidden. `ScheduleFilesPanel` shows the list of `schedule_<Name>.csv` filenames. Preview grid shows data from the first selected schedule.
- [ ] Click "Esporta". Output folder should contain one CSV per selected schedule plus `last_refresh.json`.

### Schema mapping (categories mode, opt-in)

- [ ] In Step 3 (categories), `SchemaMappingExpander` is visible and collapsed. Open it.
- [ ] Default mode is Auto. DataGrid is disabled (grayed out). Exporting does NOT produce a `.pq` file or any `_Raw` columns.
- [ ] Click "Suggerisci dai parametri". Mode flips to Suggested. DataGrid populates with `ElementId/Category/Family/Type` + every selected param. `ElementId` → int; numeric params → number; text-like → text.
- [ ] Edit a row to `fixed` or `percent`. Export. Open the CSV: that column has a `_Raw` companion with invariant-culture number (e.g. `21.5`, not `21,5` or `21 SF`).
- [ ] Open the generated `.pq` next to the CSV. Should contain a `Table.TransformColumnTypes(Promoted, {...})` with `Int64.Type`/`type number`/`Currency.Type` etc. for each typed column.
- [ ] In Power BI Desktop: Get Data → Blank Query → Advanced Editor → paste `.pq` content. Verify numeric columns import as Whole Number / Decimal Number, not Text.
- [ ] Switch mode back to Auto. Export. No `.pq` file, no `_Raw` columns.
- [ ] Save profile in Suggested mode. Reload from another session: mode + types restore correctly.

### Profiles

- [ ] Save a profile from category mode → load it from another session → verify radios, categories, params, and output settings restore.
- [ ] Save a profile from schedule mode → load it → verify `ModeSchedulesRadio` becomes checked and schedule selection restores.

### Regressions to watch for

- [ ] Window opens centered, 1200×680. No clipping at 1280×800 displays.
- [ ] Status text doesn't flicker when scope filter recomputes (it should set once per filter pass, not per row).
- [ ] The status bar at the bottom never goes blank.
- [ ] Closing the window during param discovery doesn't throw (the cancellation token + debounce timer should both be safe).
- [ ] Auto-export hook continues to work after a successful export.

## 13. Out of scope this session

- "Spatial" / "Elements" / "Preview/Edit" tabs (SheetLink has them; we explicitly skip).
- Reworking the server-side `ExportSchedules()` to honor `columnMappings`.
- Async param discovery with progress bar.
- Profile management UX (the "Profili…" button currently opens a list dialog — no changes).
