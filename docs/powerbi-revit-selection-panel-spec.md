# RevitCortex Selection — Power BI Visual Specification

**Status:** Patch 1 applied — pending commit (v1.0.0.9)
**Date:** 2026-05-12
**Visual GUID:** `revitcortexselectionvisual1A2B3C4D`
**Display name:** RevitCortex Selection
**Current version:** `1.0.0.9` (pending) — last committed `1.0.0.7`. Intermediate `1.0.0.8` superseded.

**Files:**
- `powerbi-visual/src/visual.tsx` — main React/TS source (~800 lines)
- `powerbi-visual/src/settings.ts` — minimal stub (placeholder for future formatting props)
- `powerbi-visual/capabilities.json` — data roles + WebAccess privilege
- `powerbi-visual/pbiviz.json` — visual metadata
- `powerbi-visual/package.json` — npm metadata
- `powerbi-visual/dist/*.pbiviz` — packaged builds
- `src/RevitCortex.Plugin/PowerBiLive/PbiSelectHttpListener.cs` — server counterpart (HTTP listener on port 27016)

---

## 1. Purpose

A Power BI custom visual that turns any tabular report into a remote control for the active Revit document. The user drops one column with element IDs into the visual; the visual then exposes five actions that round-trip to Revit over an HTTP localhost link:

1. **Select** — select the filtered/highlighted elements in Revit.
2. **Isolate** — selection + temporary isolate in the active view.
3. **Color** — apply graphic overrides per distinct value in the optional `colorBy` field.
4. **Create 3D view** — generate a new 3D view with a section box around the elements.
5. **Reset overrides** — clear all graphic overrides on the active view.

The user-facing payoff: filter a PBI report by category/discipline/issue/whatever, then drive Revit visually without typing IDs.

## 2. Wire contract

The visual posts JSON to a localhost HTTP listener bound to port `27016` (declared in `capabilities.json` under `WebAccess` privileges). The plugin-side listener is `PbiSelectHttpListener` and routes by `req.Url.AbsolutePath`:

| Method | Path | Payload | Response (200) |
|---|---|---|---|
| `OPTIONS` | `/pbi-select` | — | health-check ping (visual uses this every 30 s) |
| `POST` | `/pbi-select` | `{ elementIds: number[], action: "select" \| "isolate" }` | `{ success: true, validated: "N" }` |
| `POST` | `/pbi-color` | `{ items: [{ id: number, hex: "#RRGGBB" }] }` | `{ success: true, validated: "N" }` |
| `POST` | `/pbi-reset-overrides` | `{}` | `{ success: true, validated: "N" }` |
| `POST` | `/pbi-create-view` | `{ elementIds: number[], viewName?: string }` | `{ success: true, validated: "<new view name>" }` |

The visual treats `r.ok && r.body?.success === true` as the success criterion. Any other outcome (timeout, non-200, `success: false`, JSON parse error) triggers a passive re-check of the connection state but does NOT show an error toast to the user. See §9 (gaps).

## 3. Data contract (`capabilities.json`)

```json
{
  "dataRoles": [
    { "name": "elementIds", "kind": "Grouping", "displayName": "Element ID" },
    { "name": "colorBy",    "kind": "Grouping", "displayName": "Color by" }
  ],
  "dataViewMappings": [{
    "table": {
      "rows": {
        "select": [ { "for": { "in": "elementIds" } }, { "for": { "in": "colorBy" } } ],
        "dataReductionAlgorithm": { "top": { "count": 30000 } }
      }
    }
  }],
  "supportsHighlight": true,
  "privileges": [{ "name": "WebAccess", "essential": true, "parameters": ["http://localhost:27016"] }]
}
```

- `elementIds` (required Grouping): typed as number; non-number rows are silently skipped in `extractData`.
- `colorBy` (optional Grouping): any categorical field. Each distinct value gets a deterministic color from `CATEGORICAL_PALETTE` (16-stop Power BI default palette) via FNV-1a hash. Hex strings (`"#E53935"`, optionally with alpha) are honored as-is.
- `supportsHighlight: true` enables PBI's cross-filter "highlights" array — when present and non-null at row index `i`, that row is "active".
- Cap: 30 000 rows. The visual handles row arrays this size in a single linear pass per `update()`.

## 4. State model

The visual is a class-based `IVisual` (`RevitCortexSelectionVisual`) that holds:

