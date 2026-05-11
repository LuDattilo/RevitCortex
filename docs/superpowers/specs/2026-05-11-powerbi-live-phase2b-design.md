# Power BI Live — Phase 2B — DAX Query → Revit Selection

**Date:** 2026-05-11  
**Status:** Design — approved  
**Author:** RevitCortex AI session (Luigi Dattilo, GPA Ingegneria Srl)  
**Depends on:** Phase 2A spec (`2026-05-11-powerbi-live-phase2a.md`) — fully validated

---

## 1. Scope

Phase 2B adds **one tool**: `pbi_query` (PBI → Revit).

| Direction | Tool | Trigger |
|-----------|------|---------|
| PBI → Revit | `pbi_query` | On-demand (user asks Claude) |

`pbi_query` executes a DAX query against the bound Power BI push dataset via `POST /datasets/{id}/executeQueries`, extracts `ElementId` values from the result, and selects those elements in Revit via `UIDocument.Selection.SetElementIds()`.

**Explicitly out of scope:**
- Automatic/live sync (no polling, no event subscription)
- Writing back to PBI from this tool (that is `pbi_publish_selection`)
- Visual selection state from PBI visuals (not in REST API — Phase 2C future)
- Multi-table joins beyond the `Elements` table

---

## 2. Architecture

```
Claude Desktop
    │  MCP stdio
    ▼
RevitCortex.Server  ── POST /datasets/{id}/executeQueries ──▶  Power BI REST API
    │  TCP :27015                                              (returns ElementId rows)
    ▼
RevitCortex.Plugin
    ├── Background:  HTTP POST executeQueries via RunWithoutContext<T>
    └── Main thread: UIDocument.Selection.SetElementIds(ids)
```

**Threading contract (inverted vs other PBI tools):**

| Step | Thread | Revit API? |
|------|--------|-----------|
| Parse input, validate | Main | No |
| Auth (MSAL cache) | Main | No |
| HTTP POST executeQueries | Background | No |
| Parse ElementIds from response | Background | No |
| SetElementIds on UIDocument | Main | ✅ Yes |

The HTTP call happens first (background), then the result is handed back to the main thread for the Revit selection write. This is the reverse of `pbi_publish_selection` and requires two `RunWithoutContext` invocations (one for HTTP, one to capture the main-thread result).

In practice, `Execute()` is called on the Revit main thread. The flow is:
1. Authenticate (MSAL cache — main thread, no network)
2. `RunWithoutContext(() => ExecuteQueryAsync(...))` — background HTTP
3. Unpack ElementId list (main thread, no Revit API)
4. `new UIDocument(document)` → `uiDoc.Selection.SetElementIds(...)` (main thread, Revit API ✅)

---

## 3. DAX endpoint

```
POST https://api.powerbi.com/v1.0/myorg/groups/{workspaceId}/datasets/{datasetId}/executeQueries
Authorization: Bearer {token}
Content-Type: application/json

{
  "queries": [
    { "query": "EVALUATE SELECTCOLUMNS(...)" }
  ],
  "serializerSettings": { "includeNulls": true }
}
```

Response schema:
```json
{
  "results": [
    {
      "tables": [
        {
          "rows": [
            { "[ElementId]": 123456 },
            { "[ElementId]": 789012 }
          ]
        }
      ]
    }
  ]
}
```

Row keys are bracketed column names (e.g. `[ElementId]`). The parser must handle both `[ElementId]` and `ElementId` key formats.

**Tenant requirement:** The Power BI tenant setting `ExecuteQueries.Execute.All` must be enabled for this endpoint to work. If disabled, the API returns 403. The tool surfaces this with a clear error message and suggestion.

---

## 4. Tool: `pbi_query`

### 4.1 Purpose

Executes a DAX query against the bound Power BI dataset, extracts `ElementId` values, and selects those elements in Revit.

### 4.2 Inputs

