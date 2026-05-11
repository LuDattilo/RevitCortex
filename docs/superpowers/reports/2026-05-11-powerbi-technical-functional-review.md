# Power BI Live - Technical/Functional Code Review

Date: 2026-05-11
Baseline reviewed: `HEAD` = `e761f18 merge: PBI visual v1.0.0.7 + plugin fixes + CSV ElementId + spec`
Working tree note: no uncommitted code changes detected during review; only `.claude/settings.local.json` was dirty and is intentionally excluded.

## Executive Summary

The Power BI Live flow is now usable end-to-end for the current manual tests: authentication, dataset creation, schedule export with `ElementId`, custom visual actions from Power BI Desktop to Revit, and basic selection/color/view workflows.

The main remaining risk is not "does it work once", but "is it reliable and safe as a data/product contract". The most critical areas are:

1. Local HTTP control surface has no request authentication and allows any local web context to trigger Revit actions.
2. The ExternalEvent synchronous wait pattern can corrupt queued actions after timeout.
3. "Reset overrides" can remove manual/user view overrides across the whole active view.
4. Schedule long-form schema still lacks stable field identity and uses `0.0` for non-numeric values, which can corrupt Power BI measures.
5. `pbi_query` template filters `category` against `Elements[Category]` but documentation and user flow imply OST codes.
6. Publish tools still block the Revit UI thread during network operations, with no top-level timeout around the blocking join.

Recommended developer path: fix P0/P1 items before broad testing with production models; leave P2 items for schema/UX hardening unless tests expose them earlier.

## P0 - Must Fix Before Wider Use

### 1. Unauthenticated localhost HTTP endpoint can drive Revit actions

Files:
- `src/RevitCortex.Plugin/PowerBiLive/PbiSelectHttpListener.cs:116`
- `src/RevitCortex.Plugin/PowerBiLive/PbiSelectHttpListener.cs:207`
- `src/RevitCortex.Plugin/PowerBiLive/PbiSelectHttpListener.cs:241`
- `powerbi-visual/capabilities.json:33`

Problem:
The listener binds to `http://localhost:27016/`, sets `Access-Control-Allow-Origin: *`, and exposes mutation endpoints:
- `/pbi-select`
- `/pbi-color`
- `/pbi-reset-overrides`
- `/pbi-create-view`

Any local process or browser page capable of reaching localhost can POST to these endpoints while RevitCortex is running. The operations include selection, temporary isolation, color overrides, reset of overrides, and creation of 3D views.

Functional impact:
The Power BI custom visual works, but the endpoint is not bound to the trusted visual instance. This is acceptable for a prototype, not for a robust desktop integration.

Recommendation:
Add a per-session nonce/token:
- Generate token when the RevitCortex service starts.
- Store it in a local file or expose it through an explicit MCP/tool response used by the visual setup workflow.
- Require `X-RevitCortex-Token` or a signed query/header on all POST endpoints.
- Narrow CORS origin if Power BI Desktop origin can be identified reliably; otherwise token is the real control.
- Reject missing/invalid token with 401.

Minimum acceptance test:
- POST without token returns 401 and no Revit action occurs.
- POST with valid token works from Power BI visual.

### 2. ExternalEvent timeout can cause stale action overwrite/race

Files:
- `src/RevitCortex.Plugin/RevitCortexApp.cs:202`
- `src/RevitCortex.Plugin/RevitCortexApp.cs:209`
- `src/RevitCortex.Plugin/PowerBiLive/PbiActionEventHandler.cs:70`
- `src/RevitCortex.Plugin/PowerBiLive/PbiActionEventHandler.cs:116`
- `src/RevitCortex.Plugin/PowerBiLive/PbiActionEventHandler.cs:118`

Problem:
The listener callback acquires the handler lock, prepares pending state, raises the ExternalEvent, then waits up to 10 seconds. If `WaitForCompletion()` times out, the lock is released while the raised ExternalEvent may still be pending. A later request can acquire the lock and overwrite `_pendingKind`, `_pendingIds`, `_pendingColors`, etc. When Revit eventually executes the older ExternalEvent, it will read the latest pending state, not necessarily the state of the request that originally raised it.

Functional impact:
Under Revit load, modal dialogs, slow transactions, or UI stalls, Power BI clicks can:
- apply the wrong action,
- apply the right action to the wrong element ids,
- run twice,
- return failure to Power BI while still mutating Revit later.

