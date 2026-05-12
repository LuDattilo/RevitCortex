# RevitCortex → Power BI: Spec architetturale dei push

**Versione**: 0.1 draft (2026-05-12)
**Autore**: Sessione di analisi con utente luigi.dattilo
**Stato**: aperto a iterazione

> Questo documento fissa lo stato attuale delle due pipeline RevitCortex → Power BI, ne confronta le filosofie, e descrive l'architettura concordata per la prossima iterazione (refresh on-demand triggerato da Cortex su Premium). È pensato per essere letto, contestato e modificato — non è un piano finale.

---

## 1. Problem statement

RevitCortex deve portare dati dal modello Revit a una dashboard Power BI che sia:

1. **Aggiornata** quando il modello cambia (utente esporta → dashboard si aggiorna)
2. **Condivisibile** con team / cliente senza richiedere a ciascuno installazioni o licenze enterprise
3. **Ricca** abbastanza da rappresentare schedule custom, parametri ad-hoc, relazioni dimensionali
4. **Semplice** da configurare per un utente PBI di livello medio
5. **Interattiva**: l'utente deve poter usare la dashboard PBI come "control center" → cliccare un elemento in PBI e vederlo selezionato/colorato in Revit live

I primi 4 punti sono soddisfatti da **due pipeline dati** (CSV vs Live REST). Il punto 5 è soddisfatto da un **layer di integrazione interattiva** separato (custom PBI Visual + HTTP listener in Revit). Spesso confondiamo i tre concetti (incluso il nome `PushToPowerBi` che suggerisce "spinta diretta al cloud" mentre in realtà è CSV locale). Questa spec chiarisce.

---

## 2. Stato attuale: due pipeline indipendenti

### 2.1 Pipeline A — "CSV Export" (`PushToPowerBi`)

```
Revit  ──┐
         ├──► CortexRouter ──► PushToPowerBiTool ──► [CSV files in cartella locale/OneDrive]
         │                                            + manifest.json (last_refresh.json)
Pannello UI
PowerBiExportWindow
```

**Caratteristiche**:
- Scrive **file CSV** (un file per export run: elementi, schedule)
- Schema **flessibile per progetto**: i parametri sono scelti nel pannello
- File **locali** (eventualmente in OneDrive per sync)
- Loading in PBI Desktop: query Power Query M (`dashboard-template.pq` v3.0 universale)
- Refresh: manuale in PBI Desktop, oppure schedulato in PBI Service post-publish

**Use case**: dashboard analitiche ricche per progetto specifico, costruite/iterate in PBI Desktop.

**Code map**:
- `src/RevitCortex.Tools/Elements/PushToPowerBiTool.cs` (~700 LOC, 2 mode: element + schedule)
- `src/RevitCortex.Plugin/PowerBi/PowerBiExportWindow.xaml(.cs)` (wizard 3-pane)
- Output: cartella scelta dall'utente, default `OneDrive/RevitCortex/<DocName>/`

### 2.2 Pipeline B — "Live Push" (`PowerBiLive`)

```
Revit  ──┐
         ├──► CortexRouter ──► PbiPublishElementsTool ──┐
         │                                                ├──► REST API ──► PBI Service ──► Push Dataset
         │                     PbiPublishSchedulesTool  ─┘                     "RevitCortex Live - {Project} - v1.1"
Pannello UI                    PbiPublishSelectionTool ─┘
PowerBiExportWindow
(stesso panel ma altri bottoni)
```

**Caratteristiche**:
- Pusha **righe** via REST API direttamente sul **dataset cloud**
- Schema **fisso v1.1**: 5 tabelle (`Metadata`, `Elements`, `Schedules`, `ElementParameters`, `Selection`)
- Tabelle in **formato EAV** (entity-attribute-value): un parametro/cella schedule = 1 riga
- Real-time vero: dopo Push, il dataset è già aggiornato sul workspace cloud
- Loading: l'utente costruisce visual direttamente in PBI Service (browser) o tramite Live Connection in PBI Desktop

