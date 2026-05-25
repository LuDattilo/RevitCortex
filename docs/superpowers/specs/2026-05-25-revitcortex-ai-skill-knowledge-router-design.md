# RevitCortex AI Skill + Knowledge Router - Specifica tecnico-funzionale

**Date:** 2026-05-25
**Status:** Approved — ready for implementation plan
**Author:** RevitCortex AI session with Luigi Dattilo
**Review target:** Approve scope before writing the implementation plan

---

## 1. Sintesi

Questa specifica definisce un layer AI operativo per RevitCortex basato su una skill personalizzata e una knowledge base Markdown modulare.

L'obiettivo non e' sostituire l'MCP server. RevitCortex MCP resta il layer esecutivo verso Revit: tool tipizzati, dry-run, conferme native, audit log, read-only mode e risultati strutturati. La skill diventa invece il router decisionale che, prima di usare tool MCP o modificare codice, carica solo le istruzioni e i template rilevanti per il task.

Il problema da risolvere e' la ripetizione di contesto tra sessioni: tool sbagliati, default costosi, dimenticanze su lingua Revit, errori net48/net8, uso improprio di `send_code_to_revit`, workflow riscoperti piu' volte. Il sistema deve trasformare `AGENTS.md`, `WORKFLOWS.md`, `tool-schemas.txt` e le spec esistenti in memoria operativa selettiva.

---

## 2. Obiettivi

### 2.1 Obiettivi funzionali

- Creare una skill AI che riconosce il tipo di richiesta RevitCortex prima di agire.
- Caricare solo i template necessari per il task corrente.
- Guidare l'agente verso la sequenza MCP piu' sicura e meno costosa.
- Guidare lo sviluppo C# con pattern locali: `ICortexTool`, `CortexResult<T>`, dynamic tools, audit log, read-only mode, test e build multi-target.
- Ridurre il numero di riletture complete di `AGENTS.md` e `WORKFLOWS.md`.
- Rendere aggiornabile la knowledge base quando un nuovo workflow viene validato.
- Separare workflow operativi BIM da workflow di sviluppo RevitCortex.

### 2.2 Obiettivi non funzionali

- Token efficiency: i template devono essere piccoli, mirati e citabili.
- Sicurezza: nessuna scorciatoia che bypassi dry-run, conferme o read-only mode.
- Compatibilita': le istruzioni di sviluppo devono rispettare `Debug R25` e `Debug R24`.
- Manutenibilita': ogni template deve avere uno scopo chiaro e un proprietario logico.
- Portabilita': la skill deve poter vivere sia nel repo sia come skill personale installabile.
- Tracciabilita': ogni regola importante deve puntare alla fonte originale quando possibile.

---

## 3. Non obiettivi

La prima versione non deve coprire:

- Un indice completo della Revit API.
- Generazione autonoma di script C# via `send_code_to_revit`.
- Sostituzione dei tool MCP esistenti.
- Sincronizzazione automatica con Obsidian o altre app esterne.
- Ricerca semantica obbligatoria o database vettoriale.
- Copertura perfetta di ogni metodo Revit API.
- UI nel plugin Revit.
- Refactor del server MCP o del plugin C#.

---

## 4. Architettura proposta

```
User prompt
  |
  v
AI client (Codex / Claude Code)
  |
  v
revitcortex skill router
  |
  +-- references/operator_*.md
  +-- references/developer_*.md
  +-- references/index_*.md
  |
  v
Selected workflow / checklist / template
  |
  v
RevitCortex MCP tools OR repository code edits
```

La skill non esegue direttamente operazioni su Revit. La skill decide quale conoscenza caricare e quali vincoli applicare. L'esecuzione resta nei tool MCP o negli strumenti di sviluppo locali.

---

## 5. Struttura proposta

Per l'MVP, la struttura puo' vivere nel repository sotto una cartella versionabile. In una fase successiva potra' essere installata in `$CODEX_HOME/skills` o nella cartella skill di Claude Code.

La cartella skill deve chiamarsi esattamente come il nome della skill. Per l'MVP il nome raccomandato e' `revitcortex`.

