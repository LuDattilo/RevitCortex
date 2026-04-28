# Cross-App Selection — RevitCortex ↔ NavisCortex

**Date:** 2026-04-28
**Status:** Approved (brainstorming complete, awaiting implementation plan)
**Owner:** Luigi Dattilo

## Goal

Enable bidirectional, symmetric cross-application selection between Revit and
Navisworks — including elements that live inside Revit links — using a single
tool name and schema on both sides, with two LLM calls per operation (export
on one side, import on the other).

## Use cases

1. **Selection Navis → Revit:** select items in Navisworks (host file or any
   federated model), then mirror that selection in Revit, automatically
   resolving each item to either the host document or one of its loaded
   `RevitLinkInstance`s.
2. **Selection Revit → Navis:** select elements in Revit (host + linked via
   `Reference.LinkedElementId`), then mirror that selection in the federated
   Navisworks document.
3. **Clash review Navis → Revit:** given a single `clashGuid` or a
   `clashTestGuid`, export the involved items in one call and apply them in
   Revit in the second call (selection + isolate + section box + DirectShape
   markers on linked targets).

## Non-goals

- No network coupling between the Revit and Navisworks processes.
- No file-bridge or clipboard handoff format.
- No new persistent selection-set entity.
- No new ribbon UI — the MCP tools are the surface.
- No IFC standalone "third side" — IFC enters only as a fallback identifier
  (`ifcGuid`) when the federated file is IFC/NWC.

## Hard constraint: do not break existing tools

The rest of RevitCortex/NavisCortex is in production and stable. The only
missing piece is cross-app interop. The implementation MUST be additive:

- No modifications to `ShowCrossModelElementsTool`, `GetRevitReferencesTool`,
  `SelectItemsByPathTool`, `GetClashesTool`, `GetLinkedElementsTool`,
  `ItemPathResolver`, `ClashSelectionResolver`, `RevitReferenceBuilder`.
- If reuse requires extracting a helper class from `ShowCrossModelElementsTool`,
  the existing tool stays a thin wrapper — same JSON output for the same
  input, all current tests still passing.
- Full cross-target matrix must build green before declaring done:
  - **RevitCortex:** `Debug R23`, `Debug R24` (net48), `Debug R25`,
    `Debug R26` (net8), `Debug R27` (net10).
  - **NavisCortex:** `Debug N23`, `Debug N24`, `Debug N25`, `Debug N26`
    (all net48).
- Honor the net48 vs net8+ feature constraints from CLAUDE.md (no `record`,
  no `init`, no `Index`/`Range`, no `Dictionary.GetValueOrDefault`,
  `ElementId.IntegerValue` on R23/R24 vs `.Value` on R25+).

## Architecture

One symmetric tool per side, same name, same I/O schema:

- **Revit:** `RevitCortex.Tools.Interop.CrossAppSelectionTool` →
  registered as `cross_app_selection`.
- **Navis:** `NavisCortex.Tools.Interop.CrossAppSelectionTool` →
  registered as `cross_app_selection`.

Two modes via the `mode` parameter: `"export"` produces an array of
`cortexElementRef`; `"import"` consumes that same array and applies the
selection. The payload travels through the LLM/MCP client — no inter-process
communication between the Revit and Navis servers.

### Shared identifier — `CortexElementRef` (already exists)

A DTO with the same shape already lives in **both** Core projects:

- `RevitCortex.Core.Interop.CortexElementRef`
- `NavisCortex.Core.Interop.CortexElementRef`

Schema (existing):

```json
{
  "sourceApp": "Navis",
  "sourceFile": "Strutture.rvt",
  "navisInstanceGuid": "abc-123",
  "revitElementId": "606873",
  "revitUniqueId": "c8c4e8e3-...-0001a4f8",
  "ifcGuid": "1Hd...",
  "category": "OST_StructuralFraming",
  "family": "W-Wide Flange",
  "type": "W12X26"
}
```

The new tools reuse this DTO unchanged on both sides — no new schema, no
duplication. Behavior:

- `sourceFile` is the basename of the originating Revit project file or
  federated model file (case-insensitive matching). Required for resolution.
- Resolution priority on the consuming side: `revitUniqueId` → `ifcGuid` →
  `revitElementId`. The first that resolves wins.