Recommendation:
Replace shared mutable pending fields with immutable request objects and a queue:
- `ConcurrentQueue<PbiActionRequest>` plus one active request id.
- Each request has `TaskCompletionSource<PbiActionResult>`.
- ExternalEvent drains one request at a time on the UI thread.
- Timeout marks only that request as timed out/cancelled; it must not allow later requests to overwrite its payload.

Simpler short-term patch:
- Keep the lock held until `_done` fires even after timeout, or mark handler as poisoned and reject new requests until the pending event has completed. This avoids wrong-action corruption, though it can temporarily block the listener.

Minimum acceptance test:
- Simulate `Execute()` delayed beyond timeout.
- Send request A then request B.
- Verify A never executes with B payload and B is not lost.

### 3. Reset overrides clears the entire active view, not only PBI overrides

Files:
- `src/RevitCortex.Plugin/PowerBiLive/PbiActionEventHandler.cs:236`
- `src/RevitCortex.Plugin/PowerBiLive/PbiActionEventHandler.cs:244`
- `src/RevitCortex.Plugin/PowerBiLive/PbiActionEventHandler.cs:249`
- `powerbi-visual/src/visual.tsx:542`

Problem:
`ExecuteResetOverrides()` collects all visible elements in the active view and applies an empty `OverrideGraphicSettings` to each one. This removes every element override in the view, including manual overrides and overrides created by other workflows/add-ins.

Functional impact:
A user pressing "Reset overrides" from Power BI can destroy view-specific authoring/coordination work unrelated to Power BI.

Recommendation:
Track PBI-touched elements per view:
- In memory during session: `Dictionary<ViewId, HashSet<ElementId>>`.
- Optional persisted scope if needed.
- Color/isolate actions add ids to this set.
- Reset clears only ids in that set by default.
- Add an explicit "reset all visible overrides" advanced action only if product wants it, with clear wording.

Minimum acceptance test:
- Manually override element A in Revit.
- Color element B from Power BI.
- Run reset from visual.
- A keeps its manual override; B is reset.

## P1 - Data Model Reliability

### 4. `Schedules.ValueNumber` stores `0.0` for non-numeric fields

Files:
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiScheduleExporter.cs:147`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiScheduleExporter.cs:160`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiScheduleExporter.cs:231`

Problem:
Rows are emitted with:
`["ValueNumber"] = value.ValueNumber ?? 0.0`

For text fields such as `Base Constraint`, `Family`, `Type`, `Level`, etc., `ValueNumber` becomes zero. In Power BI this creates misleading sums/averages/count logic and makes it hard to distinguish real numeric zero from "not numeric".

Functional impact:
Charts and measures over `ValueNumber` can be wrong. This is especially risky because Power BI may auto-summarize numeric columns.

Recommendation:
Emit `null` for non-numeric values and confirm Power BI push dataset accepts null for Double. If Power BI rejects null, use one of:
- split numeric schedules into a separate typed table,
- add `IsNumeric` Boolean and keep `ValueNumber` null/blank-compatible,
- use a string-only long-form plus typed derived tables.

Minimum acceptance test:
- Export a schedule with text fields and numeric fields.
- Confirm text rows have blank/null `ValueNumber`, not 0.
- Confirm numeric zero remains distinguishable.

### 5. Schedule schema lacks stable field identity

Files:
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiDatasetSchema.cs:116`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiScheduleExporter.cs:108`
- `src/RevitCortex.Plugin/PowerBiLive/Tools/PbiPublishSchedulesTool.cs:282`

Problem:
The natural key is currently `(ScheduleId, ElementId, ColumnName)`. The exporter records only `ColumnName`, derived from the column heading. This is not stable:
- Users can rename headings.
- Two fields can share the same heading.
- Hidden/calculated fields can collide with visible labels.
- Localized headings are not stable across language installs.

Functional impact:
Deduplication can drop valid cells if two schedule fields have the same heading. Power BI relationships/measures become fragile because `ColumnName` is acting as both display label and identity.

Recommendation:
Schema v1.2 should add at least:
- `ColumnIndex` Int64
- `FieldId` String or Int64 if available from Revit API
- `ParameterId` Int64
- `ParameterName` String
- `StorageType` String
- `UnitType` or `SpecType` String when available
- `IsHidden` Boolean if exporting hidden fields

Then use dedup key `(ScheduleId, ElementId, ColumnIndex)` or `(ScheduleId, ElementId, FieldId)`.

Minimum acceptance test:
- Create a schedule with two columns using the same displayed heading.
- Export.
- Both fields survive dedup and are distinguishable in Power BI.

### 6. `maxRowsPerSchedule` actually limits elements, not emitted rows

Files:
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiScheduleExporter.cs:34`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiScheduleExporter.cs:132`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiScheduleExporter.cs:142`

