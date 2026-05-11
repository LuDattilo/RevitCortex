# PBI Live — Phase 2C Handoff

**Data:** 2026-05-11
**Versione plugin:** post v1.0.18 (in `main`)
**Visual PBIVIZ:** `1.0.0.3`
**Stato:** ✅ End-to-end validato il 2026-05-11

> Documento di sintesi tecnica per chi voglia capire o riprendere il lavoro su Phase 2C senza rileggere spec, plan e tutti i commit. Pensato anche come base per un post di stato del WIP.

---

## Obiettivo

Permettere a un utente di Power BI **Desktop** (non Service) di selezionare elementi in Revit cliccando un pulsante in un report.

Workflow prima di Phase 2C:
1. Utente clicca su una riga di una matrice in PBI Desktop con `revitcortex://select?ids=...` → vecchio protocol handler → Revit seleziona
2. Funziona ma richiede una colonna URL preparata via DAX e non ha feedback di stato connessione

Workflow Phase 2C:
1. Utente trascina la colonna `ElementId` su un custom visual RevitCortex
2. I filtri di pagina/report determinano in automatico cosa il visual riceve
3. L'utente clicca un pulsante "Seleziona in Revit" → richiesta HTTP POST a `localhost:27016` → Revit seleziona

Vantaggi rispetto al protocol handler:
- Niente colonna DAX preparata
- Indicatore "connesso/non connesso" in tempo reale
- Funziona con cross-filter (un click su un altro visual filtra automaticamente cosa va selezionato)
- Supporta anche **isolate temporary** (oltre alla selezione)
- Non richiede registrazione protocol handler nel registro di Windows

---

## Architettura

```
┌──────────────────────────────────┐         ┌──────────────────────────────────┐
│   Power BI Desktop               │         │   Revit + RevitCortex Plugin     │
│                                  │         │                                  │
│   ┌──────────────────────────┐   │  POST   │   ┌──────────────────────────┐   │
│   │ RevitCortex Selection    │───┼─────────┼──→│ PbiSelectHttpListener    │   │
│   │   (custom visual TS/React)│   │  27016  │   │   System.Net.HttpListener│   │
│   └──────────────────────────┘   │         │   │   background thread       │   │
│           │                       │         │   └──────────────────────────┘   │
│           │ dataView.table.rows   │         │           │                      │
│           │ dataView.table.       │         │           │ Prepare(rawIds,      │
│           │   highlights          │         │           │   action)            │
│           ▼                       │         │           ▼                      │
│   Element IDs                     │         │   PbiSelectionEventHandler        │
│                                  │         │   IExternalEventHandler          │
│                                  │         │           │                      │
│                                  │         │           │ Execute(UIApp)       │
│                                  │         │           │ ← main thread        │
│                                  │         │           ▼                      │
│                                  │         │   uiDoc.Selection.               │
│                                  │         │     SetElementIds(validIds)      │
│                                  │         │   doc.ActiveView.                │
│                                  │         │     IsolateElementsTemporary     │
│                                  │         │     (if action=="isolate")       │
└──────────────────────────────────┘         └──────────────────────────────────┘
```

**Threading model — punto critico:**
- HTTP listener gira su un thread di background dedicato
- Riceve `POST /pbi-select` con `{ elementIds, action }`
- **Non** valida gli ID né tocca il `Document` sul background thread
- Chiama `PbiSelectionEventHandler.Prepare(rawIds, action)` (memorizza stato) + `ExternalEvent.Raise()`
- Revit chiama poi `Execute(UIApplication)` sul thread UI; lì valida `doc.GetElement(eid)` e applica `Selection.SetElementIds`

Questa separazione è obbligatoria: la Revit API è **strictly single-threaded** — qualsiasi accesso al `Document` da un thread != main può crashare l'add-in o corrompere dati.

---

## Componenti C# (lato Revit)

### `PbiSelectHttpListener.cs` (175 righe)

`System.Net.HttpListener` su `http://localhost:27016/`. Niente dipendenza da `RevitAPI.dll`, quindi 100% unit-testabile.

