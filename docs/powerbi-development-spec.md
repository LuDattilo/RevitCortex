# RevitCortex x Power BI Live - Specifica per sviluppo

**Versione**: 1.0  
**Data**: 2026-05-11  
**Branch di riferimento**: `feat/powerbi-live-phase0`  
**Scopo**: documento operativo da consegnare allo sviluppo per consolidare Phase 0 e implementare Phase 1+ dell'integrazione Power BI Live.  
**Documenti sorgente**:

- `docs/powerbi-integration-spec.md`
- `docs/powerbi-live-architecture-review.md`
- `docs/powerbi-live-handoff.md`
- `WORKFLOWS.md`

---

## 1. Obiettivo del lavoro

L'integrazione deve permettere a RevitCortex di pubblicare dati del modello Revit verso Power BI in due modalita:

1. **CSV / filesystem**, gia operativa, per export verso cartella OneDrive e refresh Power BI Desktop/Service.
2. **Live / REST API Power BI**, da completare, per creare o aggiornare push datasets direttamente su Power BI Service.

La direzione tecnica e corretta: CSV resta il fallback affidabile e tracciabile; Live diventa il canale per dashboard team, selezione corrente, report filtrati e automazioni future.

La prossima fase non deve rifare il wizard CSV. Deve consolidare Phase 0 e costruire il nucleo Live: autenticazione, workspace, dataset, publish elementi.

---

## 2. Stato attuale verificato

### 2.1 Gia presente

- Wizard CSV `Power BI Export` da ribbon Revit.
- Tool `push_to_powerbi` per export elementi/schedule in CSV.
- Tool `push_table_to_powerbi` per tabelle arbitrarie generate da Claude o da elaborazioni esterne.
- Tool `import_from_powerbi` per round-trip CSV verso parametri Revit, con dry-run e conferma.
- Tool `select_from_powerbi` per selezione/isolate da protocol handler.
- Protocol handler `revitcortex://` registrabile in HKCU.
- Phase 0 Live:
  - `PowerBiAuthService`
  - `PowerBiSettings`
  - `PowerBiServiceClient` con `ListWorkspaces`
  - tool `pbi_check_auth`
  - tool `pbi_list_workspaces`
  - cache MSAL cifrata DPAPI in `%LOCALAPPDATA%\.revitcortex\msal_cache.bin`

### 2.2 Verifica build

Le build eseguite sul worktree `naughty-thompson-a6b7c9` risultano:

- `dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`: OK, con warning preesistenti.
- `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`: OK, con warning preesistenti.

Nota: una build R24/R25 lanciata in parallelo puo sporcare `obj/project.assets.json` e generare un falso errore NETSDK1005. Per validazione usare build sequenziali o obj separati.

---

## 3. Punti da correggere prima di Phase 1

Questi punti sono prerequisiti consigliati prima di implementare push dataset.

### 3.1 Device-code dialog

Problema: la specifica dice che il TaskDialog si chiude automaticamente al completamento login, ma l'implementazione attuale mostra un TaskDialog via `Dispatcher.BeginInvoke` e non sembra chiuderlo automaticamente. Il bottone `Cancel` chiude la finestra, ma non e necessariamente collegato a una cancellazione reale del flow MSAL.

Richiesta sviluppo:

- Rendere esplicito il comportamento UX.
- Se il dialog resta manuale, aggiornare messaggi e spec.
- Se si vuole auto-close, usare una finestra WPF controllabile invece di `TaskDialog`, oppure una UX a due step: mostra codice, apri browser, poi utente conferma "Ho completato".
- Il risultato tool deve distinguere:
  - non loggato
  - login completato
  - login annullato dall'utente
  - timeout/tenant error

### 3.2 Protocol handler hardening

Problema: lo script PowerShell generato dal protocol handler usa `System.Web.HttpUtility` prima dell'`Add-Type`, e converte gli ID con `[long]$_` senza filtro preventivo robusto.

Richiesta sviluppo:

- Caricare `System.Web` prima di usare `HttpUtility`.
- Accettare solo action whitelist: `select`, `highlight`, `isolate`.
- Validare `ids` con regex numerica prima della conversione.
- Scartare valori vuoti e rifiutare valori non numerici con log chiaro.
- Limitare il numero massimo di ID per singola chiamata, ad esempio 500 o 1000, per evitare URL troppo grandi e freeze UI.

