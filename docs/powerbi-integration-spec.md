# RevitCortex × Power BI — Specifica Tecnico-Funzionale

**Versione**: 0.1 (draft)
**Data**: 2026-05-09
**Branch di riferimento**: `feat/powerbi-live-phase0`
**Status**: implementazione parziale; Phase 0 deployata, Phase 1+ da costruire
**Audience**: BIM Manager, sviluppatori plugin Revit, revisori IT, utenti finali

---

## 1. Scopo

Definire requisiti, architettura e contratti funzionali dell'integrazione tra
RevitCortex (plugin Revit + MCP server) e Power BI (Desktop + Service), in
modo da permettere:

- pubblicazione dei dati di modello (elementi, parametri, schedule, selezione)
  verso una dashboard Power BI in modo automatico e ripetibile
- consultazione bidirezionale: dalla dashboard si torna agli elementi Revit
  selezionati o filtrati
- evoluzione incrementale, senza rifacimenti, da una prima versione
  CSV-based (operativa) a un'integrazione live via REST API Power BI

La specifica è scritta per essere indipendente dall'implementazione: una
seconda implementazione (es. in Python o via Dynamo) potrebbe nascere dallo
stesso documento.

---

## 2. Glossario

| Termine | Significato |
|---|---|
| **Plugin** | Addin Revit `RevitCortex.Plugin.dll` |
| **MCP server** | Processo `RevitCortex.Server.exe` che parla stdio JSON-RPC con un client tipo Claude Desktop |
| **Router** | `CortexRouter`: dispatcher in-process del plugin che esegue gli `ICortexTool` |
| **Cortex Switch** | Toggle ribbon che avvia/ferma il listener TCP locale |
| **Push dataset** | Dataset Power BI scrivibile via REST API senza gateway, schema fisso |
| **Workspace** | Container Power BI Service (group), può essere Premium o Pro |
| **Tenant** | Istanza Microsoft Entra (Azure AD) di un'organizzazione |
| **Drillthrough** | Salto da una riga di un report Power BI a una vista di dettaglio (in questo caso, a Revit) |
| **Device-code flow** | Autenticazione OAuth2 in cui l'utente vede un codice e completa il login su un altro device/browser |
| **DPAPI** | Windows Data Protection API, cifra per-utente o per-macchina |
| **DTO** | Data Transfer Object — copia "detached" dei dati Revit, sicura da serializzare e passare fuori dal main thread |

---

## 3. Requisiti

### 3.1 Funzionali (FR)

| ID | Requisito | Priorità |
|---|---|---|
| FR-01 | L'utente seleziona categorie e parametri Revit da un wizard WPF e li esporta come CSV in cartella OneDrive | MVP ✅ |
| FR-02 | L'utente può scegliere come sorgente schedule esistenti del modello | MVP ✅ |
| FR-03 | L'utente può limitare lo scope a "tutto il modello", "vista attiva" o "selezione corrente" | MVP ✅ |
| FR-04 | L'utente può salvare/caricare profili di export riutilizzabili tra progetti | MVP ✅ |
| FR-05 | L'utente può definire alias custom per le colonne CSV e colonne calcolate con formule | MVP ✅ |
| FR-06 | L'utente può riapplicare un CSV (eventualmente modificato in Excel) ai parametri Revit, con anteprima dryRun | MVP ✅ |
| FR-07 | Claude può generare e inviare a Power BI una tabella arbitraria (analisi multi-doc, computi calcolati) | MVP ✅ |
| FR-08 | Da Power BI si può cliccare un elemento e selezionarlo/isolarlo in Revit via protocol handler | MVP ✅ |
| FR-09 | L'utente autentica il proprio account Power BI dal plugin senza credenziali in chiaro | Phase 0 ✅ |
| FR-10 | Il plugin enumera i workspace Power BI accessibili all'utente | Phase 0 ✅ |
| FR-11 | Il plugin crea un push dataset su un workspace Power BI scelto, e accoda righe in modalità append o replace | Phase 1 ⏭ |
| FR-12 | Il plugin pubblica le schedule del modello in formato long alla dashboard live | Phase 1.5 ⏭ |
| FR-13 | La UI offre una modalità "Live" alternativa a CSV, scegliendo workspace, dataset, tabelle | Phase 2 ⏭ |
| FR-14 | Il plugin osserva i cambi di selezione Revit e aggiorna una tabella `Selection` sul dataset live | Phase 3 ⏭ |
| FR-15 | Dal plugin l'utente può aprire un report Power BI già filtrato sulla selezione corrente | Phase 3 ⏭ |
| FR-16 | I parametri custom del modello vengono pubblicati in tabella long-form `ElementParameters` | Phase 4 ⏭ |
| FR-17 | L'export può essere ripetuto automaticamente al salvataggio del modello, con throttle | Phase 4 ⏭ |

