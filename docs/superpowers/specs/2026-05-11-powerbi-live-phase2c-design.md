# Power BI Live — Phase 2C — Custom Visual → Revit Selection

**Date:** 2026-05-11  
**Status:** Design — awaiting review  
**Author:** RevitCortex AI session (Luigi Dattilo, GPA Ingegneria Srl)  
**Depends on:** Phase 1 spec (`2026-05-11-powerbi-live-phase1-design.md`) — fully validated

---

## 1. Scope

Phase 2C adds a **Power BI custom visual** (PBIVIZ) that lets the user select
elements in Revit directly from a Power BI Desktop report, plus the
**HTTP listener** inside RevitCortex.Plugin that receives those selections.

| Component | Type | Where it runs |
|-----------|------|--------------|
| `RevitCortex Selection` visual | TypeScript / React | Power BI Desktop WebView |
| `PbiSelectHttpListener` | C# | RevitCortex.Plugin (background thread) |

**Target host:** Power BI Desktop only (not Power BI Service).  
Power BI Desktop uses an embedded Chromium WebView that does **not** enforce
mixed-content restrictions, so `http://localhost` calls from an `https://`
origin are allowed.

**Explicitly out of scope:**
- Power BI Service / cloud hosting (blocked by mixed-content policy)
- Azure Function relay / SignalR cloud relay
- Automatic sync on every cross-filter change (on-demand button press only)
- NavisCortex integration (separate spec)
- Authentication between the visual and the listener (localhost-only, no auth needed)

---

## 2. Architecture

```
Power BI Desktop
  └── Report page
        └── RevitCortex Selection visual (PBIVIZ)
              ├── [→ Seleziona filtrati]   button
              └── [→ Seleziona highlighted] button
                    │
                    │ POST http://localhost:27016/pbi-select
                    │ Content-Type: application/json
                    │ Body: { "elementIds": [123, 456, ...], "action": "select"|"isolate" }
                    ▼
RevitCortex.Plugin
  └── PbiSelectHttpListener  (HttpListener, background thread, port 27016)
        └── Deserialize body
              └── Dispatcher.BeginInvoke (Revit main thread)
                    └── UIDocument.Selection.SetElementIds(validIds)
```

### Port

`27016` — one above the existing RevitCortex TCP bridge port (`27015` / configurable).
The HTTP listener port is fixed at `27016` for Phase 2C. A future setting can make it configurable.

### Startup / shutdown

`PbiSelectHttpListener` is started in `RevitCortexApp.StartService()` alongside
`SocketService`, and stopped in `RevitCortexApp.StopService()`. If the port is
already in use (another Revit instance), the listener logs a warning and skips —
it does not crash the plugin startup.

---

## 3. HTTP endpoint

```
POST http://localhost:27016/pbi-select
Content-Type: application/json

{
  "elementIds": [123456, 789012],
  "action": "select"
}
```

### Request fields

| Field | Type | Required | Default | Notes |
|-------|------|----------|---------|-------|
| `elementIds` | number[] | Yes | — | Revit ElementId.Value (Int64) |
| `action` | string | No | `"select"` | `"select"` or `"isolate"` |

### Responses

**200 OK — success:**
```json
{ "success": true, "elementCount": 42, "action": "select" }
```

**200 OK — no active document:**
```json
{ "success": false, "error": "No active Revit document." }
```

**200 OK — empty list:**
```json
{ "success": true, "elementCount": 0, "warning": "No ElementIds provided." }
```

**400 Bad Request — malformed JSON:**
```json
{ "success": false, "error": "Invalid JSON body." }
```

**405 Method Not Allowed** — non-POST request.

All responses include CORS headers:
```
Access-Control-Allow-Origin: *
Access-Control-Allow-Methods: POST, OPTIONS
Access-Control-Allow-Headers: Content-Type
```
(Required because Power BI Desktop WebView sends preflight OPTIONS requests.)

---

## 4. C# component: `PbiSelectHttpListener`

### 4.1 File