**Use case**: dashboard operative/condivise, telemetria modello, dashboard "always fresh" senza setup gateway.

**Code map**:
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiServiceClient.cs` (REST client con auth, retry, throttling)
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiDatasetSchema.cs` (schema fisso v1.1)
- `src/RevitCortex.Plugin/PowerBiLive/Tools/Pbi*Tool.cs` (3 tools)
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiAuthService.cs` (MSAL OAuth flow)

### 2.3 Sintesi visiva: due pipeline + un layer interattivo

```
   Pipeline A: CSV               Pipeline B: Live REST       Layer C: Custom Visual
   ───────────────               ─────────────────────       ──────────────────────
   Revit                         Revit                       PBI Desktop
     │                             │                          (custom visual)
     ▼                             ▼                              │
   CSV in cartella               POST rows ───► PBI Service       │ user click
     │                                              │             ▼
     │ (manuale / sync OneDrive)                   │       POST localhost:27016
     ▼                                              ▼             │
   PBI Desktop                   Push Dataset cloud               ▼
   (open .pbix, Refresh)         (sempre fresh)              Revit listener
     │                                              │             │
     ▼                                              ▼             ▼
   PBI Service                   Report PBI Service          Revit API
   (publish, gateway refresh)    (utenti vedono già)         (select/color/view)