### 3.2 Non funzionali (NFR)

| ID | Requisito | Note |
|---|---|---|
| NFR-01 | **Cross-target Revit**: deve compilare e funzionare su Revit 2023 (net48), 2024 (net48), 2025 (net8), 2026 (net8), 2027 (net10) | enforced via configurations multiple |
| NFR-02 | **Niente blocchi UI Revit**: chiamate HTTP non devono girare sul main thread Revit | uso Task.Run + DTO detached |
| NFR-03 | **Niente credenziali in chiaro**: i token OAuth vivono in cache MSAL protetta DPAPI; settings JSON contiene solo ID | |
| NFR-04 | **Read-only mode**: in modalità read-only di RevitCortex tutti i tool che fanno push esterni sono bloccati se non `allowExternalWrites=true` | distinto da "scrittura Revit" |
| NFR-05 | **Audit log**: ogni chiamata a tool PBI viene registrata in `audit.jsonl` con workspace, dataset, rowCount, errorCode; mai token né payload completi | |
| NFR-06 | **Localizzazione**: i nomi categoria/parametro nel modello sono localizzati (IT/EN/FR/DE); le chiavi tecniche (OST_* e BuiltInCategory) sono language-independent | il plugin rileva la locale e si adatta |
| NFR-07 | **Tolleranza ai fallimenti API**: 429/503/network → retry con exponential backoff (Polly o equivalente); throttling è normale operating behavior | Phase 1+ |
| NFR-08 | **Confidenzialità**: i dati di modello non escono mai dalla macchina dell'utente senza un opt-in esplicito (CSV → OneDrive aziendale, oppure push → workspace dichiarato) | |
| NFR-09 | **Performance discovery**: l'apertura del wizard su un modello da 50k elementi deve restare sotto 3 secondi (sampling 200 elementi per categoria) | |
| NFR-10 | **Selection debounce**: cambi di selezione frequenti devono essere coalescenti con finestra 750-1500 ms per evitare API throttling | Phase 3 |

### 3.3 Requisiti utente (UX)

| ID | Requisito |
|---|---|
| UX-01 | Il primo uso completo (autenticazione + scelta workspace + primo push) deve essere completabile in <5 minuti da un BIM coordinator senza supporto |
| UX-02 | Profili di export salvabili e condivisibili tra colleghi via file `.json` (rete aziendale) |
| UX-03 | Stato sempre visibile in basso al wizard: "Sto analizzando…", conteggi, errori con codice |
| UX-04 | Errori mostrati con messaggio human-readable + suggerimento azionabile (es. "Run pbi_check_auth con signIn=true") |
| UX-05 | Color legend nei pannelli parametri: verde=istanza, giallo=tipo, rosso=read-only, marker shared parameter |
| UX-06 | Anteprima delle prime 5 righe del CSV prima dell'export, con header reali |

---

## 4. Architettura

### 4.1 Componenti

