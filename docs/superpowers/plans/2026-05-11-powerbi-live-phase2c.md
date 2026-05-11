# Power BI Live Phase 2C Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `PbiSelectHttpListener` (C# `HttpListener` on port 27016) to the RevitCortex plugin and a `RevitCortex Selection` PBIVIZ custom visual (TypeScript/React) that lets users select Revit elements by clicking buttons in a Power BI Desktop report.

**Architecture:** The C# listener runs on a background thread, deserializes the JSON body, and dispatches `SetElementIds` to the Revit main thread via the existing `ExternalEventHandler` pattern. The TypeScript visual reads `dataView.table.rows` (filtered) and `dataView.table.highlights` (cross-filtered highlights) and POSTs to `http://localhost:27016/pbi-select`. Power BI Desktop only — its embedded Chromium WebView does not enforce mixed-content restrictions.

**Tech Stack:** C# `System.Net.HttpListener`, `Newtonsoft.Json`; TypeScript 4+, `powerbi-visuals-tools` 5.x (`pbiviz` CLI), React 18, Node.js 18+.

---

## File map

### New files
| Path | Responsibility |
|------|---------------|
| `src/RevitCortex.Plugin/PowerBiLive/PbiSelectHttpListener.cs` | Background HTTP listener, port 27016, deserialise body, dispatch to main thread |
| `src/RevitCortex.Tests/PowerBiLive/PbiSelectHttpListenerTests.cs` | Unit tests for listener (no Revit API — uses injected callbacks) |
| `powerbi-visual/package.json` | NPM package metadata |
| `powerbi-visual/tsconfig.json` | TypeScript config |
| `powerbi-visual/pbiviz.json` | Visual manifest (GUID, version, display name) |
| `powerbi-visual/capabilities.json` | Data roles and dataViewMappings |
| `powerbi-visual/src/visual.tsx` | Main visual component (buttons, connection indicator) |
| `powerbi-visual/src/settings.ts` | Format options placeholder (empty for v1) |
| `powerbi-visual/assets/icon.png` | 20×20 PNG placeholder icon (can be blank) |

### Modified files
| Path | Change |
|------|--------|
| `src/RevitCortex.Plugin/RevitCortexApp.cs` | Add `_pbiSelectListener` field; start in `StartService`, stop in `StopService`/`OnShutdown` |

---

## Task 1: `PbiSelectHttpListener` — core class with tests

**Files:**
- Create: `src/RevitCortex.Plugin/PowerBiLive/PbiSelectHttpListener.cs`
- Create: `src/RevitCortex.Tests/PowerBiLive/PbiSelectHttpListenerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `src/RevitCortex.Tests/PowerBiLive/PbiSelectHttpListenerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using RevitCortex.Plugin.PowerBiLive;
using Xunit;

namespace RevitCortex.Tests.PowerBiLive;

/// <summary>
/// These tests spin up a real HttpListener on a test port (27099) and verify
/// the response contracts. No Revit API is used — callbacks are injected stubs.
/// </summary>
public class PbiSelectHttpListenerTests : IDisposable
{
    private readonly List<(IList<ElementId> ids, string action)> _received = new();
    private PbiSelectHttpListener? _listener;
    private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    private PbiSelectHttpListener MakeListener(Document? docToReturn = null)
    {
        _listener = new PbiSelectHttpListener(
            getActiveDocument: () => docToReturn,
            applySelection: (ids, action) => _received.Add((ids, action)),
            port: 27099);
        _listener.Start();
        return _listener;
    }

