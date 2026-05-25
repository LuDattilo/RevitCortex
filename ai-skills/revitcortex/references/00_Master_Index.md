# 00 — Master Index

**Scope:** Punto di ingresso quando il router non e certo del template da caricare.
**Sources:** Tutta la knowledge base ai-skills/revitcortex/references/
**Last verified:** 2026-05-25

## Reference catalog

### Operator (workflow BIM)
| File | Quando usare |
|---|---|
| `operator_01_Session_Start_Locale.md` | Prima chiamata della sessione: detect lingua, primo `get_project_info` |
| `operator_02_Tool_Selection_Hierarchy.md` | Scegliere il tool giusto tra alternative (find elements, model status...) |
| `operator_03_Destructive_Operations_DryRun.md` | Delete, purge, rename, modifiche massive |
| `operator_04_Parameter_Workflows.md` | Set parametri singoli / bulk / sync CSV |
| `operator_05_Model_Health_Warnings_Clash.md` | Morning check, get_warnings, clash detection |
| `operator_06_View_And_Annotation_Workflows.md` | Tag, color, dimensions, view templates |
| `operator_07_IFC_Workflows.md` | Link IFC, rebuild, export IFC |
| `operator_08_PowerBI_Workflows.md` | Push elements/schedules, query, selection roundtrip |
| `operator_09_Obsidian_Workflows.md` | Vault snapshot, command note, write-back |
| `operator_10_SendCodeToRevit_Escalation.md` | Quando proporre script vs tool nativo |

### Developer (sviluppo C# RevitCortex)
| File | Quando usare |
|---|---|
| `developer_20_New_Tool_Checklist.md` | Aggiungere un nuovo `ICortexTool` |
| `developer_21_CortexResult_And_Errors.md` | Envelope success/fail, error codes |
| `developer_22_Net48_Net8_Compatibility.md` | Compilare per R23/R24 (net48) e R25/R26 (net8) |
| `developer_23_Dynamic_Tools_And_Capabilities.md` | Tool che dipendono da `DocumentCapabilities` |
| `developer_24_ReadOnly_Audit_Security.md` | Naming convention read-only, audit, sandbox |
| `developer_25_Build_Test_Release_Checklist.md` | Build matrix R23->R27, deploy, release |

### Indices
| File | Cosa contiene |
|---|---|
| `index_40_Tool_Signature_Index.md` | Firme compatte dei 157 tool MCP |
| `index_41_Workflow_Source_Map.md` | Mappa template -> fonte canonica nel repo |

## Quick triage

1. La richiesta tocca codice RevitCortex C#? -> `developer_*`
2. La richiesta tocca un modello Revit aperto? -> `operator_01` + dominio
3. La richiesta e "come e documentato X"? -> `index_41`
4. La richiesta e "quale tool fa X"? -> `index_40` + `operator_02`