### 3.3 Read-only mode ed external writes

Problema: la spec prevede `AllowExternalWrites`, ma il router oggi classifica i tool solo con prefissi read-only. Quindi `AllowExternalWrites` e presente nei settings ma non governa davvero l'enforcement dei push esterni.

Decisione richiesta:

- Definire una classificazione esplicita per i tool:
  - read-only locale
  - write Revit
  - external write
  - mixed write
- Regola proposta:
  - `pbi_check_auth`, `pbi_list_workspaces`, `pbi_list_datasets`: sempre ammessi in read-only mode.
  - `pbi_create_dataset`, `pbi_publish_*`, `pbi_delete_rows`, `push_to_powerbi`: bloccati in read-only mode salvo `AllowExternalWrites=true`.
  - `import_from_powerbi`: sempre bloccato in read-only mode perche modifica Revit.
- La regola deve essere testata con unit test su `CortexRouter`.

### 3.4 Scope OAuth

Problema: Phase 0 richiede gia `Dataset.ReadWrite.All`, `Workspace.Read.All`, `Report.Read.All`. Questo e comodo per evitare re-consent dopo, ma puo aumentare la probabilita di blocchi tenant/admin consent.

Decisione richiesta:

- Per prototipo interno: mantenere gli scope attuali se GPA li consente.
- Per rilascio piu ampio: valutare consenso progressivo o app registration custom.
- Documentare chiaramente il failure mode `AADSTS65001` come richiesta admin consent.

---

## 4. Architettura target

### 4.1 Componenti

Componenti da mantenere separati:

- `PowerBiAuthService`: login MSAL, silent token, device-code, sign-out, cache DPAPI.
- `PowerBiSettings`: client ID, tenant, workspace/dataset/report default, `AllowExternalWrites`, debounce.
- `PowerBiServiceClient`: wrapper REST Power BI, retry/backoff, parsing errori.
- `PowerBiDatasetSchema`: definizione schema fisso e versionato.
- `PowerBiElementExporter`: snapshot Revit -> DTO detached.
- `PowerBiScheduleExporter`: schedule Revit -> righe long-form.
- `PowerBiSelectionPublisher`: publish selezione con debounce e replace mode.
- Tool MCP / router:
  - `pbi_check_auth`
  - `pbi_list_workspaces`
  - `pbi_list_datasets`
  - `pbi_create_dataset`
  - `pbi_publish_elements`
  - `pbi_publish_schedules`
  - `pbi_publish_selection`
  - `open_in_powerbi`

### 4.2 Regola threading

Regole obbligatorie:

1. Qualsiasi accesso a Revit API deve avvenire sul main thread Revit tramite il dispatcher esistente.
2. La lettura dal modello deve produrre DTO senza riferimenti a oggetti Revit.
3. Le chiamate HTTP a Power BI devono avvenire fuori dal main thread Revit.
4. Progress e status UI devono rientrare su WPF dispatcher.

Pattern richiesto:

```text
Revit main thread
  -> collect elements/schedules/current selection
  -> convert to DTO
  -> return DTO list

Background task
  -> create/find dataset
  -> delete rows if replace
  -> post rows in batches
  -> retry transient errors
  -> return publish summary

UI/MCP result
  -> show success/error, row counts, target workspace/dataset
```

---

## 5. Contratti dati Power BI

### 5.1 Versioning

Ogni dataset creato da RevitCortex Live deve avere schema versionato.

Regola proposta:

- Dataset name default: `RevitCortex Live - {ProjectName} - v1`
- Campo `_SchemaVersion` in ogni tabella: string, valore iniziale `1.0`
- Tabella `Metadata` obbligatoria:

```text
Key              string
Value            string
UpdatedAtUtc     datetime
```

Valori minimi `Metadata`:

- `SchemaVersion`
- `CreatedBy`
- `CreatedAtUtc`
- `ProjectName`
- `ProjectId`
- `DocumentGuid`
- `RevitCortexVersion`

