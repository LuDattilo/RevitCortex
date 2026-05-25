---
name: revitcortex
description: Use when working with RevitCortex operations, MCP tool workflows, Revit model automation, or RevitCortex C# development. Routes Codex to focused references for model operations, safe write workflows, send_code_to_revit escalation, IFC, PowerBI, Obsidian, tool development, net48/net8 compatibility, audit/read-only security, and build/test checks.
---

# RevitCortex Skill Router

Router per Codex / Claude Code. Carica solo i reference necessari per il task corrente. **Non eseguire mai operazioni MCP senza aver prima caricato il reference operator pertinente.**

## Always-on rules

Queste regole valgono in ogni sessione, indipendentemente dal task:

1. Per richieste su modello Revit: caricare `operator_01_Session_Start_Locale.md` prima di qualsiasi altra cosa.
2. Per modifiche distruttive: caricare `operator_03_Destructive_Operations_DryRun.md` e usare `dryRun: true` come prima esecuzione.
3. `send_code_to_revit` per bulk/batch richiede consenso esplicito: caricare `operator_10_SendCodeToRevit_Escalation.md`.
4. Per modifiche al codice C#: caricare `developer_22_Net48_Net8_Compatibility.md` e ricordare le build `Debug R25` + `Debug R24`.
5. In read-only mode (`~/.revitcortex/settings.json`): nessun workaround; caricare `developer_24_ReadOnly_Audit_Security.md`.

## Request classification

| Tipo richiesta | Trigger tipici | Reference da caricare |
|---|---|---|
| Operazione modello | warning, clash, parametri, viste, tag, schedule, IFC | `operator_01`, `operator_02` + dominio |
| Modifica parametri | update, compilare, svuotare, copiare, sync CSV | `operator_01`, `operator_03`, `operator_04` |
| Operazione distruttiva | delete, purge, rename, modifica massiva | `operator_03` (+ `operator_10` se script) |
| Health/Clash | morning check, warning, clash detection | `operator_01`, `operator_05` |
| Vista / annotazione | tag, color, dimension, view template | `operator_01`, `operator_06` |
| IFC | import, link, rebuild, export | `operator_01`, `operator_07` |
| PowerBI | publish, query, selection, schedule | `operator_01`, `operator_08` |
| Obsidian / knowledge | vault, note, write-back | `operator_09` |
| Script consenso | bulk via send_code, complex logic | `operator_10` |
| Nuovo tool C# | add tool, new MCP tool, schema | `developer_20`, `developer_21`, `developer_22`, `developer_25` |
| Fix C# error | net48 error, build failure | `developer_22`, `developer_25` |
| Dynamic tools | DocumentAnalyzer, capabilities | `developer_23` |
| Security / audit | read-only, audit, sandbox, permission | `developer_24` |

## How to use a reference

1. Leggi `references/00_Master_Index.md` se non sai dove cercare.
2. Carica solo i reference indicati dalla classificazione.
3. Segui `Decision rules` -> `Required checks` -> `Avoid` nell'ordine.
4. Se un workflow funzionante non e documentato, aggiungilo a `WORKFLOWS.md` E aggiorna `index_41_Workflow_Source_Map.md`.

## Indices

- `index_40_Tool_Signature_Index.md`: firme rapide dei 157 tool MCP.
- `index_41_Workflow_Source_Map.md`: mappa template -> fonte canonica.