| Parameter | Type | Required | Default | Notes |
|-----------|------|----------|---------|-------|
| `workspaceId` | string | No | from binding | Falls back to ProjectBinding |
| `datasetId` | string | No | from binding | Falls back to binding, then name-lookup |
| `datasetName` | string | No | from binding | Used for lookup when datasetId absent |
| `category` | string | No | — | OST code or display name (template param) |
| `level` | string | No | — | Level name, e.g. "Level 1" (template param) |
| `parameterName` | string | No | — | Column name in Elements table (template param) |
| `parameterValue` | string | No | — | Value to match (string equality) (template param) |
| `exportRunId` | string | No | — | GUID of a previous publish run (template param) |
| `daxQuery` | string | No | — | Raw DAX override — ignores all template params |
| `action` | string | No | `"select"` | `"select"` or `"isolate"` |
| `maxElements` | int | No | `5000` | Safety cap on ElementIds to select |

**Validation rules:**
- At least one filter must be specified: one of `category`, `exportRunId`, or `daxQuery`. If none, return `InvalidInput`.
- `parameterName` requires `parameterValue` and vice versa.
- `daxQuery` takes precedence over all template params; if both are present, template params are silently ignored.

### 4.3 DAX template generation

Templates always produce:
```dax
EVALUATE SELECTCOLUMNS(
  FILTER(Elements, <conditions>),
  "ElementId", Elements[ElementId]
)
```

**Condition building:**

| Params present | Condition added |
|----------------|-----------------|
| `category` | `Elements[Category] = "{category}"` |
| `level` | `Elements[Level] = "{level}"` |
| `parameterName` + `parameterValue` | `Elements[{parameterName}] = "{parameterValue}"` |
| `exportRunId` | `Elements[ExportRunId] = "{exportRunId}"` |

Multiple conditions are combined with `&&`.

**String escaping:** double-quotes inside DAX string literals are escaped as `""` (DAX standard). This is applied to all template-generated values.

**`daxQuery` raw:** passed through verbatim to the API. The tool validates that it starts with `EVALUATE` (case-insensitive) to catch obvious mistakes.

### 4.4 `action` parameter

| Value | Behavior |
|-------|----------|
| `"select"` | `uiDoc.Selection.SetElementIds(ids)` — standard selection |
| `"isolate"` | Select elements, then `uiDoc.ActiveView.IsolateElementsTemporary(ids)` |

Default is `"select"`.

### 4.5 Behavior

1. **Authenticate** (MSAL cache, main thread)
2. **Resolve dataset** (stale-binding detection — same pattern as Phase 1/2A)
3. **Build DAX** (from template params or pass-through `daxQuery`)
4. **HTTP POST executeQueries** (background thread)
5. **Parse response** — extract `[ElementId]` column, apply `maxElements` cap
6. **If 0 elements** — return success with warning, do NOT modify Revit selection
7. **SetElementIds** (main thread, Revit API)
8. **Return response**

### 4.6 Response

**Success — elements selected:**
```json
{
  "success": true,
  "workspaceId": "...",
  "datasetId": "...",
  "datasetName": "...",
  "elementCount": 42,
  "action": "select",
  "daxUsed": "EVALUATE SELECTCOLUMNS(...)",
  "cappedAt": null,
  "durationMs": 280
}
```

`cappedAt` is non-null (equals `maxElements`) when the result was truncated.

**Success — 0 elements (selection unchanged):**
```json
{
  "success": true,
  "elementCount": 0,
  "warning": "Query returned no elements. Current Revit selection unchanged.",
  "daxUsed": "...",
  "durationMs": 180
}
```

### 4.7 Error cases

| Condition | Error code | Message |
|-----------|-----------|---------|
| Not signed in | PermissionDenied | "Not signed in to Power BI." |
| AllowExternalWrites=false | PermissionDenied | Standard gate message |
| No active document | InvalidInput | "No active Revit document." |
| No filter params | InvalidInput | "Specify at least one filter: category, exportRunId, or daxQuery." |
| `parameterName` without `parameterValue` | InvalidInput | "parameterName requires parameterValue." |
| `daxQuery` doesn't start with EVALUATE | InvalidInput | "daxQuery must start with EVALUATE." |
| workspaceId not resolvable | InvalidInput | "workspaceId is required." |
| Dataset not found (404) | ElementNotFound | "Dataset not found. Run pbi_publish_elements first." |
| 403 executeQueries | PermissionDenied | "Power BI tenant setting 'ExecuteQueries.Execute.All' may be disabled. Ask your PBI admin to enable it, or use the Power BI admin portal." |
| Token expired (401) | PermissionDenied | "Power BI token expired during query." |
| ElementId not found in Revit | (skip silently) | ElementIds that no longer exist in the model are skipped; `elementCount` reflects only successfully resolved ids |