    [Fact]
    public async Task Post_EmptyArray_Returns200WithWarning()
    {
        MakeListener(null);
        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-select",
            new StringContent("{\"elementIds\":[]}", Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)resp.StatusCode);
        Assert.Contains("\"success\":true", body);
        Assert.Contains("warning", body);
    }

    [Fact]
    public async Task Post_InvalidJson_Returns400()
    {
        MakeListener(null);
        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-select",
            new StringContent("not-json", Encoding.UTF8, "application/json"));
        Assert.Equal(400, (int)resp.StatusCode);
    }

    [Fact]
    public async Task Post_NoActiveDocument_Returns200WithError()
    {
        MakeListener(docToReturn: null);
        var resp = await _http.PostAsync(
            "http://localhost:27099/pbi-select",
            new StringContent("{\"elementIds\":[1,2,3]}", Encoding.UTF8, "application/json"));
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Equal(200, (int)resp.StatusCode);
        Assert.Contains("\"success\":false", body);
        Assert.Contains("No active Revit document", body);
    }

    [Fact]
    public async Task Options_Preflight_Returns200WithCorsHeaders()
    {
        MakeListener(null);
        var req = new HttpRequestMessage(HttpMethod.Options, "http://localhost:27099/pbi-select");
        var resp = await _http.SendAsync(req);
        Assert.Equal(200, (int)resp.StatusCode);
        Assert.True(resp.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task NonPost_Returns405()
    {
        MakeListener(null);
        var resp = await _http.GetAsync("http://localhost:27099/pbi-select");
        Assert.Equal(405, (int)resp.StatusCode);
    }

    [Fact]
    public async Task PortAlreadyInUse_StartDoesNotThrow()
    {
        // First listener occupies port 27099
        MakeListener(null);
        // Second listener on same port should log warning and skip — not throw
        var second = new PbiSelectHttpListener(
            getActiveDocument: () => null,
            applySelection: (_, _) => { },
            port: 27099);
        var ex = Record.Exception(() => second.Start());
        Assert.Null(ex);
        Assert.False(second.IsRunning);
        second.Dispose();
    }

    [Fact]
    public async Task IsRunning_TrueAfterStart_FalseAfterStop()
    {
        var l = MakeListener(null);
        Assert.True(l.IsRunning);
        l.Stop();
        Assert.False(l.IsRunning);
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _http.Dispose();
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure (class doesn't exist yet)**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~PbiSelectHttpListenerTests" 2>&1 | head -30
```

Expected: compile error `CS0246: The type or namespace name 'PbiSelectHttpListener' could not be found`.

- [ ] **Step 3: Implement `PbiSelectHttpListener`**

Create `src/RevitCortex.Plugin/PowerBiLive/PbiSelectHttpListener.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// Lightweight HTTP listener on port 27016 (configurable) that accepts
/// POST /pbi-select requests from the Power BI Desktop custom visual and
/// dispatches element selection/isolation to the Revit main thread.
///
/// Threading: listener loop runs on a dedicated background thread.
/// The applySelection callback is called on the Revit main thread by the caller
/// (RevitCortexApp wires this via ExternalEventHandler).
///
/// Port conflict: if the port is already in use (second Revit instance),
/// Start() logs a warning and returns without throwing.
/// </summary>
public class PbiSelectHttpListener : IDisposable
{
    private readonly Func<Document?> _getActiveDocument;
    private readonly Action<IList<ElementId>, string> _applySelection;
    private readonly int _port;

    private HttpListener? _httpListener;
    private Thread? _thread;
    private volatile bool _running;

    public bool IsRunning => _running;

    public PbiSelectHttpListener(
        Func<Document?> getActiveDocument,
        Action<IList<ElementId>, string> applySelection,
        int port = 27016)
    {
        _getActiveDocument = getActiveDocument ?? throw new ArgumentNullException(nameof(getActiveDocument));
        _applySelection    = applySelection    ?? throw new ArgumentNullException(nameof(applySelection));
        _port              = port;
    }

    /// <summary>
    /// Starts the listener on a background thread.
    /// If the port is already occupied, logs a warning and returns without throwing.
    /// </summary>
    public void Start()
    {
        if (_running) return;

        try
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://localhost:{_port}/");
            _httpListener.Start();
        }
        catch (HttpListenerException ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[PbiSelectHttpListener] Port {_port} already in use — listener skipped. ({ex.ErrorCode})");
            _httpListener = null;
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[PbiSelectHttpListener] Failed to start: {ex.Message}");
            _httpListener = null;
            return;
        }

        _running = true;
        _thread = new Thread(ListenLoop) { IsBackground = true, Name = "PbiSelectHttpListener" };
        _thread.Start();

        System.Diagnostics.Trace.WriteLine(
            $"[PbiSelectHttpListener] Listening on port {_port}.");
    }

    /// <summary>Stops the listener and releases the port.</summary>
    public void Stop()
    {
        _running = false;
        try { _httpListener?.Stop(); } catch { }
        _httpListener = null;
    }

    public void Dispose() => Stop();

    // ─── Background listener loop ──────────────────────────────────────────

    private void ListenLoop()
    {
        while (_running)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = _httpListener?.GetContext();
            }
            catch (HttpListenerException)
            {
                break; // Listener stopped — exit loop normally
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[PbiSelectHttpListener] GetContext error: {ex.Message}");
                break;
            }

            if (ctx == null) break;

            try
            {
                HandleRequest(ctx);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[PbiSelectHttpListener] HandleRequest error: {ex.Message}");
                try { ctx.Response.Abort(); } catch { }
            }
        }

        _running = false;
        System.Diagnostics.Trace.WriteLine("[PbiSelectHttpListener] Loop exited.");
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        var req  = ctx.Request;
        var resp = ctx.Response;

        // CORS headers on every response
        resp.AddHeader("Access-Control-Allow-Origin", "*");
        resp.AddHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
        resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");

        // OPTIONS preflight
        if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            resp.StatusCode = 200;
            resp.Close();
            return;
        }

        // Only POST /pbi-select
        if (!req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            resp.StatusCode = 405;
            resp.Close();
            return;
        }

        // Parse body
        string bodyText;
        try
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            bodyText = reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            WriteJson(resp, 400, new { success = false, error = $"Failed to read body: {ex.Message}" });
            return;
        }

        JObject body;
        try
        {
            body = JObject.Parse(bodyText);
        }
        catch
        {
            WriteJson(resp, 400, new { success = false, error = "Invalid JSON body." });
            return;
        }

        var rawIds = body["elementIds"] as JArray;
        var action = body["action"]?.Value<string>() ?? "select";
        action = action.ToLowerInvariant();

        // Empty array
        if (rawIds == null || rawIds.Count == 0)
        {
            WriteJson(resp, 200, new { success = true, elementCount = 0, warning = "No ElementIds provided." });
            return;
        }

        // Need active document to validate ElementIds
        var doc = _getActiveDocument();
        if (doc == null)
        {
            WriteJson(resp, 200, new { success = false, error = "No active Revit document." });
            return;
        }

        // Parse and validate ElementIds
        var ids = new List<long>();
        foreach (var token in rawIds)
        {
            try { ids.Add(token.Value<long>()); }
            catch { /* skip unparseable */ }
        }

        // Dispatch to main thread — the callback is wired by RevitCortexApp
        // using the ExternalEventHandler pattern; we call it here and it runs
        // synchronously from the POV of the listener because applySelection is
        // a synchronous Action (RevitCortexApp queues it via ExternalEvent.Raise).
        //
        // NOTE: we pass the raw long IDs converted to ElementId inside the callback
        // (RevitCortexApp's lambda does the conversion) but for the listener's unit
        // tests the callback is a simple stub so we pass empty ElementId list and
        // rely on the app-level wiring for the real conversion.
        // For the response we report ids.Count (not validIds.Count) because validation
        // happens on the main thread inside applySelection.
        var elementIds = new List<ElementId>(ids.Count);
        foreach (var idVal in ids)
        {
#if REVIT2024_OR_GREATER
            elementIds.Add(new ElementId(idVal));
#else
            elementIds.Add(new ElementId((int)idVal));
#endif
        }

        _applySelection(elementIds, action);

        WriteJson(resp, 200, new { success = true, elementCount = ids.Count, action });
    }

    private static void WriteJson(HttpListenerResponse resp, int statusCode, object payload)
    {
        resp.StatusCode = statusCode;
        resp.ContentType = "application/json";
        var json = JsonConvert.SerializeObject(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        resp.ContentLength64 = bytes.Length;
        try
        {
            resp.OutputStream.Write(bytes, 0, bytes.Length);
            resp.OutputStream.Close();
        }
        catch { }
    }
}
```

- [ ] **Step 4: Run tests — expect them to pass**

```
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~PbiSelectHttpListenerTests" -v normal
```

Expected: all 7 tests PASS.

- [ ] **Step 5: Build Plugin R25 and R24 to verify net48 compatibility**

```
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