```
┌─────────────────────────────────────────────────────────────────────┐
│  REVIT (processo principale)                                         │
│                                                                      │
│  ┌──────────────┐  ┌──────────────────┐  ┌─────────────────────┐   │
│  │ Ribbon UI    │  │ Wizard WPF       │  │  AutoExport Hook    │   │
│  │ "PBI Export" │  │ (3 step + tab    │  │  (DocumentSaved)    │   │
│  │ "PBI Live"   │  │  + dual-pane)    │  │                     │   │
│  └──────┬───────┘  └────────┬─────────┘  └──────────┬──────────┘   │
│         │                   │                        │              │
│         └───────────┬───────┴────────────┬──────────┘              │
│                     ▼                    ▼                          │
│           ┌───────────────────────────────────────┐                 │
│           │      CortexRouter (in-process)        │                 │
│           │  - dispatch ICortexTool by name       │                 │
│           │  - audit log, read-only enforcement   │                 │
│           └────┬──────────────────────────┬───────┘                 │
│                │                          │                          │
│   ┌────────────▼───────┐    ┌─────────────▼──────────────────────┐ │
│   │ PowerBi CSV Tools  │    │ PowerBi Live Tools                 │ │
│   │ - push_to_powerbi  │    │ - pbi_check_auth                   │ │
│   │ - import_*         │    │ - pbi_list_workspaces              │ │
│   │ - push_table_*     │    │ - pbi_create_dataset      (Phase 1)│ │
│   │ - select_*         │    │ - pbi_publish_elements    (Phase 1)│ │
│   │                    │    │ - pbi_publish_schedules (Phase 1.5)│ │
│   │                    │    │ - pbi_publish_selection   (Phase 3)│ │
│   │                    │    │ - open_in_powerbi         (Phase 3)│ │
│   └────────┬───────────┘    └──────────────┬─────────────────────┘ │
│            │                                │                       │
│            ▼                                ▼                       │
│   ┌────────────────┐           ┌────────────────────────────────┐  │
│   │ Filesystem     │           │ PowerBiAuthService             │  │
│   │ - CSV out      │           │  (MSAL device-code, DPAPI)     │  │
│   │ - profiles     │           │ PowerBiServiceClient           │  │
│   │ - sidecar      │           │  (REST + retry/backoff)        │  │
│   │   metadata     │           │ PowerBiElementExporter         │  │
│   │ - debug log    │           │ PowerBiSelectionPublisher      │  │
│   └────────┬───────┘           │ PowerBiSettings                │  │
│            │                   └──────────────┬─────────────────┘  │
└────────────┼──────────────────────────────────┼────────────────────┘
             │                                  │
             ▼                                  ▼ HTTPS
    OneDrive / SharePoint              Power BI Service
         (CSV)                         (REST API + dataset push)
             │                                  │
             │                                  ▼
             └────► Power BI Desktop ◄────► Power BI Report
                    (Get Data Folder /         (visual, drillthrough,
                     direct dataset query)      URL filter)
                                                    │
                                                    ▼ protocol handler
                                            revitcortex://select?ids=...
                                                    │
                                                    └──► Revit selection
```

### 4.2 Modalità di trasporto

L'integrazione opera in **due modalità complementari**, non mutualmente
esclusive:

#### Modalità A — CSV / Filesystem (sempre disponibile)

- L'export scrive uno o più file `.csv` nella cartella `OneDrive\RevitCortex\<NomeProgetto>\`
- Power BI Desktop legge la cartella via Get Data → Folder
- Refresh manuale (utente) o schedulato (PBI Service via gateway OneDrive aziendale)
- Funziona offline, senza account PBI, senza tenant config
- È la modalità default per il primo onboarding

#### Modalità B — Live via REST API (Phase 1+)

- Il plugin si autentica con Microsoft Entra (device-code) e ottiene un token PBI
- Crea/recupera un push dataset su un workspace dell'utente
- Pubblica righe via `POST /datasets/{id}/tables/{name}/rows`
- Latency ~secondi, niente file intermedi, refresh automatico nel report
- Richiede licenza PBI Pro/Premium dell'utente, workspace scrivibile

L'utente sceglie la modalità nel wizard; le due possono coesistere (es. CSV
per archivio + Live per dashboard team).

### 4.3 Schema dati live (Phase 1+)

Lo schema del push dataset è **fisso e versionato**. Lo modifichiamo solo
con bump esplicito di versione del dataset. Approccio long-form per i campi
che variano per progetto.