**Endpoint:**
- `OPTIONS /pbi-select` → 200 + headers CORS (preflight per il WebView2 di PBI Desktop)
- `POST /pbi-select` → 200 con `{ success, elementCount, action, validated }` o 4xx su body invalido
- `GET|PUT|DELETE /pbi-select` → 405

**Comportamento:**
- Avvio: se la porta è già occupata (es. seconda istanza Revit aperta), logga e ritorna **senza throw** — non blocca lo startup del plugin
- Shutdown: `Stop()` chiude il listener e fa `_thread.Join(500)` per evitare `AppDomainUnloadedException` quando Revit unload-a l'assembly

**Contratto callback:**
```csharp
Func<IList<long>, string, string?>
//        rawIds   action    return
//                           - null  → "no active doc" → risponde success=false
//                           - "..." → "ok" → risponde success=true, validated="..."
```

### `PbiSelectionEventHandler.cs` (96 righe)

`IExternalEventHandler` registrato una volta in `OnStartup` (la creazione di un `ExternalEvent` deve avvenire prima di `OnIdling`).

`Prepare(rawIds, action)` viene chiamato dal background thread, scrive su due campi `volatile`. `Execute(UIApplication)` legge i campi, valida ogni ID (con `#if REVIT2024_OR_GREATER` per `new ElementId(long)` vs `new ElementId((int)idVal)` su net48), e applica la selezione.

`IsolateElementsTemporary` non viene wrappato in una `Transaction` manuale — gestisce la propria transazione internamente. Wrappare causa `InvalidOperationException` "modifiable document".

### `RevitCortexApp.cs` (modifiche)

In `OnStartup`:
```csharp
_pbiSelectionHandler = new PbiSelectionEventHandler();
_pbiSelectionEvent = ExternalEvent.Create(_pbiSelectionHandler);
```

In `StartService(Document)` (chiamato dal Cortex Switch):
```csharp
_pbiSelectListener = new PbiSelectHttpListener(
    handleSelection: (rawIds, action) =>
    {
        var uiApp = _uiApplication;  // letto dinamicamente (assegnato lazy in OnIdling)
        if (uiApp == null) return null;
        handler.Prepare(rawIds, action);
        evt.Raise();
        return "queued";
    },
    port: 27016);
_pbiSelectListener.Start();
```

In `StopService` + `OnShutdown`: `_pbiSelectListener?.Stop()` + `Dispose()`.

---

## Componenti TypeScript (lato Power BI)

### `capabilities.json`

```json
{
  "dataRoles": [{
    "name": "elementIds",
    "kind": "Grouping",
    "displayName": "Element ID"
  }],
  "dataViewMappings": [{
    "table": {
      "rows": {
        "for": { "in": "elementIds" },
        "dataReductionAlgorithm": { "top": { "count": 30000 } }
      }
    }
  }],
  "supportsHighlight": true,
  "privileges": [{
    "name": "WebAccess",
    "essential": true,
    "parameters": ["http://localhost:27016"]
  }]
}
```

Punti chiave:
- `kind: "Grouping"` → PBI tratta `ElementId` come dimensione, non come misura. Niente aggregazione `Count of ElementId`
- `supportsHighlight: true` → l'API popula `dataView.table.highlights` quando il cross-filter è attivo
- `privileges: [WebAccess]` → richiesto per fetch a URL esterni dal sandbox PBI; senza, le richieste fallirebbero silenziosamente

### `visual.tsx`

Singolo file ~325 righe. React 18 (`ReactDOM.render` — TODO: migrare a `createRoot` quando salira la API version).

**Pipeline dati:**
- `update(options)` → estrae `filteredIds` (tutte le righe del table data view) e `highlightedIds` (subset evidenziato dal cross-filter)
- Re-render React quando i dati cambiano o quando lo stato di connessione cambia