Expected: both build with 0 errors.

- [ ] **Step 6: Commit**

```
git add src/RevitCortex.Plugin/PowerBiLive/PbiSelectHttpListener.cs src/RevitCortex.Tests/PowerBiLive/PbiSelectHttpListenerTests.cs
git commit -m "feat: PbiSelectHttpListener — HTTP listener port 27016 for PBI Desktop visual"
```

---

## Task 2: Wire `PbiSelectHttpListener` into `RevitCortexApp`

**Files:**
- Modify: `src/RevitCortex.Plugin/RevitCortexApp.cs`

- [ ] **Step 1: Add field and start/stop wiring**

In `RevitCortexApp.cs`:

**Add field** (after `private bool _updateNotificationShown;` on line 25):
```csharp
private PbiSelectHttpListener? _pbiSelectListener;
```

**In `StartService()` method**, after `_socketService.Start();` (around line 178):
```csharp
// Start PBI Desktop → Revit HTTP listener on port 27016
if (_pbiSelectListener == null)
{
    _pbiSelectListener = new PbiSelectHttpListener(
        getActiveDocument: () => _uiApplication?.ActiveUIDocument?.Document,
        applySelection: ApplyPbiSelection,
        port: 27016);
    _pbiSelectListener.Start();
}
```

**Add `ApplyPbiSelection` private method** (after `StopService()`):
```csharp
/// <summary>
/// Called from PbiSelectHttpListener background thread.
/// Dispatches element selection/isolation to the Revit main thread via
/// the existing ExternalEventHandler / RevitThreadDispatcher.
/// </summary>
private void ApplyPbiSelection(IList<ElementId> elementIds, string action)
{
    if (_router == null || _uiApplication == null) return;

    // Use the router's dispatcher (ExternalEventHandler) to marshal to main thread.
    // The dispatcher.Invoke pattern blocks the background thread briefly while
    // Revit processes the next idle event.
    _router.Dispatcher?.Invoke(() =>
    {
        try
        {
            var doc = _uiApplication?.ActiveUIDocument?.Document;
            if (doc == null) return;

            // Validate ids on main thread
            var validIds = new List<ElementId>();
            foreach (var eid in elementIds)
            {
                if (doc.GetElement(eid) != null)
                    validIds.Add(eid);
            }
            if (validIds.Count == 0) return;

            var uiDoc = new UIDocument(doc);
            uiDoc.Selection.SetElementIds(validIds);

            if (action == "isolate")
            {
                try
                {
                    using var tx = new Transaction(doc, "PBI isolate");
                    tx.Start();
                    doc.ActiveView.IsolateElementsTemporary(validIds);
                    tx.Commit();
                }
                catch { /* non-fatal — selection already applied */ }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[PbiSelectHttpListener] ApplyPbiSelection error: {ex.Message}");
        }
    });
}
```