#### Tabella `Elements` — wide, ~25 colonne stable

Una riga per ogni elemento Revit esportato. Solo campi universali
("highest value parameter set"):

```
ExportRunId          string    GUID di questa esecuzione export
ExportedAtUtc        datetime  ISO 8601
ProjectId            string    Hash stabile del path file modello
DocumentGuid         string    Document.WorksharingCentralGUID o GUID locale
ElementId            int64     ElementId.Value (R24+) o IntegerValue (R23)
UniqueId             string    Element.UniqueId (stabile cross-session)
Category             string    Category.Name localizzato
OstCode              string    BuiltInCategory.ToString() (language-independent)
CategoryType         string    Model | Annotation | Analytical | Internal
FamilyName           string
TypeName             string
Level                string    Parametro Level (qualunque sia in locale)
Workset              string
PhaseCreated         string
PhaseDemolished      string
Name                 string
Mark                 string
Comments             string
Volume               double    metri cubi (project units convertite)
Area                 double    metri quadri
Length               double    metri
BoundingBoxMinX/Y/Z  double    coordinate del bounding box
BoundingBoxMaxX/Y/Z  double
```

#### Tabella `ElementParameters` — long, flessibile (Phase 4)

Una riga per (elemento × parametro). Non vincolata da schema PBI.

```
ExportRunId        string
ElementId          int64
ParameterName      string    Nome localizzato come appare in Revit
ParameterScope     string    Instance | Type
StorageType        string    String | Integer | Double | ElementId
ValueString        string    Valore formattato (con unità se Double)
ValueNumber        double?   Valore numerico raw (in unità progetto) se Double/Integer
Unit               string    es. "m³", "m²", "mm"
```

#### Tabella `Schedules` — long, generica (Phase 1.5)

Una riga per (schedule × riga × colonna). Permette di esportare *qualsiasi*
schedule senza mutare lo schema dataset.

```
ExportRunId        string
ScheduleId         int64
ScheduleName       string
RowIndex           int       (0 = prima riga body, dopo header)
ColumnName         string    Header della colonna nello schedule Revit
ValueString        string
ValueNumber        double?
```

#### Tabella `Selection` — small, replace-mode (Phase 3)

Sempre riflette l'ultima selezione di Revit. Mai cresce indefinitamente.

```
UpdatedAtUtc       datetime  ISO 8601
ProjectId          string
DocumentGuid       string
ElementId          int64
UniqueId           string
SelectionSetId     string?   se la selezione viene da un selection set salvato
```

### 4.4 Threading model

Regole hard:

1. **Lettura Revit API → solo main thread**. Il plugin chiama
   `FilteredElementCollector`, `Element.Parameter`, `Document.GetElement`
   ecc. esclusivamente sul thread UI di Revit.
2. **Snapshot → DTO detached**. Una volta letto, ogni elemento viene
   convertito in un POCO (es. `ElementDto`) che non contiene riferimenti
   ad oggetti Revit. Da quel momento può viaggiare ovunque.
3. **HTTP a PBI → fuori dal main thread**. `Task.Run(() => client.PostRowsAsync(...))`.
4. **UI feedback → marshalled al dispatcher WPF**. Lo status text e i
   progressi vanno aggiornati via `Dispatcher.BeginInvoke`.

Il pattern è:

```
[main thread Revit]
  collect snapshot  ──► detach to DTOs  ──► hand off to background task
                                              │
                                              ▼
                                  [thread pool task]
                                  POST /datasets/.../rows
                                          │
                                          ▼
                                  Dispatcher.BeginInvoke(updateUI)
```

### 4.5 Autenticazione (Phase 0)

**Flow**: OAuth2 device-code (RFC 8628) via MSAL.NET `IPublicClientApplication`.

1. Plugin chiede un device code a Microsoft Entra
2. Mostra all'utente URL (`microsoft.com/devicelogin`) + codice in un TaskDialog Revit
3. Utente apre browser, incolla codice, completa login + MFA + consenso scope
4. MSAL fa polling sul Microsoft endpoint finché ottiene token
5. Token salvato in cache MSAL, serializzato in
   `%LOCALAPPDATA%\.revitcortex\msal_cache.bin`, cifrato DPAPI
   per-utente-per-macchina