Se cambia lo schema di una tabella, non mutare il dataset esistente in modo invisibile. Creare nuovo dataset o usare suffix versione.

### 5.2 Tabella Elements

Tabella wide e stabile. Una riga per elemento esportato.

```text
_SchemaVersion       string
ExportRunId          string
ExportedAtUtc        datetime
ProjectId            string
ProjectName          string
DocumentGuid         string
ElementId            int64
UniqueId             string
Category             string
OstCode              string
CategoryType         string
FamilyName           string
TypeName             string
Level                string
Workset              string
PhaseCreated         string
PhaseDemolished      string
Name                 string
Mark                 string
Comments             string
Volume               double
Area                 double
Length               double
BoundingBoxMinX      double
BoundingBoxMinY      double
BoundingBoxMinZ      double
BoundingBoxMaxX      double
BoundingBoxMaxY      double
BoundingBoxMaxZ      double
```

Note:

- `Category` e nomi parametro possono essere localizzati.
- `OstCode` deve essere language-independent.
- `ElementId` deve usare `ElementId.Value` per R24+ e `IntegerValue` per R23.
- Le unita numeriche devono essere convertite in unita progetto o in SI coerente. La scelta va documentata e mantenuta stabile.

### 5.3 Tabella Schedules

Long-form generica per evitare schema dinamico.

```text
_SchemaVersion       string
ExportRunId          string
ExportedAtUtc        datetime
ProjectId            string
DocumentGuid         string
ScheduleId           int64
ScheduleName         string
RowIndex             int
ColumnName           string
ValueString          string
ValueNumber          double
```

Regola: non creare una tabella diversa per ogni schedule in Phase 1.5. La forma long e piu stabile per progetti e lingue diverse.

### 5.4 Tabella ElementParameters

Long-form per parametri custom e shared parameters.

```text
_SchemaVersion       string
ExportRunId          string
ExportedAtUtc        datetime
ProjectId            string
DocumentGuid         string
ElementId            int64
UniqueId             string
ParameterName        string
ParameterScope       string
StorageType          string
ValueString          string
ValueNumber          double
Unit                 string
IsReadOnly           bool
```

`ParameterScope` ammessi:

- `Instance`
- `Type`

### 5.5 Tabella Selection

Tabella piccola, replace-mode, rappresenta l'ultima selezione Revit.

```text
_SchemaVersion       string
UpdatedAtUtc         datetime
ProjectId            string
DocumentGuid         string
ElementId            int64
UniqueId             string
SelectionSetId       string
```

Regole:

- Ogni update cancella le righe precedenti e scrive la selezione corrente.
- Se la selezione e vuota, cancellare righe e scrivere summary `selectionCount=0`.
- Debounce consigliato: 1000 ms.

---

## 6. Contratti tool MCP

### 6.1 `pbi_check_auth`

Input:

```json
{
  "signIn": false
}
```

Output success:

```json
{
  "signedIn": true,
  "signedJustNow": false,
  "username": "user@domain.com",
  "tenantId": "tenant-guid",
  "tokenExpiresOn": "2026-05-11T12:00:00Z",
  "tokenLifetimeMinutes": 59,
  "clientId": "client-guid",
  "allowExternalWrites": false,
  "tip": "Use pbi_list_workspaces next."
}
```

Output non loggato:

```json
{
  "signedIn": false,
  "message": "Not signed in. Call again with signIn=true to start device-code flow.",
  "clientId": "client-guid",
  "tenantId": "organizations"
}
```

### 6.2 `pbi_list_workspaces`

Input: nessuno.

Output success:

```json
{
  "signedInAs": "user@domain.com",
  "workspaceCount": 2,
  "workspaces": [
    {
      "id": "workspace-guid",
      "name": "RevitCortex Live",
      "type": "Workspace",
      "state": "Active",
      "premium": false,
      "userRole": "Admin"
    }
  ]
}
```

Richiesta: includere `userRole` se disponibile dall'API Power BI. Serve per capire se l'utente puo creare dataset.

### 6.3 `pbi_list_datasets`

Phase 1.

Input:

```json
{
  "workspaceId": "workspace-guid"
}
```

Output:

```json
{
  "workspaceId": "workspace-guid",
  "datasetCount": 1,
  "datasets": [
    {
      "id": "dataset-guid",
      "name": "RevitCortex Live - ProjectA - v1",
      "configuredBy": "user@domain.com",
      "isRefreshable": false,
      "createdAt": "2026-05-11T10:00:00Z"
    }
  ]
}
```

### 6.4 `pbi_create_dataset`

Phase 1.

Input:

```json
{
  "workspaceId": "workspace-guid",
  "datasetName": "RevitCortex Live - ProjectA - v1",
  "schemaVersion": "1.0",
  "tables": ["Metadata", "Elements", "Selection"]
}
```

Output:

```json
{
  "workspaceId": "workspace-guid",
  "datasetId": "dataset-guid",
  "datasetName": "RevitCortex Live - ProjectA - v1",
  "created": true,
  "schemaVersion": "1.0"
}
```

Read-only: external write, blocked unless policy allows.

### 6.5 `pbi_publish_elements`

Phase 1.

Input:

```json
{
  "workspaceId": "workspace-guid",
  "datasetName": "RevitCortex Live - ProjectA - v1",
  "datasetId": null,
  "mode": "replace",
  "scopeMode": "WholeModel",
  "categoryFilter": ["OST_Walls", "OST_Doors"],
  "maxElements": 10000
}
```

`mode` ammessi:

- `create`: crea dataset se manca, fallisce se esiste con schema incompatibile.
- `append`: aggiunge nuove righe.
- `replace`: cancella righe tabella `Elements` e scrive snapshot corrente.

Output:

```json
{
  "success": true,
  "workspaceId": "workspace-guid",
  "datasetId": "dataset-guid",
  "table": "Elements",
  "mode": "replace",
  "exportRunId": "guid",
  "rowCount": 1247,
  "batchCount": 1,
  "durationMs": 3421,
  "warnings": []
}
```

Regole:

- `replace` deve cancellare solo la tabella target, non il dataset intero.
- Batching massimo 10.000 righe per request.
- Retry su 429, 503, timeout/network.
- Non loggare payload righe.

### 6.6 `pbi_publish_schedules`

Phase 1.5.

Input:

```json
{
  "workspaceId": "workspace-guid",
  "datasetId": "dataset-guid",
  "scheduleIds": [12345, 67890],
  "mode": "append"
}
```

Output:

```json
{
  "table": "Schedules",
  "exportRunId": "guid",
  "scheduleCount": 2,
  "rowCount": 350,
  "batchCount": 1
}
```

### 6.7 `pbi_publish_selection`

Phase 3 o manuale in Phase 1.

Input:

```json
{
  "workspaceId": "workspace-guid",
  "datasetId": "dataset-guid",
  "elementIds": [123, 456]
}
```

Output:

```json
{
  "table": "Selection",
  "selectionCount": 2,
  "updatedAtUtc": "2026-05-11T10:00:00Z"
}
```

### 6.8 `open_in_powerbi`

Phase 3.

Input:

```json
{
  "workspaceId": "workspace-guid",
  "reportId": "report-guid",
  "elementIds": [123, 456]
}
```

Output:

```json
{
  "opened": true,
  "url": "https://app.powerbi.com/groups/.../reports/...?filter=..."
}
```

Regole:

- Validare selezione non vuota.
- Limitare numero elementi nel filtro URL.
- Per selezioni grandi, usare tabella `Selection` invece di URL filter con lista lunga.

---

## 7. Power BI REST API da implementare

Nel `PowerBiServiceClient` aggiungere:

- `ListWorkspacesAsync`
- `ListDatasetsAsync(workspaceId)`
- `CreatePushDatasetAsync(workspaceId, datasetName, schema)`
- `GetDatasetByNameAsync(workspaceId, datasetName)`
- `PostRowsAsync(workspaceId, datasetId, tableName, rows)`
- `DeleteRowsAsync(workspaceId, datasetId, tableName)`
- `UpdateMetadataAsync(...)` o publish verso tabella `Metadata`

Endpoint previsti:

