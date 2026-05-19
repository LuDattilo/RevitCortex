# RevitCortex + Obsidian Knowledge Workflow - Specifica tecnico-funzionale

**Date:** 2026-05-19
**Status:** Design draft, not implemented
**Author:** RevitCortex AI session with Luigi Dattilo
**Review target:** Brainstorming and feasibility review before writing code

---

## 1. Sintesi

Questa specifica descrive una possibile integrazione tra RevitCortex e Obsidian senza usare Onexus.

L'obiettivo e' trasformare Obsidian in una memoria tecnica persistente per i progetti BIM, mantenendo RevitCortex come layer operativo verso Revit. Il sistema deve supportare due direzioni:

1. **Revit -> Obsidian:** pubblicazione controllata di snapshot, audit, note su elementi, warning, selezioni, abachi e decisioni.
2. **Obsidian -> Revit:** write-back controllato, inizialmente limitato a modifiche parametriche esplicite e validate con dry-run.

Il primo MVP non deve introdurre sincronizzazione real-time ne' authoring geometrico. Obsidian deve essere trattato come sorgente di intenzioni e documentazione, non come database autoritativo che modifica Revit automaticamente.

---

## 2. Obiettivi

### 2.1 Obiettivi funzionali

- Creare una struttura Obsidian standard per documentare un modello Revit.
- Esportare da RevitCortex note Markdown leggibili, con frontmatter stabile e link interni.
- Salvare snapshot tecnici del modello: stato progetto, categorie, livelli, fasi, warnings, health check, selezioni, issue e parametri rilevanti.
- Permettere all'agente di leggere note operative da Obsidian e proporre modifiche verso Revit.
- Consentire write-back sicuro per parametri, stato issue e classificazioni BIM.
- Tracciare ogni operazione con stato `draft`, `ready`, `validated`, `applied`, `failed` o `cancelled`.
- Evitare dipendenze da Onexus.

### 2.2 Obiettivi non funzionali

- Sicurezza: nessuna modifica a Revit senza dry-run e conferma.
- Auditabilita': ogni modifica deve lasciare traccia in Obsidian e nel log RevitCortex.
- Reversibilita': le operazioni devono essere ricostruibili da una nota o da un export.
- Compatibilita': il disegno deve rispettare net48/net8+ e i pattern esistenti di RevitCortex.
- Token efficiency: le note devono privilegiare sintesi, aggregati e riferimenti stabili invece di dump completi.
- Estendibilita': il layer Obsidian deve essere un adapter sostituibile, per non dipendere da un singolo MCP server Obsidian.

---

## 3. Non obiettivi

La prima versione non deve coprire:

- Sincronizzazione real-time automatica a ogni salvataggio di nota.
- Creazione/modifica geometrica da Obsidian: muri, pavimenti, famiglie, spostamenti, mirror, array.
- Cancellazioni, purge, rename massivi o operazioni distruttive guidate direttamente da note.
- Replica completa di tutti gli elementi Revit nel vault.
- Knowledge graph proprietario stile Onexus.
- Nuova UI complessa nel plugin Revit.
- Dipendenza obbligatoria da un servizio cloud.

---

## 4. Architettura proposta

```
MCP Client / AI Agent
    |
    | MCP stdio
    v
RevitCortex.Server
    |
    | TCP JSON-RPC localhost
    v
RevitCortex.Plugin
    |
    | Revit API
    v
Autodesk Revit

MCP Client / AI Agent
    |
    | MCP Obsidian adapter OR filesystem adapter
    v
Obsidian Vault
    |
    | Optional index
    v
Local knowledge graph / semantic search
```

### 4.1 Responsabilita'

**RevitCortex**
- Legge dati Revit.
- Esegue dry-run e modifiche reali.
- Applica le protezioni esistenti: `CortexResult`, errori tipizzati, `RequestConfirmation`, read-only mode, audit log.
- Produce payload compatti e stabili per l'agente.

**Obsidian**
- Conserva note Markdown, decisioni, issue, snapshot, comandi proposti e risultati.
- Permette editing umano.
- Espone contenuti all'agente tramite MCP Obsidian o filesystem controllato.

