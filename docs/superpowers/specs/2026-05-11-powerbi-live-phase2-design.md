# Power BI Live — Phase 2 — Selection Publishing

**Date:** 2026-05-11  
**Status:** Design — revised after independent review ✅  
**Author:** RevitCortex AI session (Luigi Dattilo, GPA Ingegneria Srl)  
**Depends on:** Phase 1 spec (`2026-05-11-powerbi-live-phase1-design.md`) — fully validated

---

## Review Findings (2026-05-11)

Independent review identified three blockers in the original design:

1. **`GET /tables/{table}/rows` does not exist for push datasets.** The Power BI Push Dataset REST API exposes only `POST rows`, `DELETE rows`, `GET tables` — not a row-read endpoint. `select_from_powerbi` as designed would call a non-existent endpoint.
2. **`select_from_powerbi` name conflict.** A tool with `Name => "select_from_powerbi"` already exists (`SelectFromPowerBiTool.cs`, MCP wrapper in `ElementTools.cs` line 415). The new tool would collide.
3. **PBI → Revit "click visual" is not obtainable via REST alone.** Power BI REST does not expose visual selection state. A real PBI→Revit channel requires Power BI Embedded JS, a custom visual, or Power Automate writeback — out of scope.

**Resolution:** Phase 2 is split into 2A and 2B:
- **Phase 2A (this spec):** `pbi_publish_selection` only — Revit → PBI, fully implementable.
- **Phase 2B (future spec):** PBI → Revit via DAX `executeQueries` or explicit ElementId passing — requires separate design.

---

## 1. Scope

Phase 2A adds **one tool**: `pbi_publish_selection` (Revit → PBI).

| Direction | Tool | Trigger |
|-----------|------|---------|
| Revit → PBI | `pbi_publish_selection` | On-demand (user asks Claude) |
| PBI → Revit | *(Phase 2B — future)* | — |

**Explicitly out of scope:**
- `select_from_powerbi` new variant (blocked — see review findings)
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

## 5. Phase 2B — PBI → Revit (Future, Not Implemented)

Blocked by two constraints identified in review:

1. **No `GET rows` endpoint on push datasets.** The Push Dataset REST API does not expose a row-read endpoint. Reading data back requires either: (a) DAX `executeQueries` (requires Build permission + tenant setting), or (b) a separate writeback channel (Power Automate, custom visual, Embedded JS).
2. **Visual selection state is not in the REST API.** "What the user clicked in a PBI visual" is not queryable via REST. It requires Power BI Embedded JS SDK running in a browser context.

**Pragmatic Phase 2B design (future spec):**
- Option A: User or Claude provides ElementId list explicitly → use existing `select_from_powerbi(elementIds, action)` tool (already implemented)
- Option B: DAX `executeQueries` to read a filtered subset from the dataset → design separately, requires additional API permissions

---

## 6. ElementId Compatibility (R23 vs R25+)

```csharp
// ElementIdHelper.cs (already exists in codebase)
// Reading from doc: .Value (R24+) or .IntegerValue (R23)
// Writing to Int64 column: (long)id.Value or (long)id.IntegerValue
```

Stored as `Int64` in the Selection table. Values fit in int32 for Revit's actual id range.

---

## 7. New Files (Phase 2A only)

```
src/RevitCortex.Plugin/PowerBiLive/Tools/
└── PbiPublishSelectionTool.cs    (new)

src/RevitCortex.Server/Tools/
└── ElementTools.cs               (add 1 MCP wrapper: pbi_publish_selection)

tool-schemas.txt                  (add 1 entry)
WORKFLOWS.md                      (add Phase 2A section)
docs/USER_GUIDE.md                (update PBI Live table)
```

No changes to `PowerBiDatasetSchema.cs`, `PowerBiSettings.cs`, `PowerBiToolHelper.cs`, or `PowerBiServiceClient.cs`.

---

## 8. Threading Contract

| Step | Thread | Revit API? |
|------|--------|-----------|
| Parse input, validate | Main | No |
| Auth (MSAL cache) | Main | No |
| Snapshot selection | Main | ✅ Yes — `UIDocument.Selection.GetElementIds()` |
| HTTP DELETE + POST | Background | No |

Single main-thread phase (snapshot), then background HTTP. Same pattern as `pbi_publish_elements`.

---

## 9. Test Matrix

| Test | Expected |
|------|----------|
| `pbi_publish_selection` — elements selected | rowCount = selection size, Selection table updated in PBI |
| `pbi_publish_selection` — nothing selected, clearIfEmpty=false | rowCount=0, warning, table NOT cleared |
| `pbi_publish_selection` — nothing selected, clearIfEmpty=true | rowCount=0, table cleared |
| `pbi_publish_selection` — no binding, no params | Fail: workspaceId required |
| `pbi_publish_selection` — binding auto-resolve | Uses binding workspaceId/datasetId |
| `pbi_publish_selection` — stale binding (dataset deleted) | Fallback to name-lookup, recreates dataset |

---

## 10. Known Constraints

1. **UIDocument constructor requires an open document.** `new UIDocument(document)` throws if document is not open in the UI. Handled by `RequiresDocument = true` guard in the router.
2. **Selection is scoped to active view in some Revit contexts.** `GetElementIds()` returns elements selectable in the current view. No special handling needed — this is the expected behavior.
3. **ElementId int32 ceiling.** Revit ElementId values fit in int32 even on newer versions; storing as Int64 is safe and forward-compatible.