```text
GET    /groups
GET    /groups/{groupId}/datasets
POST   /groups/{groupId}/datasets
POST   /groups/{groupId}/datasets/{datasetId}/tables/{tableName}/rows
DELETE /groups/{groupId}/datasets/{datasetId}/tables/{tableName}/rows
```

Gestione errori:

- 400: errore schema/input, non retry.
- 401: token scaduto/non valido, tentare refresh silent una volta.
- 403: permessi insufficienti, messaggio actionabile.
- 404: workspace/dataset/table non trovato.
- 409: dataset esistente o conflitto schema.
- 429: retry con exponential backoff e jitter.
- 503/network timeout: retry con exponential backoff.

Backoff consigliato:

- tentativi: 3
- base delay: 1 s
- max delay: 10 s
- rispettare `Retry-After` se presente.

---

## 8. Audit log

Ogni tool PBI deve scrivere audit log tramite il router o logger esistente.

Campi minimi:

```json
{
  "ts": "2026-05-11T10:00:00.000Z",
  "tool": "pbi_publish_elements",
  "user": "user@domain.com",
  "workspace_id": "workspace-guid",
  "dataset_id": "dataset-guid",
  "table": "Elements",
  "row_count": 1247,
  "result": "ok",
  "error_code": null,
  "duration_ms": 3421
}
```

Non loggare mai:

- access token
- refresh token
- header `Authorization`
- payload completo delle righe
- valori parametro completi se possono contenere dati sensibili

Per input summary usare count, table name, workspace/dataset IDs e mode.

---

## 9. Sicurezza

### 9.1 Data egress

Qualsiasi publish verso Power BI e data egress. Deve essere esplicito.

Requisiti:

- Mostrare workspace e dataset target prima del primo push UI.
- `AllowExternalWrites=false` di default.
- In read-only mode, bloccare external writes salvo scelta esplicita di policy.
- CSV su OneDrive aziendale e Power BI workspace sono entrambi canali di uscita dati.

### 9.2 Token

Requisiti:

- Nessun segreto in `powerbi-live.json`.
- Token solo in MSAL cache DPAPI CurrentUser.
- Supportare reset cache/sign-out.
- Gestire cache corrotta senza crash plugin.

### 9.3 Protocol handler

Requisiti:

- Registrazione HKCU, no admin.
- Listener solo `127.0.0.1`.
- Action whitelist.
- ID numerici validati.
- Log locale in `~/.revitcortex/protocol/protocol.log`.
- Nessuna esecuzione dinamica, nessuna `eval`, nessun passaggio comandi shell derivati dalla URL.

---

## 10. Roadmap implementativa

### Step 0 - Hardening Phase 0

Deliverable:

- Test reale device-code con account GPA.
- Fix/decisione UX TaskDialog device-code.
- Harden protocol handler.
- Definizione enforcement `AllowExternalWrites`.
- Conferma tenant/client ID.

Acceptance:

- `pbi_check_auth(signIn=false)` funziona.
- `pbi_check_auth(signIn=true)` completa login o ritorna errore actionabile.
- `pbi_list_workspaces` mostra workspace.
- Token sopravvive a riavvio Revit.
- Build R24 e R25 OK.

### Step 1 - Dataset schema e REST client

Deliverable:

- `PowerBiDatasetSchema`.
- `PowerBiServiceClient` completo per dataset/tables/rows.
- Retry/backoff.
- Unit test senza Revit per schema e client parsing, dove possibile.

Acceptance:

- Dataset push creato su workspace scelto.
- Tabelle `Metadata`, `Elements`, `Selection` presenti.
- Errori API mappati in `CortexResult` con suggerimenti.

### Step 2 - Publish Elements manuale

Deliverable:

- `PowerBiElementExporter`.
- Tool `pbi_publish_elements`.
- Supporto `create`, `append`, `replace`.
- Batching 10.000 righe.

Acceptance:

- Pubblicazione 100 elementi test.
- Power BI Desktop/Service legge dataset.
- Replace non duplica righe.
- Append aggiunge righe con nuovo `ExportRunId`.
- Audit log contiene workspace, dataset, rowCount, duration.

### Step 3 - Schedules long-form

Deliverable:

- `PowerBiScheduleExporter`.
- Tool `pbi_publish_schedules`.