```
ai-skills/
  revitcortex/
    SKILL.md
    agents/
      openai.yaml
    references/
      00_Master_Index.md
      operator_01_Session_Start_Locale.md
      operator_02_Tool_Selection_Hierarchy.md
      operator_03_Destructive_Operations_DryRun.md
      operator_04_Parameter_Workflows.md
      operator_05_Model_Health_Warnings_Clash.md
      operator_06_View_And_Annotation_Workflows.md
      operator_07_IFC_Workflows.md
      operator_08_PowerBI_Workflows.md
      operator_09_Obsidian_Workflows.md
      operator_10_SendCodeToRevit_Escalation.md
      developer_20_New_Tool_Checklist.md
      developer_21_CortexResult_And_Errors.md
      developer_22_Net48_Net8_Compatibility.md
      developer_23_Dynamic_Tools_And_Capabilities.md
      developer_24_ReadOnly_Audit_Security.md
      developer_25_Build_Test_Release_Checklist.md
      index_40_Tool_Signature_Index.md
      index_41_Workflow_Source_Map.md
```

Il nome `ai-skills/` e' intenzionale: evita confusione con `src/`, `docs/` e con cartelle runtime tipo `.claude/`.

Le reference sono volutamente a un solo livello sotto `references/`, cosi' `SKILL.md` puo' linkarle direttamente e l'agente non deve inseguire percorsi annidati. Non creare `README.md`, guide installazione o changelog dentro la skill: la skill deve contenere solo istruzioni e risorse necessarie all'agente.

---

## 6. Skill router

`SKILL.md` deve essere breve. Il suo compito e' classificare la richiesta e caricare i riferimenti necessari.

Il frontmatter di `SKILL.md` deve contenere solo:

```yaml
---
name: revitcortex
description: Use when working with RevitCortex operations, MCP tool workflows, Revit model automation, or RevitCortex C# development. Routes Codex to focused references for model operations, safe write workflows, send_code_to_revit escalation, IFC, PowerBI, Obsidian, tool development, net48/net8 compatibility, audit/read-only security, and build/test checks.
---
```

### 6.1 Classificazione richiesta

| Tipo richiesta | Trigger tipici | Template da caricare |
|----------------|----------------|----------------------|
| Operazione su modello Revit | warning, clash, parametri, viste, tag, schedule, IFC | `operator_01`, `operator_02`, piu' template dominio |
| Modifica parametri | update, compilare, svuotare, copiare, sync CSV | `operator_03`, `operator_04` |
| Operazione distruttiva | delete, purge, rename, modifica massiva | `operator_03`, `operator_10` se script richiesto |
| Sviluppo nuovo tool | add tool, new MCP tool, schema, router | `developer_20`, `developer_21`, `developer_22`, `developer_25` |
| Sicurezza / sandbox | read-only, audit, send_code, permission | `developer_24`, `operator_10` |
| IFC | import, link, rebuild, export IFC | `operator_07` |
| PowerBI | publish, query, selection, schedule data | `operator_08` |
| Obsidian / knowledge | vault, note, write-back, issue | `operator_09` |

### 6.2 Regole globali sempre attive

- Non usare `send_code_to_revit` per bulk/batch senza consenso esplicito.
- Per richieste Revit operative, rilevare lingua e contesto modello all'inizio sessione.
- Per modifiche distruttive, usare dry-run quando disponibile.
- Per sviluppo C#, verificare `Debug R25` e `Debug R24` quando si toccano file C#.
- Non caricare documenti lunghi se un template mirato basta.
- Se un workflow funzionante non e' ancora documentato, proporre aggiornamento della knowledge base.

---

## 7. Template MVP

### 7.1 Operator templates

**operator_01_Session_Start_Locale.md**
- Quando chiamare `get_project_info`.
- Quando filtrare include successivi.
- Come rilevare lingua Revit dai parametri.
- Come evitare contesto sporco.

**operator_02_Tool_Selection_Hierarchy.md**
- Matrice: model health, element search, parameter update, clash, schedule/export.
- Preferenza per tool mirati.
- Default da sovrascrivere.

**operator_03_Destructive_Operations_DryRun.md**
- Lista tool distruttivi.
- Pattern dry-run -> preview -> execute.
- Gestione `Cancelled`.

