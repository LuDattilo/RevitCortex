# RevitCortex + Power BI Live - Technical and Functional Review

Date: 2026-05-09

## Executive Summary

The proposed architecture is technically sound and strategically aligned with RevitCortex. It turns RevitCortex from a pure model-operation MCP bridge into a cross-application BIM intelligence layer: Revit remains the authoring environment, while Power BI becomes the live analytical surface for model state, schedules, quantities, warnings, and current selection.

The most valuable part of the proposal is not the Power BI export itself, but the live loop:

1. Revit model data is pushed to a Power BI semantic model.
2. Revit selection state can update a lightweight `Selection` table.
3. Power BI reports can be opened with URL filters based on selected Revit elements.
4. A later callback mechanism could let Power BI drive selection back into Revit.

The concept is promising, but the first implementation should be narrower than the full target diagram. The recommended MVP is: authentication, workspace discovery, push dataset creation, manual push of `Elements`, and a minimal `Selection` table. Wizard UI, auto-export, generic schedules, and custom visual callbacks should come after the core authentication and push flow is proven.

## Fit With RevitCortex

The architecture fits the current RevitCortex design well:

- The plugin already owns the Revit-side UI surface through the ribbon and settings window.
- The router/tool architecture already provides a clean path for MCP tools such as `pbi_check_auth`, `push_to_powerbi_live`, and `open_in_powerbi`.
- The audit log and read-only mode patterns are directly relevant because Power BI pushes are external writes, even if they do not mutate the Revit model.
- Existing export tools already know how to collect model data; Power BI live push can reuse the same extraction philosophy while changing the delivery target.

The main design principle should be: keep Revit data collection, Power BI authentication, REST API transport, and UI orchestration separate. This avoids turning the plugin into one large service object that is hard to test and risky across Revit versions.

## Technical Assessment

### Authentication

MSAL device-code authentication is the right first choice for Revit:

- It avoids embedding a browser in WPF/Revit.
- It works naturally with MFA and enterprise login flows.
- It does not require storing a client secret on the workstation.
- It is compatible with a public-client desktop application model.

The token cache should be file-based and encrypted using Windows user context protection. A practical location is `%LOCALAPPDATA%\.revitcortex\msal_cache.bin`.

One important correction: the client ID should not be hardcoded as a permanent dependency on a Microsoft first-party app ID. That can be useful for a prototype, but production should support a configurable Entra application registration. Some tenants block user consent, restrict public clients, enforce Conditional Access, or disallow unapproved app IDs.

Recommended approach:

- Add a default `clientId` setting for prototype use.
- Allow override in RevitCortex settings.
- Document the preferred production path as a custom tenant app registration.
- Use delegated Power BI scopes, starting with `Dataset.ReadWrite.All` and adding report/workspace scopes only when needed.

### Power BI REST Client

A dedicated `PowerBiServiceClient` is the right abstraction. It should own:

- HTTP client creation and bearer token injection.
- Workspace listing.
- Dataset lookup and creation.
- Table row deletion and row posting.
- Retry/backoff for `429`, `503`, network failures, and transient authentication refresh.
- Power BI-specific response parsing and typed errors.

The API limits matter. Microsoft documents a maximum of 10,000 rows per single `POST rows` request for push semantic models, plus throttling and model-size limits. The implementation should batch rows and treat throttling as normal operating behavior, not an exceptional edge case.

Sources:

- Power BI push semantic model limitations: https://learn.microsoft.com/en-us/power-bi/developer/embedded/push-datasets-limitations
- Power BI REST APIs overview: https://learn.microsoft.com/en-us/rest/api/power-bi/
- Push rows API: https://learn.microsoft.com/en-us/rest/api/power-bi/push-datasets/datasets-post-rows
- MSAL token cache serialization: https://learn.microsoft.com/en-gb/entra/msal/dotnet/how-to/token-cache-serialization

### Data Model

The proposed `Elements`, `Schedules`, and `Selection` split is directionally correct, but the schema needs adjustment.

The risky part is the phrase "Schedules (qualsiasi schedule del modello)". A single wide `Schedules` table cannot safely represent arbitrary Revit schedules because schedule columns differ by model, language, discipline, and user customization. Power BI push models also have structural limits, including table and column limits.

Recommended schema:

#### Elements

A stable, moderately wide table for high-value universal element fields.

Candidate columns:

- `ProjectId`
- `DocumentGuid`
- `ExportRunId`
- `ExportedAtUtc`
- `ElementId`
- `UniqueId`
- `Category`
- `FamilyName`
- `TypeName`
- `Level`
- `Workset`
- `PhaseCreated`
- `Name`
- `Mark`
- `Comments`
- `Volume`
- `Area`
- `Length`
- `BoundingBoxMinX/Y/Z`
- `BoundingBoxMaxX/Y/Z`

