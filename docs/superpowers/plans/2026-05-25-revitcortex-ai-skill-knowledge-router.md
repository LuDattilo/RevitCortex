# RevitCortex AI Skill + Knowledge Router — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Costruire `ai-skills/revitcortex/`, una skill router + 17 file `.md` di knowledge base, che guida Codex/Claude Code verso template mirati invece di rileggere CLAUDE.md/WORKFLOWS.md/AGENTS.md per ogni task.

**Architecture:** Knowledge base statica (zero modifiche al codice C#). `SKILL.md` agisce da classificatore: legge la richiesta utente, sceglie 2-4 reference `.md` da caricare, lascia l'esecuzione ai tool MCP esistenti. Separazione netta tra `operator_*` (workflow BIM) e `developer_*` (pattern C#).

**Tech Stack:** Markdown puro. Nessuna dipendenza, nessun build step. Frontmatter YAML in `SKILL.md` per la registrazione skill.

**Spec di riferimento:** `docs/superpowers/specs/2026-05-25-revitcortex-ai-skill-knowledge-router-design.md`

---

## File Structure

```
ai-skills/
  revitcortex/
    SKILL.md                                          # Router (≤80 righe)
    agents/
      openai.yaml                                     # Codex agent metadata
    references/
      00_Master_Index.md                              # TOC + flowchart classificazione
      operator_01_Session_Start_Locale.md             # Detect lingua, first call get_project_info
      operator_02_Tool_Selection_Hierarchy.md         # Quale tool per quale task
      operator_03_Destructive_Operations_DryRun.md    # dryRun pattern + tool con conferma
      operator_04_Parameter_Workflows.md              # set vs bulk vs sync_csv
      operator_05_Model_Health_Warnings_Clash.md      # Morning check, warnings, clash
      operator_06_View_And_Annotation_Workflows.md    # tag_rooms, color_elements, dimensions
      operator_07_IFC_Workflows.md                    # Link, rebuild, export IFC
      operator_08_PowerBI_Workflows.md                # Publish, query, selection
      operator_09_Obsidian_Workflows.md               # Vault snapshot, write-back
      operator_10_SendCodeToRevit_Escalation.md       # Consenso esplicito, alternativa nativa
      developer_20_New_Tool_Checklist.md              # File da toccare per nuovo ICortexTool
      developer_21_CortexResult_And_Errors.md         # Envelope, error codes, suggestion
      developer_22_Net48_Net8_Compatibility.md        # Feature vietate su R23/R24
      developer_23_Dynamic_Tools_And_Capabilities.md  # DocumentAnalyzer, EnableTool
      developer_24_ReadOnly_Audit_Security.md         # Naming convention, audit, sandbox
      developer_25_Build_Test_Release_Checklist.md    # Build R25/R24/R23/R26/R27 + deploy
      index_40_Tool_Signature_Index.md                # Firme compatte da tool-schemas.txt
      index_41_Workflow_Source_Map.md                 # template -> fonte (anti-drift)
```

**Responsabilità per file:**
- `SKILL.md`: classifica + link ai reference. Mai contenuto operativo inline.
- `00_Master_Index.md`: tabella richiesta → reference. Caricato sempre.
- `operator_*`: workflow BIM, estratti da AGENTS.md + WORKFLOWS.md.
- `developer_*`: pattern C#, estratti da CLAUDE.md (project) + memoria utente.
- `index_*`: cataloghi rapidi (firme tool, mappa sorgenti).

Ogni reference segue lo schema:
```markdown
# Titolo

**Scope:** quando usare questo template
**Sources:** AGENTS.md §X, WORKFLOWS.md §Y, docs/Z.md
**Last verified:** 2026-05-25

## Decision rules
...

## Required checks
...

## Avoid
...
```

---

## Task 1: Scaffold cartelle e file vuoti

**Files:**
- Create: `ai-skills/revitcortex/SKILL.md` (placeholder)
- Create: `ai-skills/revitcortex/agents/openai.yaml` (placeholder)
- Create: `ai-skills/revitcortex/references/00_Master_Index.md` (placeholder)
- Create: 10 `operator_*.md` (placeholder)
- Create: 6 `developer_*.md` (placeholder)
- Create: 2 `index_*.md` (placeholder)

- [ ] **Step 1: Creare la struttura di cartelle**

```bash
mkdir -p ai-skills/revitcortex/agents
mkdir -p ai-skills/revitcortex/references
```

- [ ] **Step 2: Creare i file con un header minimo**

Per ogni file, scrivere il solo header (titolo + scope + sources + last verified) come placeholder strutturato. Esempio per `operator_01_Session_Start_Locale.md`:

```markdown
# Session Start & Locale Detection

**Scope:** Prima chiamata MCP di una sessione RevitCortex su un modello aperto.
**Sources:** AGENTS.md §"Detect Revit Language First", CLAUDE.md §"IMPORTANT: Detect Revit Language First", WORKFLOWS.md §"Rilevamento Lingua Revit"
**Last verified:** 2026-05-25

## Decision rules
TBD — Task 4

## Required checks
TBD — Task 4

## Avoid
TBD — Task 4
```

I `TBD — Task N` sono **intenzionali** in questo step: indicano in quale task successivo il file verrà popolato. Lo step 3 verifica che tutti i `TBD` siano risolti prima del merge finale.

- [ ] **Step 3: Verificare la struttura**

Run: `ls -R ai-skills/revitcortex/`
Expected: 20 file totali (1 SKILL.md + 1 openai.yaml + 1 master index + 10 operator + 6 developer + 2 index)

- [ ] **Step 4: Commit**

```bash
git add ai-skills/
git commit -m "feat(skill): scaffold ai-skills/revitcortex/ structure"
```

---

## Task 2: SKILL.md router

**Files:**
- Modify: `ai-skills/revitcortex/SKILL.md` (riscrivi completo)

- [ ] **Step 1: Scrivere SKILL.md con frontmatter + tabella di classificazione**

```markdown
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
3. Segui `Decision rules` → `Required checks` → `Avoid` nell'ordine.
4. Se un workflow funzionante non è documentato, aggiungilo a `WORKFLOWS.md` E aggiorna `index_41_Workflow_Source_Map.md`.

## Indices

- `index_40_Tool_Signature_Index.md`: firme rapide dei 157 tool MCP.
- `index_41_Workflow_Source_Map.md`: mappa template → fonte canonica.
```

- [ ] **Step 2: Verifica che SKILL.md non ecceda 80 righe**

Run: `wc -l ai-skills/revitcortex/SKILL.md`
Expected: ≤80 righe

- [ ] **Step 3: Commit**

```bash
git add ai-skills/revitcortex/SKILL.md
git commit -m "feat(skill): SKILL.md router with classification table"
```

---

## Task 3: 00_Master_Index.md + index_41_Workflow_Source_Map.md

**Files:**
- Modify: `ai-skills/revitcortex/references/00_Master_Index.md`
- Modify: `ai-skills/revitcortex/references/index_41_Workflow_Source_Map.md`

- [ ] **Step 1: Scrivere 00_Master_Index.md**

```markdown
# 00 — Master Index

**Scope:** Punto di ingresso quando il router non è certo del template da caricare.
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
| `developer_25_Build_Test_Release_Checklist.md` | Build matrix R23→R27, deploy, release |

### Indices
| File | Cosa contiene |
|---|---|
| `index_40_Tool_Signature_Index.md` | Firme compatte dei 157 tool MCP |
| `index_41_Workflow_Source_Map.md` | Mappa template → fonte canonica nel repo |

## Quick triage

1. La richiesta tocca codice RevitCortex C#? → `developer_*`
2. La richiesta tocca un modello Revit aperto? → `operator_01` + dominio
3. La richiesta è "come è documentato X"? → `index_41`
4. La richiesta è "quale tool fa X"? → `index_40` + `operator_02`
```

- [ ] **Step 2: Scrivere index_41_Workflow_Source_Map.md**

```markdown
# 41 — Workflow Source Map

**Scope:** Mappa ogni reference della skill alla sua fonte canonica nel repo. Serve a evitare drift.
**Sources:** Tutto il repo RevitCortex.
**Last verified:** 2026-05-25

## Operator → fonti

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

## Developer → fonti

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
1. Aggiungerlo a `WORKFLOWS.md` se è operativo BIM, o al `CLAUDE.md` se è una regola di sviluppo.
2. Aggiornare il reference rilevante in `ai-skills/revitcortex/references/`.
3. Bumpare `Last verified` del reference.
4. Aggiornare questa tabella se cambia la fonte primaria.
```

- [ ] **Step 3: Commit**

```bash
git add ai-skills/revitcortex/references/00_Master_Index.md ai-skills/revitcortex/references/index_41_Workflow_Source_Map.md
git commit -m "feat(skill): master index + workflow source map"
```

---

## Task 4: operator_01 + operator_02 (session start + tool selection)

**Files:**
- Modify: `ai-skills/revitcortex/references/operator_01_Session_Start_Locale.md`
- Modify: `ai-skills/revitcortex/references/operator_02_Tool_Selection_Hierarchy.md`

- [ ] **Step 1: Scrivere operator_01_Session_Start_Locale.md**

Contenuto da estrarre da:
- `CLAUDE.md` §"IMPORTANT: Detect Revit Language First" (righe ~210-225)
- `CLAUDE.md` §"Default Parameters to Override" → bullet `get_project_info`
- `WORKFLOWS.md` §"Rilevamento Lingua Revit"

```markdown
# 01 — Session Start & Locale Detection

**Scope:** Prima chiamata MCP di una sessione su modello aperto.
**Sources:** CLAUDE.md §"IMPORTANT: Detect Revit Language First", WORKFLOWS.md §"Rilevamento Lingua Revit"
**Last verified:** 2026-05-25

## Decision rules

1. La **prima** chiamata di sessione deve essere `get_project_info` con tutti gli include attivi (default).
2. Da quel momento, ogni `get_project_info` successivo deve filtrare: `{"includeLevels": false, "includeLinks": false, "includePhases": false, "includeWorksets": false}`.
3. La lingua si rileva dai nomi dei parametri restituiti, non si assume:
   - EN: "Level", "Comments", "Type Name"
   - IT: "Livello", "Commenti", "Nome del tipo"
   - FR: "Niveau", "Commentaires", "Nom du type"
   - DE: "Ebene", "Kommentare", "Typname"
4. Per categorie, preferire sempre i codici `OST_*` (language-independent) ai nomi localizzati.

## Required checks

- [ ] `get_project_info` completo eseguito una sola volta a inizio sessione.
- [ ] Lingua rilevata e annotata nel contesto della conversazione.
- [ ] Se il modello ha fasi (`phases.length > 0`), tenerlo presente per `set_element_phase`.
- [ ] Se è workshared (`isWorkshared: true`), tenerlo presente per `set_element_workset`.

## Avoid

- Non assumere la lingua: sempre verificare.
- Non rieseguire `get_project_info` completo durante la sessione: filtrare.
- Non confondere `isWorkshared` con presenza di fasi: sono indipendenti.
```

- [ ] **Step 2: Scrivere operator_02_Tool_Selection_Hierarchy.md**

Contenuto da estrarre da:
- `CLAUDE.md` §"Tool Selection Hierarchy" (righe ~390-440)
- `WORKFLOWS.md` §"Verifica Stato Modello (Costo Crescente)"
- `WORKFLOWS.md` §"Ricerca Elementi"

```markdown
# 02 — Tool Selection Hierarchy

**Scope:** Scegliere il tool con minor costo token che risolve il task.
**Sources:** CLAUDE.md §"Tool Selection Hierarchy", WORKFLOWS.md §"Verifica Stato Modello", WORKFLOWS.md §"Ricerca Elementi"
**Last verified:** 2026-05-25

## Decision rules

### Stato del modello (costo crescente)

| Step | Tool | Token cost | Quando |
|---|---|---|---|
| 1 | `check_model_health` | ~200 | Quick check |
| 2 | `analyze_model_statistics` (compact: true) | ~400 | Statistiche basilari |
| 3 | `workflow_model_audit` con filtri | ~800 | Audit mirato |
| 4 | `workflow_model_audit` completo | ~3000 | Audit completo (raro) |

### Trovare elementi

| Caso | Tool | Note |
|---|---|---|
| 1 parametro, valore esatto | `export_elements_data` con `filterParameterName`/`filterValue` | Veloce |
| Range / AND-OR / multi-param | `ai_element_filter` | Wrappare in `{"data": {...}}` |
| Elementi vista attiva | `get_current_view_elements` con `fields` e `limit` | |
| Volume/stanza | `get_elements_in_spatial_volume` con `categoryFilter` | |
| Parametro custom vuoto | NON guess: prima `get_element_parameters` su 1 elemento campione per scoprire i nomi | Mai assumere il formato del nome |

### Modifica parametri

| Caso | Tool |
|---|---|
| 1 elemento, 1-3 parametri | `set_element_parameters` |
| N elementi, stesso parametro/valore | `bulk_modify_parameter_values` (dryRun prima) |
| N elementi, parametri diversi | `sync_csv_parameters` |
| Copia tra elementi | `match_element_properties` con `parameterNames` esplicito |

### Clash

| Caso | Tool |
|---|---|
| Conteggio + lista ID | `clash_detection` |
| Review visuale 3D | `workflow_clash_review` |

## Required checks

- [ ] Verificato che `check_model_health` non basti prima di salire al livello 3-4.
- [ ] `ai_element_filter` chiamato con il wrapper `data` obbligatorio.
- [ ] `bulk_modify_parameter_values` eseguito con `dryRun: true` come prima call.
- [ ] Su modelli architettonici, ricordare che colonne = `OST_Columns` (non `OST_StructuralColumns`).

## Avoid

- Non partire dal livello 4 (`workflow_model_audit` completo) se basta il livello 1.
- Non usare `ai_element_filter` con `maxElements: 1000` di default.
- Non chiamare `audit_families` globale per cercare una singola categoria.
- Non assumere nomi parametri custom (WBS_*, Code_*): scoprirli prima.
```

- [ ] **Step 3: Commit**

```bash
git add ai-skills/revitcortex/references/operator_01_Session_Start_Locale.md ai-skills/revitcortex/references/operator_02_Tool_Selection_Hierarchy.md
git commit -m "feat(skill): operator_01 session start + operator_02 tool selection"
```

---

## Task 5: operator_03 + operator_04 (destructive + parameter workflows)

**Files:**
- Modify: `ai-skills/revitcortex/references/operator_03_Destructive_Operations_DryRun.md`
- Modify: `ai-skills/revitcortex/references/operator_04_Parameter_Workflows.md`

- [ ] **Step 1: Scrivere operator_03_Destructive_Operations_DryRun.md**

Contenuto da estrarre da:
- `CLAUDE.md` §"Confirmation Dialogs for Destructive Operations"
- `WORKFLOWS.md` §"Operazioni Distruttive (Pattern dryRun)"

```markdown
# 03 — Destructive Operations & DryRun

**Scope:** Qualsiasi tool che cancella, modifica massivamente o sovrascrive dati.
**Sources:** CLAUDE.md §"Confirmation Dialogs", WORKFLOWS.md §"Operazioni Distruttive"
**Last verified:** 2026-05-25

## Tool con conferma nativa Revit

`delete_element`, `delete_selection`, `delete_material`, `purge_unused`, `wipe_empty_tags`, `set_element_parameters`, `set_compound_structure`, `batch_rename`, `override_graphics`, `set_element_phase`, `set_element_workset`, `change_element_type`, `load_family`.

Tutti questi mostrano un TaskDialog nativo prima dell'esecuzione.

## Decision rules

1. Per tool con flag `dryRun`: **sempre** prima call con `dryRun: true`.
2. Leggere SOLO i contatori (`modifiedCount`, `skippedCount`, `plannedCount`) dal dryRun, non la lista elementi.
3. Eseguire la versione reale solo dopo aver mostrato l'anteprima all'utente o aver ottenuto consenso esplicito.
4. Se l'utente annulla, il tool restituisce `CortexErrorCode.Cancelled`:
   ```json
   {"success": false, "error": {"code": "Cancelled", "message": "Operation cancelled by user"}}
   ```
5. Non ripetere automaticamente un'operazione cancellata: chiedere all'utente cosa fare.

## Required checks

- [ ] DryRun eseguito.
- [ ] Contatori letti, non lista elementi.
- [ ] Conferma esplicita dell'utente prima dell'esecuzione reale.
- [ ] Gestione `Cancelled`.

## Avoid

- Non eseguire operazioni distruttive senza dryRun.
- Non leggere l'intera lista elementi dal dryRun (spreca token).
- Non chiamare il tool una seconda volta automaticamente dopo `Cancelled`.
- Non bypassare con `send_code_to_revit` per "saltare" la conferma.
```

- [ ] **Step 2: Scrivere operator_04_Parameter_Workflows.md**

Contenuto da estrarre da:
- `CLAUDE.md` §"Modifying parameters" + §"Default Parameters to Override" → `bulk_modify_parameter_values`
- `WORKFLOWS.md` §"Aggiornamento Parametri su N Elementi"
- `WORKFLOWS.md` §"Aggiornamento Parametri Diversi per Elemento"
- `WORKFLOWS.md` §"Copia Parametri tra Elementi"

```markdown
# 04 — Parameter Workflows

**Scope:** Read/Write parametri Revit (single, bulk, CSV-based, copy).
**Sources:** CLAUDE.md §"Modifying parameters", WORKFLOWS.md §"Aggiornamento Parametri"
**Last verified:** 2026-05-25

## Decision rules

### Quale tool

| Caso | Tool | Note |
|---|---|---|
| 1 elemento, 1-3 parametri | `set_element_parameters` | |
| N elementi, stesso parametro+valore | `bulk_modify_parameter_values` | dryRun obbligatorio |
| N elementi, parametri diversi per ognuno | `sync_csv_parameters` | CSV con colonna `ElementId` |
| Copia parametri tra elementi | `match_element_properties` | sempre con `parameterNames` esplicito |

### Discovery nomi parametri

1. Per parametri custom (WBS_*, Code_*, ecc.): mai assumere il nome.
2. `get_element_parameters` su 1 elemento campione → leggere i nomi esatti.
3. Type parameter sono prefissati con `[Type]` nella risposta.

### Type parameter

- Per filtrare un type parameter (es. nome del tipo): `filter_by_parameter_value` con `parameterType: "type"`.
- Default `parameterType: "both"` può NON risolvere stringhe type-level.

## Required checks

- [ ] Nomi parametri verificati prima del bulk update.
- [ ] `bulk_modify_parameter_values` con `dryRun: true` come prima call.
- [ ] Dal dryRun lette solo `modifiedCount` e `skippedCount`.
- [ ] `match_element_properties` sempre con `parameterNames` esplicito.

## Avoid

- Non chiamare `set_element_parameters` in loop per N elementi: usare `bulk` o `sync_csv`.
- Non assumere nomi parametri custom.
- Non eseguire `bulk_modify_parameter_values` senza dryRun.
- Non leggere l'intera lista elementi dal dryRun.
```

- [ ] **Step 3: Commit**

```bash
git add ai-skills/revitcortex/references/operator_03_Destructive_Operations_DryRun.md ai-skills/revitcortex/references/operator_04_Parameter_Workflows.md
git commit -m "feat(skill): operator_03 destructive + operator_04 parameter workflows"
```

---

## Task 6: operator_05 + operator_06 (model health/clash + view/annotation)

**Files:**
- Modify: `ai-skills/revitcortex/references/operator_05_Model_Health_Warnings_Clash.md`
- Modify: `ai-skills/revitcortex/references/operator_06_View_And_Annotation_Workflows.md`

- [ ] **Step 1: Scrivere operator_05_Model_Health_Warnings_Clash.md**

Contenuto da estrarre da:
- `CLAUDE.md` §"Session A — Morning Check"
- `CLAUDE.md` §"Default Parameters to Override" → `get_warnings`
- `WORKFLOWS.md` §"Controllo Qualita Mattutino"
- `WORKFLOWS.md` §"Clash Detection"
- Memoria utente `feedback_lines_per_view_crash.md`

```markdown
# 05 — Model Health, Warnings, Clash

**Scope:** Controlli rapidi sul modello e clash detection.
**Sources:** CLAUDE.md §"Session A — Morning Check", WORKFLOWS.md §"Controllo Qualita Mattutino"
**Last verified:** 2026-05-25

## Decision rules

### Morning check (~500-800 token)

Sequenza canonica:
1. `check_model_health` (compact: true)
2. `get_warnings` con `maxWarnings: 10` (mai 500 di default)
3. [opzionale] `clash_detection` su una coppia di discipline

Dopo questa sequenza, chiudere la sessione. Non incatenare task di authoring.

### get_warnings

| Scenario | maxWarnings |
|---|---|
| Quick check | 10 |
| Analisi categoria | 50 |
| Export completo | (no default) |

### Clash detection

| Caso | Tool |
|---|---|
| Conteggio + lista ID | `clash_detection` (400-600 token) |
| Review visuale 3D con section box | `workflow_clash_review` (800+ token) |

Su modelli architettonici, ricordare: colonne = `OST_Columns`, NON `OST_StructuralColumns`.

### lines_per_view_count

⚠️ ATTENZIONE: questo tool può crashare il server su modelli con 300+ viste.
- Mai eseguire in parallelo con altri tool.
- Sempre con `threshold >= 20`.
- Su modelli grandi, considerare di non chiamarlo affatto.

## Required checks

- [ ] `get_warnings` chiamato con `maxWarnings` esplicito (mai default).
- [ ] `clash_detection`: specificate le due categorie esatte.
- [ ] `workflow_model_audit` usato solo se quick check non basta.

## Avoid

- Non usare `workflow_model_audit` per un check veloce (3000+ token vs 500-800).
- Non chiamare `get_warnings` senza `maxWarnings`.
- Non eseguire `lines_per_view_count` in parallelo.
- Non mescolare QA + authoring nella stessa sessione lunga.
```

- [ ] **Step 2: Scrivere operator_06_View_And_Annotation_Workflows.md**

Contenuto da estrarre da:
- `CLAUDE.md` §"Tool Behavioral Notes"
- `CLAUDE.md` §"Tool-Specific Corrections" → `color_elements`, `create_dimensions`, `tag_rooms`/`tag_walls`
- `WORKFLOWS.md` §"Tagging Automatico"
- `WORKFLOWS.md` §"Colorazione Elementi per Categoria"
- `WORKFLOWS.md` §"Quotatura Elementi"
- Memoria utente `feedback_revit_view_name_illegal_chars.md`

```markdown
# 06 — View & Annotation Workflows

**Scope:** Tag, color, dimensions, view templates, viewports.
**Sources:** CLAUDE.md §"Tool Behavioral Notes", WORKFLOWS.md §"Tagging/Colorazione/Quotatura"
**Last verified:** 2026-05-25

## Decision rules

### Tag rooms / Tag walls

- Operano SOLO sulla vista attiva di Revit.
- La vista deve contenere elementi visibili della categoria richiesta.
- Sequenza: `get_current_view_info` → verifica → `tag_rooms` / `tag_walls`.

### Color elements

- Usa nomi categoria **localizzati** (dipende dalla lingua Revit).
- Funziona solo su viste con elementi visibili di quella categoria.
- FALLISCE su DrawingSheet / Cover Sheet: passare prima a FloorPlan o 3D.

### Create dimensions

- Il parametro Z deve corrispondere ESATTAMENTE alla quota del livello.
- Sequenza: `get_project_info` (livelli con quote) → `create_dimensions` con Z esatto.

### Naming viste

I seguenti caratteri sono **vietati** nei nomi vista:
`:` `\` `/` `{` `}` `[` `]` `|` 

Per timestamp, usare `HH-mm-ss` (mai `HH:mm:ss`).

## Required checks

- [ ] Vista attiva verificata con `get_current_view_info` prima di tag/color.
- [ ] Per `color_elements`: vista NON è sheet/cover.
- [ ] Per `create_dimensions`: Z preso da `get_project_info` (non a mano).
- [ ] Per nomi vista: nessun carattere vietato.

## Avoid

- Non chiamare `tag_rooms`/`tag_walls` senza verificare la vista attiva.
- Non usare `color_elements` su sheet (fallirà).
- Non approssimare Z in `create_dimensions`.
- Non includere `:` o `/` nei nomi vista.
```

- [ ] **Step 3: Commit**

```bash
git add ai-skills/revitcortex/references/operator_05_Model_Health_Warnings_Clash.md ai-skills/revitcortex/references/operator_06_View_And_Annotation_Workflows.md
git commit -m "feat(skill): operator_05 health/clash + operator_06 view/annotation"
```

---

## Task 7: operator_07 + operator_08 (IFC + PowerBI)

**Files:**
- Modify: `ai-skills/revitcortex/references/operator_07_IFC_Workflows.md`
- Modify: `ai-skills/revitcortex/references/operator_08_PowerBI_Workflows.md`

- [ ] **Step 1: Scrivere operator_07_IFC_Workflows.md**

Contenuto da estrarre da:
- `docs/RevitCortex_IFC_Guide.md`
- `WORKFLOWS.md` §"IFC"

```markdown
# 07 — IFC Workflows

**Scope:** Link, rebuild, export IFC. Pattern dryRun obbligatorio per rebuild.
**Sources:** docs/RevitCortex_IFC_Guide.md, WORKFLOWS.md §"IFC"
**Last verified:** 2026-05-25

## Decision rules

### Verifica capacità

Prima di qualsiasi operazione IFC: `ifc_get_capabilities`. Mostra versioni IFC supportate e se `revit-ifc` plugin è installato.

### Sequenza canonica rebuild

1. `ifc_open_or_import` oppure `ifc_link`
2. `ifc_analyze_rebuildability` con `compact: true`
3. `ifc_list_rebuild_candidates` con `compact: true` (filtrato per categoria)
4. Per categoria: `ifc_rebuild_walls` / `ifc_rebuild_floors` / `ifc_rebuild_roofs` / `ifc_rebuild_openings` / `ifc_rebuild_structural_members` / `ifc_rebuild_family_instances`
5. `ifc_compare_original_vs_rebuilt` per verifica.
6. `ifc_tag_unreconstructable_elements` per gli elementi non ricostruibili.

### Export

- Basic: `ifc_export_basic`
- Con configurazione: `ifc_get_export_configuration` → `ifc_export_with_configuration`

### Mapping famiglie

`ifc_set_family_mapping_file` consente di caricare un mapping custom prima del rebuild.

## Required checks

- [ ] `ifc_get_capabilities` chiamato come prima call IFC della sessione.
- [ ] Rebuild eseguito categoria per categoria, non in blocco.
- [ ] `ifc_validate_request` chiamato prima di rebuild costosi.
- [ ] `compact: true` per i tool di analisi (rebuildability, candidates).

## Avoid

- Non tentare rebuild senza prima `ifc_analyze_rebuildability`.
- Non chiamare rebuild su tutte le categorie in parallelo.
- Non importare IFC pesanti senza verificare prima le capacità.
```

- [ ] **Step 2: Scrivere operator_08_PowerBI_Workflows.md**

Contenuto da estrarre da:
- `docs/powerbi-push-architecture-spec.md`
- `docs/USER_GUIDE.md` (sezioni PBI)
- Memoria utente `reference_pbi_push_api_constraints.md`
- Memoria utente `feedback_pbi_elementid_mandatory.md`

```markdown
# 08 — PowerBI Workflows

**Scope:** Push elements/schedules, query roundtrip, selection cross-app.
**Sources:** docs/powerbi-push-architecture-spec.md, docs/USER_GUIDE.md §PBI
**Last verified:** 2026-05-25

## Decision rules

### Auth check

`pbi_check_auth` prima di qualsiasi push. Se non autenticato, l'utente deve fare sign-in manualmente.

### Push elements / schedules / selection

| Caso | Tool |
|---|---|
| Push elementi filtrati | `pbi_publish_elements` |
| Push uno schedule esistente | `pbi_publish_schedules` |
| Push selezione corrente | `pbi_publish_selection` |
| Push tabella arbitraria | `push_table_to_powerbi` |

**REGOLA CRITICA:** Ogni riga pushata DEVE contenere una colonna `ElementId`. È la chiave che permette a PBI di joinare con la tabella master Elements e di abilitare il cross-filter.

### Query / Selection roundtrip

1. `pbi_query` → ottiene risultati DAX/JSON
2. `select_from_powerbi` → applica selezione filtrata in Revit
3. `pbi_get_binding` → verifica binding workspace ↔ dataset

### Override visuali

Gli override pittati da PBI devono essere wipati solo su elementi che PBI stesso ha toccato (non iterare TUTTI gli elementi visibili). Usare `clear_overrides` con scope ristretto.

### Vincoli Push API

- No upsert: ogni push aggiunge righe.
- DELETE è all-or-nothing.
- Limite: 1.000.000 righe/ora.
- API deprecata da 2027-10-31: prevedere migrazione a Service Principal + REST.

## Required checks

- [ ] `pbi_check_auth` chiamato.
- [ ] Ogni payload pushato ha colonna `ElementId`.
- [ ] Per CSV/Excel: ElementId presente.
- [ ] Override reset scoped, non globale.

## Avoid

- Non pushare dati senza `ElementId`.
- Non usare DELETE per "aggiornare": è all-or-nothing.
- Non iterare TUTTI gli elementi visibili nel reset override.
- Non assumere che l'utente sia autenticato.
```

- [ ] **Step 3: Commit**

```bash
git add ai-skills/revitcortex/references/operator_07_IFC_Workflows.md ai-skills/revitcortex/references/operator_08_PowerBI_Workflows.md
git commit -m "feat(skill): operator_07 IFC + operator_08 PowerBI workflows"
```

---

## Task 8: operator_09 + operator_10 (Obsidian + send_code_to_revit)

**Files:**
- Modify: `ai-skills/revitcortex/references/operator_09_Obsidian_Workflows.md`
- Modify: `ai-skills/revitcortex/references/operator_10_SendCodeToRevit_Escalation.md`

- [ ] **Step 1: Scrivere operator_09_Obsidian_Workflows.md**

Contenuto da estrarre da:
- `docs/superpowers/specs/2026-05-19-revitcortex-obsidian-integration-design.md`

```markdown
# 09 — Obsidian Workflows

**Scope:** Integrazione vault Obsidian con RevitCortex (snapshot, note, write-back).
**Sources:** docs/superpowers/specs/2026-05-19-revitcortex-obsidian-integration-design.md
**Last verified:** 2026-05-25

## Stato

Spec approvata, implementazione non ancora rilasciata in production. I tool descritti qui sono pianificati. Verificare in `tool-schemas.txt` se sono disponibili prima di proporli all'utente.

## Decision rules (preview)

### Snapshot verso vault

Esportare lo stato del modello (livelli, fasi, categorie, parametri custom) come file Markdown dentro un vault Obsidian configurato.

### Command note

Note Obsidian con frontmatter `cortex-command` interpretate dalla skill: contengono un comando MCP, lo stato di esecuzione, il timestamp.

### Write-back parametrico

Modifiche fatte nel vault (es. valore di un parametro in un file `.md`) possono essere applicate al modello via `sync_csv_parameters` o tool dedicato, con dry-run obbligatorio.

## Avoid

- Non proporre questi tool se non sono ancora nello schema.
- Non bypassare dry-run su write-back.
```

- [ ] **Step 2: Scrivere operator_10_SendCodeToRevit_Escalation.md**

Contenuto da estrarre da:
- `CLAUDE.md` §"send_code_to_revit" (righe ~245-260)
- `docs/SECURITY.md` §"Sandbox for `send_code_to_revit`"
- `docs/superpowers/specs/2026-04-15-send-code-to-revit-design.md`

```markdown
# 10 — send_code_to_revit Escalation

**Scope:** Quando proporre uno script C# custom invece dei tool nativi.
**Sources:** CLAUDE.md §"send_code_to_revit", docs/SECURITY.md §"Sandbox"
**Last verified:** 2026-05-25

## Regola fondamentale

**MAI usare `send_code_to_revit` autonomamente per bulk/batch operations.** Sempre chiedere consenso esplicito all'utente prima.

## Frase standard di consenso

> "Posso usare `send_code_to_revit` per eseguire questa operazione in modo più efficiente con uno script C#, oppure preferisci che proceda con i tool standard (potrebbe richiedere più chiamate)?"

Solo dopo risposta affermativa, procedere con lo script.

## Decision rules

Motivi per cui chiedere e non assumere:

1. Gli script bypassano il safety layer nativo (dryRun, conferme).
2. DLL conflicts (archintelligence, BIM360, altri add-in) possono crashare silenziosamente `send_code_to_revit`.
3. L'utente può preferire tracciabilità via discrete tool calls.
4. **NON chiamare `Document.EditFamily` da `ExternalEvent`**: i dialog modali deadlockano Revit (riferimento: incident b292ace su Snowdon Towers).

## Sandbox

Namespace **vietati** dal sandbox (causano `CortexErrorCode.PermissionDenied`):
- `System.IO`
- `System.Net`
- `System.Diagnostics.Process`
- `Microsoft.Win32`
- `System.Reflection.Emit`
- `System.Runtime.InteropServices`

Validazione in `CodeSandbox.Validate(string code)` (`RevitCortex.Core`).

## Naming variabili

- Document: `document` (non `doc`, `Doc`, `uidoc`).
- UIDocument: `new UIDocument(document)`.
- ElementId: `.Value` su R2024+, `.IntegerValue` su R2023.

## Required checks

- [ ] Consenso utente ottenuto.
- [ ] Alternativa nativa proposta come opzione A.
- [ ] Nessuna chiamata `EditFamily` da `ExternalEvent`.
- [ ] Sandbox validation in `CodeSandbox.Validate` non bypassata.

## Avoid

- Non proporre autonomamente per >1 elemento senza consenso.
- Non usare namespace IO/Net/Process.
- Non chiamare `EditFamily` da `ExternalEvent`.
- Non assumere che l'utente preferisca script: è opzione B di default.
```

- [ ] **Step 3: Commit**

```bash
git add ai-skills/revitcortex/references/operator_09_Obsidian_Workflows.md ai-skills/revitcortex/references/operator_10_SendCodeToRevit_Escalation.md
git commit -m "feat(skill): operator_09 Obsidian + operator_10 send_code escalation"
```

---

## Task 9: developer_20 + developer_21 (new tool checklist + CortexResult)

**Files:**
- Modify: `ai-skills/revitcortex/references/developer_20_New_Tool_Checklist.md`
- Modify: `ai-skills/revitcortex/references/developer_21_CortexResult_And_Errors.md`

- [ ] **Step 1: Scrivere developer_20_New_Tool_Checklist.md**

Contenuto da estrarre da:
- `CLAUDE.md` §"ICortexTool"
- `src/RevitCortex.Core/Tools/ICortexTool.cs`
- Memoria utente `feedback_update_docs_after_tool_change.md`

```markdown
# 20 — New Tool Checklist

**Scope:** Aggiungere un nuovo `ICortexTool` al server RevitCortex.
**Sources:** CLAUDE.md §"ICortexTool", src/RevitCortex.Core/Tools/ICortexTool.cs
**Last verified:** 2026-05-25

## File da toccare

| File | Responsabilità |
|---|---|
| `src/RevitCortex.Tools/<Category>/<ToolName>Tool.cs` | Implementazione `ICortexTool` |
| `src/RevitCortex.Server/Tools/<Category>Tools.cs` | Definizione MCP (nome, descrizione, JsonSchema) |
| `tool-schemas.txt` | Firma compatta (rigenerare con `node server/generate-tool-schemas-csharp.mjs`) |
| `docs/USER_GUIDE.md` | Documentazione end-user |
| `WORKFLOWS.md` | Se il tool fa parte di un workflow nuovo o esistente |
| `CLAUDE.md` | Se introduce regole/anti-pattern specifici |
| `ai-skills/revitcortex/references/operator_*.md` | Reference operativo se cambia un workflow |

## Naming

- Nome MCP: `snake_case` (es. `get_element_parameters`)
- Classe C#: `PascalCase` + suffisso `Tool` (es. `GetElementParametersTool`)
- Categoria: PascalCase (es. "Elements", "Views", "Materials", "Ifc", "PowerBI")

## Interfaccia minima

```csharp
public class MyNewTool : ICortexTool
{
    public string Name => "my_new_tool";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // 1. Validare input
        // 2. Se distruttivo: session.RequestConfirmation("action", count)
        // 3. Eseguire dentro Transaction se modifica il doc
        // 4. Ritornare CortexResult<object>.Ok(...) o .Fail(...)
    }
}
```

## RequiresDocument

| Valore | Significato |
|---|---|
| `true` | Tool ha bisogno di un modello Revit aperto |
| `false` | Tool meta (es. `say_hello`, capability check) |

## IsDynamic

Se `true`, il tool è registrato solo se `DocumentCapabilities` lo abilita. Vedi `developer_23_Dynamic_Tools_And_Capabilities.md`.

## Required checks

- [ ] `ICortexTool` implementato correttamente.
- [ ] Naming convention rispettata.
- [ ] Schema MCP definito in `<Category>Tools.cs`.
- [ ] `tool-schemas.txt` rigenerato.
- [ ] `USER_GUIDE.md` aggiornato.
- [ ] Se distruttivo: `RequestConfirmation` chiamato.
- [ ] Build R25 + R24 verde (vedi `developer_22`).
- [ ] Test unitario in `RevitCortex.Tests/`.

## Avoid

- Non aggiungere un tool senza aggiornare `tool-schemas.txt`.
- Non aggiungere un tool senza test.
- Non dimenticare il `RequestConfirmation` per operazioni distruttive.
- Non usare `record` types (vedi `developer_22`).
```