```ts
private filteredIds: number[];          // all rows that survived PBI filters
private highlightedIds: number[];       // subset highlighted by cross-filter (may be empty)
private filteredColored: ColoredRow[];  // {id, hex} for rows with a resolved color
private highlightedColored: ColoredRow[];
private hasColorColumn: boolean;
private connected: boolean;             // last health-check result
private feedback: Feedback | null;      // success toast payload
private feedbackVisible: boolean;       // toggles slide-in/fade-out anim
private busy: BusyKind;                 // which action is currently in-flight
private checkTimer, feedbackTimer, feedbackFadeTimer: timers
```

The React component (`SelectionPanel`) is a function component that re-renders on every host call to `render()`. State is owned by the class; the component is purely presentational.

**Highlight logic** in the component:

```ts
const useHighlighted = highlightedIds.length > 0;
const activeIds      = useHighlighted ? highlightedIds : filteredIds;
const activeLabel    = useHighlighted ? t.highlighted   : t.filtered;
```

So when a cross-filter is active, actions operate on highlighted rows; otherwise on all filtered rows. The primary button shows `<active count> <label> · <total count> totali` when the two differ.

## 5. UI layout

```
┌────────────────────────────────────────────────────────┐
│ ● Connected to Revit          (or ● not connected)     │
│                                                        │
│ ┌────────────────────────────────────────────────────┐ │
│ │  Select in Revit                                   │ │
│ │  12 highlighted · 87 totali                        │ │
│ └────────────────────────────────────────────────────┘ │
│ ┌──────────────────────────┬────────────────────────┐  │
│ │  Isolate in Revit        │  Color in Revit (12)   │  │
│ └──────────────────────────┴────────────────────────┘  │
│ ┌──────────────────────────┬────────────────────────┐  │
│ │  Create 3D view…         │  Reset overrides       │  │
│ └──────────────────────────┴────────────────────────┘  │
│  ✓ Sent 12 elements  ← toast (slide-in 180 ms,        │
│                         fade-out 220 ms, total 3 s)   │
└────────────────────────────────────────────────────────┘
```

Style: Fluent-ish neutral. Primary button accent `#0078D4` (PBI/Fluent blue). Sharp 2 px corners. Hover/active darken. Focus ring `0 0 0 2px #0078D440`. Status dot `#107C10` (green) when connected, `#A19F9D` (gray) when not. Toast `#DFF6DD` background, `#107C10` text. No product branding inside the visual — title comes from PBI host (`Format → Title`).

## 6. Behavior details

### 6.1 Connection check

