# Power BI Live - Production Hardening Design

Date: 2026-05-11
Status: Draft for developer handoff
Related report: `docs/superpowers/reports/2026-05-11-powerbi-technical-functional-review.md`

## 1. Purpose

This specification describes the next hardening layer for RevitCortex Power BI Live.

The current implementation proves the end-to-end workflow:

- authenticate with MSAL device-code flow;
- publish Revit elements and schedules to a Power BI push dataset;
- build Power BI visuals over Revit data;
- send selections/actions from Power BI Desktop back to Revit through the custom visual.

The goal of this design is to move from a working prototype to a reliable product workflow where:

- the Power BI semantic model is stable and explainable;
- reports are not built manually from fragile assumptions;
- tests can distinguish product bugs from Power BI report setup mistakes;
- Revit actions triggered from Power BI are auditable and safe;
- developers have diagnostics before debugging manually.

## 2. Non-Goals

This spec does not replace the critical fixes already identified in the code review report.

Out of scope for this document:

- implementing the local HTTP request token;
- fixing the `ExternalEvent` timeout race;
- scoping `Reset overrides` to PBI-touched elements;
- fixing immediate `pbi_query` OST-code filtering bug.

Those remain required technical fixes. This document defines the product/data architecture that should come immediately after or in parallel.

## 3. Design Principles

1. Power BI must consume a semantic model, not raw export leftovers.
2. `ElementId` is useful for live Revit actions, but not sufficient as the durable identity.
3. The default user path should be a supplied `.pbit` template, not manual chart construction.
4. Every live action from Power BI to Revit should be diagnosable after the fact.
5. Schema versions must be explicit and incompatible changes must create a new dataset version.
6. A developer should be able to run one diagnostic command and know where the system is broken.

## 4. Proposed Architecture

### 4.1 Layers

The hardened integration should be split into five conceptual layers:

1. **Revit Snapshot Layer**
   Extracts stable DTOs from Revit on the main thread.

2. **Power BI Dataset Contract**
   Defines versioned tables, columns, keys, relationships, and expected semantics.

3. **Publish/Sync Layer**
   Pushes data to Power BI, tracks publish state, and reports diagnostics.

4. **Report Template Layer**
   Provides an official `.pbit` with relationships, measures, pages, and the custom visual layout.

5. **Live Action Layer**
   Receives Power BI custom visual actions, applies them in Revit, and logs the result.

## 5. Canonical Power BI Model

The current tables are functional but too raw for long-term reliability. The proposed schema version should be `1.2`.

### 5.1 Tables

#### `DimElement`

One row per Revit element in the published model scope.

Required columns:

- `_SchemaVersion` String
- `ExportRunId` String
- `ExportedAtUtc` DateTime
- `DocumentKey` String
- `ProjectId` String
- `ProjectName` String
- `DocumentGuid` String
- `ElementId` Int64
- `UniqueId` String
- `StableElementKey` String
- `Category` String
- `OstCode` String
- `FamilyName` String
- `TypeName` String
- `Level` String
- `Workset` String
- `PhaseCreated` String
- `PhaseDemolished` String
- `Name` String
- `Mark` String
- `Comments` String
- `AreaInternal` Double
- `VolumeInternal` Double
- `LengthInternal` Double
- `BoundingBoxMinX` Double
- `BoundingBoxMinY` Double
- `BoundingBoxMinZ` Double
- `BoundingBoxMaxX` Double
- `BoundingBoxMaxY` Double
- `BoundingBoxMaxZ` Double

`StableElementKey` should be:

```text
DocumentKey + "|" + UniqueId
```

`ElementId` remains present because the custom visual needs it for live Revit actions. It should not be the only durable relationship key.

#### `DimSchedule`

One row per exported schedule.

Required columns:

- `_SchemaVersion` String
- `ExportRunId` String
- `DocumentKey` String
- `ScheduleId` Int64
- `ScheduleUniqueId` String
- `ScheduleName` String
- `IsTemplate` Boolean
- `ScheduleCategory` String
- `ExportedAtUtc` DateTime