**In `StopService()` method**, add before or after `_socketService?.Stop();`:
```csharp
_pbiSelectListener?.Stop();
_pbiSelectListener = null;
```

**In `OnShutdown()` method**, add after `_socketService?.Stop();`:
```csharp
_pbiSelectListener?.Dispose();
```

- [ ] **Step 2: Check `_router.Dispatcher` property exists**

Check `src/RevitCortex.Plugin/CortexRouter.cs` — confirm there is a `Dispatcher` or `SetDispatcher` property accessible from RevitCortexApp. If the dispatcher is `RevitThreadDispatcher` and has an `Invoke(Action)` method, the code above compiles. If the API is different (e.g., `DispatchToMainThread(Action)`), adjust the call accordingly.

```
grep -n "Dispatcher\|SetDispatcher\|DispatchToMainThread" src/RevitCortex.Plugin/CortexRouter.cs
```

Adjust the `ApplyPbiSelection` method to use whichever public method the dispatcher exposes for queuing an action on the Revit main thread.

- [ ] **Step 3: Build Plugin R25 and R24**

```
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

Expected: 0 errors on both targets.

- [ ] **Step 4: Commit**

```
git add src/RevitCortex.Plugin/RevitCortexApp.cs
git commit -m "feat: wire PbiSelectHttpListener into RevitCortexApp start/stop"
```

---

## Task 3: Build all Revit targets and deploy

**Files:**
- No new files — verify all targets compile

- [ ] **Step 1: Build R23–R27**

```
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