- `sourceApp` is informational ("Revit" or "Navis") — useful for debugging.
- `category` should use `OST_*` BuiltInCategory codes when produced from
  Revit (language-independent, per the CLAUDE.md cheat sheet); from Navis
  it carries whatever `RevitReferenceBuilder` extracted.
- The "isLinked host vs link" distinction does NOT need a flag in the DTO —
  it is recomputed on import by looking up `sourceFile` against the loaded
  links of the active Revit document.

## Tool contract

### Input — `mode: "export"`

```json
{
  "mode": "export",
  "useCurrentSelection": true,
  "clashGuid": "<navis only, optional>",
  "clashTestGuid": "<navis only, optional>"
}
```

Source-of-truth priority on Navis: `clashGuid` > `clashTestGuid` >
`useCurrentSelection`. On Revit only `useCurrentSelection` is supported.

### Output — `mode: "export"`

```json
{
  "side": "revit",
  "exportedCount": 5,
  "refs": [ /* cortexElementRef[] */ ],
  "skipped": [ { "reason": "no source file resolved", "elementId": 12345 } ]
}
```

The `refs` array is the literal input expected by the opposite side's
`mode=import` — copy/paste, no transformation.

### Input — `mode: "import"`

```json
{
  "mode": "import",
  "refs": [ /* cortexElementRef[] */ ],
  "append": false,
  "isolate": true,
  "createSectionBox": true,
  "createLinkedMarkers": true
}
```

`append`, `isolate`, `createSectionBox`, `createLinkedMarkers` default to the
same values as the existing tools they delegate to. The last three flags are
Revit-only (Navis ignores them).

### Output — `mode: "import"`

```json
{
  "side": "navis",
  "requested": 5,
  "resolved": 4,
  "selected": 4,
  "hostMatches": 2,
  "linkedMatches": 2,
  "notFound": [
    { "ref": { "sourceFile": "X.rvt", "uniqueId": "..." },
      "reason": "source file not loaded" }
  ],
  "isolatedView": "3D - Cross App",
  "markers": [ /* directShape ids, revit only */ ]
}
```

## Pipeline

| Use case | Call 1 | Call 2 |
|---|---|---|
| Selection Navis → Revit | `cross_app_selection` (Navis, `mode=export`) | `cross_app_selection` (Revit, `mode=import, refs=…`) |
| Selection Revit → Navis | `cross_app_selection` (Revit, `mode=export`) | `cross_app_selection` (Navis, `mode=import, refs=…`) |
| Clash Navis → Revit | `cross_app_selection` (Navis, `mode=export, clashGuid=…`) | `cross_app_selection` (Revit, `mode=import, refs=…`) |

Always two calls. Output of call 1 is the literal input of call 2.

## Components

### Revit (additive)

```
src/RevitCortex.Tools/Interop/
├── CrossAppSelectionTool.cs       # tool: mode=export|import dispatcher
├── HostLinkResolver.cs            # sourceFile → host/link lookup;
│                                  # CortexElementRef → ElementId resolution with fallbacks
└── SelectionExporter.cs           # Selection.GetReferences() → CortexElementRef[]
```

`CortexElementRef` already exists in `RevitCortex.Core.Interop` and is
reused as-is.

`mode=import` builds the `{hostElementIds, linkedElements}` payload and then
applies it through one of two reuse paths (decided in the implementation
plan, not here):

1. **Composition via tool registry:** `CrossAppSelectionTool` invokes
   `show_cross_model_elements` internally with the same JSON it would
   accept from MCP. Zero source changes to the existing tool.
2. **Shared helper extraction:** the selection/isolate/marker core of
   `ShowCrossModelElementsTool` is moved into a new `CrossModelSelector`
   class, and `ShowCrossModelElementsTool` becomes a thin wrapper over it.
   This is allowed ONLY if it preserves 100% backward compatibility:
   identical JSON output for identical input, all current tests still
   passing, no signature changes.

Both paths leave external consumers of `show_cross_model_elements`
unaffected.

### Navis (additive)

```
src/NavisCortex.Tools/Interop/
├── CrossAppSelectionTool.cs       # tool: mode=export|import dispatcher
├── RevitRefMatcher.cs             # CortexElementRef[] → ModelItem[]
│                                  # single-pass tree scan, S35/S40 hardened
└── SelectionExporter.cs           # CurrentSelection / clashGuid → refs
                                   # reuses RevitReferenceBuilder (read-only)
```