---

## 5. New / modified files

```
src/RevitCortex.Plugin/PowerBiLive/Tools/
└── PbiQueryTool.cs                   (new)

src/RevitCortex.Plugin/PowerBiLive/
└── PowerBiServiceClient.cs           (add ExecuteQueryAsync)

src/RevitCortex.Server/Tools/
└── ElementTools.cs                   (add 1 MCP wrapper: pbi_query)

tool-schemas.txt                      (add 1 entry)
WORKFLOWS.md                          (add Phase 2B section)
docs/USER_GUIDE.md                    (update PBI Live table)
```

No changes to `PowerBiDatasetSchema.cs`, `PowerBiSettings.cs`, or `PowerBiToolHelper.cs`.

---

## 6. PowerBiServiceClient — new method

```csharp
public async Task<List<long>> ExecuteQueryAsync(
    string workspaceId,
    string datasetId,
    string daxQuery)
{
    var url = $"groups/{workspaceId}/datasets/{datasetId}/executeQueries";
    var body = new
    {
        queries = new[] { new { query = daxQuery } },
        serializerSettings = new { includeNulls = true }
    };
    var json = await PostJsonAsync(url, body).ConfigureAwait(false);
    return ParseElementIds(json);
}

private static List<long> ParseElementIds(string json)
{
    // results[0].tables[0].rows[] -> each row has "[ElementId]" or "ElementId" key
    var result = new List<long>();
    var root = JObject.Parse(json);
    var rows = root["results"]?[0]?["tables"]?[0]?["rows"] as JArray;
    if (rows == null) return result;
    foreach (var row in rows)
    {
        var val = row["[ElementId]"] ?? row["ElementId"];
        if (val != null)
        {
            try { result.Add(val.Value<long>()); } catch { }
        }
    }
    return result;
}
```

---

## 7. ElementId resolution in Revit

After receiving the `List<long>` of ElementIds:

```csharp
var validIds = new List<ElementId>();
foreach (var id in rawIds)
{
    // R24+: new ElementId((long)id) — R23: new ElementId((int)id)
    var eid = ElementIdHelper.FromLong(id);
    var el = doc.GetElement(eid);
    if (el != null) validIds.Add(eid);
}
uiDoc.Selection.SetElementIds(validIds);
```

Invalid/deleted ElementIds are silently skipped. `elementCount` in the response reflects `validIds.Count`.

---

## 8. Test matrix

| Test | Expected |
|------|----------|
| `pbi_query` — category filter | Selects all elements of that category in Revit |
| `pbi_query` — category + level | Selects elements on that level only |
| `pbi_query` — parameterName/Value | Selects elements matching parameter |
| `pbi_query` — exportRunId | Reselects elements from a previous publish |
| `pbi_query` — daxQuery raw | Executes verbatim DAX, selects result |
| `pbi_query` — 0 results | Success, warning, selection unchanged |
| `pbi_query` — no filter params | Fail: InvalidInput |
| `pbi_query` — maxElements cap | elementCount = maxElements, cappedAt set |
| `pbi_query` — stale binding | Falls back to name-lookup |
| `pbi_query` — 403 executeQueries | PermissionDenied with tenant setting suggestion |
| `pbi_query` — deleted ElementIds | Skipped silently, elementCount reflects valid ids only |

---

## 9. Known constraints

1. **`ExecuteQueries.Execute.All` tenant setting.** This Power BI admin setting must be enabled. It is off by default in some tenants. The tool returns a clear 403 error with actionable suggestion.
2. **Push dataset rows are eventually consistent.** Rows posted via `pbi_publish_elements` may take a few seconds to be queryable via DAX. In practice this is sub-second, but a `pbi_query` call immediately after `pbi_publish_elements` may return stale results.
3. **`Elements[Category]` column stores OST codes.** Template queries match against the `Category` column which stores `OST_Walls`, `OST_Doors`, etc. (as exported by `PowerBiElementExporter`). Display names are not stored in that column.
4. **`parameterName` refers to dataset column names, not Revit parameter names.** The `Elements` table only contains a fixed set of columns as defined in `PowerBiDatasetSchema`. Custom Revit parameters are not exported in Phase 1-2 (Phase 3+).
