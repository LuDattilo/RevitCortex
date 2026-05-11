# Power BI Live — Phase 2 — Bidirectional Selection

**Date:** 2026-05-11  
**Status:** Design — pending independent review  
**Author:** RevitCortex AI session (Luigi Dattilo, GPA Ingegneria Srl)  
**Depends on:** Phase 1 spec (`2026-05-11-powerbi-live-phase1-design.md`) — fully validated

---

## 1. Scope

Phase 2 adds **bidirectional selection** between Revit and Power BI:

| Direction | Tool | Trigger |
|-----------|------|---------|
| Revit → PBI | `pbi_publish_selection` | On-demand (user asks Claude) |
| PBI → Revit | `select_from_powerbi` | On-demand (user asks Claude) |

**Explicitly out of scope:**
- Automatic `SelectionChanged` watcher (polling/event loop)
- Named selection sets / selection history
- `pbi_bind_document` (explicit binding override)
- `ElementParameters` table population (Phase 3+)

---

## 2. Architecture

No new infrastructure. Both tools follow the exact same threading contract as Phase 1:

```
Claude Desktop
    │  MCP stdio
    ▼
RevitCortex.Server  ── GET /datasets/{id}/tables/Selection/rows  ──▶  Power BI REST API
    │  TCP :27015
    ▼
RevitCortex.Plugin
    ├── Main thread:   UIDocument.Selection  (get/set ElementIds)
    └── Background:    HTTP REST via RunWithoutContext<T>
```

The `Selection` table already exists in the dataset schema (created by `pbi_publish_elements` / `pbi_create_dataset`). No schema migration needed.

---

## 3. Selection Table Schema (existing, no changes)

```
Selection
├── _SchemaVersion  String     "1.0"
├── UpdatedAtUtc    DateTime   UTC timestamp of the publish
├── ProjectId       String     ProjectDocumentKey (stable doc identifier)
├── DocumentGuid    String     doc.ProjectInformation.UniqueId
├── ElementId       Int64      Revit ElementId.Value (or .IntegerValue on R23)
├── UniqueId        String     element.UniqueId
└── SelectionSetId  String     exportRunId GUID (groups one publish batch)
```

One row per selected element. Replace semantics: each publish DELETEs all rows then POSTs the new snapshot. The `SelectionSetId` lets consumers in PBI identify which rows belong to the same selection event.

---

## 4. Tool: `pbi_publish_selection`

### 4.1 Purpose

Snapshots the currently selected elements in Revit and pushes them to the `Selection` table in the bound Power BI dataset.

### 4.2 Inputs

| Parameter | Type | Required | Default | Notes |
|-----------|------|----------|---------|-------|
| `workspaceId` | string | No | from binding | Falls back to ProjectBinding |
| `datasetId` | string | No | from binding | Falls back to binding, then name-lookup |
| `datasetName` | string | No | from binding | Used for lookup when datasetId absent |
| `clearIfEmpty` | bool | No | `false` | If `true`, DELETE rows even when nothing is selected |

### 4.3 Behavior

1. **Main thread — snapshot selection:**
   - `UIDocument uiDoc = new UIDocument(document)`
   - `ICollection<ElementId> ids = uiDoc.Selection.GetElementIds()`
   - If `ids.Count == 0 && !clearIfEmpty`: return early with `{ rowCount: 0, warning: "Nothing selected in Revit. Pass clearIfEmpty=true to explicitly clear the Selection table." }`
   - For each id: build a row dict with all 7 columns
   - `ElementId.Value` on R2024+, `ElementId.IntegerValue` on R23 (handled via `ElementIdHelper`)

2. **Background thread — HTTP publish:**
   - Resolve dataset (stale-binding detection — same pattern as Phase 1)
   - `DELETE /datasets/{id}/tables/Selection/rows`
   - `POST /datasets/{id}/tables/Selection/rows` with batch ≤ 10,000 rows