```

Sono **due pipeline dati alternative + un layer interattivo ortogonale**:
- **Pipeline A**: dati Revit → CSV → PBI (workflow analitico)
- **Pipeline B**: dati Revit → Push Dataset cloud (workflow operativo)
- **Layer C**: PBI Desktop → Revit (interazione bidirezionale, LOCALE)

Un dato esportato in CSV NON finisce nel Push Dataset. Un dato pushato sul Push Dataset NON è disponibile come CSV. **Il custom visual è compatibile con ENTRAMBE le pipeline** — usa solo ElementId/UniqueId dei dati, non importa come sono arrivati in PBI.

### 2.4 Layer C — RevitCortex Selection Visual (anticipazione, dettagli in §4.5)

| Cosa è | Custom PBI Visual `.pbiviz` (React+TS) che permette selezione/colorazione/isolamento elementi Revit dal `.pbix` |
|---|---|
| **Dove vive** | Nel `.pbix` come visual del Report layer |
| **Come comunica** | HTTP POST a `localhost:27016` → `PbiSelectHttpListener` in plugin Revit |
| **Funziona** | Solo se PBI Desktop e Revit sono sulla stessa macchina (LOCALHOST) |
| **NON funziona** | In PBI Service cloud (browser non può chiamare localhost) |
| **Versione** | 1.0.0.10 (in `powerbi-visual/` del repo) |

---

## 3. Confronto delle pipeline

### 3.1 Tabella comparativa

| Dimensione | Pipeline A — CSV | Pipeline B — Live REST |
|---|---|---|
| **Sorgente dati dopo l'export** | File CSV locali (o OneDrive) | Dataset cloud su workspace |
| **Schema** | Wide, flessibile, per progetto | EAV, fisso v1.1, universale |
| **Door_Schedule come tabella** | Sì, propria | No, è "virtuale" dentro tabella `Schedules` |
| **Parametri custom** | Colonne dirette nel CSV | Riga in `ElementParameters` |
| **Relazioni esplicite (star schema)** | Sì (DimElement → 7 fact, DimDocument → DimElement) | No (PBI Push Datasets non le supportano) |
| **DAX semplici** | Sì (`Door_Schedule[Width]`) | Richiede `CALCULATE + FILTER` su EAV |
| **Real-time aggiornamento** | No (refresh manuale o schedule) | Sì (push immediato) |
| **Condivisione cloud team** | Richiede publish a workspace | Nativa (è già in workspace) |
| **Costo PBI** | Pro per chi consuma | Pro per chi consuma + workspace |
| **Limiti dataset** | Nessuno (dipende da PBI Desktop) | Push Datasets: 5M righe storiche, schema rigido |
| **Refresh schedulato cloud** | Sì (gateway + OneDrive) | Non applicabile (è già fresh) |
| **Refresh on-demand cloud** | Sì via API (TriggerRefresh) | Non applicabile |
| **Curva di apprendimento PBI utente** | Bassa | Alta (DAX su EAV) |

### 3.2 Quando usare quale (heuristic)

**Pipeline A (CSV)** è la scelta giusta quando:
- L'utente PBI è di livello medio o quello che consuma è non-tecnico
- Servono dashboard analitiche ricche con visualizzazioni complesse
- Le schedule custom sono parte essenziale (Door_Schedule, Area_Schedule, ecc.)
- Il refresh "ogni ora" o "ogni giorno" è sufficiente
- Si vuole pieno controllo del modello (relazioni, misure, calculated columns)

**Pipeline B (Live)** è la scelta giusta quando:
- Serve dashboard real-time (es. design review live, monitor operativo)
- L'utente PBI è avanzato e conosce DAX su modelli EAV
- I dati cambiano frequentemente (decine di push al giorno)
- Non si vuole gestire `.pbix` locali e gateway
- Si vogliono dashboard semplici "telemetria" (count, sum, slicer base)

**Entrambe insieme** ha senso quando:
- La pipeline B alimenta una dashboard "operativa" sempre fresca
- La pipeline A genera periodicamente snapshot per analisi profonde
- Sono separate per design — non si interferiscono

---

## 4. Architettura concordata per la prossima iterazione

### 4.1 Decisione: Opzione C — CSV + refresh on-demand triggered

Tra le opzioni valutate:

| Opzione | Descrizione | Decisione |
|---|---|---|
| **A (Composite Model)** | `.pbit` con Live Connection al Push Dataset + DAX calc tables che pivotano EAV | Rinviata. Complessa, richiede tenant setting "Allow DirectQuery to PBI datasets". Ritorna in iterazione successiva. |
| **B (Dataflow intermedio)** | Dataflow PBI come layer di pivot tra Push Dataset e `.pbit` | Rinviata. Richiede setup Dataflow + idealmente Premium-only features. Iterazione successiva. |
| **C (CSV + refresh trigger)** | CSV via OneDrive + `.pbix` Import + Cortex chiama API per triggerare refresh dopo ogni push | **SCELTA**. Pragmatica, fattibile in 1-2 sessioni, riusa Pipeline A esistente, Premium supporta 48 refresh/giorno → on-demand di fatto. |
| **D (Strada 3 originale)** | `.pbit` Import dal Live + Power Query pivot | **TECNICAMENTE IMPOSSIBILE**. PBI dataset connector non offre modalità Import per dataset cloud. |

### 4.2 Flow utente (Opzione C, target)

```
1. SETUP UNA TANTUM (per ogni progetto Revit):
   a. Utente apre PBI Desktop
   b. Apre `dashboard-template.pq` v3.0 universale
   c. Imposta FolderPath = "OneDrive/RevitCortex/<Progetto>/"
   d. Materializza tabelle (Add as new query)
   e. Crea relazioni star schema (8 click)
   f. Salva come <progetto>.pbix
   g. File → Publish → workspace Premium
   h. Annota il datasetId dal portal PBI

2. CONFIGURAZIONE BINDING (una volta, nel pannello Cortex):
   - Apri pannello PBI Export
   - Nuova sezione "Cloud Refresh":
       Workspace: [dropdown popolato via REST]
       Dataset:   [dropdown popolato via REST]
       ☐ Trigger refresh dopo export
   - Cortex memorizza per (EpisodeId) la coppia (workspaceId, datasetId)

