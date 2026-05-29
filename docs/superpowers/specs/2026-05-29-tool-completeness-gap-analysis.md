# RevitCortex — Tool Completeness Gap Analysis

**Date:** 2026-05-29
**Goal:** Complete the 215 existing `ICortexTool` implementations by adding the operations (actions/parameters) their primary Revit API class offers but they don't yet expose. NOT new tools — only filling out existing tools' domains.

**Method:** Four parallel audits, one per folder group, each comparing every tool's current operations against its primary Revit API surface. Reference for "complete": `ManageGlobalParametersTool` (10 actions covering the full `GlobalParametersManager` surface).

**Scope of inventory:** 215 `ICortexTool` impls; 173 exposed via MCP. Folders: Elements (51), Project (40), Views (12), Sheets (5), Annotations (7), LinkedFiles (11), IFC (20), Interop (1), Meta (3).

---

## Category 0 — LATENT BUGS (operation advertised but not implemented)

These are NOT gaps — they are correctness defects where the tool's `Description` or a parsed parameter promises behavior the code never performs. **Highest priority** because they actively mislead the LLM/user.

| # | Tool | Defect | File |
|---|------|--------|------|
| B1 | `modify_schedule` | Description claims "set/clear filters" but action switch has no `set_filter`/`clear_filter` case | `Project/ModifyScheduleTool.cs` |
| B2 | `batch_export` | Description mentions PDF export but `format` switch never handles PDF (falls to "unsupported") | `Project/BatchExportTool.cs` |
| B3 | `tag_walls` | `useLeader` is parsed but never applied to the created `IndependentTag` (always leader=false) | `Annotations/TagWallsTool.cs` |
| B4 | `measure_between_elements` | `closest_points` mode documented but silently returns bounding-box center | `Elements/MeasureBetweenElementsTool.cs` |
| B5 | `align_viewports` | `alignMode` ("placement"/"model") parsed but ignored — only box-center copy implemented | `Sheets/AlignViewportsTool.cs` |
| B6 | `create_line_based_element` | beam `baseOffset` computed but never applied to the created instance | `Elements/CreateLineBasedElementTool.cs` |
| B7 | `create_view` | Description advertises "elevation" view but switch has no elevation/drafting case → silent default error | `Views/CreateViewTool.cs` |
| B8 | `create_dimensions` | point-to-point branch silently drops `dimensionStyleId` | `Annotations/CreateDimensionsTool.cs` |
| B9 | `duplicate_sheet_with_content` | Description claims "all annotations" copied; only viewports/schedules/revisions are | `Sheets/DuplicateSheetWithContentTool.cs` |