**Scopes Power BI delegati**:

| Scope | Usato per |
|---|---|
| `https://analysis.windows.net/powerbi/api/Dataset.ReadWrite.All` | Creare/leggere/scrivere dataset push |
| `https://analysis.windows.net/powerbi/api/Workspace.Read.All` | Enumerare workspaces |
| `https://analysis.windows.net/powerbi/api/Report.Read.All` | Costruire URL filter per `open_in_powerbi` |

**Client ID**: configurabile in settings.

- Default: client ID pubblico Microsoft "Power BI Embedded Sample"
  (`871c010f-5e61-4fb1-83ac-98610a7e9110`), non richiede app registration
  sul tenant
- Override consigliato per produzione: app registration custom sul tenant
  GPA → richiede 1 ticket IT, dà branding/audit migliori

**Tenant**: default `organizations` (qualunque tenant work M365, esclude
account personali). Override possibile con tenant ID specifico.

### 4.6 Settings persistente

File JSON in `~/.revitcortex/powerbi-live.json`:

```json
{
  "ClientId": "871c010f-5e61-4fb1-83ac-98610a7e9110",
  "TenantId": "organizations",
  "DefaultWorkspaceId": null,
  "DefaultDatasetId": null,
  "DefaultReportId": null,
  "AllowExternalWrites": false,
  "SelectionDebounceMs": 1000
}
```

Nessun segreto. I token vivono in cache MSAL DPAPI separato.

### 4.7 Protocol handler `revitcortex://`

Registrato in `HKCU\Software\Classes\revitcortex` (per-utente, no admin).

**Schema URL**:

```
revitcortex://<action>?<param>=<value>&<param>=<value>
```

| Action | Parametri | Effetto |
|---|---|---|
| `select` | `ids=123,456,789` | Selezione + zoom in vista attiva |
| `highlight` | `ids=...` | Alias di select |
| `isolate` | `ids=...` | Isola temporaneamente solo gli elementi |

**Flusso**:

1. Power BI report contiene una misura DAX che genera l'URL
2. Utente clicca → browser apre URL → Windows risolve protocol handler
3. Handler invoca uno script PowerShell helper (`%USERPROFILE%\.revitcortex\protocol\revitcortex-protocol.ps1`)
4. Helper apre socket TCP locale (porta configurata in `~/.revitcortex/settings.json`)
5. Invia JSON-RPC `select_from_powerbi` con la lista ID
6. Plugin riceve, marshalla al main thread, esegue selezione, porta Revit in foreground

---

## 5. Sicurezza

### 5.1 Modello di minaccia

| Minaccia | Mitigazione |
|---|---|
| Token PBI esfiltrato da malware sul PC | Cifratura DPAPI: il token è leggibile solo dall'utente sulla stessa macchina |
| Push accidentale di dati sensibili su un workspace pubblico | `AllowExternalWrites=false` di default; conferma esplicita in UI prima del primo push; il workspace target è sempre visibile |
| Modello modificato silenziosamente da CSV malevolo | `import_from_powerbi` di default è `dryRun=true`; richiede conferma TaskDialog; transazione singola con rollback automatico |
| Sostituzione del plugin con DLL malevola | Manifest `.addin` in `C:\ProgramData\...\Revit\Addins\` richiede admin per modifica; deploy è procedura controllata |
| Esposizione porta TCP plugin verso rete | Server TCP è in ascolto solo su `127.0.0.1`, mai 0.0.0.0; nessuna autenticazione (locale = trusted) |
| Iniezione comandi via protocol handler | URL parsing usa whitelist di action; ID list filtrata a numerici long; nessuna `eval` |

### 5.2 Audit log

Ogni invocazione di tool PBI scrive una riga JSONL in `~/.revitcortex/audit.jsonl`:

```json
{
  "ts": "2026-05-09T13:42:11.123Z",
  "tool": "pbi_publish_elements",
  "user": "luigi.dattilo@gpapartners.com",
  "workspace_id": "abc-123",
  "dataset_id": "def-456",
  "table": "Elements",
  "row_count": 1247,
  "result": "ok",
  "error_code": null,
  "duration_ms": 3421
}
```

**Mai** loggati: access token, refresh token, header `Authorization`, payload
completo delle righe.

### 5.3 Read-only mode

`RevitCortex.Plugin` ha un flag globale `ReadOnlyMode` in `~/.revitcortex/settings.json`.

| Tool | Categoria | Bloccato in read-only? |
|---|---|---|
| `pbi_check_auth` | read-only | No |
| `pbi_list_workspaces` | read-only | No |
| `pbi_list_datasets` | read-only | No (Phase 1) |
| `pbi_create_dataset` | external write | Sì, salvo `AllowExternalWrites=true` |
| `pbi_publish_*` | external write | Sì |
| `pbi_delete_rows` | external write | Sì |
| `push_to_powerbi` | external write | Sì (anche se scrive solo file locali, è data egress) |
| `import_from_powerbi` | internal write | Sì (modifica parametri Revit) |

---

## 6. Modalità d'uso

### 6.1 Workflow CSV (modalità A, già operativa)

```
[Utente] Ribbon → Power BI Export
    │
    ▼