**Pipeline rete:**
- Polling ogni 30 s di `OPTIONS http://localhost:27016/pbi-select` → aggiorna indicatore "Connesso a Revit / RevitCortex non attivo"
- Click button → `POST /pbi-select` con `{ elementIds, action }` + `AbortSignal.timeout(5000)`. Su fallimento, re-check immediato della connessione

**UI:**
- Titolo "RevitCortex Selection" in teal `#00838F` (palette in linea con il Settings del plugin)
- Pill stato sotto al titolo (verde con `#DFF6DD` su `#107C10` per "Connesso", grigio per "Non attivo")
- Pulsante primario grande "Seleziona in Revit" → invia gli `activeIds` (= highlighted se cross-filter attivo, altrimenti tutti i filtered)
- Sotto-testo nel pulsante: `N highlighted · M totali` o `N filtered`
- Pulsante secondario "Isola in Revit" (outline) → stesso set di ID ma `action="isolate"`
- Feedback "✓ Inviati N elementi" per 3 secondi dopo invio andato a buon fine

**i18n:**
- `detectLang(host)` legge `host.locale` (es. `it-IT`, `en-US`) e ritorna `"it"` o `"en"`
- Fallback su `navigator.language` se l'host non lo passa
- `STRINGS["it"]` e `STRINGS["en"]` contengono tutte le label

---

## Sicurezza (sintesi)

Il listener accetta POST da **qualunque processo sulla stessa macchina sessione utente**. Su loopback Windows isola tra utenti, ma non tra processi dello stesso utente. Operazioni esposte: `select` + `isolate temporary` — entrambe **non distruttive** (nessuna modifica al modello, nessun I/O su disco).

Mitigazioni implementate:
- Binding `http://localhost:27016/` (non `+`/`*`) → SO rifiuta connessioni esterne
- CORS preflight con `Access-Control-Allow-Origin: *` ma method allowlist
- Auto-stop alla chiusura del documento o al cambio di Cortex Switch

Mitigazioni non implementate ma documentate in `docs/SECURITY.md`:
- Token per-sessione
- Allowlist `Host` header (anti DNS rebinding)
- Rate limiting

Rationale: le operazioni esposte sono non distruttive, quindi rischio basso. Se in futuro si esporranno operazioni di scrittura, le mitigazioni sopra sono il punto di partenza.

---

## Test

8 unit test su `PbiSelectHttpListener` + 2 aggiunti dopo code review = **10 test totali**:

| Test | Cosa verifica |
|------|---------------|
| `Post_EmptyArray_Returns200WithWarning` | Array vuoto → success=true + warning |
| `Post_InvalidJson_Returns400` | Body malformato → 400 |
| `Post_NoActiveDocument_Returns200WithError` | Callback ritorna null → success=false |
| `Options_Preflight_Returns200WithCorsHeaders` | OPTIONS preflight per CORS |
| `NonPost_Returns405` | GET/PUT/DELETE → 405 |
| `PortAlreadyInUse_StartDoesNotThrow` | Seconda istanza non crasha |
| `IsRunning_TrueAfterStart_FalseAfterStop` | Lifecycle base |
| `Post_ValidIds_CallbackReceivesIds` | Happy path |
| `Post_IsolateAction_CallbackReceivesIsolate` | Azione "isolate" arriva al callback |
| `Post_SuccessResponse_IncludesValidatedField` | Forma della risposta success |

Tutti girano senza `RevitAPI.dll` perché la callback usa raw `long`, non `ElementId`.

Stato build: **5 target verdi** (R23/R24 net48, R25/R26 net8, R27 net10). 181/182 test passano nel runner (1 fallimento pre-esistente `RevitAPIUI` non legato).

---

## Cronologia commit (su `main`)

Phase 2C in ordine:

1. `209ae66` docs: spec design
2. `d3f1d77` docs: spec review fixes
3. `4c41894` docs: implementation plan
4. `26794ac` feat: `PbiSelectHttpListener`
5. `821c3b9` feat: wire-up in `RevitCortexApp`
6. `832df64` chore: 5 targets clean
7. `9a626da` feat: PBIVIZ scaffold
8. `e932501` feat: `visual.tsx` + capabilities
9. `56d4fc5` chore: pbiviz dist artefact
10. `36e88b6` docs: install/usage in WORKFLOWS + USER_GUIDE