**AI Agent**
- Orchestrazione.
- Traduce snapshot Revit in note Markdown.
- Traduce note operative Obsidian in chiamate RevitCortex.
- Non deve bypassare RevitCortex con script custom salvo consenso esplicito dell'utente.

**Optional knowledge graph**
- Indicizza il vault.
- Permette ricerca semantica o grafo tra note.
- Non e' sorgente autoritativa per modifiche Revit.

---

## 5. Strategia di integrazione Obsidian

### 5.1 Adapter, non dipendenza rigida

Esistono piu' MCP server Obsidian con capacita' diverse: alcuni leggono/scrivono direttamente file Markdown, altri usano il plugin Obsidian Local REST API, altri girano come plugin dentro Obsidian.

La specifica deve quindi definire un'interfaccia logica minima:

| Capability | Descrizione |
|------------|-------------|
| `list_notes` | Elenca note o cartelle in un sotto-path del vault |
| `read_note` | Legge una nota Markdown |
| `write_note` | Crea o sovrascrive una nota |
| `append_note` | Aggiunge contenuto a una nota esistente |
| `patch_note` | Aggiorna frontmatter, sezione o blocco specifico |
| `search_notes` | Cerca per testo, tag o frontmatter |
| `move_note` | Opzionale, per riorganizzazione |

In implementazione, queste capability possono essere mappate su:

- MCPVault (`@bitbonsai/mcpvault`), che espone file operations, frontmatter, search e write modes.
- `cyanheads/obsidian-mcp-server`, che usa Obsidian Local REST API e supporta patch, append, replace, frontmatter e allowlist.
- `MarkusPfundstein/mcp-obsidian`, che espone list/search/get/patch/append/delete via Local REST API.
- Un adapter filesystem locale, solo se il client MCP non espone Obsidian.

### 5.2 Vincolo importante

Il codice RevitCortex non dovrebbe dipendere direttamente da Obsidian. La prima integrazione puo' vivere come workflow agente:

1. RevitCortex produce dati.
2. L'agente genera/aggiorna note Obsidian.
3. L'agente legge note operative.
4. RevitCortex valida ed esegue.

Solo in una fase successiva si puo' valutare un tool RevitCortex dedicato per generare pacchetti Markdown su disco.

---

## 6. Struttura vault proposta

```
RevitCortex/
  Projects/
    <ProjectSlug>/
      Project.md
      Model-Status/
        2026-05-19-health-check.md
        2026-05-19-warnings.md
      Elements/
        <ElementId>-<ShortName>.md
      Selections/
        <SelectionName>.md
      Schedules/
        <ScheduleName>.md
      Issues/
        RCX-0001.md
      Commands/
        2026-05-19-parameter-update.md
      Runs/
        <syncRunId>.md
      Decisions/
        2026-05-19-obsidian-writeback-policy.md
```

### 6.1 Note consigliate

**Project.md**
- Identita' progetto.
- Doc key RevitCortex.
- Lingua Revit rilevata.
- Ultimo snapshot.
- Link a warning, issue, selezioni, schedule, decisioni.

