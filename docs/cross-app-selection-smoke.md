# Cross-App Selection — Smoke Tests

Run these manually after every release that touches the interop tools.

## 1. Selection Revit → Navis

1. Revit: open a project with at least one Revit link loaded.
2. Select 1 host element + 1 element inside the link (use TAB to pick into the link).
3. Call MCP tool: `cross_app_selection { mode: "export" }` on Revit. Copy `refs`.
4. Navis: open the federated NWF/NWD that contains both source files.
5. Call MCP tool: `cross_app_selection { mode: "import", refs: <pasted> }` on Navis.
6. Expected: both items selected in Navis with selection count = 2.

## 2. Selection Navis → Revit

1. Navis: select 2 items belonging to two different source files.
2. Call: `cross_app_selection { mode: "export" }` on Navis. Copy `refs`.
3. Revit: open the host project for one of those source files (the other will be a link).
4. Call: `cross_app_selection { mode: "import", refs: <pasted> }` on Revit.
5. Expected: host element selected; linked element flagged with a red DirectShape marker; section box framing both; isolate active.

## 3. Clash Navis → Revit

1. Navis: open a clash test with at least one clash.
2. Call: `cross_app_selection { mode: "export", clashGuid: "<one clash guid>" }`. Copy `refs`.
3. Revit: same host project.
4. Call: `cross_app_selection { mode: "import", refs: <pasted>, isolate: true, createSectionBox: true }`.
5. Expected: both clashing elements visible — host selected, linked marked.

## 4. Cross-target sanity

Run `RevitCortex/build-release.ps1` (or your standard release build) and confirm no warnings/errors on R23, R24, R25, R26, R27. Run the equivalent Navis build for N23/N24/N25/N26.
