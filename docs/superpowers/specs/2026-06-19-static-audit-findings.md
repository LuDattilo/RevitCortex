# RevitCortex — Static Audit Findings (whole-app, read-only)

**Date:** 2026-06-19
**Commit audited:** `f87e568` (branch `main`)
**Method:** two-round multi-agent static audit. Per-area read-only finders → adversarial per-finding verification → kept only `isReal && confidence ≥ 75`. Coverage: all 22 source areas across every project except tests.
**Constraint:** no code modified (goal: test, don't modify). This document records findings only.

| Round | Areas | Candidates | Confirmed ≥75 | Agents |
|---|---|---|---|---|
| 1 | Core, Communication/Router, RevitBridge, PowerBiLive, Elements, Project, IFC, Steel+Rebar, CodeExecution | 73 | 51 | 82 |
| 2 | Views, LinkedFiles, Parameters, Annotations, Sheets, Workflows, Utilities, Interop/Meta, PowerBi-legacy, UI/Commands, Updates, Discovery, Server wrappers, Core-session | 111 | 79 | 125 |
| **Total** | **22** | **184** | **130** | **207** |

After hand-verification, **2 of the 130 were false positives** (documented at the end) → **~128 real findings**.

---

## Executive summary

**One systemic defect accounts for ~110 of the findings:** a write `Transaction` that does not call
`TransactionFailureHandling.SuppressWarnings(tx)` and/or does not check `tx.Commit() != TransactionStatus.Committed`.

Two real failure modes:
1. **MCP bridge freeze** — a Revit warning at Commit opens a modal TaskDialog on the UI thread; Revit hangs until a human clicks it.
2. **Silent success on rollback** — Revit rolls the transaction back, the tool returns `Ok`, the model is unchanged but the caller believes it succeeded.

**Root cause:** the 2026-06-11 hardening (commit `6e2fe2e`) wired this pattern into ~15 tools but never propagated to the rest of the suite. **The foundation is correct** — `TransactionFailureHandling.SuppressWarnings` and `ToolHelpers.GetDryRun` were read by hand and are sound; the bugs are tools not *calling* them.

**The fix is mechanical and batchable per folder.** Highest-ROI work: a few lines per file, eliminates the most dangerous bug class.

When fixing: build BOTH `Debug R24` (net48) and `Debug R25` (net8), one file at a time per the atomic-execution rule.

---

## A. Systemic transaction-safety debt (~110 findings)

Each tool below is missing `SuppressWarnings(tx)` and/or the `Commit() != Committed` check. Grouped by folder.

### Views (all 12 tools — none use the pattern)
- `Views/CreateViewTool.cs:40-105`
- `Views/RenameViewsTool.cs:61-81`
- `Views/DuplicateViewTool.cs:50-80`
- `Views/ApplyViewTemplateTool.cs:87-99` (ApplyTemplate) + `:111-123` (RemoveTemplate)
- `Views/ManageViewTemplatesTool.cs:71-102` (Duplicate) + `:111-123` (Delete) + `:142-146` (Rename)
- `Views/CreateViewFilterTool.cs:82-142`
- `Views/CreateViewsFromRoomsTool.cs:48-111`
- `Views/BatchModifyViewRangeTool.cs:47-83`
- `Views/ManageUnplacedViewsTool.cs:72-79`
- `Views/OverrideGraphicsTool.cs:60-120`
- `Views/PlaceViewportTool.cs:61-84`
- `Views/SectionBoxFromSelectionTool.cs:70-92` (also see B: NullRef)

### Parameters (heaviest concentration)
- `Parameters/ManageGlobalParametersTool.cs` — ALL ~7 transactions: Create `:106-118`, SetValue `:148-151`, Delete `:170-173`, Rename `:199-202`, SetFormula `:228-233`, Reorder `:268-274`, Sort `:296-300`
- `Parameters/ManageProjectParametersTool.cs` — Create `:169-173`, Delete `:243-274`, SetBindingType `:368-383`, Modify `:565-569`, SetParameterGroup `:689-705`
- `Parameters/ClearParameterValuesTool.cs:64` + `:82`
- `Parameters/TransferParametersTool.cs:78` + `:81`
- `Parameters/AddSharedParameterTool.cs:124` + `:136`

### Annotations (all 7)
- `Annotations/TagRoomsTool.cs:77-108`
- `Annotations/TagWallsTool.cs:77-106`
- `Annotations/CreateTextNoteTool.cs:41-68`
- `Annotations/CreateColorLegendTool.cs:82-115`
- `Annotations/CreateDimensionsTool.cs:41-57` (also see B: orphan detail lines)
- `Annotations/ImportTableTool.cs:85-144`
- `Annotations/WipeEmptyTagsTool.cs:140-159`

### Sheets (all 5)
- `Sheets/BatchCreateSheetsTool.cs:41-98`
- `Sheets/CreatePlaceholderSheetsTool.cs:56-71` + `:122-155` + `:169-181`
- `Sheets/DuplicateSheetWithViewsTool.cs:91-175`
- `Sheets/DuplicateSheetWithContentTool.cs:81-154`
- `Sheets/AlignViewportsTool.cs:54-89`

### LinkedFiles
- `LinkedFiles/AlignLinkToHostTool.cs:62-105`
- `LinkedFiles/PinUnpinLinkInstanceTool.cs:44-66`
- `LinkedFiles/MoveLinkInstanceTool.cs:72-75`
- `LinkedFiles/HighlightLinkedElementTool.cs:112-122`
- `LinkedFiles/ShowCrossModelElementsTool.cs:235-288`

### Workflows
- `Workflows/WorkflowClashReviewTool.cs:85-101` (missing SuppressWarnings + Commit check)

### Elements (round 1)
- `Elements/CopyElementsTool.cs:90-102, 144-157, 162-174` (3 blocks)
- `Elements/SetElementWorksetTool.cs:48-114`
- `Elements/SetElementPhaseTool.cs:44-146`
- `Elements/CreateLevelTool.cs:75-112`
- `Elements/CreateArrayTool.cs:71-94` + `:106-171`
- `Elements/ChangeElementTypeTool.cs:90`
- `Elements/ColorElementsTool.cs:92-101` + `:130-165`
- `Elements/CreateFloorTool.cs:156-159`
- `Elements/CreateGridTool.cs:68-103`
- `Elements/CreateLineBasedElementTool.cs:182-190` + `:224-262`
- `Elements/ImportFromExcelTool.cs:80-127`
- `Elements/OperateElementTool.cs:130-205` (multiple Commit calls)
- `Elements/DuplicateFamilyTypeTool.cs:86-128`
- `Elements/RenameFamiliesTool.cs:61-94`
- `Elements/LoadFamilyTool.cs:57-62`

### Project (round 1)
- `Project/SetCompoundStructureTool.cs:158, 216, 273, 376, 456` (all 5 action methods)

### IFC (round 1)
- `IFC/IfcTagUnreconstructableElementsTool.cs:67-109`
- `IFC/IfcReloadLinkTool.cs:84-89`
- `IFC/IfcExportWithConfigurationTool.cs:150-153` + `IfcExportBasicTool.cs`

### StructuralSteel + Rebar (round 1)
- `StructuralSteel/StructuralSteelConnectionTools.cs:87, 160, 272, 407, 470, 562, 602, 646` (8)
- `StructuralSteel/StructuralSteelCutTools.cs:87, 187, 239, 307, 363` (5)
- `StructuralSteel/StructuralSteelFabricationTools.cs:76, 191` (2)
- `Rebar/RebarCreationTools.cs:85, 199, 289, 335, 379, 417, 458, 507` (8) — also missing dryRun preview
- `Rebar/RebarSystemTools.cs:103, 193` — also missing dryRun preview
- `Rebar/FabricReinforcementTools.cs:95, 171, 223, 285, 330` (5) — also missing dryRun preview

### CodeExecution (round 1, highest blast radius — send_code_to_revit)
- `CodeExecution/RoslynExecutor.cs:82-96` (TransactionGroup, no SuppressWarnings, Assimilate unchecked) + `:99-113` (Transaction, no SuppressWarnings, Commit unchecked)
- `CodeExecution/CodeDomExecutor.cs:89-102` (TransactionGroup) + `:106-119` (Transaction)

### Rebar/Fabric — also missing dryRun preview-first (P1)
`RebarCreationTools`, `RebarSystemTools`, `FabricReinforcementTools` predate the 2026-06-11 audit and skip `ToolHelpers.GetDryRun` entirely → `{dryRun:true}` still mutates the document.

---

## B. Genuinely distinct bugs (not the transaction theme) — hand-verified

| Sev | File:line | Bug |
|---|---|---|
| P1 | `Core/Caching/ToolResultCache.cs:54` | `entry.HitCount++` non-atomic on a shared ConcurrentDictionary value → lost-update race. Class comment falsely claims all mutation is guarded. ✅ verified |
| P1 | `Plugin/Threading/ToolExecutionHandler.cs:14` | `ManualResetEvent` (unmanaged WaitHandle) never disposed → kernel-object leak for plugin lifetime |
| P1 | `Tools/IFC/IfcGeometryHelper.cs:70-86` | `FindNearestLevel` rebuilds+sorts the full Level collector once **per element** across 6 IFC rebuild tools → O(N·L) on import |
| P1 | `Plugin/PowerBiLive/PbiActionEventHandler.cs:413` | reset-overrides clears the registry **before** validating Commit → registry/view drift; also `:460` (create-view) + `:300` (select-isolate) unchecked Commit |
| P2 | `Tools/Views/SectionBoxFromSelectionTool.cs:88,91` | `FirstOrDefault(...)!` can be null when duplicateView=false + no non-template View3D → NullRef on SetSectionBox instead of structured error. ✅ verified |
| P2 | `Plugin/Discovery/LocaleDetector.cs:19` | `param.Definition.Name` unguarded; Definition can be null → NullRef instead of defaulting to "en". Rest of codebase uses `Definition?.Name`. ✅ verified |
| P2 | `Plugin/UI/GeneralSettingsPage.xaml.cs:370` | port validated for range only, not in-use; `SocketService.Start()` SocketException has no catch → silent startup failure with no UI feedback |
| P2 | `Tools/Annotations/CreateDimensionsTool.cs:220-259` | orphan detail lines cleaned only on `NewDimension==null`, not on exception in between → invisible annotations accumulate |
| P2 | `Tools/Workflows/WorkflowRoomDocumentationTool.cs:127` | `createdViews` returned unbounded (failures capped at 50, this isn't) → huge payload on 500-room models |
| P2 | `Plugin/PowerBiLive/PowerBiElementExporter.cs:72,200` + `Tools/PbiPublishElementsTool.cs:162` | BuildRow swallows exceptions → null → rows silently dropped, no attempted-vs-exported diagnostic |
| P2 | `Tools/IFC/IfcAnalyzeRebuildabilityTool.cs:70` (IfcValidateRequest) | bare `catch {}` masks IFC parse errors → returns `valid:true` on corrupt file |
| P3 | `Plugin/PowerBi/AutoExportHook.cs:91` | async-void OnDocumentSaved swallows push failures to Trace only → silent broken PBI refresh |
| P3 | `Core/Discovery/DocumentCapabilities.cs:31,47` | `SharedParameterNames` dead field (declared + cleared in Reset, never populated/read) — cleanup candidate |
| P3 | `Server/Tools/ElementTools.cs:171` | find_untagged_elements sends both `categories`+`category`; INTENTIONAL (bridge-compat comment), benign bandwidth redundancy |

---

## C. False positives caught (verification did its job)

These passed the ≥75 confidence gate but were refuted on direct code reading. **Do NOT act on them.**

1. **`is not ViewSchedule` flagged as net48 break** — `GetScheduleDataTool.cs:84`, confidence 95.
   WRONG: `is not` is a Roslyn compiler feature (C# 9), not a runtime/BCL feature like `record`/`init`/`GetValueOrDefault`. All csproj set `<LangVersion>latest</LangVersion>`; `is not` is already used in 3 other files inside green R24 builds. See memory `feedback_is_not_pattern_safe_on_net48`.

2. **UpdateChecker Sha256 "race"** — `UpdateChecker.cs:178`, confidence 88.
   NOT manifestable: `StartDownloadAsync` is state-guarded (Idle→Downloading at `:142,:147`); re-reading an updated Sha256 on an already-downloaded file is defense-in-depth (the `C4`/`C5` comments show intent), not an exploitable race.

The ≥75 filter leaks ~3% noise — expected; high-impact findings should still be hand-verified before fixing.

---

## Suggested fix order (when authorized)

1. **`CodeExecution`** — RoslynExecutor + CodeDomExecutor. Highest blast radius (`send_code_to_revit`).
2. **`Parameters`** — highest concentration of unprotected transactions.
3. **`Views` / `Annotations` / `Sheets` / `LinkedFiles`** — whole-folder sweeps, mechanical.
4. **Distinct bugs (section B)** — each is a small targeted fix; ToolResultCache race and the two NullRefs are quick wins.
5. **Cleanups (P3)** — dead field, benign wrapper redundancy.

Build R24 + R25 after each file. Findings sourced from session `692b1c8d` task outputs `wnbyubq5m` (round 1) and `wl5ghxyth` (round 2).