3. OGNI EXPORT (ricorrente):
   - Utente in Revit clicca "Export & Push" nel pannello
   - Cortex:
       a. scrive CSV in OneDrive
       b. (se binding configurato) chiama POST /datasets/{id}/refreshes
   - PBI Service:
       a. coda refresh
       b. legge CSV da OneDrive (auto-sync)
       c. ricomputa dataset
       d. aggiorna report cloud
   - Team su app.powerbi.com vede dashboard aggiornata in ~30-60s
```

### 4.3 Stato implementazione (al 2026-05-12)

**Done**:
- ✅ `dashboard-template.pq` v3.0 universale (auto-discover CSV)
- ✅ `dashboard-template.md` aggiornata
- ✅ `PowerBiServiceClient.TriggerRefreshAsync()` + `GetRefreshStatusAsync()` + `GetLastRefreshStatusAsync()`
- ✅ `PbiTriggerRefreshTool` (Cortex-callable) auto-registrato
- ✅ Build verificato R23/R24/R25/R26 — 0 errori

**TODO (next session)**:
- ⏸ UI checkbox "Trigger refresh dopo export" + dropdown workspace/dataset nel pannello `PowerBiExportWindow`
- ⏸ Storage binding per-document in `PowerBiSettings` (estendere `ProjectBinding` o nuovo `CsvRefreshBinding`)
- ⏸ Auto-resolve workspace+dataset via REST list, con cache
- ⏸ Wire dell'invocazione tool dopo CSV export riuscito
- ⏸ Deploy multi-anno (R23/R24/R25/R26)
- ⏸ Test end-to-end su Snowdon
- ⏸ Commit strutturato

**FUTURE**:
- 🔮 Generazione automatica `.pbit` da Cortex (reverse-engineering format binario)
- 🔮 Opzione A (Composite Model) come template alternativo
- 🔮 Opzione B (Dataflow) per portfolio multi-progetto

---

## 4.4 Layer Semantic Model vs Report — dove vivono i visual

Un `.pbix` (e per estensione un workspace pubblicato) è composto da **due layer ortogonali**:

| Layer | Contenuto | Cosa lo tocca |
|---|---|---|
| **Semantic Model** (dataset, dopo publish) | Query M, tabelle, relazioni, misure DAX, dati materializzati | Refresh (`pbi_trigger_refresh`), modifica `.pq`, re-publish |
| **Report** (report, dopo publish) | Pagine, visual, slicer, filtri, formattazione, bookmark | Edit in PBI Desktop o PBI Service browser |

### Pubblicazione: 1 `.pbix` → 2 entità separate

```
.pbix file (locale)                    Workspace PBI Service (cloud)
─────────────────────                  ──────────────────────────
[Semantic Model]    ─── Publish ───►   "Snowdon" dataset
[Report]            ─── Publish ───►   "Snowdon" report

