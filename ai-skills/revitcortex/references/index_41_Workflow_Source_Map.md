# 41 — Workflow Source Map

**Scope:** Mappa ogni reference della skill alla sua fonte canonica nel repo. Serve a evitare drift.
**Sources:** Tutto il repo RevitCortex.
**Last verified:** 2026-05-25

## Operator -> fonti

| Reference | Fonte primaria | Fonti secondarie |
|---|---|---|
| `operator_01_Session_Start_Locale.md` | `CLAUDE.md` §"IMPORTANT: Detect Revit Language First" | `WORKFLOWS.md` §"Rilevamento Lingua Revit", `AGENTS.md` |
| `operator_02_Tool_Selection_Hierarchy.md` | `CLAUDE.md` §"Tool Selection Hierarchy" | `WORKFLOWS.md` §"Verifica Stato Modello", `WORKFLOWS.md` §"Ricerca Elementi" |
| `operator_03_Destructive_Operations_DryRun.md` | `CLAUDE.md` §"Confirmation Dialogs for Destructive Operations" | `WORKFLOWS.md` §"Operazioni Distruttive" |
| `operator_04_Parameter_Workflows.md` | `CLAUDE.md` §"Modifying parameters" | `WORKFLOWS.md` §"Aggiornamento Parametri" |
| `operator_05_Model_Health_Warnings_Clash.md` | `CLAUDE.md` §"Session A — Morning Check" | `WORKFLOWS.md` §"Controllo Qualita Mattutino", `WORKFLOWS.md` §"Clash Detection" |
| `operator_06_View_And_Annotation_Workflows.md` | `CLAUDE.md` §"Tool Behavioral Notes" | `WORKFLOWS.md` §"Tagging Automatico", `WORKFLOWS.md` §"Colorazione" |
| `operator_07_IFC_Workflows.md` | `docs/RevitCortex_IFC_Guide.md` | `WORKFLOWS.md` §"IFC" |
| `operator_08_PowerBI_Workflows.md` | `docs/powerbi-push-architecture-spec.md` | `docs/powerbi-integration-spec.md`, `docs/USER_GUIDE.md` §PBI |
| `operator_09_Obsidian_Workflows.md` | `docs/superpowers/specs/2026-05-19-revitcortex-obsidian-integration-design.md` | — |
| `operator_10_SendCodeToRevit_Escalation.md` | `CLAUDE.md` §"send_code_to_revit" | `docs/superpowers/specs/2026-04-15-send-code-to-revit-design.md` |

## Developer -> fonti

| Reference | Fonte primaria | Fonti secondarie |
|---|---|---|
| `developer_20_New_Tool_Checklist.md` | `CLAUDE.md` §"ICortexTool" | `src/RevitCortex.Core/Tools/ICortexTool.cs` |
| `developer_21_CortexResult_And_Errors.md` | `CLAUDE.md` §"CortexResult<T>" | `src/RevitCortex.Core/Results/CortexResult.cs` |
| `developer_22_Net48_Net8_Compatibility.md` | `CLAUDE.md` §"Cross-Target Compatibility" | Memoria utente `feedback_revit_target_range.md` |
| `developer_23_Dynamic_Tools_And_Capabilities.md` | `CLAUDE.md` §"IsDynamic Convention" | `src/RevitCortex.Core/Discovery/DocumentCapabilities.cs` |
| `developer_24_ReadOnly_Audit_Security.md` | `docs/SECURITY.md` | `CLAUDE.md` §"Security Requirements (NFR)" |
| `developer_25_Build_Test_Release_Checklist.md` | `CLAUDE.md` §"Build Commands" | `deploy.ps1`, memoria utente `reference_release_flow.md` |

## Aggiornamento

Quando emerge un nuovo workflow stabile:
1. Aggiungerlo a `WORKFLOWS.md` se e operativo BIM, o al `CLAUDE.md` se e una regola di sviluppo.
2. Aggiornare il reference rilevante in `ai-skills/revitcortex/references/`.
3. Bumpare `Last verified` del reference.
4. Aggiornare questa tabella se cambia la fonte primaria.