- [ ] **Step 2: Scrivere developer_21_CortexResult_And_Errors.md**

Contenuto da estrarre da:
- `CLAUDE.md` §"CortexResult<T>"
- `src/RevitCortex.Core/Results/CortexResult.cs`

```markdown
# 21 — CortexResult & Error Codes

**Scope:** Envelope unificato per tutti i tool RevitCortex.
**Sources:** CLAUDE.md §"CortexResult<T>", src/RevitCortex.Core/Results/CortexResult.cs
**Last verified:** 2026-05-25

## Regola fondamentale

Ogni tool ritorna `CortexResult<T>`. **Mai** lanciare eccezioni o ritornare stringhe raw.

## Success

```csharp
return CortexResult<object>.Ok(new {
    greeting = "Hello",
    count = 42
});
```

## Failure

```csharp
return CortexResult<object>.Fail(
    CortexErrorCode.ElementNotFound,
    "Element 12345 does not exist in the active document",
    suggestion: "Check the element ID or ensure the correct document is open");
```

## Error codes

| Code | Numerico | Quando |
|---|---|---|
| `ElementNotFound` | 100 | ID non esiste, elemento eliminato |
| `PermissionDenied` | 200 | Read-only mode, sandbox negato |
| `TransactionFailed` | 300 | Transaction commit failed |
| `InvalidInput` | 400 | Parametri input malformati |
| `Timeout` | 500 | Operation > timeout limit |
| `Cancelled` | 600 | Utente ha annullato (TaskDialog) |
| `Unknown` | 900 | Eccezione non classificata |

## Propagazione errori dal Plugin al Server

`RevitBridge` (server) trasforma `CortexResult.Fail` in payload JSON strutturato senza lanciare eccezioni. Verificato live 2026-05-15.

Esempio payload:
```json
{
  "success": false,
  "error": {
    "code": "Cancelled",
    "message": "Operation cancelled by user",
    "suggestion": null
  }
}
```

## Required checks

- [ ] Tool ritorna `CortexResult<T>` sempre.
- [ ] Errori usano `CortexErrorCode` enum, mai stringhe libere.
- [ ] `suggestion` compilato quando utile per l'utente.
- [ ] Nessun `throw` non gestito da `Execute`.

## Avoid

- Non lanciare eccezioni che escono da `Execute`.
- Non ritornare stringhe raw o JObject sciolti.
- Non inventare error codes oltre quelli enum.
- Non lasciare `suggestion: null` se c'è una azione utile per l'utente.
```