[Wizard Step 1] Sorgente: Categorie+Parametri / Schedule esistenti
                Scope: Tutto modello / Vista attiva / Selezione corrente
                Tab: Model / Annotation / Analytical / Altre
    │
    ▼
[Wizard Step 2 (solo Categorie)] Dual-pane parametri Available/Selected
                                 Filtro + coverage % + color legend
    │
    ▼
[Wizard Step 3] Cartella output, nome file, sovrascrittura,
                AutoExport, Mappa colonne (alias + formula)
                Anteprima CSV (5 righe)
    │
    ▼
[Esporta] → CSV in OneDrive\RevitCortex\<Modello>\
    │
    ▼
[Power BI Desktop] Get Data → Folder → carica + Power Query M
                   Refresh schedulato se cartella su OneDrive sync
```

### 6.2 Workflow Live (modalità B, Phase 1+)

```
[Utente] Ribbon → Power BI Live → Connect
    │
    ▼
[TaskDialog] Codice device + URL → browser → login → conferma
    │
    ▼
[Wizard Live] Scegli workspace
              Scegli/crea dataset
              Scegli tabelle (Elements / Schedules / Selection)
              Append / Replace
    │
    ▼
[Pubblica] → POST batched rows → dataset live su PBI Service
    │
    ▼
[Power BI Report] Visual su dataset live, refresh ad ogni push
                  Drillthrough URL → revitcortex://select?ids=...
```

### 6.3 Workflow round-trip (modifica via CSV)

```
[Utente] Wizard Step 3 → Applica da CSV…
    │
    ▼
[OpenFileDialog] Seleziona file CSV (anche modificato in Excel)
    │
    ▼
[Plugin] dryRun preview: rows, scrivibili, anteprima conteggio
    │
    ▼
[TaskDialog] Conferma scrittura
    │
    ▼
[Transazione Revit] Single tx, write back parametri, rollback su errore
                    Skip silenzioso: ElementId, Category, Family, Type, read-only
    │
    ▼