```
src/RevitCortex.Plugin/PowerBiLive/PbiSelectHttpListener.cs   (new)
```

### 4.2 Interface

```csharp
public class PbiSelectHttpListener : IDisposable
{
    public PbiSelectHttpListener(
        Func<Document?> getActiveDocument,
        Action<IList<ElementId>, string> applySelection,
        int port = 27016);

    public void Start();   // starts background HttpListener thread
    public void Stop();    // stops listener, releases port
    public void Dispose();
    public bool IsRunning { get; }
}
```

`getActiveDocument` and `applySelection` are injected by `RevitCortexApp` so the
listener has no direct dependency on Revit API — the callbacks run on the Revit
main thread via `ExternalEventHandler` or `Application.IdleEventHandler`.

### 4.3 Threading contract

| Step | Thread | Notes |
|------|--------|-------|
| `HttpListener.GetContext()` | Background | No Revit API |
| Parse JSON body | Background | No Revit API |
| Deserialize ElementIds | Background | No Revit API |
| `applySelection(ids, action)` callback | **Main thread** | Revit API ✅ |

The callback is dispatched to the Revit main thread via `RevitCortexApp.Instance`
using the same `ExternalEventHandler` pattern already used by `CortexRouter`.
Alternatively, `Dispatcher.CurrentDispatcher.BeginInvoke` can be used if the
dispatcher is captured at startup. Use whichever pattern is already established
in the codebase.

### 4.4 ElementId resolution

```csharp
var validIds = new List<ElementId>();
foreach (var idVal in rawIds)
{
#if REVIT2024_OR_GREATER
    var eid = new ElementId((long)idVal);
#else
    var eid = new ElementId((int)idVal);
#endif
    if (doc.GetElement(eid) != null)
        validIds.Add(eid);
}
```

Deleted/invalid ElementIds are silently skipped. `elementCount` in the response
reflects `validIds.Count`.

### 4.5 `action` handling

| Value | Behavior |
|-------|----------|
| `"select"` | `uiDoc.Selection.SetElementIds(validIds)` |
| `"isolate"` | `SetElementIds` + `doc.ActiveView.IsolateElementsTemporary(validIds)` inside a Transaction |

### 4.6 Integration in `RevitCortexApp`

In `StartService()`, after `_socketService.Start()`:

```csharp
_pbiSelectListener = new PbiSelectHttpListener(
    getActiveDocument: () => _session?.Store.Get<object>("activeDocument") as Document,
    applySelection: ApplySelectionOnMainThread,
    port: 27016);
_pbiSelectListener.Start();
```

In `StopService()`, before or after `_socketService.Stop()`:

```csharp
_pbiSelectListener?.Stop();
```

---

## 5. TypeScript component: `RevitCortex Selection` visual

### 5.1 Visual metadata

| Property | Value |
|----------|-------|
| Display name | RevitCortex Selection |
| GUID | `revitcortex-selection-visual` |
| Version | 1.0.0 |
| Capabilities | `dataRoles`, `dataViewMappings`, `supportsHighlight` |

### 5.2 Data roles

The visual requires one data role:

```json
{
  "name": "elementIds",
  "kind": "GroupingOrMeasure",
  "displayName": "Element ID",
  "description": "Map the ElementId column from the Elements table"
}
```

The user drags the `ElementId` column (Int64) from the `Elements` table into
this role in the Fields panel.

### 5.3 UI

A minimal card-style panel with:

```
┌─────────────────────────────────────┐
│  RevitCortex Selection              │
│                                     │
│  [→ Seleziona filtrati (42)]        │
│  [→ Seleziona highlighted (3)]      │
│                                     │
│  ● Connesso a localhost:27016       │
└─────────────────────────────────────┘
```

- **"Seleziona filtrati (N)"** — extracts `ElementId` values from
  `dataView.table.rows` (all rows visible after cross-filters).
  Count shown in the button label.

- **"Seleziona highlighted (N)"** — extracts `ElementId` values only from
  highlighted (cross-filtered selection) rows, detected via
  `dataView.table.rows[i].highlights` being non-null.
  Count shown. Button is disabled when count = 0.