- [ ] **Step 3: Commit**

```bash
git add ai-skills/revitcortex/references/developer_20_New_Tool_Checklist.md ai-skills/revitcortex/references/developer_21_CortexResult_And_Errors.md
git commit -m "feat(skill): developer_20 tool checklist + developer_21 CortexResult"
```

---

## Task 10: developer_22 + developer_23 (net48 compat + dynamic tools)

**Files:**
- Modify: `ai-skills/revitcortex/references/developer_22_Net48_Net8_Compatibility.md`
- Modify: `ai-skills/revitcortex/references/developer_23_Dynamic_Tools_And_Capabilities.md`

- [ ] **Step 1: Scrivere developer_22_Net48_Net8_Compatibility.md**

Contenuto da estrarre da:
- `CLAUDE.md` §"Cross-Target Compatibility"
- Memoria utente `feedback_revit_target_range.md`
- Global instructions §"Cross-Target Compatibility"

```markdown
# 22 — Net48 vs Net8+ Compatibility

**Scope:** Compilare RevitCortex per Revit 2023→2027 (target framework variabile).
**Sources:** CLAUDE.md §"Cross-Target Compatibility", memoria feedback_revit_target_range
**Last verified:** 2026-05-25

## Matrix target framework

| Revit | Framework |
|---|---|
| 2023 | net48 |
| 2024 | net48 |
| 2025 | net8.0-windows |
| 2026 | net8.0-windows |
| 2027 | net10.0-windows |

## Feature C# vietate su net48

| Feature | net8+ | net48 | Fix |
|---|---|---|---|
| `record` types | OK | **ERROR** CS0518 (`IsExternalInit` missing) | Usare `class` con readonly properties + constructor |
| `Dictionary.GetValueOrDefault()` | OK | **ERROR** CS1061 | `TryGetValue` con ternario |
| `init` accessors | OK | **ERROR** CS0518 | `{ get; }` + constructor |
| `Index`/`Range` (`^1`, `..`) | OK | **ERROR** | `.Length - 1`, `.Substring()` |
| `IAsyncEnumerable<T>` | OK | **ERROR** | Non disponibile su net48 |
| `file`-scoped types | OK | **ERROR** | Usare `internal` |
| Default interface methods | OK | **ERROR** | Spostare su abstract class o helper |

## Regola di verifica

Dopo OGNI modifica a un file C#:

```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