**Model-Status/**
- Health check.
- Warning compatti.
- Statistiche modello.
- Esito clash review.

**Elements/**
- Solo elementi rilevanti, non tutti gli elementi.
- Da usare per issue, elementi selezionati, clash, componenti critici, WBS mancanti.

**Issues/**
- Una nota per problema BIM tracciato.
- Deve collegare elementi, categoria, vista, stato, decisione e prossima azione.

**Commands/**
- Note operative che possono generare write-back verso Revit.
- Devono avere schema esplicito e stato.

**Runs/**
- Log sintetico di ogni operazione agentica: input, dry-run, esecuzione, output, errori.

---

## 7. Frontmatter standard

### 7.1 Frontmatter comune

```yaml
---
rcx:
  kind: model-status | element | issue | command | run | decision | schedule | selection
  projectSlug: "snowdon-towers"
  docKey: "projuid:..."
  revitVersion: "2026"
  locale: "en"
  createdAt: "2026-05-19T17:00:00+02:00"
  updatedAt: "2026-05-19T17:00:00+02:00"
  syncRunId: "20260519-170000-a1b2"
---
```

### 7.2 Nota elemento

```yaml
---
rcx:
  kind: element
  projectSlug: "snowdon-towers"
  docKey: "projuid:..."
  elementId: 123456
  uniqueId: "..."
  ostCode: "OST_Walls"
  categoryDisplayName: "Walls"
  family: "Basic Wall"
  type: "Generic - 200mm"
  level: "Level 1"
  phase: "New Construction"
  lastSeenAt: "2026-05-19T17:00:00+02:00"
  stale: false
---
```

### 7.3 Nota comando

```yaml
---
rcx:
  kind: command
  commandType: parameter-update
  status: draft
  projectSlug: "snowdon-towers"
  docKey: "projuid:..."
  targetMode: explicit-elements
  createdBy: human
  dryRunRequired: true
  confirmationRequired: true
  lastDryRunAt:
  appliedAt:
---
```

---

## 8. Write-back: policy funzionale

### 8.1 Principio

Obsidian non modifica Revit direttamente. Obsidian contiene proposte, tabelle e intenzioni. L'agente legge la nota, valida i dati, invoca RevitCortex in modalita' preview quando il tool la supporta, aggiorna la nota con il risultato, poi chiede conferma prima di eseguire.

Nel MVP, "dry-run" significa:

- dry-run nativo quando il tool lo supporta, per esempio `sync_csv_parameters(dryRun: true)` o `bulk_modify_parameter_values(dryRun: true)`;
- validazione agente + preview esplicita quando il tool non espone `dryRun`, per esempio controllando documento, elementi, parametri e valori prima di chiamare il tool write.

### 8.2 Livelli di write-back

| Livello | Stato | Descrizione | Tool RevitCortex probabili |
|---------|-------|-------------|-----------------------------|
| L1 | MVP | Parametri su elementi espliciti | `sync_csv_parameters`, poi `set_element_parameters` solo per casi piccoli |
| L2 | Next | Parametri bulk per categoria/filtro | `bulk_modify_parameter_values`, `filter_by_parameter_value`, `export_elements_data` |
| L3 | Next | Fasi, workset, selezioni, viste, schedule | `set_element_phase`, `set_element_workset`, `save_selection`, `create_schedule` |
| L4 | Future | Grafica e review visuale | `override_graphics`, `color_elements`, `section_box_from_selection` |
| L5 | Blocked in MVP | Geometria, cancellazioni, purge, rename massivi | Da bloccare nel primo MVP |

### 8.3 Comandi ammessi nel MVP

Il primo MVP deve accettare solo:

- `parameter-update`
- `parameter-clear` solo se esplicitamente abilitato
- `issue-status-sync` verso parametri dedicati, se il modello li ha

Ogni comando deve indirizzare elementi con:

- `ElementId`, per operativita' corrente.
- `UniqueId`, per controllo anti-stale quando disponibile.
- `docKey`, per evitare applicazione sul modello sbagliato.

---

## 9. Formato comando Obsidian per MVP

### 9.1 Comando parametri con tabella Markdown

```markdown
---
rcx:
  kind: command
  commandType: parameter-update
  status: draft
  docKey: "projuid:..."
  targetMode: explicit-elements
  dryRunRequired: true
  confirmationRequired: true
---

# Aggiornamento parametri WBS

## Proposed Changes

| ElementId | UniqueId | Parameter | NewValue | Notes |
|---:|---|---|---|---|
| 123456 | abc... | WBS_Code | A.01.20 | validato da Luigi |
| 123457 | def... | Comments | Verificare stratigrafia | issue RCX-0004 |

## Dry Run Result

_Non ancora eseguito._

## Execution Result

_Non ancora applicato._
```

### 9.2 Stato della nota

| Status | Significato |
|--------|-------------|
| `draft` | Nota editabile, non pronta |
| `ready` | L'utente dichiara che il comando puo' essere validato |
| `validated` | Dry-run eseguito con successo |
| `needs-review` | Dry-run con warning o righe ambigue |
| `applied` | Modifica applicata in Revit |
| `failed` | Errore tecnico o validazione fallita |
| `cancelled` | Operazione annullata dall'utente |

### 9.3 Regola di esecuzione

L'agente puo' eseguire write-back solo se:

1. `rcx.kind == command`
2. `rcx.status == ready` oppure l'utente lo chiede esplicitamente nella chat
3. `rcx.commandType` e' in allowlist
4. `docKey` corrisponde al documento attivo
5. dry-run nativo o preview di validazione completata con esito accettabile
6. l'utente conferma l'esecuzione reale
7. il tool RevitCortex conferma via TaskDialog se l'operazione e' distruttiva o modificante

---

## 10. Flusso Revit -> Obsidian

### 10.1 Publish model status

**Obiettivo:** creare o aggiornare uno snapshot sintetico del modello.

Sequenza agente:

1. `get_project_info` completo solo se e' la prima call della sessione.
2. `check_model_health`.
3. `get_warnings(maxWarnings: 10)`.
4. Opzionale: `analyze_model_statistics(compact: true)`.
5. Genera note:
   - `Project.md`
   - `Model-Status/<date>-health-check.md`
   - `Model-Status/<date>-warnings.md`
   - `Runs/<syncRunId>.md`

### 10.2 Publish selected elements

**Obiettivo:** creare note solo per elementi selezionati o critici.

Sequenza agente:

1. `get_selected_elements`.
2. `get_element_parameters` sugli ID selezionati, con `includeTypeParameters: true`.
3. Genera/aggiorna `Elements/<ElementId>-<ShortName>.md`.
4. Genera `Selections/<name>.md` con link agli elementi.

### 10.3 Publish issues

**Obiettivo:** trasformare warning, clash o controlli BIM in issue Obsidian.

Sequenza agente:

1. Tool RevitCortex mirato: `get_warnings`, `clash_detection`, `find_untagged_elements`, `find_undimensioned_elements`, ecc.
2. Deduplicazione per chiave stabile.
3. Creazione note `Issues/RCX-####.md`.
4. Link a elementi, viste, categorie e run.

---

## 11. Flusso Obsidian -> Revit

### 11.1 Validazione comando

Sequenza agente:

1. Cerca note in `Commands/` con `status: ready`.
2. Legge nota completa.
3. Valida frontmatter e tabella.
4. Verifica documento attivo con `get_project_info` filtrato o contesto gia' noto.
5. Risolve elementi:
   - se disponibili `UniqueId`: usare `get_elements_by_unique_id` per verificare identita';
   - altrimenti usare `ElementId` con warning.
6. Esegue dry-run nativo con il tool piu' adatto. Per il MVP tabellare, preferire `sync_csv_parameters(dryRun: true)`.
7. Aggiorna la sezione `Dry Run Result`.
8. Se tutto e' coerente, mette `status: validated`.

### 11.2 Esecuzione comando

Sequenza agente:

1. L'utente conferma in chat.
2. Il tool RevitCortex mostra eventuale TaskDialog.
3. Se l'utente conferma, RevitCortex applica la modifica.
4. L'agente fa spot check con `get_element_parameters` su 1-2 elementi.
5. Aggiorna `Execution Result`.
6. Imposta `status: applied` o `failed`.
7. Scrive una nota `Runs/<syncRunId>.md`.

### 11.3 Regola per `send_code_to_revit`

`send_code_to_revit` non deve essere usato autonomamente in questo flusso.

Se un comando Obsidian richiede logica complessa o oltre 100 elementi, l'agente deve proporre due strade:

- tool standard, piu' tracciabile ma con piu' passaggi;
- script C# via `send_code_to_revit`, piu' efficiente ma da autorizzare esplicitamente.

Senza consenso esplicito, usare solo tool standard.

---

## 12. Validazioni e conflitti

### 12.1 Validazioni obbligatorie

- Documento attivo corretto (`docKey`).
- Lingua Revit rilevata prima di usare nomi display o parametri localizzati.
- Elementi esistenti.
- Parametri esistenti e scrivibili.
- Tipo parametro compatibile con il valore.
- Righe duplicate nella tabella.
- Valori vuoti intenzionali vs errori di compilazione.
- Stato nota coerente.

### 12.2 Conflitti possibili

| Conflitto | Comportamento |
|-----------|---------------|
| ElementId non trovato | Riga skipped, `needs-review` |
| UniqueId diverso da ElementId | Bloccare riga, `needs-review` |
| Parametro non trovato | Bloccare riga, suggerire discovery con `get_element_parameters` |
| Parametro read-only | Skipped, mostrare motivo |
| Documento sbagliato | Bloccare intero comando |
| Nota gia' `applied` | Non rieseguire salvo override esplicito |
| Dry-run vecchio | Richiedere nuovo dry-run se modello cambiato |

---

## 13. Sicurezza

### 13.1 Protezioni richieste

- Allowlist dei `commandType`.
- Nessuna esecuzione automatica su salvataggio nota.
- Dry-run obbligatorio.
- Conferma utente obbligatoria per esecuzione reale.
- Rispetto di `readOnlyMode`.
- Rispetto dei confirmation dialog RevitCortex.
- Audit log RevitCortex invariato.
- Log Obsidian in `Runs/`.
- Nessun accesso a path fuori vault.
- Nessuna scrittura in `.obsidian/`.

### 13.2 Cartelle consigliate per write access

Se l'MCP Obsidian supporta allowlist, concedere scrittura solo a:

```
RevitCortex/Projects/
RevitCortex/Inbox/
```

E lettura a:

```
RevitCortex/
```

### 13.3 Comandi Obsidian pericolosi

Se il server Obsidian espone un tool generico per eseguire command-palette commands, tenerlo disabilitato. Alcuni comandi Obsidian possono cancellare, muovere o alterare file in modo opaco.

---

## 14. Error handling

Ogni operazione deve produrre output umano e machine-readable.

### 14.1 Preview / dry-run result consigliato

```markdown
## Dry Run Result

Status: needs-review
Run: [[Runs/20260519-170000-a1b2]]

| Metric | Count |
|---|---:|
| Planned rows | 12 |
| Valid rows | 10 |
| Skipped rows | 2 |
| Warnings | 1 |

### Skipped rows

| Row | ElementId | Reason |
|---:|---:|---|
| 4 | 123999 | Element not found |
| 8 | 124000 | Parameter is read-only |
```

### 14.2 Execution result consigliato

```markdown
## Execution Result

Status: applied
AppliedAt: 2026-05-19T17:08:22+02:00
Run: [[Runs/20260519-170822-b9c1]]

Modified: 10
Skipped: 2
Spot check: passed
```

---

## 15. Test plan

### 15.1 Test senza Revit

- Parser frontmatter e tabella Markdown.
- Validazione stato nota.
- Conversione tabella Markdown -> DTO comando.
- Generazione note da payload fake RevitCortex.
- Patch sicura della sezione `Dry Run Result`.

### 15.2 Test con RevitCortex fake/router

- Simulare `get_project_info`.
- Simulare `get_elements_by_unique_id`.
- Simulare dry-run di `set_element_parameters`.
- Verificare update nota `validated`, `needs-review`, `failed`.

### 15.3 Test live su Revit

1. Selezionare 2-3 elementi.
2. Pubblicare note `Elements/` e `Selections/`.
3. Creare nota comando in `Commands/`.
4. Eseguire dry-run nativo con `sync_csv_parameters(dryRun: true)` oppure preview validata se il tool scelto non supporta dry-run.
5. Confermare modifica reale.
6. Spot check parametri.
7. Verificare audit log e nota `Runs/`.

### 15.4 Build richiesti se si modifica RevitCortex

Se si aggiungono o modificano file C#:

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"
```

Se si modifica solo documentazione, non e' richiesto build.

---

## 16. MVP proposto

### 16.1 MVP A - Agent-only, zero codice RevitCortex

**Descrizione:** usare RevitCortex esistente + MCP Obsidian esistente. L'agente fa l'orchestrazione.

**Funzioni:**
- Pubblica `Project.md`.
- Pubblica health check e warnings.
- Pubblica selezione corrente.
- Legge una nota comando `parameter-update`.
- Esegue dry-run con tool standard.
- Applica dopo conferma.
- Aggiorna nota e run log.

**Pro:**
- Verifica fattibilita' subito.
- Nessuna modifica a RevitCortex.
- Rischio basso.

**Contro:**
- Parsing e convenzioni restano nel prompt/agente.
- Meno ripetibile se cambia client MCP.

### 16.2 MVP B - Export knowledge pack da RevitCortex

**Descrizione:** aggiungere un tool RevitCortex read-only che produce un pacchetto Markdown/JSON compatto, poi l'agente lo scrive nel vault.

Tool candidato:

```text
export_obsidian_knowledge_pack(scope, categories, elementIds, includeWarnings, includeParameters, maxElements)
```

**Pro:**
- Output standard e testabile.
- Migliore token efficiency.
- Meno logica fragile nel prompt.

**Contro:**
- Richiede codice.
- Va mantenuto cross-target.

### 16.3 MVP C - Adapter Obsidian dentro RevitCortex

**Descrizione:** RevitCortex scrive direttamente nel vault.

**Pro:**
- Workflow end-to-end piu' integrato.

**Contro:**
- Accoppia RevitCortex a un vault locale.
- Aumenta superficie sicurezza.
- Meno portabile tra client MCP.

### 16.4 Raccomandazione

Partire da **MVP A** per validare il valore funzionale e le regole di write-back. Solo dopo 2-3 sessioni reali passare a MVP B per consolidare i flussi frequenti. Evitare MVP C nella prima fase.

---

## 17. Roadmap

### Phase 0 - Feasibility

- Scegliere MCP Obsidian o filesystem adapter.
- Creare vault test.
- Validare read/write note.
- Validare patch frontmatter/sezioni.

### Phase 1 - Read publish

- Pubblicare status modello.
- Pubblicare warnings.
- Pubblicare selezione.
- Definire naming note e frontmatter.

### Phase 2 - Parameter write-back

- Definire formato `parameter-update`.
- Validare parser tabella.
- Eseguire dry-run.
- Applicare modifiche confermate.
- Aggiornare note comando e run.

### Phase 3 - Issue workflow

- Creare issue da warning/clash.
- Collegare issue a elementi e selezioni.
- Sincronizzare stato issue verso parametri Revit dedicati, se disponibili.

### Phase 4 - Consolidamento in RevitCortex

- Valutare tool read-only `export_obsidian_knowledge_pack`.
- Valutare helper per trasformare payload RevitCortex in Markdown.
- Aggiornare `WORKFLOWS.md` solo dopo un flusso reale verificato.

---

## 18. Punti aperti per brainstorming

1. **Adapter Obsidian:** quale MCP server usare nel primo test: MCPVault, Local REST API based, plugin Vault as MCP, o filesystem?
2. **Vault target:** path reale del vault e naming progetto.
3. **Identita' elemento:** usare sempre `UniqueId` oltre a `ElementId`? Raccomandato si.
4. **Parametri ammessi:** quali parametri possono essere modificati da Obsidian nel primo MVP?
5. **Stato comando:** preferire stato in frontmatter o in sezione Markdown? Raccomandato frontmatter.
6. **Conferma:** conferma solo in chat + TaskDialog Revit, o anche cambio status manuale a `ready`?
7. **Issue workflow:** serve subito o dopo il primo round-trip parametri?
8. **Knowledge graph:** usare solo link Obsidian inizialmente o indicizzazione semantica esterna?
9. **Ambito write-back:** bloccare tutte le operazioni geometriche nel codice o solo nella policy agente?

---

## 19. Fonti e riferimenti

- RevitCortex AGENTS.md e WORKFLOWS.md: architettura, tool selection, dry-run, locale, read-only mode, `send_code_to_revit` policy.
- RevitCortex `tool-schemas.txt`: superficie tool corrente.
- MCPVault: https://github.com/bitbonsai/mcpvault
- cyanheads Obsidian MCP server: https://github.com/cyanheads/obsidian-mcp-server
- MarkusPfundstein mcp-obsidian: https://github.com/MarkusPfundstein/mcp-obsidian
- Vault as MCP Obsidian plugin: https://community.obsidian.md/plugins/vault-as-mcp

---

## 20. Decisione consigliata

Procedere con una specifica operativa MVP A:

1. Obsidian come memoria e command surface.
2. RevitCortex invariato nella prima prova.
3. Write-back limitato a `parameter-update` su elementi espliciti.
4. Dry-run obbligatorio.
5. Esecuzione reale solo dopo conferma.
6. Log in `Runs/` e stato comando aggiornato in frontmatter.

Questa scelta massimizza apprendimento e riduce rischio. Se il workflow risulta utile, la seconda iterazione puo' introdurre un tool RevitCortex dedicato per produrre pacchetti Markdown standard.