Natural key:

```text
DocumentKey + "|" + ScheduleId
```

#### `DimScheduleField`

One row per exported field/column in a schedule.

Required columns:

- `_SchemaVersion` String
- `ExportRunId` String
- `DocumentKey` String
- `ScheduleId` Int64
- `FieldKey` String
- `ColumnIndex` Int64
- `ColumnName` String
- `ParameterId` Int64
- `ParameterName` String
- `StorageType` String
- `SpecType` String
- `UnitSymbol` String
- `IsHidden` Boolean
- `IsCalculated` Boolean

`FieldKey` should be stable inside a schedule export. Preferred format:

```text
DocumentKey + "|" + ScheduleId + "|" + ColumnIndex + "|" + ParameterId
```

The current `ColumnName` must be treated as a display label only, not as identity.

#### `FactScheduleCell`

One row per schedule element/field value.

Required columns:

- `_SchemaVersion` String
- `ExportRunId` String
- `ExportedAtUtc` DateTime
- `DocumentKey` String
- `ScheduleId` Int64
- `FieldKey` String
- `ElementId` Int64
- `UniqueId` String
- `StableElementKey` String
- `RowIndex` Int64
- `ColumnIndex` Int64
- `ValueString` String
- `ValueNumber` Double nullable
- `IsNumeric` Boolean
- `ValueElementId` Int64 nullable

Natural key:

```text
DocumentKey + "|" + ScheduleId + "|" + StableElementKey + "|" + FieldKey
```

Important rule:

`ValueNumber` must be null for non-numeric values. It must not use `0.0` as a placeholder.

#### `FactSelection`

One row per selected/highlighted element snapshot.

Required columns:

- `_SchemaVersion` String
- `UpdatedAtUtc` DateTime
- `DocumentKey` String
- `SelectionSetId` String
- `SelectionSource` String
- `ElementId` Int64
- `UniqueId` String
- `StableElementKey` String

`SelectionSource` examples:

- `RevitCurrentSelection`
- `PowerBiVisual`
- `PbiQuery`

#### `FactPbiActionLog`

One row per action sent from Power BI to Revit.

Required columns:

- `_SchemaVersion` String
- `ActionId` String
- `TimestampUtc` DateTime
- `DocumentKey` String
- `Action` String
- `RequestedElementCount` Int64
- `ValidatedElementCount` Int64
- `SkippedElementCount` Int64
- `ActiveViewId` Int64
- `ActiveViewName` String
- `Result` String
- `ErrorMessage` String
- `Source` String

This table is primarily for diagnostics. It helps answer: "Power BI sent N ids, Revit accepted M, what happened?"

## 6. Relationships in Power BI

The official template should define these relationships:

- `DimElement[StableElementKey]` -> `FactScheduleCell[StableElementKey]`
- `DimSchedule[ScheduleId]` -> `DimScheduleField[ScheduleId]`
- `DimSchedule[ScheduleId]` -> `FactScheduleCell[ScheduleId]`
- `DimScheduleField[FieldKey]` -> `FactScheduleCell[FieldKey]`
- `DimElement[StableElementKey]` -> `FactSelection[StableElementKey]`

`ElementId` should be exposed in the report because the custom visual needs it, but joins should prefer `StableElementKey`.

## 7. Official Power BI Template

The integration should ship with a `.pbit` template.

### 7.1 Template Goals

The template should:

- remove manual setup ambiguity;
- contain correct relationships;
- contain base measures;
- include the custom visual placement;
- make the schema version visible;
- provide a diagnostics page.

### 7.2 Required Pages

#### Page 1: Overview

Visuals:

- total elements;
- elements by category;
- elements by level;
- latest export timestamp;
- schema version;
- selected workspace/dataset label.

#### Page 2: Walls

Visuals:

- wall count by `Base Constraint`;
- wall area by type;
- wall length by level;
- table with `ElementId`, `UniqueId`, `Level`, `FamilyName`, `TypeName`.