Una build R25 verde **non garantisce** che R24 compili.

Prima del release: tutti i target R23→R27 devono buildare:
```bash
for cfg in "R23" "R24" "R25" "R26" "R27"; do
  dotnet build -c "Debug $cfg" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
done
```

## R27 e .NET 10 SDK

R27 richiede SDK ≥ 10. `global.json` pinna SDK 8 con `rollForward: latestMajor`. Senza SDK 10 installato: `NETSDK1045`. Runtime end-user: serve .NET 10 runtime (Revit 2027 lo ship).

## Required checks

- [ ] Nessuna `record` type nei file C#.
- [ ] Nessun `GetValueOrDefault()` su `Dictionary`.
- [ ] Build R25 verde.
- [ ] Build R24 verde.
- [ ] Per release: anche R23, R26, R27 verdi.

## Avoid

- Non usare feature C# 9+ senza verificare net48.
- Non assumere che R25 verde = R24 verde.
- Non skippare la build R24 prima del commit.
```

- [ ] **Step 2: Scrivere developer_23_Dynamic_Tools_And_Capabilities.md**

Contenuto da estrarre da:
- `CLAUDE.md` §"IsDynamic Convention"
- `src/RevitCortex.Core/Discovery/DocumentCapabilities.cs`

```markdown
# 23 — Dynamic Tools & DocumentCapabilities