- **Connection indicator** — on mount, does a lightweight
  `OPTIONS http://localhost:27016/pbi-select` preflight. Green dot = reachable,
  grey dot = RevitCortex not running.

### 5.4 Core logic

```typescript
// Filtered rows = all rows in the current data view
function getFilteredIds(dataView: DataView): number[] {
  return dataView.table!.rows.map(row => row[0] as number);
}

// Highlighted rows = rows where the highlight value is not null
function getHighlightedIds(dataView: DataView): number[] {
  return dataView.table!.rows
    .filter((_, i) => dataView.table!.highlights?.[i] !== null)
    .map(row => row[0] as number);
}

async function sendToRevit(elementIds: number[], action: "select" | "isolate") {
  await fetch("http://localhost:27016/pbi-select", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ elementIds, action }),
  });
}
```

### 5.5 Format options (optional, Phase 2C v1)

None for v1. A future version may add:
- Custom port override
- Button labels (localization)
- Color theme

### 5.6 Build output

```
dist/
└── revitcortex-selection.pbiviz   (packaged visual, importable into PBI Desktop)
```

---

## 6. New files

```
src/RevitCortex.Plugin/PowerBiLive/
└── PbiSelectHttpListener.cs        (new — C# HTTP listener)

powerbi-visual/                     (new top-level folder)
├── package.json
├── tsconfig.json
├── pbiviz.json
├── capabilities.json
├── src/
│   ├── visual.tsx                  (main visual component)
│   └── settings.ts                 (format options — empty for v1)
└── assets/
    └── icon.png
```

**Modifications to existing files:**
- `src/RevitCortex.Plugin/RevitCortexApp.cs` — add `_pbiSelectListener` field, start/stop in `StartService`/`StopService`

---

## 7. Test matrix

| Test | Expected |
|------|----------|
| POST valid ElementIds, action=select | Elements selected in Revit, response `{success:true, elementCount:N}` |
| POST valid ElementIds, action=isolate | Elements selected + isolated in active view |
| POST empty array | Response `{success:true, elementCount:0, warning:...}` |
| POST invalid JSON | Response `{success:false, error:"Invalid JSON body."}` |
| POST with deleted ElementIds | Deleted ids skipped, elementCount reflects valid only |
| OPTIONS preflight | 200 with CORS headers |
| Non-POST method | 405 |
| Port already in use | Listener skips, logs warning, plugin starts normally |
| No active document | Response `{success:false, error:"No active Revit document."}` |
| Visual — filtrati button | Sends all rows from dataView.table |
| Visual — highlighted button | Sends only highlighted rows |
| Visual — connection indicator | Green when RevitCortex running, grey otherwise |
| Visual — highlighted count=0 | Button disabled |

---

## 8. Known constraints

1. **Power BI Desktop only.** The `http://localhost` call is blocked by browsers
   when loading PBI Service from `https://`. This is a known browser
   mixed-content restriction. PBI Desktop uses an embedded WebView that does not
   enforce this policy.

2. **One Revit instance only.** Port 27016 is occupied by the first Revit
   instance that starts. If two instances are open, the second silently skips
   the listener. A future multi-instance design could use a port-discovery
   mechanism.

3. **`highlights` API reliability.** Power BI's `dataView.table.highlights`
   is not always populated depending on the visual type and PBI Desktop version.
   If `highlights` is null/undefined, the "highlighted" button falls back to
   showing count=0 and remaining disabled.

4. **pbiviz toolchain.** The custom visual requires `powerbi-visuals-tools`
   (`pbiviz` CLI) version 5.x. Node.js 18+ required for build. The PBIVIZ
   file is a standalone distributable — no npm registry publish needed.

5. **CORS preflight on `action=isolate`.** `IsolateElementsTemporary` requires
   an open view that supports isolation (not sheets, schedules). Isolation
   failure is non-fatal — selection is applied regardless.