This table should stay below Power BI push model column limits and should not attempt to include every Revit parameter.

#### ElementParameters

A long/narrow table for flexible parameters.

Columns:

- `ExportRunId`
- `ElementId`
- `ParameterName`
- `ParameterScope` (`Instance` or `Type`)
- `StorageType`
- `ValueString`
- `ValueNumber`
- `Unit`

This is less convenient for casual report building than a wide table, but it is much more robust across projects and languages.

#### Schedules

Represent schedules in long form rather than trying to create one table per schedule or one universal schedule table with dynamic columns.

Columns:

- `ExportRunId`
- `ScheduleName`
- `RowIndex`
- `ColumnName`
- `ValueString`
- `ValueNumber`

This makes schedule export generic without continuously mutating Power BI table schemas.

#### Selection

A small volatile table for current Revit selection.

Columns:

- `ProjectId`
- `DocumentGuid`
- `UpdatedAtUtc`
- `ElementId`
- `UniqueId`
- `SelectionSetId`

Even if the UI concept says "always 1 row per id", the table should support multiple selected IDs. On update, delete existing rows and post the latest selection.

### Threading and Responsiveness

Revit API access must remain on the Revit UI thread. Power BI HTTP calls should not run on the Revit UI thread.

Recommended flow:

1. Use Revit-thread execution only to collect a model snapshot.
2. Convert the snapshot into DTOs detached from Revit API objects.
3. Push to Power BI asynchronously outside the Revit API context.
4. Report progress and errors back to UI/MCP as status objects.

Selection watch especially needs debouncing. Revit selection can change rapidly through clicks, window selections, and command interactions. Pushing every event immediately would create unnecessary API traffic and could make Revit feel unstable.

Recommended selection behavior:

- Debounce selection events by 750-1500 ms.
- Coalesce rapid changes into "last selection wins".
- Skip push when selected IDs have not changed.
- Pause during modal dialogs or when Revit document state is unavailable.
- Store last successful selection push status for diagnostics.

## Functional Assessment

### Main User Value

The strongest functional value is giving BIM users a near-live analytical dashboard without requiring manual export/import cycles.

High-value use cases:

- Live model health dashboard.
- Quantity and parameter completeness reporting.
- Door/window/room/family QA dashboards.
- Selection-driven inspection: select elements in Revit, open or filter a Power BI report.
- Coordination reviews where managers consume Power BI and modelers work in Revit.

The user experience should be framed as "Publish model intelligence to Power BI", not merely "export data".

### Wizard Experience

The wizard should be introduced after the first backend flow is stable. When added, it should be simple:

- Connect Power BI account.
- Choose workspace.
- Choose or create dataset.
- Choose tables to publish.
- Choose replace or append.
- Run push and show status.

Avoid exposing too many technical options at first. Advanced options can live in settings or an "advanced" expander.

### AutoExport on Save

AutoExport is useful, but it should not be in the MVP. It has several product risks:

- Save operations are already latency-sensitive.
- Power BI calls may fail or throttle.
- Users may not expect a cloud push on every save.
- Large models can generate large data pushes.

Recommended behavior:

- Off by default.
- Per-project opt-in.
- Throttled, for example no more than one push every N minutes.
- Visible status indicator.
- Clear failure state, but no blocking of Revit save unless explicitly configured.

### Revit to Power BI URL Filtering

This is a good near-term feature because it is simple and high impact.

It should depend on stored configuration:

- Workspace ID.
- Report ID.
- Dataset/table naming convention.
- Element ID field name.

The tool should validate that selection is non-empty and that a report target is configured. If multiple reports are possible, let the user choose from settings rather than prompting every time.

### Power BI to Revit Callback

The custom visual callback is powerful but should remain future-phase. It introduces a separate TypeScript project, browser-to-localhost constraints, security prompts, and deployment complexity.

Before building a custom visual, consider lower-cost alternatives:

- Power BI drillthrough URL using `revitcortex://select?ids=...`
- Local protocol handler registered by installer.
- A small local HTTP endpoint only after a security review.

## Security and Governance Considerations

This feature exports BIM data to a Microsoft cloud service. That makes governance explicit:

- Users must know which workspace receives data.
- The plugin should show the target workspace and dataset before pushing.
- Dataset names should include project identity but avoid leaking unnecessary file paths.
- Token cache must be protected per Windows user.
- Audit logs should record Power BI tool invocations, destination workspace/dataset, row counts, and error codes.
- Do not log access tokens, refresh tokens, authorization headers, or full row payloads.