**Scope:** Tool che si abilitano solo quando il modello soddisfa prerequisiti.
**Sources:** CLAUDE.md §"IsDynamic Convention", src/RevitCortex.Core/Discovery/
**Last verified:** 2026-05-25

## Pattern

Un tool con `IsDynamic = true` è esposto al client MCP solo se `DocumentCapabilities` lo abilita.

## Flow

1. Plugin apre un documento.
2. `DocumentAnalyzer.Analyze(doc)` popola `DocumentCapabilities`:
   - Categorie presenti (es. presenza di `OST_Rooms`)
   - Worksets
   - Phases
   - Plugin esterni (revit-ifc, ecc.)
3. Per ogni tool con `IsDynamic = true`, l'analyzer chiama `capabilities.EnableTool("tool_name")` se i prerequisiti sono soddisfatti.
4. `CortexRouter` espone solo i tool dove `!IsDynamic || capabilities.IsToolEnabled(tool.Name)`.

## Quando usare IsDynamic

| Tool | IsDynamic? | Motivo |
|---|---|---|
| `get_element_parameters` | false | Funziona su qualsiasi modello |
| `ifc_link` | true | Richiede `revit-ifc` installato |
| `set_element_phase` | true | Solo se `doc.Phases.Size > 0` |
| `pbi_publish_elements` | true | Solo se PBI workspace configurato |

## Verifica capabilities

```csharp
if (session.Capabilities.IsToolEnabled("ifc_link"))
{
    // OK to proceed
}
```

`DocumentAnalyzer` deve aggiornare le capabilities anche quando il documento cambia (apertura nuovo modello, plugin esterni caricati a runtime).

## Required checks

- [ ] `IsDynamic` impostato correttamente per i tool con prerequisiti.
- [ ] `DocumentAnalyzer` aggiornato per chiamare `EnableTool` quando appropriato.
- [ ] `CortexRouter` filtra dinamicamente (verificato in `CortexRouter.cs`).

## Avoid