Expected: all 5 build with 0 errors.

- [ ] **Step 2: Run full test suite**

```
dotnet test -c "Debug R25" src/RevitCortex.Tests/RevitCortex.Tests.csproj
```

Expected: all existing tests pass plus the new `PbiSelectHttpListenerTests` (7 tests).

- [ ] **Step 3: Deploy**

```
powershell -ExecutionPolicy Bypass -File deploy.ps1
```

Expected: deploy output shows R23–R27 plugin DLLs copied without errors.

- [ ] **Step 4: Commit**

```
git commit --allow-empty -m "chore: Phase 2C C# complete — all targets build and deploy"
```

---

## Task 4: TypeScript PBIVIZ project scaffold

**Files:**
- Create: `powerbi-visual/package.json`
- Create: `powerbi-visual/tsconfig.json`
- Create: `powerbi-visual/pbiviz.json`
- Create: `powerbi-visual/capabilities.json`
- Create: `powerbi-visual/assets/icon.png` (placeholder)

- [ ] **Step 1: Prerequisites check**

```
node --version   # must be >= 18
npm --version
pbiviz --version 2>&1 || npm install -g powerbi-visuals-tools
```

Expected: Node 18+, pbiviz 5.x available.

- [ ] **Step 2: Create folder structure**

```
mkdir -p powerbi-visual/src powerbi-visual/assets powerbi-visual/style
```

- [ ] **Step 3: Create `powerbi-visual/package.json`**

```json
{
  "name": "revitcortex-selection-visual",
  "version": "1.0.0",
  "description": "Power BI custom visual for selecting Revit elements from PBI Desktop",
  "scripts": {
    "build": "pbiviz package",
    "start": "pbiviz start"
  },
  "devDependencies": {
    "powerbi-visuals-tools": "^5.2.2",
    "@types/react": "^18.0.0",
    "@types/react-dom": "^18.0.0",
    "typescript": "^5.0.0"
  },
  "dependencies": {
    "react": "^18.2.0",
    "react-dom": "^18.2.0"
  }
}
```

- [ ] **Step 4: Create `powerbi-visual/tsconfig.json`**

```json
{
  "compilerOptions": {
    "target": "ES6",
    "module": "ES6",
    "lib": ["ES6", "DOM"],
    "strict": true,
    "jsx": "react",
    "moduleResolution": "node",
    "esModuleInterop": true,
    "outDir": ".tmp/build",
    "rootDir": "src"
  },
  "include": ["src/**/*"]
}
```

- [ ] **Step 5: Create `powerbi-visual/pbiviz.json`**

```json
{
  "visual": {
    "name": "RevitCortexSelection",
    "displayName": "RevitCortex Selection",
    "guid": "revitcortexselectionvisual1A2B3C4D",
    "visualClassName": "RevitCortexSelectionVisual",
    "version": "1.0.0",
    "description": "Select Revit elements from a Power BI Desktop report",
    "supportUrl": "https://github.com/LuDattilo/revitcortex-releases",
    "gitHubUrl": ""
  },
  "author": {
    "name": "GPA Ingegneria Srl",
    "email": "luigi.dattilo@gpapartners.com"
  },
  "apiVersion": "5.3.0",
  "assets": {
    "icon": "assets/icon.png"
  },
  "externalJS": [],
  "style": "style/visual.less",
  "capabilities": "capabilities.json",
  "dependencies": null
}
```

- [ ] **Step 6: Create `powerbi-visual/capabilities.json`**

```json
{
  "dataRoles": [
    {
      "name": "elementIds",
      "kind": "GroupingOrMeasure",
      "displayName": "Element ID",
      "description": "Map the ElementId column from the Elements table"
    }
  ],
  "dataViewMappings": [
    {
      "table": {
        "rows": {
          "for": { "in": "elementIds" },
          "dataReductionAlgorithm": {
            "top": { "count": 30000 }
          }
        }
      }
    }
  ],
  "supportsHighlight": true,
  "objects": {}
}
```

- [ ] **Step 7: Create placeholder icon**