3. **Return:**
```json
{
  "success": true,
  "workspaceId": "...",
  "datasetId": "...",
  "datasetName": "...",
  "table": "Selection",
  "exportRunId": "...",
  "rowCount": 42,
  "durationMs": 310
}
```

### 4.4 Error cases

| Condition | Error code | Message |
|-----------|-----------|---------|
| Not signed in | PermissionDenied | "Not signed in to Power BI." |
| AllowExternalWrites=false | PermissionDenied | Standard gate message |
| No active document | InvalidInput | "No active Revit document." |
| workspaceId not resolvable | InvalidInput | "workspaceId is required." |
| Dataset not found (404) | ElementNotFound | "Dataset not found. Run pbi_publish_elements first." |
| Token expired (401) | PermissionDenied | "Power BI token expired during publish." |

---

## 5. Tool: `select_from_powerbi`

### 5.1 Purpose

Reads ElementIds from a Power BI table and applies them as the active Revit selection. Enables "click a visual in PBI → elements selected in Revit" via Claude as the bridge.

### 5.2 Inputs

| Parameter | Type | Required | Default | Notes |
|-----------|------|----------|---------|-------|
| `workspaceId` | string | No | from binding | |
| `datasetId` | string | No | from binding | |
| `datasetName` | string | No | from binding | |
| `sourceTable` | string | No | `"Selection"` | Which table to read from. Can be `"Elements"` for filtered selection |
| `elementIdColumn` | string | No | `"ElementId"` | Column containing the Int64 Revit ElementId |
| `maxElements` | int | No | `500` | Safety cap — prevents accidentally selecting 10k elements |

### 5.3 Behavior

1. **Background thread — read from PBI:**
   - `GET /v1.0/myorg/groups/{workspaceId}/datasets/{datasetId}/tables/{sourceTable}/rows`
   - Extract `elementIdColumn` values → `List<long>`
   - Apply `maxElements` cap (warn if truncated)

2. **Main thread — apply selection:**
   - Convert each `long` to `ElementId` (`new ElementId((int)id)` on R23/R24, `new ElementId(id)` on R25+)
   - Filter: only include ids that exist in the active document (`doc.GetElement(id) != null`)
   - `uiDoc.Selection.SetElementIds(validIds)`
   - If `validIds.Count == 0`: return warning, do not clear existing selection

3. **Return:**
```json
{
  "success": true,
  "selectedCount": 38,
  "skippedCount": 4,
  "skippedReason": "ElementIds not found in active document",
  "sourceTable": "Selection",
  "durationMs": 420,
  "tip": "Use operate_element with action='isolate' to isolate selected elements in the current view."
}
```

### 5.4 Error cases

| Condition | Error code | Message |
|-----------|-----------|---------|
| Not signed in | PermissionDenied | "Not signed in to Power BI." |
| No active document | InvalidInput | "No active Revit document." |
| workspaceId not resolvable | InvalidInput | "workspaceId is required." |
| Dataset not found (404) | ElementNotFound | "Dataset not found." |
| Table empty (0 rows) | ok (warning) | Returns selectedCount=0 with tip |
| All ids skipped (not in doc) | ok (warning) | Returns selectedCount=0, skippedCount=N |

---

## 6. Power BI REST API — Row Read

The `GET .../tables/{table}/rows` endpoint returns:

```json
{
  "value": [
    { "ElementId": 123456, "UniqueId": "...", ... },
    ...
  ]
}
```

This is already handled by `PowerBiServiceClient`. A new method `GetTableRowsAsync(workspaceId, datasetId, tableName)` returns `List<Dictionary<string, object?>>`. The caller extracts the column by key.

**Pagination:** The push dataset API returns all rows in a single response (no `$top`/`$skip` for push datasets). The `maxElements` cap is applied client-side after the read.

---

## 7. ElementId Compatibility (R23 vs R25+)

```csharp
// ElementIdHelper.cs (already exists in codebase)
// Reading: always use .Value (R24+) or .IntegerValue (R23)
// Writing: new ElementId((int)longValue) on R23/R24 (net48)
//          new ElementId(longValue)       on R25+ (net8)
```