[Status] N aggiornati / M elementi mancanti / X read-only / Y errori
```

---

## 7. Roadmap

### Phase 0 — Auth + Discovery ✅ deployata, da testare

- `PowerBiAuthService` (MSAL device-code, DPAPI cache)
- `PowerBiSettings` persistente
- `PowerBiServiceClient` (solo `ListWorkspaces`)
- Tool `pbi_check_auth`, `pbi_list_workspaces`
- Success criterion: utente autentica e lista i suoi workspace

### Phase 1 — Push dataset Elements ⏭

- `PowerBiServiceClient`: `CreatePushDataset`, `GetDataset`, `PostRows`, `DeleteRows`, retry/backoff
- Schema `Elements` (sezione 4.3)
- `PowerBiElementExporter`: snapshot Revit → DTOs
- Tool `pbi_publish_elements(workspaceId, datasetName, mode)`
- Idempotenza via `ExportRunId`

### Phase 1.5 — Schedules long-form ⏭

- Schema `Schedules` (sezione 4.3)
- `PowerBiScheduleExporter`
- Tool `pbi_publish_schedules(workspaceId, datasetName, scheduleIds)`

### Phase 2 — Wizard Live ⏭

- Aggiungere modalità "Live" al wizard esistente
- Workspace combo populated da `pbi_list_workspaces`
- Dataset create/select
- Modalità append/replace
- Progress + error display

### Phase 3 — Selection watch + URL filter ⏭

- `PowerBiSelectionPublisher` con debounce 750-1500 ms, coalescing,
  pausa durante modal dialogs
- Tabella `Selection` (replace mode, last selection wins)
- Tool `open_in_powerbi(elementIds)`: costruisce URL
  `https://app.powerbi.com/groups/{ws}/reports/{id}?filter=Elements/ElementId%20in%20(...)`
  e apre il browser
- Bottone ribbon "View in PBI"

### Phase 4 — ElementParameters + AutoExport ⏭

- Schema `ElementParameters` (sezione 4.3)
- Tool `pbi_publish_parameters`
- AutoExport opt-in con throttle (es. min 5 min tra push), status indicator,
  failure non blocca il save

### Phase 5 — PBI → Revit advanced (futuro)

In ordine di priorità tecnica:

1. **Già fatto**: protocol handler drillthrough singolo
2. **Phase 5a**: HTTP callback locale per multi-element filter
3. **Phase 5b** (se ROI): Custom Visual TypeScript per embedded experience

---

## 8. Vincoli e limiti noti

### 8.1 Limiti Power BI push dataset

| Limite | Valore |
|---|---|
| Tabelle per dataset | 75 |
| Colonne per tabella | 75 |
| Righe per `POST rows` call | 10.000 |
| Righe/ora per dataset | 1.000.000 |
| Storico righe in dataset | 5.000.000 (FIFO drop) |
| Schema modificabile dopo creazione | **No** (richiede DELETE dataset + CREATE) |

Conseguenze nella nostra architettura:

- L'`Elements` table sta sotto i 75 colonne (~25 colonne stable)
- Schedules e parametri custom in long form → non sforano mai
- Batching 10k righe nel client
- Schema versioning: se cambiamo lo schema, bump versione + nuovo dataset

### 8.2 Limiti Revit cross-version

| API | R23 (net48) | R24 (net48) | R25+ (net8+) |
|---|---|---|---|
| `ElementId.IntegerValue` | ✓ | deprecated | rimosso |
| `ElementId.Value` (long) | ✗ | ✓ | ✓ |
| `Definition.ParameterGroup` | ✓ | deprecated | rimosso |
| `Definition.GetGroupTypeId()` | ✗ | ✓ | ✓ |
| `record` types | ✗ | ✗ | ✓ |
| `Microsoft.Identity.Client` 4.62 | ✓ (con DPAPI package) | ✓ | ✓ |

Il codice usa `#if REVIT2024_OR_GREATER` / `#if REVIT2025_OR_GREATER` per
le diramazioni di API.

### 8.3 Limiti di consenso tenant

Tenant Microsoft Entra possono bloccare:

- Public client app non registrate sul tenant
- Scope `Dataset.ReadWrite.All` senza admin consent (raro su Pro, comune su Premium dedicato)
- Conditional Access policy (es. solo da workstation managed)

Mitigazione: client ID configurabile; documentazione passo-passo per app
registration custom in caso di tenant restrittivo.

---

## 9. Test e validazione

### 9.1 Test di Phase 0 (questo step)

**Pre-requisiti**:
- Revit 2025 aperto con modello qualunque
- Cortex Switch ON
- Claude Desktop con MCP server `revitcortex` configurato

**Procedura**:

1. **Silent check**: chiamare `pbi_check_auth` con `signIn=false`.
   Atteso: `{"signedIn": false, ...}`. Conferma che il tool è registrato
   e MSAL si carica senza eccezioni.