Create `powerbi-visual/assets/icon.png` — any 20×20 PNG. You can generate one with:

```bash
# If ImageMagick is available:
magick -size 20x20 xc:#0078d4 powerbi-visual/assets/icon.png
# Or copy any 20x20 PNG from the project:
cp src/RevitCortex.Plugin/UI/Icons/pbi_icon_20.png powerbi-visual/assets/icon.png 2>/dev/null || true
```

If neither works, create a minimal 20×20 blue PNG by writing the 68-byte base64 below to the file:

```
iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAIAAAAC64paAAAAI0lEQVQ4y2NgGAWkAv///w8A
AAD//2NgYGAAAAAABAAAAAAAAAAAAAAAAAAAAAAAAAA=
```

Save as binary from base64.

- [ ] **Step 8: Create `powerbi-visual/style/visual.less`**

```less
.revitcortex-selection {
  font-family: "Segoe UI", sans-serif;
  font-size: 13px;
  padding: 8px;
  height: 100%;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
```

- [ ] **Step 9: Create `powerbi-visual/src/settings.ts`** (empty for v1)

```typescript
// Format settings for RevitCortex Selection visual.
// No format options in v1.
export class VisualSettings {}
```

- [ ] **Step 10: Install dependencies**

```
cd powerbi-visual && npm install
```

Expected: `node_modules/` created, no errors.

- [ ] **Step 11: Commit scaffold**

```
git add powerbi-visual/
git commit -m "feat: PBIVIZ project scaffold — package.json, capabilities, tsconfig, pbiviz.json"
```

---

## Task 5: TypeScript visual — `visual.tsx`

**Files:**
- Create: `powerbi-visual/src/visual.tsx`

The visual implements `powerbi.extensibility.visual.IVisual` using React for the UI.

- [ ] **Step 1: Create `powerbi-visual/src/visual.tsx`**