The RevitCortex custom visual should be present and mapped to `DimElement[ElementId]`.

#### Page 3: Schedules

Visuals:

- schedule selector;
- field selector;
- matrix/table of schedule values;
- count of cells and elements by schedule.

This page should demonstrate correct use of:

- `DimSchedule`
- `DimScheduleField`
- `FactScheduleCell`

#### Page 4: Revit Live Selection

Visuals:

- current selection table;
- count selected by category;
- custom visual for select/isolate/color/create view;
- last action status from `FactPbiActionLog`.

#### Page 5: Diagnostics

Visuals:

- row count per table;
- schema version;
- duplicate `ElementId` count;
- duplicate `StableElementKey` count;
- orphan schedule cells without element match;
- orphan schedule cells without field match;
- last publish time;
- last Power BI -> Revit action result.

## 8. Diagnostic Tool

Add a new MCP tool:

```text
pbi_diagnose_model
```

### 8.1 Inputs

- `workspaceId` optional
- `datasetId` optional
- `datasetName` optional
- `includeSamples` optional Boolean, default `false`
- `maxSampleRows` optional Int64, default `10`

If ids are omitted, resolve from project binding.

### 8.2 Checks

The tool should verify:

1. MSAL auth state.
2. External writes enabled.
3. Workspace accessible.
4. Dataset exists.
5. Dataset schema version matches `PowerBiDatasetSchema.CurrentVersion`.
6. Required tables exist.
7. Required columns exist with expected data types.
8. Row counts per table.
9. Duplicate `StableElementKey` in `DimElement`.
10. Duplicate `ElementId` in `DimElement`.
11. `FactScheduleCell` rows without matching `DimElement`.
12. `FactScheduleCell` rows without matching `DimScheduleField`.
13. Null/non-null distribution for `ValueNumber` and `IsNumeric`.
14. Whether custom visual endpoint is reachable at `localhost:27016`.
15. Last action log entry if available.

### 8.3 Output

Return a structured result:

```json
{
  "success": true,
  "health": "ok|warning|error",
  "workspaceId": "...",
  "datasetId": "...",
  "datasetName": "...",
  "schemaVersionExpected": "1.2",
  "schemaVersionActual": "1.2",
  "tables": [
    {
      "name": "DimElement",
      "exists": true,
      "rowCount": 1234,
      "missingColumns": [],
      "warnings": []
    }
  ],
  "issues": [
    {
      "severity": "error",
      "code": "MissingTable",
      "message": "FactScheduleCell table not found.",
      "suggestion": "Recreate the dataset with schema v1.2."
    }
  ]
}
```

## 9. Live Action Logging

Every Power BI custom visual action should produce an action log record.

### 9.1 Logged Actions

- `select`
- `isolate`
- `color`
- `reset_pbi_overrides`
- `create_3d_view`

### 9.2 Result Values

Allowed `Result` values:

- `success`
- `partial`
- `failed`
- `timeout`
- `rejected`

### 9.3 Storage

Short term:

- append to local JSONL file under `~/.revitcortex/pbi-action-log.jsonl`;
- optionally publish latest rows to `FactPbiActionLog`.

Long term:

- expose `pbi_get_action_log`;
- allow publishing action log to Power BI as part of diagnostics.

## 10. Environment Modes

Add an explicit mode to Power BI settings:

```json
{
  "PowerBiMode": "Demo"
}
```

Allowed values:

- `Demo`
- `Test`
- `Production`

### 10.1 Demo

Optimized for fast experiments.

- Lower friction.
- Warnings instead of hard failures where safe.
- Smaller default row limits.

### 10.2 Test

Optimized for repeatable QA.

- Requires schema match.
- Requires diagnostics to pass before live actions.
- Logs all actions.
- Allows controlled reset of datasets.

### 10.3 Production

Optimized for safety.

- Requires local HTTP request token.
- Requires scoped reset only.
- Blocks publishing to incompatible schema.
- Warns on copied model binding.
- Requires explicit user confirmation for broad destructive live actions.

## 11. Implementation Phases