2. **Device-code flow**: chiamare `pbi_check_auth` con `signIn=true`.
   Atteso: TaskDialog in Revit mostra URL + codice; bottone "Apri il
   browser ora" lancia il default browser; utente completa login con
   account GPA; al ritorno la TaskDialog si chiude; risposta MCP contiene
   `{"signedIn": true, "username": "luigi.dattilo@gpapartners.com",
   "tenantId": "...", "tokenLifetimeMinutes": 60}`.

3. **List workspaces**: chiamare `pbi_list_workspaces`. Atteso: lista di
   workspace M365 visibili all'utente.

4. **Persistenza token**: chiudere Revit, riaprire, ri-eseguire
   `pbi_check_auth` con `signIn=false`. Atteso: `{"signedIn": true}`
   senza nuova UI (token caricato da cache DPAPI e rinnovato silenzioso
   da MSAL).

**Failure modes da gestire**:

| Sintomo | Possibile causa | Azione |
|---|---|---|
| `AADSTS50194 / AADSTS70011` | client ID non valido sul tenant GPA | Override `TenantId` con tenant GPA specifico, oppure registrare app custom |
| `AADSTS65001` | consent richiesto admin | Chiedere a IT consent admin per gli scope PBI |
| `Insufficient privileges` su `groups` | account senza licenza PBI Pro/Premium | Verificare assegnazione licenza |
| Plugin crash su startup | MSAL.dll conflitto con DLL Revit | Downgrade MSAL 4.61 o 4.60; verificare logs Trace |
| Token cache corrupted | DPAPI errore (cambio utente Windows, profilo roaming) | Sign-out + sign-in; reset `%LOCALAPPDATA%\.revitcortex\msal_cache.bin` |

### 9.2 Test di Phase 1 (futuro)

**Procedura**:

1. Phase 0 OK
2. `pbi_publish_elements(workspaceId=…, datasetName="Test1", mode="create")`
   con 100 elementi del modello
3. Aprire Power BI Desktop, connettersi al dataset, verificare 100 righe
4. Re-eseguire con `mode="append"` → atteso 200 righe (o 100 se idempotenza per `ExportRunId`)
5. Re-eseguire con `mode="replace"` → atteso 100 righe (le nuove)
6. Verificare audit log

### 9.3 Test cross-target

Per ogni release: build R23+R24 (net48) + R25+R26 (net8) + R27 (net10). Zero
errori, warnings tollerati solo se preesistenti.

---

## 10. Decisioni tecniche aperte

| ID | Decisione | Default | Da rivedere quando |
|---|---|---|---|
| D-01 | Client ID well-known vs app registration custom GPA | Well-known | Quando si passa a deployment di team (>3 utenti) |
| D-02 | Single workspace per tutti i progetti vs workspace-per-progetto | Single | Se i progetti sono >20 |
| D-03 | Push dataset vs streaming dataset | Push | Se latency <1 sec diventa requisito |
| D-04 | XMLA endpoint per schema flessibile | No (push) | Se la rigidità schema diventa problema |
| D-05 | Schema versioning: in-row column vs separate metadata table | `_schema_version` colonna | Se diventa scomodo da gestire |
| D-06 | Custom Visual TypeScript per filtro live PBI→Revit | No (Phase 5) | Se l'esperienza protocol handler resta insufficiente |
| D-07 | AutoExport throttle window | 5 min | Su feedback utente reale |

---

## 11. Documenti correlati

- `docs/powerbi-live-architecture-review.md` — analisi tecnico-funzionale indipendente
- `docs/powerbi-live-handoff.md` — stato/ripresa lavoro del branch
- `WORKFLOWS.md` (sezione PBI) — procedure operative
- `tool-schemas.txt` — signature compatta dei tool MCP
- `templates/powerbi/` — starter pack PBI Desktop

---

## 12. Cronologia revisioni

| Versione | Data | Cambiamenti | Autore |
|---|---|---|---|
| 0.1 | 2026-05-09 | Prima stesura: Phase 0 implementata, Phase 1+ da costruire | Claude + Luigi |