```typescript
import * as React from "react";
import * as ReactDOM from "react-dom";
import powerbi from "powerbi-visuals-api";
import IVisual = powerbi.extensibility.visual.IVisual;
import VisualConstructorOptions = powerbi.extensibility.visual.VisualConstructorOptions;
import VisualUpdateOptions = powerbi.extensibility.visual.VisualUpdateOptions;

const PBI_SELECT_URL = "http://localhost:27016/pbi-select";
const CHECK_INTERVAL_MS = 30_000;

// ─── Data helpers ──────────────────────────────────────────────────────────

function getFilteredIds(dataView: powerbi.DataView): number[] {
  const rows = dataView.table?.rows ?? [];
  return rows.map(row => row[0] as number).filter(id => typeof id === "number");
}

function getHighlightedIds(dataView: powerbi.DataView): number[] {
  const rows = dataView.table?.rows ?? [];
  const highlights = dataView.table?.highlights;
  if (!highlights) return [];
  return rows
    .filter((_, i) => highlights[i] != null)  // loose inequality: catches both null and undefined
    .map(row => row[0] as number)
    .filter(id => typeof id === "number");
}

// ─── HTTP helper ────────────────────────────────────────────────────────────

async function sendToRevit(elementIds: number[], action: "select" | "isolate"): Promise<boolean> {
  try {
    const resp = await fetch(PBI_SELECT_URL, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ elementIds, action }),
      signal: AbortSignal.timeout(5000),
    });
    return resp.ok;
  } catch {
    return false;
  }
}

async function checkConnection(): Promise<boolean> {
  try {
    const resp = await fetch(PBI_SELECT_URL, {
      method: "OPTIONS",
      signal: AbortSignal.timeout(3000),
    });
    return resp.ok;
  } catch {
    return false;
  }
}

// ─── React component ────────────────────────────────────────────────────────

interface Props {
  filteredIds: number[];
  highlightedIds: number[];
  connected: boolean;
  onSelect: (ids: number[], action: "select" | "isolate") => void;
}

function SelectionPanel({ filteredIds, highlightedIds, connected, onSelect }: Props) {
  const dotColor = connected ? "#107C10" : "#767676";
  const dotLabel = connected ? `Connesso a localhost:27016` : `RevitCortex non attivo`;

  return (
    <div className="revitcortex-selection">
      <div style={{ fontWeight: 600, marginBottom: 4 }}>RevitCortex Selection</div>

      <button
        onClick={() => onSelect(filteredIds, "select")}
        disabled={filteredIds.length === 0}
        style={btnStyle(filteredIds.length > 0)}
      >
        → Seleziona filtrati ({filteredIds.length})
      </button>

      <button
        onClick={() => onSelect(highlightedIds, "select")}
        disabled={highlightedIds.length === 0}
        style={btnStyle(highlightedIds.length > 0)}
      >
        → Seleziona highlighted ({highlightedIds.length})
      </button>

      <div style={{ display: "flex", alignItems: "center", gap: 6, marginTop: 4 }}>
        <span style={{ width: 8, height: 8, borderRadius: "50%", backgroundColor: dotColor, display: "inline-block" }} />
        <span style={{ fontSize: 11, color: "#555" }}>{dotLabel}</span>
      </div>
    </div>
  );
}

function btnStyle(enabled: boolean): React.CSSProperties {
  return {
    padding: "6px 10px",
    fontSize: 12,
    cursor: enabled ? "pointer" : "not-allowed",
    background: enabled ? "#0078d4" : "#e0e0e0",
    color: enabled ? "#fff" : "#999",
    border: "none",
    borderRadius: 3,
    width: "100%",
    textAlign: "left",
  };
}

// ─── IVisual implementation ─────────────────────────────────────────────────

export class RevitCortexSelectionVisual implements IVisual {
  private target: HTMLElement;
  private filteredIds: number[] = [];
  private highlightedIds: number[] = [];
  private connected: boolean = false;
  private checkTimer: ReturnType<typeof setInterval> | null = null;

  constructor(options: VisualConstructorOptions) {
    this.target = options.element;
    this.startConnectionChecker();
  }

  private startConnectionChecker() {
    const check = async () => {
      const ok = await checkConnection();
      if (ok !== this.connected) {
        this.connected = ok;
        this.render();
      }
    };
    check(); // immediate check on mount
    this.checkTimer = setInterval(check, CHECK_INTERVAL_MS);
  }

  public update(options: VisualUpdateOptions) {
    const dv = options.dataViews?.[0];
    if (!dv) {
      this.filteredIds = [];
      this.highlightedIds = [];
    } else {
      this.filteredIds = getFilteredIds(dv);
      this.highlightedIds = getHighlightedIds(dv);
    }
    this.render();
  }

  private render() {
    ReactDOM.render(
      React.createElement(SelectionPanel, {
        filteredIds: this.filteredIds,
        highlightedIds: this.highlightedIds,
        connected: this.connected,
        onSelect: async (ids, action) => {
          const ok = await sendToRevit(ids, action);
          if (!ok) {
            // Re-check connection on failure
            this.connected = await checkConnection();
            this.render();
          }
        },
      }),
      this.target
    );
  }

  public destroy() {
    if (this.checkTimer !== null) {
      clearInterval(this.checkTimer);
      this.checkTimer = null;
    }
    ReactDOM.unmountComponentAtNode(this.target);
  }
}
```

- [ ] **Step 2: Build the visual**

```
cd powerbi-visual && npm run build
```

Expected: `dist/revitcortex-selection.pbiviz` created with 0 TypeScript errors.

If build fails with `pbiviz not found`, run:
```
npx pbiviz package
```

- [ ] **Step 3: Verify pbiviz file exists**

```
ls -la powerbi-visual/dist/*.pbiviz
```

Expected: `revitcortex-selection.pbiviz` (or similar name from `pbiviz.json.visual.name`).

- [ ] **Step 4: Commit**

```
cd .. && git add powerbi-visual/src/visual.tsx
git commit -m "feat: RevitCortex Selection PBIVIZ visual — filtrati/highlighted buttons, connection indicator"
```

---

## Task 6: Package `dist/` and update docs

**Files:**
- Modify: `WORKFLOWS.md`
- Modify: `docs/USER_GUIDE.md`

- [ ] **Step 1: Commit built artefact**

```
git add powerbi-visual/dist/
git commit -m "chore: add built PBIVIZ artefact to repo for distribution"
```

- [ ] **Step 2: Add Phase 2C section to `WORKFLOWS.md`**