Acceptance:

- Una schedule Revit viene pubblicata in long-form.
- Nomi colonne localizzati restano in `ColumnName`.
- Nessuna modifica schema dataset per schedule diverse.

### Step 4 - Wizard Live

Deliverable:

- Modalita Live nel wizard.
- Combo workspace.
- Dataset create/select.
- Mode append/replace.
- Progress/status.

Acceptance:

- Un BIM coordinator completa login, seleziona workspace, crea dataset e pubblica elementi in meno di 5 minuti.

### Step 5 - Selection live e open report

Deliverable:

- `pbi_publish_selection`.
- `PowerBiSelectionPublisher` con debounce.
- `open_in_powerbi`.
- Bottone ribbon opzionale "View in PBI".

Acceptance:

- Cambi di selezione aggiornano tabella `Selection` senza saturare API.
- Selezione Revit apre report Power BI filtrato o basato su tabella `Selection`.

---

## 11. Test obbligatori

### 11.1 Build

Da repo/worktree root:

```powershell
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj
```

Prima di merge finale estendere a:

```powershell
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

### 11.2 Test Phase 0

1. Revit 2025 aperto con modello qualunque.
2. Cortex Switch ON.
3. `pbi_check_auth(signIn=false)`.
4. `pbi_check_auth(signIn=true)`.
5. Completare login GPA.
6. `pbi_list_workspaces`.
7. Riavviare Revit.
8. `pbi_check_auth(signIn=false)` deve tornare signed in.

### 11.3 Test Phase 1

1. Workspace test disponibile.
2. `pbi_create_dataset`.
3. `pbi_publish_elements` con `maxElements=100`, mode `replace`.
4. Verifica dataset in Power BI.
5. Re-run `replace`: row count stabile.
6. Re-run `append`: row count aumenta e `ExportRunId` cambia.
7. Verifica audit log.
8. Simulare errore permessi o workspace non valido.

### 11.4 Test protocol handler

1. Registrare protocol handler.
2. Aprire `revitcortex://select?ids=123`.
3. Verificare selezione Revit.
4. Test URL con ID non numerico: deve fallire senza eccezioni.
5. Test action non ammessa: deve fallire e loggare.

---

## 12. Decisioni aperte da chiudere

| ID | Decisione | Default consigliato |
|---|---|---|
| D-01 | Client ID pubblico vs app registration GPA | Pubblico per test, app GPA per produzione |
| D-02 | Scope OAuth progressivi vs tutti subito | Tutti subito per prototipo, progressivi o app custom per rollout |
| D-03 | Workspace unico vs per progetto | Workspace unico, dataset per progetto |
| D-04 | Replace come delete rows o recreate dataset | Delete rows per tabella |
| D-05 | Versioning schema | `_SchemaVersion` + tabella `Metadata` |
| D-06 | Selection grande in URL filter | Usare tabella `Selection`, non URL lungo |
| D-07 | AutoExport on save | Fuori MVP, opt-in e throttled |

---

## 13. Definition of Done Phase 1

Phase 1 e completata quando:

- Phase 0 e testata end-to-end con account reale.
- Un utente autenticato puo elencare workspace.
- Il plugin puo creare un push dataset Power BI con schema `Metadata`, `Elements`, `Selection`.
- Il plugin puo pubblicare elementi Revit in `Elements` con `replace` e `append`.
- Le chiamate HTTP non bloccano il main thread Revit.
- `ReadOnlyMode` blocca correttamente write Revit ed external writes secondo policy.
- Audit log registra le chiamate PBI senza token e senza payload completo.
- Build R24 e R25 passano.
- Il comportamento e documentato in `WORKFLOWS.md` se emerge un flusso operativo nuovo o corretto.

---

## 14. Nota finale per sviluppo

La priorita non e aggiungere subito tutta la UI. La priorita e provare il canale dati Live con una superficie piccola e robusta:

1. login
2. workspace
3. dataset
4. publish `Elements`
5. audit e policy

Solo dopo conviene investire nel wizard Live, selection watch, schedule long-form e report filtering. Questa sequenza riduce il rischio su tenant Microsoft, limiti Power BI, threading Revit e governance dei dati.