```

Dopo publish:
- Il **dataset** ha relazione 1:N con i **report** (un dataset può alimentare più report, anche su workspace diversi)
- Il **refresh** agisce solo sul dataset; i visual nei report leggono il dataset aggiornato automaticamente

### Lifecycle visual

| Evento | Effetto su visual |
|---|---|
| Cortex `pbi_trigger_refresh` | Dataset aggiornato; visual leggono nuovo valore. Design intatto. |
| Refresh fallisce | Dataset/visual restano sui dati precedenti |
| Re-publish `.pbix` | Report cloud **sovrascritto** col locale. Eventuali edit in PBI Service vanno persi. |
| Edit report in PBI Service browser | Modifica solo il cloud. `.pbix` locale resta indietro → drift. |
| Aggiunta misura nel `.pbix` locale | Visibile solo dopo re-publish |

### Pratica consigliata

1. **`.pbix` locale = source of truth** per struttura dati + visual base. Versionabile in git insieme al `.pq` template.
2. **Edit in PBI Service** consentito solo per: filtri utente salvati, "Pin to dashboard", bookmark personali. NON per modifiche strutturali.
3. **Re-publish** sovrascrive il cloud — se il team ha fatto edit cloud, comunicarli prima.

### Implicazione per Opzione C

L'invocazione `pbi_trigger_refresh` da Cortex agisce **solo sul Semantic Model lato cloud**. I visual nel Report cloud restano intatti e si aggiornano col nuovo dato. **Il design del visual creato in PBI Desktop sopravvive a qualsiasi numero di refresh**.

Visual creati come parte del setup iniziale (es. Card "Doors Count", slicer "Category", Table "Door_Schedule") sono persistiti nel `.pbix` e replicati nel report cloud al primo publish. Da lì in poi, ogni Cortex push + refresh aggiorna i loro valori senza intervento.

## 4.5 Cross-App Selection: il custom visual RevitCortex Selection

Oltre ai visual standard (Card, Table, Slicer), RevitCortex include un **custom PBI Visual** chiamato **`revitcortex-selection-visual`** (package `.pbiviz` versione 1.0.0.10, React + TypeScript) che permette **interazione bidirezionale PBI Desktop ↔ Revit live**.

### Cosa fa

Il visual è installabile nel `.pbix` come qualsiasi altro visual ("Get more visuals" → Import a visual file from your computer → `revitcortex-selection-visual.pbiviz`). Una volta inserito nel report, accetta come Fields:
- `ElementId` (Int64, obbligatorio)
- `UniqueId` (text, opzionale ma raccomandato per multi-document)
- `Category`, `Name`, ecc. per display

Quando l'utente clicca/seleziona righe nel visual, invia richieste HTTP POST a **`localhost:27016`** dove `PbiSelectHttpListener` (parte del plugin Cortex in Revit) le riceve e marshalla sulla UI thread di Revit.

### Endpoint supportati (versione corrente)

| Endpoint | Body | Azione in Revit |
|---|---|---|
| `POST /pbi-select` | `{ elementIds, action: "select"\|"isolate" }` | Seleziona o isola elementi nella vista attiva |
| `POST /pbi-color` | `{ items: [{id, hex}, ...] }` | Applica color override (es. heatmap) |
| `POST /pbi-reset-overrides` | `{}` | Pulisce tutti gli override grafici |
| `POST /pbi-create-view` | `{ elementIds, viewName? }` | Crea una nuova view contenente gli elementi |

Tutti gli endpoint sono **idempotent + thread-safe** (marshalling via ExternalEventHandler). Supportano CORS preflight (OPTIONS).

### Rich dispatch (v1.0.0.10+)

I callback "Rich" accettano anche `UniqueIds` (lista di GUID Revit) + `DocumentTitle`, abilitando:
- **Multi-document disambiguation**: se hai 2 modelli Revit aperti, il visual può specificare il target tramite `documentTitle`
- **UniqueId-first lookup**: l'ElementId non è globally unique cross-document; UniqueId sì
- **Strutturati error codes**: es. `wrong_document` se l'utente è sul progetto sbagliato

### Diagramma flow

```
┌───────────────────────────────┐         ┌─────────────────────────────┐
│   PBI Desktop (locale)        │         │   Revit (locale, stesso PC) │
│                               │         │                             │
│   ┌─────────────────────────┐ │         │   ┌───────────────────────┐ │
│   │ revitcortex-selection-  │ │         │   │ PbiSelectHttpListener │ │
│   │ visual (in report)      │ │         │   │ porta 27016           │ │
│   │                         │ │         │   │                       │ │
│   │ User clicca riga ────────┼─POST───►│   │ ┌───────────────────┐ │ │
│   │                         │ │         │   │ │ External Event    │ │ │
│   └─────────────────────────┘ │         │   │ │ Handler (UI thread)│ │ │
│                               │         │   │ └────────┬──────────┘ │ │
└───────────────────────────────┘         │            │              │ │
                                          │   ┌────────▼──────────┐  │ │
                                          │   │ Revit API:        │  │ │
                                          │   │  uidoc.Selection. │  │ │
                                          │   │  SetElementIds    │  │ │
                                          │   └───────────────────┘  │ │
                                          └─────────────────────────────┘