`checkConnection()` does an `OPTIONS /pbi-select` with `AbortSignal.timeout(3000)`. The visual runs it:
- Once on `constructor()`.
- Every `CHECK_INTERVAL_MS = 30_000` via `setInterval`.
- After any failed action (silent recovery — re-checks but doesn't notify).

If the result changes, the class flips `this.connected` and re-renders. The status dot + sublabel update accordingly.

### 6.2 Action lifecycle

`runAction(kind, fn)` is the in-flight guard:

```ts
if (this.busy != null) return;   // one action at a time, period
this.setBusy(kind);
try { await fn(); }
finally { this.setBusy(null); }
```

The button matching `busy` keeps `enabled = busy === <kind>` so its spinner stays visible until the POST resolves; all other buttons disable for the duration.

### 6.3 Feedback toast

On success the class calls `showFeedback({ kind, count|name })`:
- Renders the toast with `rc-toast-in` slide-in animation.
- After 2 780 ms flips `feedbackVisible = false` → `rc-toast-out` fade-out begins.
- After 3 000 ms total, clears `this.feedback` and re-renders without the toast node.

Both timers are cancelled and re-armed on each new feedback event so back-to-back actions don't queue up stale toasts.

### 6.4 Color resolution

```
resolveColor(raw):
  null/empty               → null  (no override emitted for this row)
  matches /^#?[0-9a-f]{6}(.{2})?$/i  → normalized "#RRGGBB(AA)"
  else                     → CATEGORICAL_PALETTE[fnv1a(str) % 16]
```

`fnv1a` is a 32-bit hash so identical category values map to identical colors across all renders, all sessions, and across users. The 16-color palette is hard-coded; not theme-aware.

### 6.5 i18n

`detectLang()`:
1. `host.locale` (PBI culture string like `"it-IT"`).
2. `navigator.language` and `navigator.languages` fallback.
3. Defaults to English.

Any candidate starting with `it` (case-insensitive) flips to Italian. Two `Strings` records inline in `STRINGS: Record<Lang, Strings>`. No extra string files — keeps the .pbiviz small.

## 7. Build & version flow

| File | Format | Role |
|---|---|---|
| `pbiviz.json` | metadata | Source of truth for `visual.version`. Read by `pbiviz package`. |
| `package.json` | npm | Display version + scripts (`build`, `package`, `start`). Should mirror `pbiviz.json`. |
| `dist/package.json` | inside .pbiviz | Generated copy embedded in the package. Should mirror `pbiviz.json` after build. |
| `dist/<name>.<version>.pbiviz` | binary | The actual installable artifact (one per version). |

Build command: `cd powerbi-visual && pbiviz package`. The resulting `.pbiviz` is uploaded to a PBI Desktop report via "More visuals → Import a visual from a file".

## 8. Patch 1 — v1.0.0.9 (applied)

Triggered by an independent design review pointing out semantic / UX gaps in the v1.0.0.7 visual. The cosmetic 1.0.0.8 (hide stale sublabel when disconnected) is rolled into the 1.0.0.9 cut, so 1.0.0.8 is never published.

### 8.1 Server-side (`PbiActionEventHandler.cs`)

**`ExecuteResetOverrides`** — now disables temporary view isolation BEFORE clearing graphic overrides. Previously a user that clicked "Isola" then "Reset" was left stuck in temporary isolation mode because the handler only touched overrides, never the view's `TemporaryHideIsolate` state.

```csharp
try {
    using var txIso = new Transaction(doc, "PBI reset temporary isolation");
    txIso.Start();
    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
    txIso.Commit();
} catch { /* no temp mode active — fine */ }
// ... then clear PBI-tracked overrides as before ...
```

The override clear remains scoped to PBI-tracked elements only (via `_registry.GetTracked`), so manual overrides created by the user or other add-ins are never touched. The reviewer's "two reset modes" suggestion (`revitcortex` vs `all_view_overrides`) is out of scope — the conservative default is already correct.

**`ExecuteCreateView`** — now activates the new view AND restores the selection on it. Previously the new view was created in Project Browser but the user stayed on their old view, which was confusing for the action label "Create 3D view **from** selection".

```csharp
try {
    var uiDoc = new UIDocument(doc);
    uiDoc.ActiveView = newView;
    uiDoc.Selection.SetElementIds(validIds);
} catch (Exception ex) {
    Trace.WriteLine($"CreateView activate failed (non-fatal): {ex.Message}");
}
```

### 8.2 Visual-side (`visual.tsx`)

**Reset button rename**: `"Reset overrides"` → `"Reset view"`; `"Reset override"` → `"Reset vista"`. Tooltip updated to mention both isolation and overrides.

**Resolved-count feedback on Select/Isolate**: when the server's `validated` (resolved count) is less than `ids.length` (requested), the toast splits into two-number form:

- All resolved: `✓ Inviati 120 elementi`
- Partial: `✓ Inviati 118 di 120 (2 non trovati)`

**Reset feedback split**: `validated === 0` (nothing was painted) shows `✓ Vista ripristinata (nessun override RevitCortex)` instead of the previous `✓ Reset 0 elementi` which made it sound like the action no-op'd.

**Error toast** (closes gap #1 from §9): on any non-success response, the visual now renders a red toast with one of three messages — `errorNotConnected` / `errorTimeout` / `errorGeneric` (with server `error` payload appended when present). Previously failures were silent except for the dot turning gray.

Implementation: `Feedback` union extended with `{ kind: "error"; message: string }`. New palette entries `errorBg` / `errorText` (Fluent system red `#A4262C`). Toast container picks bg/border/text color via `feedback.kind === "error"`.

**Sublabel hide when disconnected**: the 1.0.0.8 cosmetic fix is included — the primary button no longer shows a stale "N filtrati" sublabel when the connection dot is gray.

**Bump**: `1.0.0.7` → `1.0.0.9` in `pbiviz.json`, `package.json`, `dist/package.json`. Built artifact: `dist/revitcortexselectionvisual1A2B3C4D.1.0.0.9.pbiviz` (52 KB).

## 9. Known gaps / open improvements

Patch 1 (v1.0.0.9) addresses #1 (error toast) and #6 (reset clean-state messaging) and adds Reset-isolation + CreateView-activate. The list below is what remains for a hypothetical Patch 2:

1. ~~No error toast on failure.~~ **Fixed in v1.0.0.9.**

2. **`UniqueId` support as primary stable key.** Today the visual sends `elementIds` as integers — these survive most edits but are invalidated by purge or family reload. UniqueId is stable across the model's lifetime. Cost: dual data role in `capabilities.json`, dual resolver server-side, CSV exporter has to include `UniqueId`. Patch 2 candidate.

3. **Document title validation.** No check that the PBI dataset's `DocumentTitle` matches the active Revit doc. A user with two models open and the wrong one focused will silently get partial/no results. Add a `documentTitle` field to the POST payload; server returns `{ error: "wrong_document", expected: "...", actual: "..." }` and the error toast tells the user to switch. Patch 2 candidate.

4. **Reset overrides has no confirmation when N > 0.** Currently a single click clears all PBI-tracked overrides on the view. For most users this is fine because the scope is PBI-only, but a "Click again to confirm" pattern would be safer for large counts. Low priority.

5. **Color button label `(N)` is ambiguous.** Reads as "12" → could be 12 colors or 12 elements. The tooltip clarifies but the label doesn't. Consider `Color (12 elem.)` or just dropping the count from the label.

6. **30 s connection check is slow on first sight.** If the user opens PBI before starting Revit, they wait up to 30 s for the dot to go green. Could move to 10 s — backend OPTIONS is cheap, and the visual cancels in-flight requests on `destroy()`.

7. **Highlight detection uses runtime-typed `highlights` array.** `dataView.table.highlights` isn't in the public PBI type — the visual reads it via `as unknown as { highlights?: ... }`. Stable in practice but technically undocumented. Worth a comment (which the code already has) and worth keeping an eye on PBI runtime changes.

8. **`settings.ts` is a stub.** The visual exposes no formatting props (theme color, button label override, etc.). Adding even a single `accentColor` would let users theme it to their report.

9. **`AbortSignal.timeout(15000)` shared across all actions.** Create-view legitimately needs this long; select/color/reset on hundreds of elements typically resolves in <500 ms. Differentiating per action would surface "Revit hung" faster on the simple ones.

10. **"Mostra in Revit" as separate action (debated).** The reviewer proposed splitting Select (just `SetElementIds`) from Show (Select + `ShowElements`). Currently we only do Select. SheetLink combines the two; the cost (UI footprint) might not be worth the marginal gain. Defer until user feedback says it's needed.

## 10. Pre-conditions for the user (smoke test)

1. Revit 2024+ open with a document loaded.
2. RevitCortex plugin loaded; the PBI live HTTP listener bound to `127.0.0.1:27016`. The plugin logs `[PbiSelectHttpListener] listening on 27016` when ready.
3. PBI Desktop report with at least one table that has a numeric `ElementId` column.
4. The visual imported into the report (Format → More visuals → Import → select the `.pbiviz`).
5. Drop `ElementId` into the visual's "Element ID" role. (Optional: drop another categorical field into "Color by".)

If everything is wired correctly: the status dot turns green within 30 s; the primary button shows "Select in Revit" + the row count below.

## 11. Test checklist

### Connection / health

- [ ] Open the visual with RevitCortex NOT running. Status dot is gray, text "RevitCortex non attivo". Primary button shows label only, no sublabel.
- [ ] Start RevitCortex in Revit. Within 30 s the dot turns green and the sublabel appears.
- [ ] Stop RevitCortex. Within 30 s the dot turns gray again. (Or trigger an action first — the failure path re-checks immediately.)

### Select / Isolate

- [ ] Apply a slicer to the report so it filters down to 5–10 elements. Click "Select in Revit". Revit selects those elements. Toast `✓ Inviati N elementi`.
- [ ] Click "Isolate in Revit". Revit selects AND isolates the elements in the active view.
- [ ] Cross-filter from another visual (e.g. a bar chart) so only 3 rows are highlighted. Primary button now shows `3 evidenziati · 10 totali`. Click Select → only the 3 are selected in Revit.
- [ ] Click Select with an empty filter result. Button is disabled.

### Color

- [ ] Without "Color by" mapped: Color button is disabled, tooltip says "drop a categorical field".
- [ ] Map "Discipline" (or any categorical column). Button enables and shows `Color in Revit (N)` where N matches `filteredColored.length`.
- [ ] Click Color. Revit applies an override per distinct value. Toast `✓ Colorati N elementi`.
- [ ] Map a column containing literal hex strings like `#E53935`. The visual uses those values as-is (no palette hashing).

### Reset view (renamed in v1.0.0.9 from "Reset overrides")

- [ ] After a Color action, click Reset. Toast `✓ Vista ripristinata · N override rimossi`.
- [ ] Click Reset on a view that's never been colored by RevitCortex. Toast `✓ Vista ripristinata (nessun override RevitCortex)` — NOT "Reset 0 elementi".
- [ ] Use "Isola in Revit" to enter temporary isolation. Click Reset. Temporary isolation must be DISABLED (the previously-hidden elements are visible again). Toast appears.
- [ ] Apply a manual color override in Revit (e.g. via VG → Categories override). Click Reset. The manual override must NOT be cleared. Only RevitCortex-applied overrides are removed.

### Create 3D view (changed in v1.0.0.9)

- [ ] Filter to 3–5 elements. Click "Crea vista 3D da selezione". Revit creates a new 3D view, **activates it**, and **selects the elements**. Section box is applied. Toast `✓ Vista creata e aperta: <name>`.
- [ ] Verify the original view is intact (markup/annotations preserved). The user can go back to it from Project Browser.

### Error toast (new in v1.0.0.9)

- [ ] Stop RevitCortex; try any action. Red toast `✗ RevitCortex non attivo`.
- [ ] Trigger a timeout (e.g. modal dialog open in Revit blocking the external event for >15 s). Red toast `✗ Revit non ha risposto`.
- [ ] Trigger a server-side error (e.g. close the only open document then try Select). Red toast with the server's error message.

### Locale

- [ ] PBI Desktop set to Italian: strings appear in Italian.
- [ ] PBI Desktop set to English: strings in English.

### Cross-feature

- [ ] Trigger two actions in rapid succession. The second one is rejected silently (`runAction` guard); only the first runs. Spinner stays on the first button until it resolves.

## 12. Commit (v1.0.0.9 — Patch 1)

```
fix(pbi-visual): Reset view + activate created view + error toast; v1.0.0.9

Server (PbiActionEventHandler):
- ExecuteResetOverrides now disables TemporaryHideIsolate before clearing
  overrides, so the "Isola → Reset" sequence actually exits isolation. Was
  a bug: the user got stuck in temp-isolation mode after Reset.
- ExecuteCreateView now sets UIDocument.ActiveView to the new view AND
  re-selects the elements on it. The previous "stay on current view"
  behaviour was confusing for an action labelled "Create view from
  selection" — users had to dig into Project Browser to find what they had
  just created.

Visual (1.0.0.7 → 1.0.0.9; 1.0.0.8 was intermediate, superseded):
- "Reset overrides" / "Reset override" renamed to "Reset view" / "Reset
  vista". Tooltip updated to call out both isolation and overrides.
- Toast on Select/Isolate now compares server-reported resolved count to
  requested; shows "Inviati 118 di 120 (2 non trovati)" when partial.
- Toast on Reset shows "(nessun override RevitCortex)" when count == 0.
- Toast on CreateView says "creata e aperta" instead of "creata".
- New error toast (red) on any non-success response, with separate
  copy for notConnected / timeout / generic server error. Replaces the
  previous silent connection re-check.
- Sublabel of the primary button hidden when disconnected (rolled in
  from the intermediate 1.0.0.8).
```

Files in the commit:
- `src/RevitCortex.Plugin/PowerBiLive/PbiActionEventHandler.cs`
- `powerbi-visual/src/visual.tsx`
- `powerbi-visual/package.json`
- `powerbi-visual/pbiviz.json`
- `powerbi-visual/dist/package.json`
- `powerbi-visual/dist/revitcortexselectionvisual1A2B3C4D.1.0.0.9.pbiviz` (the built artifact)
- `docs/powerbi-revit-selection-panel-spec.md` (this document)

The unstaged `dist/revitcortexselectionvisual1A2B3C4D.1.0.0.8.pbiviz` is left untracked — it was an intermediate build that v1.0.0.9 supersedes.
