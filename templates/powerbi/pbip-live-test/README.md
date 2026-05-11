# RevitCortex Power BI Live Test PBIP

This folder is a source-controlled Power BI Project starter for testing the
RevitCortex Power BI Live model contract.

Important: the current RevitCortex live datasets are created with the Power BI
REST push semantic model API. Microsoft documents that REST push semantic
models are not accessible through the XMLA endpoint. A report-only PBIP
`byConnection` reference uses that XMLA-style live connection path, so it cannot
open directly against the current push semantic model.

It targets the current live schema v1.1:

- `Metadata`
- `Elements`
- `Schedules`
- `Selection`

The project intentionally starts with blank report pages. Power BI report JSON
visual metadata is still preview-sensitive, and the most reliable test path is:

1. build or open a report through a Power BI-supported push model path;
2. place the visuals once in Power BI Desktop or the Power BI service;
3. save/export the report so Desktop or the service writes the supported
   visual metadata.

## Files

| Path | Purpose |
|---|---|
| `RevitCortexLiveTest.pbip` | Power BI Project shortcut. |
| `RevitCortexLiveTest.Report/definition.pbir` | Report binding to the Power BI service semantic model. |
| `RevitCortexLiveTest.Report/definition/pages/*/page.json` | Empty test pages. |
| `dax/RevitCortexLiveTest-Measures.dax` | Test measures for diagnostics and wall/schedule checks. |
| `scripts/Set-PowerBiConnection.ps1` | Replaces the placeholder semantic model connection. |
| `scripts/Set-PowerBiConnectionFromRevitBinding.ps1` | Reads the latest RevitCortex Power BI binding and updates the PBIP connection. |

## Current Limitation

Do not use this PBIP as a direct live-connection template for the current
RevitCortex push semantic model. Power BI Desktop can reject it with:

```text
Cannot load model: We couldn't connect to your Analysis Services database.
```

That failure is expected for REST push semantic models opened through PBIR
`byConnection`.

## Before Using The Checklist

Publish Revit data first, so the semantic model exists in Power BI:

1. `pbi_publish_elements` for `OST_Walls`
2. `pbi_publish_schedules` for the wall schedule
3. optionally `pbi_publish_selection`

Use the generated semantic model name, normally:

```text
RevitCortex Live - {ProjectName} - v1.1
```

If you later migrate RevitCortex to an XMLA-accessible semantic model, update
this PBIP connection from the latest RevitCortex binding:

```powershell
cd templates\powerbi\pbip-live-test
.\scripts\Set-PowerBiConnectionFromRevitBinding.ps1 `
  -WorkspaceNameOrId "GPA Ingegneria Srl"
```

Or set the connection manually:

```powershell
cd templates\powerbi\pbip-live-test
.\scripts\Set-PowerBiConnection.ps1 `
  -WorkspaceNameOrId "GPA Ingegneria Srl" `
  -SemanticModelName "RevitCortex Live - Snowdon Towers - v1.1" `
  -SemanticModelId "00000000-0000-0000-0000-000000000000"
```

Use the workspace display name, not the workspace id. RevitCortex currently
persists the workspace id because the REST API accepts it, but Power BI Desktop
live connections use the XMLA workspace path and need the display name.

Open:

```text
templates\powerbi\pbip-live-test\RevitCortexLiveTest.pbip
```

Power BI Desktop must have the Power BI Project preview feature enabled.

## Required Custom Visual

Import the RevitCortex custom visual from:

```text
powerbi-visual\dist\revitcortexselectionvisual1A2B3C4D.1.0.0.7.pbiviz
```

Place it on `Walls Test` and map `Elements[ElementId]` to its `Element ID`
field well.

## Page Build Checklist

### Overview

Create:

- Card: `Total Elements`
- Card: `Wall Elements`
- Card: `Schema Version`
- Bar chart: Axis `Elements[OstCode]`, Values `Count of ElementId`
- Table: `Metadata[Key]`, `Metadata[Value]`, `Metadata[UpdatedAtUtc]`

### Walls Test

Create:

- Bar chart: Axis `Elements[Level]`, Values `Count of ElementId`, filter `OstCode = OST_Walls`
- Bar chart: Axis `Elements[TypeName]`, Values `Count of ElementId`, filter `OstCode = OST_Walls`
- Table: `Elements[ElementId]`, `Elements[Level]`, `Elements[FamilyName]`, `Elements[TypeName]`, `Elements[Area]`, `Elements[Length]`
- RevitCortex custom visual: `Elements[ElementId]`

Use this page for Power BI -> Revit select/isolate/color/create-view tests.

### Schedules Raw Test

Create:

- Slicer: `Schedules[ScheduleName]`
- Slicer: `Schedules[ColumnName]`
- Table: `Schedules[ScheduleName]`, `Schedules[ElementId]`, `Schedules[ColumnName]`, `Schedules[ValueString]`, `Schedules[ValueNumber]`
- Bar chart for Base Constraint:
  - Visual filter: `Schedules[ColumnName] = Base Constraint`
  - Axis: `Schedules[ValueString]`
  - Values: `Count of ElementId`

Expected result for wall Base Constraint: bars should be level names such as
`Top of Footing`, `L1_35_Low`, `L1_43_High`, not area/length values.

### Selection Test

Create:

- Card: `Selected Elements`
- Table: `Selection[ElementId]`, `Selection[UniqueId]`, `Selection[UpdatedAtUtc]`
- Table: `Elements[ElementId]`, `Elements[Category]`, `Elements[Level]`, `Elements[TypeName]`

Use this page for Revit -> Power BI selection publish checks.

### Diagnostics

Create:

- Card: `Duplicate Element Rows`
- Card: `Duplicate Schedule Cells Approx`
- Card: `Schedule ElementIds Without Element`
- Card: `Selection ElementIds Without Element`
- Card: `Schedule Text Rows With Numeric Zero`
- Table: `Metadata`

Failures here are usually data-contract issues, not visual layout issues.

## Notes

- The PBIP shell uses `definition.pbir` with a Power BI service connection.
- Power BI Desktop April 2026 requires `semanticModelId` in the PBIR connection string.
- The first time you open it, Desktop may ask you to sign in/rebind if the
  workspace, semantic model name, or semantic model id is wrong.
- After you place visuals and save, Power BI Desktop will write additional
  PBIR `visual.json` files into this project.
- Save a copy as `.pbit` from Power BI Desktop only after the report opens and
  the test visuals are in place.