**operator_04_Parameter_Workflows.md**
- `set_element_parameters` vs `bulk_modify_parameter_values` vs `sync_csv_parameters`.
- Discovery nomi parametro.
- Type parameter e `parameterType: "type"`.

**operator_05_Model_Health_Warnings_Clash.md**
- Morning check.
- `get_warnings maxWarnings: 10/50`.
- `clash_detection` vs `workflow_clash_review`.

**operator_06_View_And_Annotation_Workflows.md**
- `tag_rooms`, `tag_walls`, `color_elements`, `create_dimensions`.
- Verifica vista attiva e categoria localizzata.

**operator_07_IFC_Workflows.md**
- Capacita IFC.
- Link IFC.
- Ricostruzione nativa con dry-run.
- Export con configurazione.

**operator_08_PowerBI_Workflows.md**
- Publish elements, schedules, selection.
- Query/select roundtrip.
- Limiti e path template.

**operator_09_Obsidian_Workflows.md**
- Snapshot verso vault.
- Command note con stati.
- Write-back parametrico controllato.

**operator_10_SendCodeToRevit_Escalation.md**
- Quando proporlo.
- Frase standard di consenso.
- Limiti sandbox.
- Alternative con tool standard.

### 7.2 Developer templates

**developer_20_New_Tool_Checklist.md**
- File da toccare.
- Naming.
- Categoria.
- `RequiresDocument`, `IsDynamic`.
- Schema e `tool-schemas.txt`.

**developer_21_CortexResult_And_Errors.md**
- Success/fail envelope.
- Error codes.
- Messaggi e suggestion.

**developer_22_Net48_Net8_Compatibility.md**
- Feature vietate per R23/R24.
- Pattern C# compatibili.
- Build obbligatorie.

**developer_23_Dynamic_Tools_And_Capabilities.md**
- `DocumentAnalyzer`.
- `EnableTool`.
- Router visibility.

**developer_24_ReadOnly_Audit_Security.md**
- Naming read-only.
- Audit log.
- Sandbox `send_code_to_revit`.

**developer_25_Build_Test_Release_Checklist.md**
- Build plugin R25/R24.
- Build server.
- Test project diretto.
- Regenerazione `tool-schemas.txt`.

### 7.3 Index templates

**index_40_Tool_Signature_Index.md**
- Versione compatta derivata da `tool-schemas.txt`.
- Solo firme, senza esempi lunghi.

**index_41_Workflow_Source_Map.md**
- Mappa template -> fonte: `AGENTS.md`, `WORKFLOWS.md`, docs specifici.
- Serve a evitare drift.

---

## 8. Data flow operativo

### 8.1 Uso su modello Revit

1. L'utente chiede un task BIM.
2. La skill classifica il task.
3. La skill carica 2-4 template rilevanti.
4. L'agente sceglie la sequenza MCP piu' mirata.
5. Se necessario, esegue discovery minima.
6. Per write/destructive operations, fa dry-run e chiede conferma.
7. A fine task, se emerge un nuovo workflow stabile, aggiorna `WORKFLOWS.md` o propone un template nuovo.

### 8.2 Uso nello sviluppo

1. L'utente chiede modifica a RevitCortex.
2. La skill carica template developer pertinenti.
3. L'agente implementa seguendo pattern locali.
4. Aggiorna schema, docs e workflow se necessario.
5. Verifica build/test richiesti.
6. Riporta eventuali build non eseguibili.

---

## 9. Gestione aggiornamenti knowledge base

Ogni template deve avere intestazione standard:

```md
# Titolo

**Scope:** quando usare questo template
**Sources:** AGENTS.md, WORKFLOWS.md, docs/...
**Last verified:** YYYY-MM-DD

## Decision rules
...

## Required checks
...

## Avoid
...
```

Quando si scopre un nuovo flusso:

1. Verificare che non sia gia' in `WORKFLOWS.md`.
2. Aggiungerlo a `WORKFLOWS.md` se e' operativo BIM.
3. Aggiungerlo o linkarlo nel template relativo se serve al router.
4. Aggiornare `41_Workflow_Source_Map.md`.

---

## 10. Sicurezza