- Non impostare `IsDynamic = true` per tool sempre disponibili (overhead inutile).
- Non dimenticare di aggiornare `DocumentAnalyzer` quando aggiungi un tool dinamico.
- Non fare check di capability dentro `Execute` (è già filtrato a monte).
```

- [ ] **Step 3: Commit**

```bash
git add ai-skills/revitcortex/references/developer_22_Net48_Net8_Compatibility.md ai-skills/revitcortex/references/developer_23_Dynamic_Tools_And_Capabilities.md
git commit -m "feat(skill): developer_22 net48 compat + developer_23 dynamic tools"
```

---

## Task 11: developer_24 + developer_25 (security + build/release)

**Files:**
- Modify: `ai-skills/revitcortex/references/developer_24_ReadOnly_Audit_Security.md`
- Modify: `ai-skills/revitcortex/references/developer_25_Build_Test_Release_Checklist.md`

- [ ] **Step 1: Scrivere developer_24_ReadOnly_Audit_Security.md**

Contenuto da estrarre da:
- `docs/SECURITY.md`
- `CLAUDE.md` §"Security Requirements (NFR)"

```markdown
# 24 — Read-Only Mode, Audit Log, Sandbox

**Scope:** Sicurezza non-funzionale del server RevitCortex.
**Sources:** docs/SECURITY.md, CLAUDE.md §"Security Requirements (NFR)"
**Last verified:** 2026-05-25

## Read-only mode

Quando `readOnlyMode: true` in `~/.revitcortex/settings.json`, `CortexRouter` rifiuta tutti i tool write con `CortexErrorCode.PermissionDenied`.

### Naming convention

Sono **read-only** i tool che iniziano con:
- `get_`, `list_`, `find_`, `analyze_`, `check_`, `measure_`, `audit_`, `export_`
- `say_hello`
- `clash_detection`
- `lines_per_view_count`

Tutto il resto è considerato **write tool** e bloccato in read-only mode.

Implementazione in `CortexRouter.IsReadOnlyTool(string toolName)` (public static, testabile).

## Audit log

Ogni esecuzione tool è loggata in `~/.revitcortex/audit.jsonl` (append-only):

```json
{
  "ts": "2026-05-25T10:30:00Z",
  "tool": "tool_name",
  "input_summary": "...",
  "result": "ok|fail",
  "error_code": null,
  "elements_affected": 0
}
```

`AuditLogger` in `RevitCortex.Core`. `CortexRouter` chiama `AuditLogger.Log()` dopo ogni invocazione.

Audit v2 (Apr 2026) aggiunge `duration_ms` e altri campi async. Parser Python `rclog` aggiornato dopo i dati reali.

## Sandbox send_code_to_revit

Validazione in `CodeSandbox.Validate(string code)`. Namespace vietati:
- `System.IO`
- `System.Net`
- `System.Diagnostics.Process`
- `Microsoft.Win32`
- `System.Reflection.Emit`
- `System.Runtime.InteropServices`

Bypass solo disabilitando `send_code_to_revit` interamente nelle settings.

## Required checks

- [ ] Tool naming rispetta convenzione read-only.
- [ ] `IsReadOnlyTool` aggiornato per tool nuovi che non matchano i prefissi standard.
- [ ] `AuditLogger.Log()` chiamato per ogni esecuzione.
- [ ] Per `send_code_to_revit`: `CodeSandbox.Validate` chiamato prima dell'esecuzione.

## Avoid

- Non aggiungere tool write con prefisso `get_` (confonderebbe la convenzione).
- Non skippare `AuditLogger.Log()` per "performance".
- Non bypassare il sandbox.
```

- [ ] **Step 2: Scrivere developer_25_Build_Test_Release_Checklist.md**

Contenuto da estrarre da:
- `CLAUDE.md` §"Build Commands"
- Memoria utente `reference_release_flow.md`
- Memoria utente `feedback_deploy_all_revit_targets.md`
- Memoria utente `feedback_server_publish_mode.md`

```markdown
# 25 — Build, Test, Release Checklist

**Scope:** Pre-commit checks, build matrix, release flow.
**Sources:** CLAUDE.md §"Build Commands", memoria reference_release_flow, feedback_deploy_all_revit_targets
**Last verified:** 2026-05-25

## Build plugin (5 target)