`CortexElementRef` already exists in `NavisCortex.Core.Interop` and is
reused as-is. `RevitReference` + `RevitReferenceBuilder` (in the same Core
file) are reused read-only for the export path.

`mode=import` calls `doc.CurrentSelection.AddRange` with the same hardening
pattern as `SelectItemsByPathTool` (HPCSE on `Execute`, isolated `AddRange`).

### Tools touched only as readers / callers (never modified)

- `ShowCrossModelElementsTool` (Revit) — invariant.
- `GetRevitReferencesTool` (Navis) — invariant; `RevitReferenceBuilder` is
  consumed as a library by `SelectionExporter`.
- `ItemPathResolver` (Navis) — invariant; the manual-enumerator hardening
  pattern is replicated in `RevitRefMatcher` (separate matching predicate).

## Resolution logic

### Revit `mode=import`

1. Build a `Dictionary<string, RevitLinkInstance?>` mapping basename
   (lowercased) → `RevitLinkInstance` for every loaded link, plus an entry
   for the host document with value `null` (sentinel = "host").
2. For each ref:
   - Lookup `ref.sourceFile` basename. If absent → `notFound`.
   - If host: resolve in active doc. Try `doc.GetElement(uniqueId)` →
     IfcGuid parameter scan → `doc.GetElement(new ElementId(elementId))`.
     Append to `hostElementIds`.
   - If link: open `linkInstance.GetLinkDocument()`, run the same cascade.
     Append `{instanceId: linkInstance.Id, linkedElementId: element.Id}`.
3. Hand the accumulated payload to the shared selection logic (selection +
   isolate + section box + markers, single transaction). Same UX as
   `show_cross_model_elements` today.

### Revit `mode=export`

For each `Reference` in `UIDocument.Selection.GetReferences()`:

- Host case (`LinkedElementId == InvalidElementId`):
  `sourceFile = doc.PathName.basename`, `uniqueId = element.UniqueId`,
  `ifcGuid = element.LookupParameter("IFC GUID")?.AsString()`,
  `elementId = element.Id.Value`, `isLinked = false`.
- Linked case: resolve `linkInstance` and `linkDoc`, then
  `sourceFile = linkDoc.PathName.basename`,
  `uniqueId = linkedElement.UniqueId`, etc., `isLinked = true`.
- `category` = `OST_*` from `BuiltInCategory` enum.

### Navis `mode=export`

Three exclusive sources, decreasing priority:

1. `clashGuid` → 1 clash → 2 `ModelItem`s (Item1, Item2).
2. `clashTestGuid` → all clashes of the test → 2N `ModelItem`s.
3. `useCurrentSelection=true` (default) → `doc.CurrentSelection.SelectedItems`.

For each `ModelItem`, reuse `GetRevitReferencesTool.ExtractReference` →
`RevitReferenceBuilder` → emit `cortexElementRef` with
`sourceFile = Model.FileName.basename`, `uniqueId`, `ifcGuid`, `elementId`,
`category`.

### Navis `mode=import`

Single-pass strategy (mirrors `ItemPathResolver.ResolveManyByInstanceGuid`
hardening):

1. Group input `refs` by `sourceFile` basename.
2. For each group, locate the matching `Model.RootItem` (skip groups whose
   sourceFile is not loaded; emit them as `notFound`).
3. For each group's root, scan `DescendantsAndSelf` once. For each visited
   item, read its BIM properties via `RevitReferenceBuilder`. Compare against
   the pending refs for that group: priority `uniqueId` → `ifcGuid` →
   `elementId`. Match removes the ref from "pending"; group ends when
   pending is empty.
4. Accumulate matched `ModelItem`s; commit via `doc.CurrentSelection.Clear()`
   (unless `append=true`) + `AddRange(matched)`.

Cost: O(M) on a single federated file's tree per group, not on the whole
federated model.

## Error handling

Partial-success semantics throughout (matches `select_items_by_path` and
`GetRevitReferencesTool`).

**Hard fails (`CortexResult.Fail`):**