```

### Vincoli architetturali importanti

1. **Funziona SOLO in localhost**: il visual chiama `http://localhost:27016`. Non è raggiungibile dal cloud — Revit deve essere sulla **stessa macchina** dove gira PBI Desktop.
2. **NON funziona in PBI Service** (cloud workspace nel browser): non c'è Revit "dietro" al browser. Il visual stesso si installa ma le chiamate HTTP a localhost falliscono per CORS/policy del browser.
3. **Una sola istanza Revit alla volta** può "ascoltare" sulla porta 27016. Aprire un secondo Revit → log warning, la seconda istanza non riceve eventi (gestito con grace).
4. **Cortex plugin deve essere caricato** in Revit. Il listener parte all'avvio di Cortex.

### Posizionamento nelle pipeline

Il custom visual è un **terzo layer di integrazione**, ortogonale a Pipeline A (CSV) e Pipeline B (Live REST):

| Layer | Funzione | Direzione |
|---|---|---|
| Pipeline A — CSV | Dati Revit → file → PBI | Revit → PBI |
| Pipeline B — Live REST | Dati Revit → Push Dataset cloud | Revit → PBI (cloud) |
| **Layer C — Custom Visual** | **Selezione PBI → azione Revit** | **PBI → Revit (locale)** |

Il visual è **complementare ai dati**: usa i dati esportati (da A o B) per popolare l'UI del visual stesso, poi triggera azioni live in Revit basate sulle selezioni dell'utente.

### Use case tipico

1. Pipeline A esporta CSV `Door_Schedule.csv` con tutti gli ElementId/UniqueId delle porte
2. In PBI Desktop, l'utente aggiunge il `revitcortex-selection-visual` al report e lo lega a `Door_Schedule[ElementId]` + `Door_Schedule[UniqueId]`
3. L'utente filtra il visual (es. solo porte con `Fire Rating Label = "60 min"`)
4. L'utente clicca **"Select in Revit"** nel visual
5. Revit (sulla stessa macchina) seleziona/isola/colora quelle porte nella view attiva
6. L'utente può ora misurarle, modificarle, esportare di nuovo

### Implicazione per la spec architetturale

- **Pipeline A + Layer C** = workflow analitico locale: utente lavora in PBI Desktop, naviga dati, manipola Revit
- **Pipeline B + Layer C** = workflow operativo: dati real-time + interazione real-time (entrambi locali)
- **Cloud workspace + Layer C** = ❌ non possibile: il browser non parla a localhost
- **`.pbit` generato da Cortex** dovrà includere il custom visual pre-configurato come opzione (sezione "FUTURE")

### Code map (Layer C)

- `powerbi-visual/` (root del progetto pbiviz, npm/React/TypeScript)
- `src/RevitCortex.Plugin/PowerBiLive/PbiSelectHttpListener.cs` (HTTP listener, ~600 LOC)
- `src/RevitCortex.Plugin/PowerBiLive/PbiActionEventHandler.cs` (ExternalEvent marshalling to Revit UI thread)
- `src/RevitCortex.Plugin/PowerBiLive/PbiOverrideRegistry.cs` (state delle color overrides per reset)
- Settings: `PowerBiSettings.SelectionDebounceMs` (default 1000ms — quanto aspettare prima di mandare update)

### Aperti / da decidere (Layer C)

- **Distribuzione visual**: oggi `.pbiviz` è artigianale (utente importa file). Pubblicazione su AppSource (Microsoft Marketplace) richiede certificazione. **Da valutare**.
- **Auth/security**: la porta 27016 è aperta a localhost senza auth. Su una macchina multi-utente, qualsiasi processo locale può triggerare azioni Revit. **Mitigazione**: token shared in registry / file user-profile?
- **Refresh visual dopo refresh dataset**: il custom visual non si "rinfresca" automaticamente quando il dataset si aggiorna — re-binding manuale al campo. Da migliorare.

---

## 5. Filosofie di organizzazione `.pbix` ↔ progetti