Problem:
The parameter is named `maxRowsPerSchedule`, but `rowCount` is incremented once per element after all fields are emitted. With 5,000 elements and 10 schedule fields, the exporter can emit 50,000 Power BI rows for a single schedule.

Functional impact:
The API/user contract is misleading and can surprise users with large publishes and throttling.

Recommendation:
Rename to `maxElementsPerSchedule`, or enforce a true emitted-row cap. Best option:
- `maxElementsPerSchedule`
- `maxCellsPerSchedule`
- Return warnings when either limit truncates output.

Minimum acceptance test:
- Schedule with 100 elements and 8 fields.
- `maxRowsPerSchedule=50` should either emit 50 cells or be renamed in API/docs.

### 7. Schedule export silently skips failures

Files:
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiScheduleExporter.cs:72`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiScheduleExporter.cs:123`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiScheduleExporter.cs:283`

Problem:
Several catch blocks swallow failures without warning. This includes schedule-level failures, collector failures, and parameter read failures.

Functional impact:
The user/developer may believe all schedules were exported while some schedules or fields were silently omitted. For a data model that must be "absolutely reliable", silent omission is more dangerous than a visible warning.

Recommendation:
Return an export DTO with:
- `Rows`
- `Warnings`
- `SkippedSchedules`
- `SkippedFields`
- `ReadErrors`

Keep best-effort behavior, but surface diagnostics in the tool response.

Minimum acceptance test:
- Include a key schedule/material schedule/unsupported schedule.
- Export response reports what was skipped and why.

### 8. `pbi_query` category template filters the wrong column for OST codes

Files:
- `src/RevitCortex.Plugin/PowerBiLive/Tools/PbiQueryTool.cs:26`
- `src/RevitCortex.Plugin/PowerBiLive/Tools/PbiQueryTool.cs:303`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiDatasetSchema.cs:92`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiDatasetSchema.cs:93`

Problem:
The tool input documentation says `category` is an OST code, e.g. `OST_Walls`. The DAX builder generates:
`Elements[Category] = "OST_Walls"`

But `Elements[Category]` is the localized/display category name, while `Elements[OstCode]` stores the language-independent OST code.

Functional impact:
Template queries using `category="OST_Walls"` will return zero elements even though data exists.

Recommendation:
Change DAX to use `Elements[OstCode]` for OST-code filters, or rename input to `categoryName` and add a separate `ostCode` input. Prefer `ostCode`.

Minimum acceptance test:
- Publish walls.
- Run `pbi_query` with `category="OST_Walls"`.
- Expected: wall ElementIds selected.

## P1 - UX / Operational Reliability

### 9. Network publish still blocks the Revit UI thread

Files:
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiToolHelper.cs:35`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiToolHelper.cs:49`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiServiceClient.cs:38`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiServiceClient.cs:239`

Problem:
`RunWithoutContext()` starts a background thread but immediately calls `thread.Join()` on the Revit main thread. This prevents MSAL/WPF deadlock, but still freezes Revit until the network operation completes. With retries and 60s HTTP timeout, a bad network/API condition can block Revit for minutes.

Functional impact:
Large publishes or throttling can make Revit appear hung. This can look like the original MSAL deadlock even if technically different.

Recommendation:
For publish/query tools, move to an explicit job model:
- snapshot on main thread,
- enqueue background publish,
- return job id quickly,
- expose `pbi_publish_status(jobId)`.

Short-term:
- Add a max elapsed timeout around `RunWithoutContext`.
- Return progress/warnings for large row counts before HTTP.

Minimum acceptance test:
- Simulate Power BI API timeout.
- Revit remains responsive or the tool returns within a bounded time.

### 10. Dataset binding can unintentionally follow copied local models

Files:
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiSettings.cs:136`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiSettings.cs:161`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiSettings.cs:170`

Problem:
For non-cloud files, document key priority uses `ProjectInformation.UniqueId` before path hash. The comment explicitly says this is stable across Save As. That stability is useful for renames, but risky for copied project files that should publish to separate Power BI datasets.

Functional impact:
A copied model can reuse the original dataset binding and overwrite/pollute the original Power BI dataset.