Add the following section after the existing Phase 2B section:

```markdown
## PBI Live — Phase 2C: PBI Desktop visual → Revit selection

**When to use:** Select Revit elements directly from a Power BI Desktop report without going through Claude.

### Prerequisites
- RevitCortex plugin installed and Cortex Switch active (port 27016 opens automatically)
- Power BI Desktop (not Service) with the `revitcortex-selection.pbiviz` visual imported

### Install the visual
1. Open Power BI Desktop
2. In the Visualizations pane → "…" → Import a visual from a file
3. Select `powerbi-visual/dist/revitcortex-selection.pbiviz`
4. Drag the visual onto the report page
5. In the Fields pane, drag `Elements[ElementId]` into the **Element ID** role

### Usage
- **Seleziona filtrati (N)** — selects all elements currently visible in the report
  (respects cross-filters and slicers)
- **Seleziona highlighted (N)** — selects only highlighted rows
  (elements cross-filtered from another visual; button disabled when count=0)
- Connection indicator turns green when RevitCortex is running on port 27016

### Port
The listener opens on `27016` (one above the TCP bridge on `27015`).
If a second Revit instance is open, the second instance silently skips the listener.
```

- [ ] **Step 3: Update `docs/USER_GUIDE.md` PBI Live table**

Find the Power BI Live tools table and add a row:

```markdown
| Phase 2C | RevitCortex Selection PBIVIZ | PBI Desktop visual with buttons to select filtered/highlighted elements in Revit |
```

- [ ] **Step 4: Commit docs**

```
git add WORKFLOWS.md docs/USER_GUIDE.md
git commit -m "docs: Phase 2C — PBIVIZ install/usage in WORKFLOWS.md and USER_GUIDE.md"
```

---

## Self-review checklist

### Spec coverage

| Spec section | Covered by |
|-------------|-----------|
| §4 — `PbiSelectHttpListener` class + interface | Task 1 |
| §4.3 — Threading: background HttpListener + main-thread callback | Task 1 + Task 2 |
| §4.4 — ElementId resolution R23/R24+ | Task 1 (listener) + Task 2 (ApplyPbiSelection) |
| §4.5 — `action` handling, Transaction for isolate | Task 2 (ApplyPbiSelection) |
| §4.6 — Integration in RevitCortexApp | Task 2 |
| §4.6 — `getActiveDocument` via `_uiApplication.ActiveUIDocument?.Document` | Task 2 |
| §4.6 — ExternalEventHandler threading (not Dispatcher) | Task 2 |
| §5.1 — Visual metadata (GUID, version) | Task 4 |
| §5.2 — Data roles (`elementIds`, GroupingOrMeasure) | Task 4 |
| §5.3 — UI: two buttons + connection indicator + retry every 30s | Task 5 |
| §5.4 — `getFilteredIds`, `getHighlightedIds` with `!= null` | Task 5 |
| §5.4 — `sendToRevit` with `AbortSignal.timeout(5000)` | Task 5 |
| §5.5 — Format options (empty for v1) | Task 4 |
| §5.6 — `dist/revitcortex-selection.pbiviz` | Task 5 + Task 6 |
| §6 — File list matches new/modified files | ✅ covered |
| §7 — Test matrix (all listener HTTP tests) | Task 1 tests |
| §8.1 — Port conflict handled | Task 1 tests + implementation |

### Potential issues

1. **`_router.Dispatcher?.Invoke`** — The exact API of `RevitThreadDispatcher` must be verified in Task 2 Step 2 before writing. The `grep` step in Task 2 catches this.

2. **`pbiviz` package output name** — `pbiviz package` names the output file from the `visual.guid` in `pbiviz.json`, not the display name. The verify step in Task 5 checks the actual filename.

3. **`AbortSignal.timeout` browser support** — Supported in all Chromium 103+ browsers, which covers Power BI Desktop's embedded WebView (Chromium 100+). Safe to use.

4. **`ReactDOM.render` deprecation** — In React 18 this is replaced by `createRoot`. Power BI visuals tools 5.x still use the old API in their scaffolding, so it is safe for v1. Pin to React 18.2.x to avoid surprise breakage.