(Decisione aperta — da consolidare quando wire-iamo la UI.)

### 5.1 Filosofia A: 1 `.pbix` per progetto Revit

- Ogni progetto Revit → cartella CSV propria → `.pbix` proprio → dataset cloud proprio
- **Pro**: isolamento, performance, permessi granulari, schedule possono variare
- **Contro**: setup 15 min per nuovo progetto, no cross-project analysis nativa
- **Quando**: schedule heterogenee per tipo di progetto (architettura, strutture, MEP)

### 5.2 Filosofia B: 1 `.pbix` portfolio (multi-tenant)

- Un solo `.pbix` carica tutti i progetti da una root folder, slicer su `DocumentTitle` per filtrare
- **Pro**: single setup, cross-project analysis nativa
- **Contro**: schema deve essere omogeneo, refresh più lento, no permessi granulari (serve RLS)
- **Quando**: progetti dello stesso tipo con schedule simili

### 5.3 Filosofia C: ibrida per famiglia

- 1 `.pbix` per disciplina/famiglia (architettura, strutture, MEP)
- **Pro**: bilanciamento
- **Contro**: serve definire le famiglie

### 5.4 Raccomandazione preliminare per GPA

Senza altre info, **Filosofia A**: progetti BIM tipicamente eterogenei (schedule diverse per tipo), clienti vogliono dashboard propria, refresh per progetto è naturale. Il template universale `.pq` v3.0 rende il setup leggero (15 min per nuovo progetto). Cross-project analysis si fa eventualmente come SECONDO `.pbix` portfolio costruito sopra Dataflow.

---

## 6. Glossario terminologico

| Termine | Significato in questo contesto |
|---|---|
| **Push to Power BI** | (Nome storico, fuorviante.) Pipeline A: scrittura di CSV in cartella locale/OneDrive. NON è push REST al cloud. |
| **PowerBi Live** | Pipeline B: push REST diretto al dataset cloud, formato EAV v1.1. |
| **Push Dataset** | Tipo di dataset cloud PBI alimentato via REST API. Limiti: 5M righe storiche, no relazioni esplicite, schema rigido. |
| **Import Dataset** | Tipo di dataset cloud PBI che copia i dati dalla sorgente (es. CSV in OneDrive) al momento del refresh. Schema flessibile, relazioni libere. |
| **Live Connection** | Modalità di connessione di PBI Desktop a un dataset cloud: read-only, no Power Query, no modifiche al modello remoto. |
| **Composite Model** | Estensione di Live Connection: aggiungi tabelle/misure LOCALI al dataset remoto senza modificarlo. Richiede tenant setting abilitato. |
| **Dataflow** | Layer di trasformazione PBI hostato in workspace: pivot/clean/join eseguiti in cloud, output consumato da `.pbix` come sorgente. |
| **Premium / PPU** | Licenze PBI che abilitano: 48 refresh/giorno, Enhanced Refresh API, XMLA endpoint, Dataflow computed entities. |
| **`.pbix`** | File contenitore: model dati + queries + visual + layout. Self-contained. |
| **`.pbit`** | Template: come `.pbix` ma SENZA dati (solo schema/queries/visual). All'apertura chiede i parametri. |
| **DataMashup** | Sezione binaria dentro `.pbix`/`.pbit` che racchiude il codice Power Query M. Formato proprietario Microsoft (semi-documentato). |
| **EAV** | Entity-Attribute-Value: pattern dove ogni attributo è una riga invece di una colonna. Schema universale, query DAX più complesse. |
| **EpisodeId** | GUID immutabile del documento Revit, estratto dai primi 36 char di `UniqueId`. Chiave doc cross-progetto. |
| **UniqueId** | GUID stabile dell'elemento Revit (sopravvive a edit/purge, non a delete+recreate). Chiave element-level. |
| **Custom Visual / `.pbiviz`** | Visual personalizzato distribuito come package zip con manifest + bundle JS. Si installa in PBI Desktop via "Get more visuals → Import from file". |
| **RevitCortex Selection Visual** | Il nostro `.pbiviz` (in `powerbi-visual/`, v1.0.0.10) che invia POST a `localhost:27016` per pilotare Revit dal report. |
| **Layer C** | Layer di integrazione bidirezionale PBI→Revit (custom visual + HTTP listener). LOCAL-ONLY: non funziona in PBI Service cloud. |
| **`PbiSelectHttpListener`** | HTTP listener nel plugin Cortex (porta 27016) che accetta i comandi dal custom visual e li esegue in Revit via ExternalEventHandler. |
| **Rich dispatch** | Modalità del listener (v1.0.0.10+) che accetta `UniqueIds + DocumentTitle` per disambiguare in scenari multi-modello. |