UX iterations dopo il primo test end-to-end:

11. `65d27db` fix: `_uiApplication` dinamico (chiusura del bug "non funziona")
12. `a1987c6` chore: logging diagnostico
13. `2dd0486` feat: UX overhaul (Grouping kind, big primary button, icon)
14. `cb5141d` feat: icona definitiva
15. `8584260` **fix code review**: thread safety + transaction + thread.Join + security docs

Restyling finale:

16. _commit corrente_: palette RevitCortex + i18n EN/IT (v1.0.0.3)

---

## Punti aperti / Idee per il futuro

### Selezione inversa (Revit → PBI Desktop)

PBI Desktop **non** ha un'API per ricevere selezione esterna real-time. Opzioni:

- **A) Polling dal visual** (1h di lavoro): visual fa GET su `/revit-selection` ogni 5s; mostra gli ID localmente
- **B) Filtro di report da custom visual** (3-4h): usa `host.applyJsonFilter()` per propagare la selezione Revit a tutti i visual del report. Limiti API noti, da verificare
- **C) `pbi_publish_selection`** (già esistente): funziona su PBI Service, non Desktop

Decisione attuale: nessuno dei tre. Aspettiamo se serve davvero.

### Multi-istanza Revit

Il listener accetta solo una connessione (porta 27016). Se due istanze Revit sono aperte, la seconda salta il listener silenziosamente — quindi la POST va sempre alla prima. Per gestire multi-istanza servirebbe un meccanismo di scoperta (es. lista istanze attive su porte 27016, 27017, ...) o un binding documento→porta tipo `pbi_get_binding`.

### Custom visual marketplace

Per ora il `.pbiviz` è distribuito file-per-file. Submission sul Microsoft AppSource richiederebbe certificazione + il `WebAccess` privilege probabilmente non passa la review (richiede URL pubblico, non localhost). Non un'opzione realistica.

### Migrazione React 18

`ReactDOM.render` è deprecato — migrare a `createRoot` quando saliamo l'API version di pbiviz oltre 5.3.0.

### Operazioni di scrittura

Se in futuro si volesse esporre via HTTP operazioni come `modify_parameter`, prima implementare:
1. Token per-sessione
2. Read-only mode check
3. Confirmation dialog (`session.RequestConfirmation`)
4. Audit log

---

## Per il post WIP

Sintesi narrativa breve (per LinkedIn / blog):

> RevitCortex Phase 2C aggiunge una connessione live bidirezionale tra Power BI Desktop e Revit. Un custom visual `.pbiviz` (TypeScript + React, palette in linea con il plugin) si connette a un piccolo HTTP listener integrato nel plugin Revit (porta 27016, thread di background, sandbox loopback). L'utente trascina la colonna ElementId sul visual; un click su "Seleziona in Revit" propaga la selezione filtrata o cross-filtrata istantaneamente. Niente file intermedi, niente API cloud, niente autenticazione MSAL — comunicazione locale 100% via HTTP/JSON. Lingua del visual auto-rilevata dal locale di PBI (EN/IT). Stack: C# `System.Net.HttpListener` + Revit `IExternalEventHandler`, TypeScript ES2022 + React 18 + `powerbi-visuals-tools` 5.2.

Screenshot consigliati per il post:
- Visual dentro PBI Desktop con un grafico a fianco (cross-filter attivo)
- Revit con la selezione propagata (cattura dopo click su "Seleziona in Revit")
- Eventualmente il pannello Cortex Settings per mostrare il branding coerente

---

Per detail più granulari: spec in `docs/superpowers/specs/2026-05-11-powerbi-live-phase2c-design.md`, plan in `docs/superpowers/plans/2026-05-11-powerbi-live-phase2c.md`.