### Phase A - Data Model v1.2

Deliver:

- new schema tables;
- stable element key;
- schedule field identity;
- nullable numeric semantics;
- dataset version bump to `1.2`;
- migration notes from v1.1.

Acceptance:

- schedules with duplicate column headings export correctly;
- text values have null `ValueNumber`;
- Power BI relationships are based on stable keys.

### Phase B - Diagnostic Tool

Deliver:

- `pbi_diagnose_model`;
- schema/table/relationship checks;
- row-count and orphan checks;
- custom visual endpoint reachability check.

Acceptance:

- tool clearly identifies missing table, wrong schema version, and empty dataset;
- tool returns actionable suggestions.

### Phase C - Official PBIT Template

Deliver:

- `.pbit` template;
- default pages listed in section 7;
- base measures;
- custom visual placement;
- diagnostics page.

Acceptance:

- user can connect template to the generated dataset and immediately test Revit <-> Power BI without manually building relationships.

### Phase D - Action Logging

Deliver:

- local JSONL action log;
- structured action result from listener/handler;
- optional publish to `FactPbiActionLog`.

Acceptance:

- after a Power BI click, the log shows requested count, validated count, active view, action, result.

### Phase E - Environment Modes

Deliver:

- settings field;
- behavior switches for Demo/Test/Production;
- clear warnings in tool responses.

Acceptance:

- Production mode blocks unsafe conditions that Demo mode allows with warning.

## 12. Developer Notes

### 12.1 Revit Compatibility

All C# changes must build for:

- `Debug R25`
- `Debug R24`

Avoid net8-only constructs in shared plugin code unless guarded.

### 12.2 Dataset Versioning

Changing existing table columns or semantics requires a version bump.

Recommended next dataset name:

```text
RevitCortex Live - {ProjectName} - v1.2
```

Do not silently reuse v1.1 dataset ids for v1.2.

### 12.3 Power BI Push Dataset Limits

The implementation should keep row batching and throttling visible in responses.

For large schedules, the exporter should return:

- emitted cell count;
- element count;
- field count;
- truncation warnings;
- batch count.

## 13. Acceptance Test Matrix

### Data Model

- Publish walls and wall schedules.
- Verify `DimElement` has one row per wall.
- Verify `FactScheduleCell` has one row per wall/field.
- Verify duplicate field headings do not collapse rows.
- Verify `ValueNumber` is null for text fields.
- Verify `StableElementKey` joins schedule cells to elements.

### Template

- Open official `.pbit`.
- Connect to the generated dataset.
- Confirm relationships are present.
- Confirm walls page shows counts by Base Constraint.
- Confirm custom visual can select from a chart/table context.

### Diagnostics

- Run `pbi_diagnose_model` on a valid dataset: returns `health=ok`.
- Run on old v1.1 dataset: returns schema mismatch.
- Delete one table manually: returns missing table error.
- Publish schedule without elements: returns orphan warning.

### Live Actions

- Select from Power BI: Revit selects expected elements.
- Color from Power BI: only expected elements get overrides.
- Reset PBI overrides: manual overrides remain intact.
- Create 3D view: view is created and action log records success.
- Send invalid/deleted ElementIds: action log reports skipped count.

## 14. Open Decisions

1. Should `DimElement` keep the old table name `Elements` for backward compatibility, or should v1.2 use the explicit dimensional naming?
2. Should action logs be pushed to Power BI automatically, or only when diagnostics are requested?
3. Should `.pbit` be distributed inside the repo, installer, or generated on demand?
4. Should Production mode require a user-visible confirmation before `color`, `reset`, and `create_3d_view`?
5. Should copied local models reuse project bindings or force a new dataset choice?

## 15. Recommendation

Implement this hardening as a v1.2 milestone.

Recommended order:

1. Data model v1.2.
2. Diagnostic tool.
3. Official `.pbit` template.
4. Action logging.
5. Environment modes.

The most valuable first deliverable is the diagnostic tool plus a stable schema, because it will reduce uncertainty during every later Power BI/Revit test.