---

## 7. Aperti / da decidere

1. **Filosofia `.pbix`** (sezione 5): A/B/C? Risposta utente preliminare = A. Da confermare.
2. **Storage binding refresh**: estendere `ProjectBinding` o creare `CsvRefreshBinding`? Probabilmente nuovo tipo per chiarezza.
3. **Auto-detect dataset**: dopo che l'utente pubblica `.pbix`, può Cortex auto-trovare il datasetId via REST (filter by name)? Sì. Da implementare.
4. **Conflitto Push Dataset vs Refresh Dataset**: posso esistere entrambi per lo stesso progetto Revit (Live operativo + CSV analitico)? Sì. Coesistono nel pannello come opzioni indipendenti.
5. **Trigger automatic vs manuale**: la checkbox "Trigger refresh dopo export" è opt-in di default? Probabilmente sì (off di default).
6. **Quota Premium 48/giorno**: se l'utente esporta più di 48 volte, cosa fa Cortex? Errore esplicito + suggerimento "attendi rotation della finestra di 24h" o "aumenta a PPU".
7. **`.pbit` generation**: una volta wire-ata Opzione C, ha ancora senso investire nell'iterazione 2 (generazione `.pbit`)? Forse no, se Filosofia A funziona bene con setup 15 min. Da rivalutare.

---

## 8. Riferimenti

- `dashboard-template.pq` v3.0 (in `OneDrive/RevitCortex/`)
- `dashboard-template.md` (in `OneDrive/RevitCortex/`)
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiServiceClient.cs`
- `src/RevitCortex.Plugin/PowerBiLive/Tools/PbiTriggerRefreshTool.cs`
- `src/RevitCortex.Plugin/PowerBiLive/PowerBiDatasetSchema.cs` (schema v1.1 fisso)
- `src/RevitCortex.Tools/Elements/PushToPowerBiTool.cs` (Pipeline A, manifest v2.0)
- `docs/powerbi-integration-spec.md` (esistente, livello superiore)
- `docs/powerbi-export-settings-panel.md` (UI pannello esistente)
- `docs/powerbi-live-architecture-review.md` (review architettonico Pipeline B)
- Memoria sessione: `~/.claude/projects/.../memory/project_dashboard_template_v3.md`

---

## 9. Diario decisionale

| Data | Decisione | Motivazione |
|---|---|---|
| 2026-05-12 | Opzione C scelta | Pragmatica, fattibile rapidamente, riusa Pipeline A. Premium dà 48 refresh/giorno = on-demand. |
| 2026-05-12 | Strada 3 originale scartata | PBI dataset connector non offre Import mode → tecnicamente impossibile. |
| 2026-05-12 | `.pq` reso universale (v3.0) | Eliminare l'hardcoding Snowdon-specific per supportare Filosofia A senza editing M. |
| 2026-05-12 | `.pbit` generation rinviata | Reverse-engineering formato binario è 8-12h focused work. Opzione C copre il valore principale senza questo investimento. |
| 2026-05-12 | Composite Model (Opzione A) rinviato | Possibile in iterazione 2 se Filosofia A si rivela limitante (es. utente vuole real-time vero). |