Recommendation:
Use a compound key for local files:
- `projuid + normalized central/local path hash`, or
- prompt when same ProjectInformation.UniqueId appears at a new path,
- expose `pbi_get_binding` warning when current path hash differs from stored `LastPathHash`.

Minimum acceptance test:
- Save As a model to a new path.
- `pbi_get_binding` reports possible copied model and asks for explicit dataset choice before publish.

## P2 - Design Debt / Nice To Harden

### 11. Numeric units are not explicit

Files:
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiElementExporter.cs:145`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiScheduleExporter.cs:240`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiDatasetSchema.cs:104`

Problem:
`Area`, `Length`, `Volume`, and schedule `ValueNumber` come from Revit internal values. The display text may show project units, but numeric columns do not carry units/spec.

Recommendation:
Add unit metadata or normalized display-unit columns in schema v1.2:
- `LengthFt`, `LengthM`, or `LengthInternal` with explicit naming.
- For schedule rows: `UnitSymbol`, `SpecType`, `DisplayUnit`.

### 12. Visual shows sent count from requested ids, not validated Revit count

Files:
- `powerbi-visual/src/visual.tsx:719`
- `powerbi-visual/src/visual.tsx:721`
- `src/RevitCortex.Plugin/PowerBiLive/PbiSelectHttpListener.cs:288`

Problem:
The listener returns `elementCount = ids.Count`, while the handler returns actual valid count in `validated`. The visual displays `ids.length`.

Functional impact:
If some elements were deleted or not in the active document, UI says more elements were selected than actually were.

Recommendation:
Return a typed numeric `validatedCount` from the listener and display it in the visual.

### 13. Source tests catch strings, not behavior

Files:
- `src/RevitCortex.Tests/PowerBiLive/PowerBiScheduleExporterSourceTests.cs`
- `src/RevitCortex.Tests/PowerBiLive/PowerBiDatasetBindingSourceTests.cs`
- `src/RevitCortex.Tests/PowerBiLive/PbiSelectHttpListenerTests.cs`

Problem:
Several tests verify source text patterns rather than behavior. This protects against accidental removal of tokens, but not against functional regressions.

Recommendation:
Add pure unit tests for:
- DAX builder output.
- schedule row key/dedup behavior.
- listener auth/token validation once implemented.
- ExternalEvent queue state machine using a fake executor.

## Positive Notes

- Schema was bumped to `1.1`, avoiding invisible mutation of old v1 datasets.
- `Schedules` now includes `ElementId` and `UniqueId`, which is the right direction for Power BI -> Revit drillthrough.
- Project binding now checks schema compatibility before reusing dataset ids.
- Custom visual imports cleanly and provides direct action buttons, which is the right UX path compared with `revitcortex://` URLs.
- CORS preflight support is correctly present for Power BI Desktop WebView.
- HTTP listener is isolated from Revit API types, which makes it testable.

## Recommended Fix Order

1. Add local request token/auth to `PbiSelectHttpListener`.
2. Replace shared pending fields in `PbiActionEventHandler` with immutable queued requests.
3. Scope reset overrides to PBI-touched elements only.
4. Fix `Schedules.ValueNumber` null semantics.
5. Add schedule field identity columns and adjust dedup key. This likely requires schema v1.2.
6. Fix `pbi_query` to filter `OstCode` for OST codes.
7. Add warnings for skipped schedules/fields.
8. Add bounded timeout/job model for network publishes.
9. Harden local copied-model binding behavior.

## Suggested Developer Acceptance Suite

Manual:
- Auth silent after Revit restart.
- Publish elements to new v1.1/v1.2 dataset.
- Publish wall schedule with Base Constraint, Family/Type, Area, Length.
- Power BI chart grouped by Base Constraint shows level names, not elevations or unrelated values.
- Custom visual selects, isolates, colors, resets only PBI color, and creates 3D view.
- Browser/local curl without token cannot trigger Revit.

Automated:
- R25 and R24 plugin builds.
- Power BI source/unit tests.
- DAX builder tests for `OST_Walls`.
- Schedule exporter tests with duplicate headings.
- ExternalEvent queue timeout regression.
- Listener unauthorized/authorized request tests.

## Handoff Note

The current implementation is suitable for controlled prototype demos. Before giving it to non-developer users or using it on production model views, the first three P0 items should be addressed because they affect security, determinism, and possible loss of manual view overrides.