La skill deve rinforzare le protezioni esistenti, non aggirarle.

- Le modifiche Revit devono passare dai tool MCP quando esiste un tool adatto.
- `send_code_to_revit` richiede consenso esplicito per bulk/batch o logica complessa.
- Le operazioni distruttive usano dry-run quando disponibile.
- Le cancellazioni o modifiche massive non devono essere inferite da note generiche.
- In read-only mode, l'agente non deve cercare workaround.
- I template non devono incoraggiare accesso filesystem/network/process tramite codice Revit.

---

## 11. Testing e validazione MVP

Il valore della skill va misurato con prompt reali, non con demo astratte.

### 11.1 Prompt operativi di test

1. "Controlla rapidamente lo stato del modello e dimmi se ci sono warning critici."
2. "Trova muri con parametro WBS vuoto e prepara aggiornamento massivo."
3. "Esegui clash rapido tra strutture e muri."
4. "Esporta dati schedule verso PowerBI e seleziona elementi filtrati."

### 11.2 Prompt sviluppo di test

1. "Aggiungi un nuovo tool read-only che lista X."
2. "Aggiungi un tool write con conferma e audit."
3. "Correggi un errore R24 causato da feature C# moderna."

### 11.3 Metriche

- Numero di template caricati per task.
- Numero di tool MCP chiamati.
- Numero di iterazioni prima del risultato corretto.
- Errori evitati: categoria localizzata, parametro type/instance, dry-run, build R24.
- Necessita' di rileggere `AGENTS.md` completo.

---

## 12. Piano MVP consigliato

### Fase 1 - Knowledge extraction

- Creare cartella `ai-skills/revitcortex`.
- Inizializzare la skill con lo script `init_skill.py` del sistema skill quando disponibile, poi adattare i file generati.
- Scrivere `SKILL.md` router.
- Estrarre 8 template operator fondamentali da `AGENTS.md` e `WORKFLOWS.md`.
- Estrarre 5 template developer fondamentali.
- Creare source map.
- Generare o aggiornare `agents/openai.yaml` in modo coerente con `SKILL.md`.

### Fase 2 - Trial manuale

- Usare la skill in 3 sessioni reali.
- Annotare regole mancanti.
- Correggere template troppo lunghi o troppo vaghi.

### Fase 3 - Hardening

- Aggiungere checklist di verifica.
- Aggiornare `WORKFLOWS.md` quando emergono flussi nuovi.
- Decidere se installare la skill fuori repo come skill personale.

---

## 13. Criteri di accettazione

L'MVP e' accettato quando:

- `SKILL.md` classifica almeno 8 tipi di richiesta.
- Ogni template MVP ha scope, fonti, regole e anti-pattern.
- La skill non richiede di leggere tutto `AGENTS.md` per task comuni.
- Un task operativo BIM produce una sequenza MCP coerente con `WORKFLOWS.md`.
- Un task di sviluppo C# ricorda build R25 e R24.
- `send_code_to_revit` viene trattato come escalation con consenso.
- La source map permette di capire da dove viene ogni regola.

---

## 14. Rischi e mitigazioni

| Rischio | Mitigazione |
|--------|-------------|
| Template troppo numerosi | Partire con MVP da 15-18 file, non 77 |
| Knowledge base obsoleta | `Last verified` e source map |
| Duplicazione con `AGENTS.md` | I template sono viste operative, non sostituti integrali |
| Skill troppo lenta | Router breve e caricamento selettivo |
| Regole in conflitto | Priorita': user request, AGENTS.md, template, giudizio agente |
| Falsa sicurezza API | Non promettere copertura completa Revit API |

---

## 15. Decisione raccomandata

Procedere con un MVP repo-local, senza modifiche al codice RevitCortex.

La prima iterazione deve produrre una skill utilizzabile da Codex/Claude Code come router operativo e developer assistant. Solo dopo 2-3 sessioni reali si decide se espanderla, installarla come skill personale o collegarla al futuro workflow Obsidian.

Questa direzione conserva il valore principale di RevitCortex MCP e aggiunge memoria selettiva sopra il sistema, evitando di trasformare la documentazione in un blocco enorme da rileggere ogni volta.
