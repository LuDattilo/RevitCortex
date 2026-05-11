# Power BI Data Model Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Power BI `Schedules` table reliable for Revit roundtrip by carrying stable element identity and preventing duplicate schedule-cell rows within a publish.

**Architecture:** Keep the existing Push dataset transport, but harden the exported data contract first. `Schedules` becomes element-centric: one row per `(ScheduleId, ElementId, ColumnName)` with `ElementId` and `UniqueId`, while `RowIndex` remains a display/order helper.

**Tech Stack:** C#/.NET, Revit API, xUnit source-level regression tests, Power BI Push dataset schema.

---

### Task 1: Schema Contract For Schedule Element Identity

**Files:**
- Modify: `src/RevitCortex.Plugin/PowerBiLive/PowerBiDatasetSchema.cs`
- Test: `src/RevitCortex.Tests/PowerBiLive/PowerBiDatasetSchemaSourceTests.cs`

- [ ] **Step 1: Write failing source test**

Add tests asserting `CurrentVersion` is bumped and `Schedules` defines `ElementId` before schedule value columns.

- [ ] **Step 2: Run test and verify failure**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26" --filter "FullyQualifiedName~PowerBiDatasetSchemaSourceTests"`

- [ ] **Step 3: Add schedule identity columns and schema version**

Set `CurrentVersion` to `1.1`; add `ElementId` and `UniqueId` to `SchedulesTable`.

- [ ] **Step 4: Run test and verify pass**

Run the same filtered test.

### Task 2: Element-Centric Schedule Export

**Files:**
- Modify: `src/RevitCortex.Plugin/PowerBiLive/PowerBiScheduleExporter.cs`
- Test: `src/RevitCortex.Tests/PowerBiLive/PowerBiScheduleExporterSourceTests.cs`

- [ ] **Step 1: Write failing source test**

Assert the exporter collects elements through `FilteredElementCollector(doc, sch.Id)`, emits `ElementId` and `UniqueId`, and does not rely on body row index as the natural identity.

- [ ] **Step 2: Run test and verify failure**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26" --filter "FullyQualifiedName~PowerBiScheduleExporterSourceTests"`

- [ ] **Step 3: Implement element-centric export**

For each schedule, collect displayed elements, iterate schedule fields, resolve field parameter values on instance/type, and write long-form rows keyed by element.

- [ ] **Step 4: Run test and verify pass**

Run the same filtered test.

### Task 3: Intra-Run Dedup And Append Warning

**Files:**
- Modify: `src/RevitCortex.Plugin/PowerBiLive/Tools/PbiPublishSchedulesTool.cs`
- Test: `src/RevitCortex.Tests/PowerBiLive/PbiPublishSchedulesToolSourceTests.cs`

- [ ] **Step 1: Write failing source test**

Assert schedule publish deduplicates by `ScheduleId|ElementId|ColumnName` and warns when `mode == "append"`.

- [ ] **Step 2: Run test and verify failure**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26" --filter "FullyQualifiedName~PbiPublishSchedulesToolSourceTests"`

- [ ] **Step 3: Implement minimal dedup/warning**

Apply last-write-wins dedup before posting. Add explicit warning for append mode duplicate risk.

- [ ] **Step 4: Run test and verify pass**

Run the same filtered test.

### Task 4: Build Verification

**Files:**
- No new files.

- [ ] **Step 1: Run focused tests**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26" --filter "FullyQualifiedName~PowerBi"`

- [ ] **Step 2: Run Revit target builds**

Run:
`dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
`dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`

- [ ] **Step 3: Report deployment note**

If Revit is open, do not deploy. If closed, run `powershell -ExecutionPolicy Bypass -File deploy.ps1`.