The `select_from_powerbi` tool must handle both. Since the stored value is `Int64`, the cast to `int` is safe for Revit's actual id range (< 2^31).

---

## 8. New Files

```
src/RevitCortex.Plugin/PowerBiLive/Tools/
├── PbiPublishSelectionTool.cs    (new)
└── PbiSelectFromPbiTool.cs       (new — "select_from_powerbi")

src/RevitCortex.Plugin/PowerBiLive/
└── PowerBiServiceClient.cs       (add GetTableRowsAsync method)

src/RevitCortex.Server/Tools/
└── ElementTools.cs               (add 2 MCP wrappers)

tool-schemas.txt                  (add 2 entries)
WORKFLOWS.md                      (add Phase 2 section)
docs/USER_GUIDE.md                (update PBI Live table)
```

No changes to `PowerBiDatasetSchema.cs`, `PowerBiSettings.cs`, or `PowerBiToolHelper.cs`.

---

## 9. MCP Server Wrappers (ElementTools.cs)

```csharp
// pbi_publish_selection
[McpServerTool("pbi_publish_selection")]
// inputs: workspaceId?, datasetId?, datasetName?, clearIfEmpty?

// select_from_powerbi  
[McpServerTool("select_from_powerbi")]
// inputs: workspaceId?, datasetId?, datasetName?, sourceTable?, elementIdColumn?, maxElements?
```

Both follow the existing pattern: build `JObject input`, call `bridge.InvokeToolAsync(name, input)`.

---

## 10. Threading Contract

| Step | Thread | Revit API? |
|------|--------|-----------|
| Parse input, validate | Main | No |
| Auth (MSAL cache) | Main | No |
| Snapshot selection (pbi_publish_selection) | Main | ✅ Yes |
| HTTP DELETE + POST (pbi_publish_selection) | Background | No |
| HTTP GET rows (select_from_powerbi) | Background | No |
| Apply selection (select_from_powerbi) | Main | ✅ Yes |

`select_from_powerbi` needs **two main-thread phases**: apply selection after the background HTTP read. This is handled by the existing `RunWithoutContext` pattern — the background read completes and returns `List<long>`, then the main thread applies the selection synchronously before returning.

---

## 11. Test Matrix

| Test | Expected |
|------|----------|
| `pbi_publish_selection` — elements selected | ✅ rowCount = selection size, table updated in PBI |
| `pbi_publish_selection` — nothing selected, clearIfEmpty=false | ✅ rowCount=0, warning, table NOT cleared |
| `pbi_publish_selection` — nothing selected, clearIfEmpty=true | ✅ rowCount=0, table cleared |
| `pbi_publish_selection` — no binding, no params | ✅ Fail: workspaceId required |
| `pbi_publish_selection` — binding auto-resolve | ✅ Uses binding workspaceId/datasetId |
| `select_from_powerbi` — Selection table has rows | ✅ selectedCount = valid ids |
| `select_from_powerbi` — some ids not in doc | ✅ skippedCount > 0, rest selected |
| `select_from_powerbi` — empty table | ✅ selectedCount=0, warning |
| `select_from_powerbi` — sourceTable="Elements", elementIdColumn="ElementId" | ✅ Selects filtered elements |
| `select_from_powerbi` — maxElements cap hit | ✅ Warning: "truncated to N" |
| Round-trip: publish_selection → select_from_powerbi | ✅ Same ids in, same ids out |

---

## 12. Known Constraints

1. **Push dataset row read is all-or-nothing** — no server-side filter. Large `Elements` tables (10k rows) are fully downloaded before `maxElements` is applied. For `Selection` table this is fine (typically <500 rows).
2. **UIDocument requires main thread** — `select_from_powerbi` must marshal back to main thread for `SetElementIds`. This is the existing pattern.
3. **ElementId int32 ceiling** — Revit ElementId values fit in int32 even on newer versions; the Int64→int cast is safe.
4. **Revit selection is per-view in some contexts** — `SetElementIds` works on the active view. No special handling needed.