**Doc/impl mismatches (CLAUDE.md attributes flags that don't exist):** `get_materials` (compact), `get_available_family_types` (compact), `list_schedulable_fields` (summaryOnly). Either implement the flag or correct the docs.

---

## Category 1 — HIGH IMPACT GAPS (daily BIM use)

| # | Tool | Gap | Why it matters | net48? |
|---|------|-----|----------------|--------|
| H1 | `set_element_parameters` | Writes raw `Parameter.Set(double)` — **no unit-aware path** (`SetValueString`) and **no clear/reset to empty**. User passing `3000` for a length writes 3000 **feet**, silently. | Data-integrity footgun. Single highest-value fix. Same flaw in `import_from_excel`/`import_from_powerbi`. | yes (`SetValueString` all versions) |
| H2 | `create_view` | No elevation/drafting view; no crop box (`CropBox`/`CropBoxActive`); no view-template-on-create; `View3D` no `SetOrientation`/perspective | `create_view` is core; elevation is even advertised (see B7) | yes |
| H3 | `manage_links` | Read-mostly: can reload/unload Revit links only. No remove, no reload-from-new-path, no load-new; CAD links listed but can't reload/remove | Link management is a daily BIM-manager task | yes (`ReloadFrom`/`LoadFrom` all versions) |
| H4 | `create_revision` | Cannot set `Issued`, `Visibility` (cloud+tag vs none), `NumberType` | Core revision-management fields | yes |
| H5 | `ifc_export_basic` | No `overrides`/`AddOption` dict → can't reach property-set export, 2D/active-view export, phase, site placement. Sibling `ifc_export_with_configuration` already has the pattern | IFC export toggles are routinely needed | yes |

---

## Category 2 — MEDIUM IMPACT GAPS (useful, situational)

Grouped by theme.

### 2a. "Create-only" tools that should also edit/rename existing
- `create_level`: no set-elevation / rename / toggle building-story on existing
- `create_grid`: no set-extents/datum / rename existing; no arc/multi-segment grid (`Grid.Create(Arc)`)
- `duplicate_system_type`: no rename/delete existing type

### 2b. "Create" tools that fake the associative API / ignore loops
- `create_array`: builds loose copies, not a real `ArrayElement` (`LinearArray.Create`/`RadialArray.Create`)
- `create_structural_framing_system`: loose beams, not a real `BeamSystem` (`BeamSystem.Create`)
- `create_floor` / `create_surface_based_element` / `create_filled_region`: ignore inner loops (openings) though schema implies multi-loop; roofs hardcoded flat
- `create_line_based_element`: wall is `Line`-only (no arc/profile), no location-line/justification, structural flag hardcoded false

### 2c. Filter tools — AND/single-condition only
- `ai_element_filter`: no OR (`LogicalOrFilter`), no parameter-value filter (`ElementParameterFilter`), no NOT, no level filter
- `filter_by_parameter_value`: single parameter, no multi-condition AND/OR; numeric compare is locale-fragile (display-string parse)

### 2d. Annotations — missing common options
- `create_text_note`: no leader (`AddLeader`), no vertical alignment, no rotation
- `create_dimensions`: no angular/radial/diameter/arc-length; no value override/prefix/suffix/above-below
- `place_viewport`: no viewport type, no rotation (`Viewport.Rotation`), single-view only
- `batch_create_sheets`: all viewports stacked at (0.5,0.5) — no per-view layout

### 2e. Read-only tools whose domain has obvious writes
- `get_worksets`: no create/rename/delete/open/close (`Workset.Create`, `WorksetConfiguration`), no set-active
- `get_project_info`: no write path (Author, Client `CLIENT_NAME`, Status all writable); no base/survey point

### 2f. Geometry accuracy
- `clash_detection`: bbox-overlap, not true `ElementIntersectsElementFilter` → false positives
- `get_elements_in_spatial_volume`: bbox-intersect, not room-solid containment (`Room.ClosedShell`) → over-reports

### 2g. Other medium
- `set_element_workset`: no move-by-id, can't create missing workset
- `copy_elements`: no cross-document copy (`CopyElements` overload)
- `modify_element`: rotate is Z-axis only (no arbitrary axis)
- `create_point_based_element`: no set-instance-params at creation, no face/work-plane host
- `set_material_properties`: no appearance/structural/thermal asset assignment
- `color_elements`: no reset/clear overrides
- `set_compound_structure`: no wraps / deck profile / variable-thickness flag
- `purge_unused`: only FamilySymbol + Material (native Purge spans ~20 classes)
- `manage_additional_settings`: no create/edit line/fill patterns, no per-index line weights
- `manage_project_units`: no decimal symbol/grouping/rounding/suppress-zeros/unit-symbol
- `create_view_filter`: single-rule only, override limited to fg color (no transparency/halftone/weight/cut)
- `create_color_legend`: manual overrides, not native `ColorFillScheme` (won't show in a real legend key)
- `add_linked_file`: hardcoded absolute path + all-worksets; no rotation; no overlay/attachment choice
- `reload_linked_file_from` / `ifc_reload_link`: no `Unload()`; RVT reload requires newPath (no in-place refresh)
- `get_current_view_elements`: property extraction limited to a hardcoded common-param set
- `batch_rename` / `rename_families` / `rename_views`: no regex, no case-ops/numbering; `batch_rename` renames room `Name` not `Number`
- `match_element_properties`: doesn't also match graphic overrides (true Revit "match properties" does)
- `renumber_elements`: only X+Y heuristic ordering; no spatial snake / continue-from-max

---

## Category 3 — LOW IMPACT / CONFIRMED COMPLETE

Confirmed essentially complete (gaps = "—"): `delete_element`, `delete_selection`, `delete_material`, `delete_schedule`, `set_element_phase`, `change_element_type`, `duplicate_family_type`, `get_selected_elements`, `get_element_parameters`, `get_room_openings`, `find_untagged_elements`, `find_undimensioned_elements`, `get_elements_by_unique_id`, `export_families`, `pin_unpin_link_instance`, `get_link_transform`, `get_linked_file_instances`, `get_selected_linked_elements`, `highlight_linked_element`, `show_cross_model_elements`, `cross_app_selection`, `ifc_set_family_mapping_file`, `ifc_validate_request`, `ifc_get_capabilities`, `ifc_list_export_configurations`, `ifc_get_export_configuration`, and all 9 `ifc_rebuild_*`/`ifc_analyze_*`/`ifc_compare_*`/`ifc_tag_*` specialized tools, plus all 3 Meta tools.

Low-value gaps (documented in the per-folder audits) deliberately deferred: deep family inspection (`audit_families`, `list_family_sizes` — `EditFamily` is heavy/UI-fragile per memory), `lines_per_view_count` (TCP-crash risk — do not broaden), schedule/material read tools' per-cell detail, PowerBI tools (REST API, not Revit API surface).

---

## Cross-Target Constraints (apply to every implementation)

Per CLAUDE.md, all code must compile on **net48** (R2023/R2024) AND **net8+** (R2025/R2026) AND **net10** (R2027):
- No `record` / `init` / `Dictionary.GetValueOrDefault` / `Index`/`Range` / `IAsyncEnumerable` / file-scoped types / default interface methods.
- `ElementId(long)` vs `ElementId(int)` already gated everywhere via `#if REVIT2024_OR_GREATER` — keep using `ToolHelpers.ToElementId`.
- Version-gated APIs needed by some gaps: `PDFExportOptions` (R2022+, safe for R23–R27), `RevisionNumberingSequence` (R2022+). String-rule `ParameterFilterRuleFactory` `caseSensitive` arg differs at `REVIT2025_OR_GREATER` — any new is-empty/has-value rules must gate it.
- Everything else proposed (`SetValueString`, `LinearArray.Create`, `BeamSystem.Create`, arc-wall/grid overloads, `ElementParameterFilter`, `LogicalOrFilter`, `Room.ClosedShell`, cross-doc `CopyElements`, `Workset.Create`, `ElementIntersectsElementFilter`, `TextNote.AddLeader`, `Viewport.Rotation`, angular/radial `NewDimension`) exists on all targets.

## Process Constraints (per CLAUDE.md + memory)

- **Build R23→R27 all green before every commit** (a green R25 does not guarantee R24).
- **Run tests against the .csproj**, not the solution: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"`.
- **Update docs** after every tool change: `USER_GUIDE.md`, `tool-schemas.txt` (regen via `node server/generate-tool-schemas-csharp.mjs`), `WORKFLOWS.md`/`CLAUDE.md` if applicable.
- **Destructive new actions** (delete/rename/modify) must call `session.RequestConfirmation(verb, count)` before the Transaction.
- **Read-only naming convention**: write tools are blocked in read-only mode unless prefixed `get_/list_/find_/analyze_/check_/measure_/audit_/export_`. New write actions on a `get_*`/`list_*` tool would bypass read-only mode — prefer adding writes to a non-read-prefixed tool, or split.
- TDD: each new action gets a failing test first (FakeTool/FakeAnalyzer harness in `RevitCortex.Tests`).

## Sequencing (APPROVED by user 2026-05-29)

**Decision on latent bugs (B1–B9):** *Implement the missing behavior* (not just align descriptions). Each tool will actually perform what it advertises. Where an API path is too complex/risky/not-net48-safe, fall back to description-alignment and note why.

**Decision on order:** Follow the phases below in order. Each phase = its own build (R23→R27 all green) + test (`-c "Debug R25"`) + commit cycle. User updated at end of each phase.

1. **Phase 0 — Fix latent bugs (B1–B9).** Implement the missing switch case / apply the parsed-but-ignored parameter. Smallest, highest-trust.
2. **Phase 1 — H1 unit-aware writes.** Data-integrity fix across `set_element_parameters` + Excel/PBI import (`SetValueString` path + clear/reset).
3. **Phase 2 — Remaining HIGH gaps (H2–H5).**
4. **Phase 3 — MEDIUM by theme** (2a edit-existing, 2c filters, 2d annotations, 2e read→write, 2f geometry accuracy, then the rest).
5. **Phase 4 — verify docs + full all-target build + release.**

Phases 0–2 are the 80/20. Per CLAUDE.md: one file at a time, verify build after each file before the next.

## Progress log

- **Phase 0 — DONE** (commit `280597b`): B1–B9 latent bugs implemented. All 5 targets green, 224/1/0 tests.
- **Phase 1 — DONE** (commit `0e2a949`): H1 unit-aware writes (set_element_parameters + import_from_excel; import_from_powerbi already correct) + clear. Fixed ModifySchedule contract test.
- **Phase 2 — DONE** (commit `92e163a`): H2 create_view crop+template, H3 manage_links reload_from+remove, H4 create_revision Issued/Visibility/set, H5 ifc_export_basic overrides.
- **Phase 3 — DONE** (commits `2cf3…`/`7ca0b84`/`6e856fd`/`57255b0`/`9b3ff1a`/`141411d`): MEDIUM gaps by theme — 3a read→write (manage_worksets, set_project_info), 3b filters (OR/NOT/level, multi-condition), 3c edit-existing (level/grid/type rename+delete), 3d geometry accuracy (solid clash + room ClosedShell), 3e annotations (text leader/rotation/v-align, viewport rotation/type), 3f targeted 2g (color reset, arbitrary-axis rotate). **Bonus:** fixed 5 pre-existing server-wrapper/plugin mismatches found while wiring schemas (color_elements, modify_element vectors+radians, place_viewport position, create_text_note position, filter_by_parameter_value `category` singular) — each silently dropped its inputs.
- **DEFERRED (lower value / higher risk, documented not done):** create_array & create_structural_framing_system → real ArrayElement/BeamSystem (behavioral rewrite, not a gap-fill); create_floor/create_surface_based_element/create_filled_region inner loops; create_line_based_element arc/profile walls; create_dimensions angular/radial; set_compound_structure wraps/deck; purge_unused broader classes; manage_additional_settings pattern create; manage_project_units format detail; set_material_properties asset assignment; copy_elements cross-document; create_view_filter multi-rule. These remain in the Category-2 list above for a future pass.

- **Phase 3 (historical note)**: MEDIUM gaps by theme. Order: 2e read→write (get_worksets, get_project_info), 2c filters (ai_element_filter, filter_by_parameter_value), 2a edit-existing (create_level, create_grid, duplicate_system_type), 2f geometry accuracy (clash_detection, get_elements_in_spatial_volume), 2g other, 2b associative/loops, 2d annotations. Each tool: one file, build R25+R24, then batch build all 5 + tests + commit per sub-theme.
  - **Lesson applied** (memory `feedback_plugin_build_masks_tools_errors`): after editing a Tools file, build the Tools csproj or run tests — a green Plugin build can mask a Tools compile error.