```bash
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

**Regola**: Pre-commit basta R25 + R24. Pre-release tutti e 5.

## Build server MCP

```bash
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
```

## Test

```bash
dotnet test -c "Debug R25"
```

## Deploy

```powershell
powershell -ExecutionPolicy Bypass -File deploy.ps1
```

Default deploya solo R25. Per release multi-target, deploya tutti e 5 (vedi memoria `feedback_deploy_all_revit_targets`).

## Server publish

**Mai mischiare** framework-dependent e self-contained publish su `~/.revitcortex/server`. Fingerprint del problema: `"No frameworks were found"` in `mcp-server-revitcortex.log`.

## Release flow (GitHub Releases)

Repo `LuDattilo/RevitCortex` è pubblico. Repo asset release era separato (`revitcortex-releases`), ora il main è pubblico ma il flow resta:

```bash
./release.ps1 -Version "1.0.26"
gh release create v1.0.26 --repo LuDattilo/revitcortex-releases ./release/*.zip
```

(Verifica `release.ps1` per il flow esatto: in alcune versioni crea solo i pacchetti, in altre fa anche il `gh release create`.)

## Pre-commit checklist

- [ ] Build R25 verde.
- [ ] Build R24 verde.
- [ ] Tool aggiunti/modificati: schema rigenerato (`node server/generate-tool-schemas-csharp.mjs`).
- [ ] `USER_GUIDE.md` aggiornato se nuovi tool.
- [ ] Test unitari passano.

## Pre-release checklist

- [ ] Build R23, R24, R25, R26, R27 tutte verdi.
- [ ] `deploy.ps1` testato per ogni target.
- [ ] CHANGELOG.md aggiornato.
- [ ] Server publish mode coerente (no mix).
- [ ] `gh release create` su repo corretto.

## Avoid

- Non committare con solo build R25 verde.
- Non skippare la rigenerazione di `tool-schemas.txt`.
- Non mischiare publish mode sul server.
```

- [ ] **Step 3: Commit**

```bash
git add ai-skills/revitcortex/references/developer_24_ReadOnly_Audit_Security.md ai-skills/revitcortex/references/developer_25_Build_Test_Release_Checklist.md
git commit -m "feat(skill): developer_24 security + developer_25 build/release checklist"
```

---

## Task 12: index_40_Tool_Signature_Index.md

**Files:**
- Modify: `ai-skills/revitcortex/references/index_40_Tool_Signature_Index.md`

- [ ] **Step 1: Verificare contenuto tool-schemas.txt**

Run: `head -20 tool-schemas.txt && wc -l tool-schemas.txt`
Expected: ~157+ righe (una per tool)

- [ ] **Step 2: Scrivere index_40_Tool_Signature_Index.md**

Il file NON deve duplicare `tool-schemas.txt`: deve **referenziarlo** e fornire navigazione veloce.

```markdown
# 40 — Tool Signature Index

**Scope:** Lookup veloce delle firme dei 157 tool MCP di RevitCortex.
**Sources:** tool-schemas.txt (generato da server/generate-tool-schemas-csharp.mjs)
**Last verified:** 2026-05-25

## Come consultare

La fonte canonica delle firme è `tool-schemas.txt` nella root del repo. Una riga per tool, formato compatto.

Per cercare un tool specifico:
```bash
grep "^get_element_parameters" tool-schemas.txt
```

Per listare tutti i tool di una categoria:
```bash
grep -E "^(ifc_|pbi_|workflow_)" tool-schemas.txt
```

## Categorie

| Prefisso | Categoria | Esempi |
|---|---|---|
| `get_`, `list_`, `find_`, `analyze_`, `check_`, `export_`, `measure_`, `audit_` | Read-only | `get_project_info`, `analyze_model_statistics` |
| `set_`, `bulk_`, `sync_`, `create_`, `delete_`, `purge_`, `wipe_`, `rename_`, `modify_`, `override_`, `change_` | Write | `set_element_parameters`, `bulk_modify_parameter_values` |
| `ifc_*` | IFC integration | `ifc_link`, `ifc_rebuild_walls`, `ifc_export_basic` |
| `pbi_*` | PowerBI | `pbi_publish_elements`, `pbi_query` |
| `workflow_*` | Workflow composti | `workflow_model_audit`, `workflow_clash_review` |
| `cross_app_*` | NavisCortex bridge | `cross_app_selection` |
| `say_hello`, `get_*` | Meta | Diagnostica, capabilities |

## Aggiornamento

Dopo ogni modifica a un tool:
```bash
node server/generate-tool-schemas-csharp.mjs
git add tool-schemas.txt
git commit -m "chore: regenerate tool-schemas.txt"
```

Questo file (`index_40`) NON deve essere riscritto a ogni cambio: cita solo le categorie e le convenzioni. Il dettaglio sta in `tool-schemas.txt`.
```

- [ ] **Step 3: Commit**

```bash
git add ai-skills/revitcortex/references/index_40_Tool_Signature_Index.md
git commit -m "feat(skill): index_40 tool signature index"
```

---

## Task 13: agents/openai.yaml

**Files:**
- Modify: `ai-skills/revitcortex/agents/openai.yaml`

- [ ] **Step 1: Decidere se openai.yaml è necessario per l'MVP**

Il file `agents/openai.yaml` serve solo se la skill è installata in `$CODEX_HOME/skills/` come skill Codex CLI. Per Claude Code, basta `SKILL.md`. Verificare se il team usa Codex CLI: se no, mantenere il file come placeholder con commento.

Decision rule: scriviamo il file ma minimale, in modo che la skill sia portabile a Codex senza modifiche se servirà.

- [ ] **Step 2: Scrivere openai.yaml minimale**

```yaml
# Codex CLI agent metadata for the 'revitcortex' skill.
# Mirror of SKILL.md frontmatter. Used only when the skill is installed
# in $CODEX_HOME/skills/. Claude Code reads SKILL.md directly.

name: revitcortex
description: |
  Use when working with RevitCortex operations, MCP tool workflows,
  Revit model automation, or RevitCortex C# development. Routes Codex
  to focused references for model operations, safe write workflows,
  send_code_to_revit escalation, IFC, PowerBI, Obsidian, tool development,
  net48/net8 compatibility, audit/read-only security, and build/test checks.

references_dir: references
entrypoint: SKILL.md
```

- [ ] **Step 3: Commit**

```bash
git add ai-skills/revitcortex/agents/openai.yaml
git commit -m "feat(skill): Codex CLI agent metadata"
```

---

## Task 13.5: Packaging — includere la skill nello ZIP di release

**Files:**
- Modify: `build-release.ps1`

La spec §2.1/§2.2/§5 e §13 Fase 2 richiede che la skill entri nel pacchetto di release. Il flusso di copia in `build-release.ps1` (righe 77-98) copia file dalla cartella `distribution/` verso `release/`. Aggiungiamo la copia di `ai-skills/revitcortex/` mantenendo la struttura.

- [ ] **Step 1: Leggere build-release.ps1 e identificare il punto di inserimento**

Run: `grep -n "Copy-Item.*distribution" build-release.ps1`
Expected: ~4-5 righe Copy-Item.

Il punto di inserimento è dopo la copia di `RevitCortex.addin` (riga ~88) e prima di `Compress-Archive`.

- [ ] **Step 2: Aggiungere copia della skill**

Inserire dopo la riga `Copy-Item (Join-Path $RepoRoot "src\RevitCortex.Plugin\RevitCortex.addin") $ReleaseDir`:

```powershell
# AI Skill knowledge base — copy ai-skills/ into release/ai-skills/
$skillSource = Join-Path $RepoRoot "ai-skills"
$skillTarget = Join-Path $ReleaseDir "ai-skills"
if (Test-Path $skillSource) {
    if (-not (Test-Path $skillTarget)) { New-Item -ItemType Directory -Path $skillTarget | Out-Null }
    Copy-Item "$skillSource\*" $skillTarget -Recurse -Force
    Write-Ok "Copied ai-skills/ into release"
} else {
    Write-Warn "ai-skills/ not found — skipping skill packaging"
}
```

- [ ] **Step 3: Verificare che lo ZIP includa la skill (dry run)**

Run: `powershell -ExecutionPolicy Bypass -File build-release.ps1`
Expected: `release/ai-skills/revitcortex/SKILL.md` esiste.
Run: `ls release/ai-skills/revitcortex/references/ | wc -l`
Expected: `18` file (1 master index + 10 operator + 6 developer + 2 index − errore: dovrebbe essere `18 = 1 + 10 + 6 + 2 − 1` perché `00_Master_Index` è 1, sommando 19 totali. Verificare con `find ai-skills/revitcortex/ -type f | wc -l` che restituisce **20**).

- [ ] **Step 4: Commit**

```bash
git add build-release.ps1
git commit -m "build(release): include ai-skills/ in release ZIP"
```

---

## Task 13.6: Installer — copiare la skill in target Codex e Claude Code

**Files:**
- Modify: `distribution/install.ps1`
- Modify: `check-install.ps1`

L'installer end-user deve mettere la skill in posti che Codex CLI e Claude Code riconoscono. I path standard:

| Client | Path skill |
|---|---|
| Claude Code (personale) | `%USERPROFILE%\.claude\skills\revitcortex\` |
| Codex CLI | `%USERPROFILE%\.codex\skills\revitcortex\` (se installato) |

- [ ] **Step 1: Leggere distribution/install.ps1 per capire il flusso**

Run: `grep -n "Copy-Item\|target\|Target\|destination" distribution/install.ps1 | head -20`

Identificare la sezione che gestisce le copie post-install.

- [ ] **Step 2: Aggiungere blocco install skill**

Dopo l'ultimo `Copy-Item` di install.ps1 (prima della sezione di completion / chiusura), inserire:

```powershell
# AI Skill — install RevitCortex skill to user-level paths
$skillSrc = Join-Path $PSScriptRoot "ai-skills\revitcortex"
if (Test-Path $skillSrc) {
    $skillTargets = @(
        (Join-Path $env:USERPROFILE ".claude\skills\revitcortex"),
        (Join-Path $env:USERPROFILE ".codex\skills\revitcortex")
    )
    foreach ($target in $skillTargets) {
        $parent = Split-Path $target -Parent
        if (Test-Path $parent) {
            if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target | Out-Null }
            Copy-Item "$skillSrc\*" $target -Recurse -Force
            Write-Host "  Installed skill -> $target"
        } else {
            Write-Host "  Skipped skill install (parent missing): $parent"
        }
    }
} else {
    Write-Host "  ai-skills not bundled — skipping skill install"
}
```

Note:
- Installa **solo** se `~/.claude/skills` o `~/.codex/skills` esistono già (rispetta presenza del client).
- Non crea silenziosamente `~/.claude/` o `~/.codex/` se l'utente non li ha (evita pollution).

- [ ] **Step 3: Aggiornare check-install.ps1 per validare la presenza della skill**

Cerca la sezione finale del check (dove riassume lo stato installazione). Inserire prima della chiusura:

```powershell
# Skill installation check
$skillPaths = @(
    @{ Name = "Claude Code"; Path = (Join-Path $env:USERPROFILE ".claude\skills\revitcortex\SKILL.md") },
    @{ Name = "Codex CLI";   Path = (Join-Path $env:USERPROFILE ".codex\skills\revitcortex\SKILL.md") }
)
$anyFound = $false
foreach ($entry in $skillPaths) {
    if (Test-Path $entry.Path) {
        Write-Host "  OK skill installed for $($entry.Name): $($entry.Path)" -ForegroundColor Green
        $anyFound = $true
    }
}
if (-not $anyFound) {
    Write-Host "  ! AI skill not installed in any client (.claude/.codex). Reinstall or copy ai-skills/revitcortex/ manually." -ForegroundColor Yellow
}
```

- [ ] **Step 4: Verifica modifiche**

Run: `git diff distribution/install.ps1 check-install.ps1`
Expected: solo i blocchi inseriti, nessun'altra modifica.

- [ ] **Step 5: Commit**

```bash
git add distribution/install.ps1 check-install.ps1
git commit -m "feat(installer): copy ai-skills/revitcortex into Claude/Codex user paths"
```

---

## Task 14: README dentro lo skill? No. Verifica zero file extra

**Files:**
- Verify only

La spec §5 dice esplicitamente: *"Non creare `README.md`, guide installazione o changelog dentro la skill"*. Verifichiamo che non siano stati aggiunti file extra per errore.

- [ ] **Step 1: Verificare struttura finale**

Run: `find ai-skills/revitcortex/ -type f | sort`
Expected output (esatto, 20 file):

```
ai-skills/revitcortex/SKILL.md
ai-skills/revitcortex/agents/openai.yaml
ai-skills/revitcortex/references/00_Master_Index.md
ai-skills/revitcortex/references/developer_20_New_Tool_Checklist.md
ai-skills/revitcortex/references/developer_21_CortexResult_And_Errors.md
ai-skills/revitcortex/references/developer_22_Net48_Net8_Compatibility.md
ai-skills/revitcortex/references/developer_23_Dynamic_Tools_And_Capabilities.md
ai-skills/revitcortex/references/developer_24_ReadOnly_Audit_Security.md
ai-skills/revitcortex/references/developer_25_Build_Test_Release_Checklist.md
ai-skills/revitcortex/references/index_40_Tool_Signature_Index.md
ai-skills/revitcortex/references/index_41_Workflow_Source_Map.md
ai-skills/revitcortex/references/operator_01_Session_Start_Locale.md
ai-skills/revitcortex/references/operator_02_Tool_Selection_Hierarchy.md
ai-skills/revitcortex/references/operator_03_Destructive_Operations_DryRun.md
ai-skills/revitcortex/references/operator_04_Parameter_Workflows.md
ai-skills/revitcortex/references/operator_05_Model_Health_Warnings_Clash.md
ai-skills/revitcortex/references/operator_06_View_And_Annotation_Workflows.md
ai-skills/revitcortex/references/operator_07_IFC_Workflows.md
ai-skills/revitcortex/references/operator_08_PowerBI_Workflows.md
ai-skills/revitcortex/references/operator_09_Obsidian_Workflows.md
ai-skills/revitcortex/references/operator_10_SendCodeToRevit_Escalation.md
```

- [ ] **Step 2: Verifica nessun TBD residuo**

Run: `grep -rn "TBD" ai-skills/revitcortex/ || echo "no TBD found"`
Expected: `no TBD found`

- [ ] **Step 3: Verifica nessun file extra**

Run: `find ai-skills/revitcortex/ -name "README.md" -o -name "CHANGELOG.md" -o -name "INSTALL.md"`
Expected: (vuoto)

- [ ] **Step 4: Se tutto OK, no commit (questo è solo verifica)**

---

## Task 15: Validazione MVP con prompt reali (smoke test)

**Files:**
- Create: `docs/superpowers/reports/2026-05-25-revitcortex-skill-smoke-test.md`

Questa task NON modifica `ai-skills/`. Valida che la skill funzioni come router.

- [ ] **Step 1: Aprire una sessione Codex / Claude Code nella cartella RevitCortex**

L'utente apre una sessione nuova con la skill installata o caricata.

- [ ] **Step 2: Eseguire i 4 prompt operativi di test (spec §11.1)**

1. "Controlla rapidamente lo stato del modello e dimmi se ci sono warning critici."
   - Atteso: skill carica `operator_01` + `operator_05`. Sequenza tool: `get_project_info` filtrato → `check_model_health` → `get_warnings maxWarnings: 10`.
2. "Trova muri con parametro WBS vuoto e prepara aggiornamento massivo."
   - Atteso: skill carica `operator_01` + `operator_02` + `operator_04`. Sequenza: discovery nome parametro → `export_elements_data` + `filter_by_parameter_value` → `bulk_modify_parameter_values` dryRun.
3. "Esegui clash rapido tra strutture e muri."
   - Atteso: skill carica `operator_01` + `operator_05`. Sequenza: `clash_detection` con `OST_StructuralFraming` + `OST_Walls`.
4. "Esporta dati schedule verso PowerBI e seleziona elementi filtrati."
   - Atteso: skill carica `operator_01` + `operator_08`. Sequenza: `pbi_check_auth` → `pbi_publish_schedules` → `pbi_query` → `select_from_powerbi`.

- [ ] **Step 3: Eseguire i 3 prompt sviluppo (spec §11.2)**

1. "Aggiungi un nuovo tool read-only che lista X."
   - Atteso: skill carica `developer_20` + `developer_21` + `developer_22` + `developer_24` (perché read-only).
2. "Aggiungi un tool write con conferma e audit."
   - Atteso: skill carica `developer_20` + `developer_21` + `developer_22` + `developer_24` + (forse `operator_03` per `RequestConfirmation`).
3. "Correggi un errore R24 causato da feature C# moderna."
   - Atteso: skill carica `developer_22` + `developer_25` (per build).

- [ ] **Step 4: Compilare il report smoke test**

Salvare osservazioni in `docs/superpowers/reports/2026-05-25-revitcortex-skill-smoke-test.md` con questa struttura:

```markdown
# RevitCortex Skill — Smoke Test Report

**Date:** 2026-05-25
**Tester:** Luigi Dattilo
**Skill version:** MVP v1 (branch feat/session-acceleration)

## Prompt 1: "Controlla rapidamente lo stato del modello..."
- Reference caricati: [...]
- Tool MCP eseguiti: [...]
- Numero iterazioni: [...]
- Errori evitati / introdotti: [...]
- Note: [...]

[ripetere per prompt 2-7]

## Metriche aggregate
| Metrica | Pre-skill (memoria) | Post-skill |
|---|---|---|
| Iterazioni medie | ? | ? |
| Tool MCP medi | ? | ? |
| Riletture AGENTS.md complete | ? | ? |

## Gap rilevati
- [...]

## Azioni follow-up
- [...]
```

- [ ] **Step 5: Commit report**

```bash
git add docs/superpowers/reports/2026-05-25-revitcortex-skill-smoke-test.md
git commit -m "test(skill): smoke test report for ai-skills/revitcortex MVP"
```

---

## Task 16: Aggiornare CLAUDE.md con puntatore alla skill

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Aggiungere sezione iniziale che punta alla skill**

Inserire dopo la prima riga (`# RevitCortex -- AI Assistant Guide`) e prima di `Flussi operativi collaudati`:

```markdown
## AI Skill Router

Per task operativi BIM o di sviluppo C#, la knowledge base è organizzata in `ai-skills/revitcortex/`.
Il router `ai-skills/revitcortex/SKILL.md` indica quali reference caricare per ogni tipo di richiesta.

Quando questo CLAUDE.md cresce, le regole specifiche dovrebbero migrare nei reference della skill:
- Workflow BIM → `ai-skills/revitcortex/references/operator_*.md`
- Pattern sviluppo C# → `ai-skills/revitcortex/references/developer_*.md`
- Mappa fonti → `ai-skills/revitcortex/references/index_41_Workflow_Source_Map.md`

Questo CLAUDE.md resta la fonte canonica per regole globali e build/release matrix.
```

- [ ] **Step 2: Verificare diff**

Run: `git diff CLAUDE.md`
Expected: inserimento ~13 righe dopo la prima riga, niente altro modificato.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add pointer from CLAUDE.md to ai-skills/revitcortex skill"
```

---

## Task 17: PR finale verso main

**Files:**
- No file modifications

- [ ] **Step 1: Verifica stato branch**

```bash
git status
git log --oneline main..feat/session-acceleration
```
Expected: ~16 commit puliti sul branch, nessuna modifica unstaged (eccetto `.claude/settings.local.json` che non va in PR).

- [ ] **Step 2: Push branch**

```bash
git push -u origin feat/session-acceleration
```

- [ ] **Step 3: Creare PR**

```bash
gh pr create --title "feat: RevitCortex AI Skill + Knowledge Router (MVP)" --body "$(cat <<'EOF'
## Summary
- Aggiunge `ai-skills/revitcortex/` con `SKILL.md` router e 17 reference Markdown
- Separa workflow operativi BIM (`operator_*`) da pattern sviluppo C# (`developer_*`)
- Zero modifiche al codice C# (server, plugin, tools)
- Source map (`index_41`) evita drift tra reference e fonti canoniche
- Packaging: `build-release.ps1` include la skill nello ZIP di release
- Installer: `distribution/install.ps1` copia la skill in `~/.claude/skills/` e `~/.codex/skills/` quando i client sono presenti

## Riferimenti
- Spec: `docs/superpowers/specs/2026-05-25-revitcortex-ai-skill-knowledge-router-design.md`
- Plan: `docs/superpowers/plans/2026-05-25-revitcortex-ai-skill-knowledge-router.md`
- Smoke test: `docs/superpowers/reports/2026-05-25-revitcortex-skill-smoke-test.md`

## Test plan
- [x] Tutti i 17 reference popolati senza `TBD`
- [x] `SKILL.md` ≤ 80 righe
- [x] Struttura finale 20 file (verificata con `find ai-skills/revitcortex/`)
- [x] Nessun README/CHANGELOG dentro la skill
- [x] `build-release.ps1` produce ZIP con `ai-skills/revitcortex/` incluso
- [x] `distribution/install.ps1` copia in `~/.claude/skills/` se presente
- [x] `check-install.ps1` riporta lo stato installazione skill
- [x] Smoke test 4 prompt operativi + 3 prompt sviluppo
- [ ] Review utente sul branch prima del merge

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 4: Restituire URL della PR all'utente**

---

## Self-review

**Spec coverage check:**
- §2.1/§2.2 Distribuibilità → Task 13.5 (build-release) + Task 13.6 (installer) ✓
- §4 Architettura → Task 1 (scaffold) + Task 2 (SKILL.md) ✓
- §5 Struttura → Task 1 ✓
- §6 Skill router → Task 2 ✓
- §6.1 Classificazione → Task 2 (tabella in SKILL.md) ✓
- §6.2 Regole globali → Task 2 (sezione "Always-on rules") ✓
- §7.1 Operator templates 01-10 → Task 4, 5, 6, 7, 8 ✓
- §7.2 Developer templates 20-25 → Task 9, 10, 11 ✓
- §7.3 Index templates → Task 3 (41) + Task 12 (40) ✓
- §8 Data flow → coperto implicitamente da SKILL.md e tabelle ✓
- §9 Gestione aggiornamenti → Task 3 (sezione "Aggiornamento" in `index_41`) ✓
- §10 Sicurezza → Task 8 (operator_10) + Task 11 (developer_24) ✓
- §11 Testing/validazione MVP → Task 15 (smoke test) ✓
- §12 Piano MVP Fase 1 → Task 1-12 ✓
- §13 Piano MVP Fase 2 (packaging/installer) → Task 13.5 + 13.6 ✓
- §13 Piano MVP Fase 3 (trial) → Task 15 (smoke test) ✓
- §14 Criteri accettazione → Task 14 (verifica struttura) + Task 15 (smoke test) ✓
- §15 Rischi → MVP da 17 file (non 77), `Last verified` su ogni reference ✓
- §16 Decisione raccomandata → repo-local in `ai-skills/`, no modifiche codice C# ✓

**Placeholder scan:** Nessun TBD nel plan (i TBD dentro Task 1 sono **intenzionali** come marker per Task 4+, e rimossi entro Task 14).

**Type consistency:** I nomi file usati in SKILL.md (`operator_01_Session_Start_Locale.md`, ecc.) corrispondono esattamente ai file creati in Task 1 e popolati nelle task successive. Coerenza verificata.

**Decision points aperti:**
- Task 13: `agents/openai.yaml` minimale per portabilità futura, scelta documentata nel file stesso.
- Task 12: `index_40_Tool_Signature_Index.md` rimanda a `tool-schemas.txt` invece di duplicarlo (riduce manutenzione).