Read-only mode requires a product decision. Technically, a Power BI push does not mutate Revit, but it does perform an external write. The safer rule is:

- `pbi_check_auth`, `pbi_list_workspaces`, and `pbi_list_datasets` are read-only.
- `push_to_powerbi_live`, `pbi_create_dataset`, `pbi_delete_rows`, and auto-export are write operations.
- In RevitCortex read-only mode, external writes should be blocked unless a separate `allowExternalWrites` setting is introduced.

## Risks

### Highest Risks

1. Tenant governance blocks consent or public client usage.
2. Push dataset schema becomes too wide or too dynamic.
3. Selection watch causes excessive API calls without debounce.
4. HTTP work runs on the Revit UI thread and hurts responsiveness.
5. AutoExport surprises users or slows saves.

### Medium Risks

1. Power BI throttling during large model exports.
2. Dataset naming collisions across projects.
3. Revit localized parameter names create inconsistent report columns.
4. Users lack Power BI Pro/Premium permissions or workspace write access.
5. Report templates become coupled to one schema version.

### Lower Risks

1. Ribbon UI complexity.
2. Token cache corruption.
3. Network intermittency.
4. Partial pushes that need retry/resume semantics.

## Recommended Architecture Refinement

Recommended component split:

- `PowerBiAuthService`: MSAL public-client auth, token acquisition, cache.
- `PowerBiServiceClient`: typed REST wrapper and retry/backoff.
- `PowerBiDatasetSchema`: schema definitions and versioning.
- `PowerBiElementExporter`: converts Revit document snapshots into Power BI row DTOs.
- `PowerBiSelectionPublisher`: debounced selection table updates.
- `PowerBiSettings`: client ID, tenant behavior, workspace ID, dataset ID, report ID, enabled tables, auto-export options.
- MCP tools:
  - `pbi_check_auth`
  - `pbi_list_workspaces`
  - `pbi_create_dataset`
  - `push_to_powerbi_live`
  - `open_in_powerbi`

The services should be independent enough to unit test without Revit where possible. Revit-specific extraction can be isolated behind DTO builders.

## Recommended Roadmap

### Phase 0 - Discovery and Feasibility

Goal: prove auth and tenant access without touching model export complexity.

Deliverables:

- Configurable client ID.
- Device-code authentication.
- `pbi_check_auth`.
- Workspace listing.

Success criterion: user can authenticate and list workspaces from inside RevitCortex.

### Phase 1 - Minimal Push Dataset

Goal: prove end-to-end Power BI push.

Deliverables:

- Create or find dataset.
- Push `Elements` table with stable schema.
- Push `Selection` table manually from current selection.
- Basic row batching and retry.

Success criterion: Power BI report can show model elements and current selection data.

### Phase 2 - UI Wizard

Goal: make the flow usable without chat/tool calls.

Deliverables:

- Ribbon button or settings page entry.
- Workspace selection.
- Dataset creation/selection.
- Replace/append mode.
- Progress and error display.

Success criterion: non-technical user can connect and publish without editing config files.

### Phase 3 - Live Selection and URL Filter

Goal: create the "cross-app" experience.

Deliverables:

- Debounced selection watch.
- `open_in_powerbi` command.
- Stored report target.
- URL filter generation.

Success criterion: selected Revit elements can drive Power BI filtering with low friction.

### Phase 4 - Schedules and Parameter Flexibility

Goal: support richer data without schema fragility.

Deliverables:

- `ElementParameters` long table.
- `Schedules` long table.
- Schema versioning.
- Report template updated for long-form analysis.

Success criterion: common Revit schedules and custom parameters can be analyzed without changing code for each project.

### Phase 5 - Power BI to Revit Interaction

Goal: allow Power BI interactions to select or isolate elements in Revit.

Recommended order:

1. Protocol handler approach.
2. Local HTTP callback only after security review.
3. Custom visual only if the simpler approaches are insufficient.

## Final Recommendation

Proceed with the concept, but narrow the first deliverable. The full architecture is credible, but too broad for a first commit series. The highest-confidence path is to validate authentication and push dataset mechanics first, then add UI and live interaction.

The most important product decision is whether this feature is a "Power BI live publisher" or a broader "Power BI integration platform". The architecture can grow into the second, but the first release should behave like a focused publisher:

- Connect account.
- Choose workspace.
- Publish model data.
- Publish selection.
- Open report filtered from Revit.

That version is valuable, testable, and much easier to make reliable across Revit 2023-2027.