- No active document.
- `mode` missing or not in `{"export","import"}`.
- `mode=import` with empty/missing `refs`.
- `mode=export` with `clashGuid`/`clashTestGuid` provided but unresolvable
  (Navis only).

**Partial success (`Ok` with `notFound`):**

- A single ref fails to resolve → entry in `notFound[]` with `reason`.
- `sourceFile` does not match any loaded link or host.
- All three identifier fallbacks (`uniqueId`/`ifcGuid`/`elementId`) fail
  for a ref.

**Hardening (Navis):**

- `[HandleProcessCorruptedStateExceptions]` + `[SecurityCritical]` on the
  Navis-side `Execute` (matches `SelectItemsByPathTool`).
- Manual enumerator with try/catch around `MoveNext` and `Current` during
  tree scan (BIM 360 demand-load AVE protection).
- `CurrentSelection.AddRange` wrapped separately so a commit failure still
  returns useful diagnostics instead of crashing the server.

**Logging:** every catch path goes through `CortexDebugLog.LogException`
with a dedicated tag (e.g., `CrossAppSelectionTool.Import.Resolve`,
`.Export.ReadSelection`).

## Testing

### Unit tests (additive)

`RevitCortex.Tests/Interop/CrossAppSelectionToolTests.cs`:

- `Export_HostElement_ReturnsRefWithIsLinkedFalse`
- `Export_LinkedElement_ReturnsRefWithIsLinkedTrueAndCorrectSourceFile`
- `Export_MixedSelection_ReturnsBothCorrectly`
- `Import_HostUniqueId_Selects`
- `Import_LinkedUniqueId_SelectsAndCreatesMarker`
- `Import_FallbackToIfcGuid_WhenUniqueIdMissing`
- `Import_UnknownSourceFile_ReportsNotFound_OthersResolve`
- `Import_EmptyRefs_FailsClean`
- `RoundTrip_ExportThenImport_PreservesSelection`

`NavisCortex.Tests/Interop/CrossAppSelectionToolTests.cs`:

- `Export_CurrentSelection_ReturnsRefsFromBimProperties`
- `Export_ClashGuid_ReturnsTwoRefs`
- `Export_ClashTestGuid_ReturnsAllClashItems`
- `Import_SinglePassScansOnlyMatchingSourceFile`
- `Import_PartialSuccess_OnUnresolvedRef`
- `Import_HpcseHardening_DoesNotCrashOnTreeException`

### Integration smoke tests (manual, documented)

1. Revit: open host + 1 link. Select 1 host + 1 linked element.
   `cross_app_selection mode=export` → copy `refs`.
2. Navis: open the federated model. `cross_app_selection mode=import refs=…`
   → both items selected in the federated view.
3. Navis: open a clash test. `mode=export clashGuid=…` → copy `refs`.
4. Revit: `mode=import refs=…` → selection + DirectShape markers + section
   box visible.

### Cross-target build

All target configurations must succeed before declaring done:

- **RevitCortex:** `Debug R23`, `Debug R24` (net48), `Debug R25`, `Debug R26`
  (net8.0-windows), `Debug R27` (net10.0-windows). R27 needs the .NET 10
  SDK pinned via `global.json` (see release_r27_sdk memory).
- **NavisCortex:** `Debug N23`, `Debug N24`, `Debug N25`, `Debug N26`
  (all net48).

The R23/R24/N23–N26 net48 leg is the strictest — language features valid
on net8/net10 (records, `init`, `Index`/`Range`, default interface methods,
`Dictionary.GetValueOrDefault`) will silently fail there. ElementId access
also splits: `.IntegerValue` on R23/R24, `.Value` on R25+.

## Decisions log

- **D1 — direction:** bidirectional, symmetric (option C).
- **D2 — identifier:** `(sourceFile, uniqueId)` with `ifcGuid` and
  `elementId` fallbacks (option C).
- **D3 — coupling:** payload through LLM/MCP client only — no
  network/file/clipboard bridge (option A).
- **D4 — surface:** one tool per side, two modes (`export`/`import`),
  symmetric schema. Two LLM calls per operation. Replaces the originally
  proposed three-tool split (`resolve_cross_app_refs`, `export_cross_app_refs`,
  `find_items_by_revit_ref`) for token/runtime efficiency.
- **D5 — additivity:** no modifications to existing tools; reuse via
  composition or thin extraction only.
