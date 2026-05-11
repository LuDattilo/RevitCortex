# Power BI Live — Handoff per ripresa lunedì 2026-05-12

## Stato corrente del branch `feat/powerbi-live-phase0`

Tre commit atomici sopra `main`:

1. **`feat(powerbi)`** — Export wizard CSV-based (Phase 0..4 della prima iterazione)
   - `push_to_powerbi` con scope, mapping editor, schedules
   - `import_from_powerbi` round-trip CSV → Revit
   - `push_table_to_powerbi` per tabelle Claude-generate
   - `select_from_powerbi` con protocol handler `revitcortex://`
   - UI WPF a 3 step con tab Model/Annotation/Analytical
2. **`feat(powerbi-live): phase 0`** — Auth MSAL device-code + workspace discovery
   - `pbi_check_auth`, `pbi_list_workspaces`
   - `PowerBiAuthService`, `PowerBiServiceClient`, `PowerBiSettings`
   - DPAPI token cache
3. **`docs`** — Documentazione, tool-schemas, templates, architecture review

## Cosa funziona oggi (deployato e testabile)

- Tutto il wizard CSV-based dal ribbon Revit ("Power BI Export")
- Drillthrough PBI → Revit via `revitcortex://`
- Round-trip CSV → Revit (con dryRun + commit)
- Phase 0 PBI Live: device-code + list workspaces validati con account GPA

## Cosa manca da testare prima di Phase 1

- [x] **Test Phase 0 end-to-end**: validato 2026-05-11 in Revit 2025.
      `pbi_check_auth(signIn=true)` ritorna in ~20 ms senza freeze Revit,
      device-code completato con account `luigi.dattilo-co@gpapartners.com`,
      token letto da cache MSAL/DPAPI, `pbi_list_workspaces` OK.
- [x] **Verificare consent**: il client ID well-known fallisce sul tenant GPA
      con AADSTS65002. Risolto creando app registration custom GPA e
      configurando `ClientId`/`TenantId` in `~/.revitcortex/powerbi-live.json`.

## Configurazione GPA validata

`~/.revitcortex/powerbi-live.json`:

```json
{
  "ClientId": "05d231e9-d720-4c54-8ecd-93a85dbef40b",
  "TenantId": "53372e72-8a4d-4a86-8745-257d91a1aafc",
  "DefaultWorkspaceId": null,
  "DefaultDatasetId": null,
  "DefaultReportId": null,
  "AllowExternalWrites": false,
  "SelectionDebounceMs": 1000
}
```

App registration Entra `RevitCortex Power BI`:

- Single tenant GPA
- Platform `Mobile and desktop applications`
- Redirect URI `http://localhost`
- Redirect URI `https://login.microsoftonline.com/common/oauth2/nativeclient`
- Delegated Power BI permissions: `Dataset.ReadWrite.All`, `Report.Read.All`, `Workspace.Read.All`
- Admin consent granted for GPA
- Manifest: `"allowPublicClient": true` esplicito. Se resta `null`, il browser conferma il device login ma MSAL fallisce il token exchange con AADSTS7000218.

Workspace restituiti nel test:

- `GPA BIM`
- `24HBS`
- `AutomatedML`
- `Test`

## Roadmap residua (allineata a `docs/powerbi-live-architecture-review.md`)

### Phase 1 — Push dataset Elements (prossima sessione)

Goal: pushare effettivamente dati Revit a Power BI Service.

Deliverables:
- Espandere `PowerBiServiceClient`: `CreatePushDataset`, `GetDataset`,
  `PostRows` con batching 10k, `DeleteRows`, retry/backoff Polly
- Schema `Elements` (wide, ~25 colonne stable)
- `PowerBiElementExporter`: snapshot del Document → DTO list (sul main thread)
- Push HTTP fuori dal main thread
- Tool `pbi_publish_elements(workspaceId, datasetName, mode)` con
  `mode = "create" | "append" | "replace"`
- `ExportRunId` in ogni riga per idempotenza

Success criterion: report PBI Desktop si connette al dataset push e mostra
gli elementi del modello aperto.

### Phase 1.5 — Schedules in long form

Goal: esportare le schedule del modello in formato long generico.

Deliverables:
- Schema `Schedules` (ExportRunId, ScheduleName, RowIndex, ColumnName, ValueString, ValueNumber)
- `PowerBiScheduleExporter` 
- Tool `pbi_publish_schedules(workspaceId, datasetName, scheduleIds[])`

### Phase 2 — Wizard PBI Live

Goal: UX completa senza chat.

Deliverables:
- Aggiungere modalità "Live" alternativa a CSV nel wizard esistente
- Workspace selection (combo popolato da `pbi_list_workspaces`)
- Dataset create/select
- Replace/append
- Progress + error display
- Salva i defaults in `PowerBiSettings`

### Phase 3 — Selection watch + URL filter

Goal: cross-app experience.

Deliverables:
- `PowerBiSelectionPublisher` con debounce 1000ms
- Aggancio a `UIDocument.Selection.SelectionChanged` (su `OnIdling` perché
  l'evento richiede UIApplication caricata)
- Tabella `Selection` (replace mode, sempre last selection wins)
- Tool `open_in_powerbi(elementIds)` che apre browser su URL filter
- Bottone ribbon "View in PBI"

### Phase 4 — ElementParameters long + AutoExport

Goal: parametri custom flessibili + opzionale auto-publish.

Deliverables:
- Schema `ElementParameters` long (ExportRunId, ElementId, ParameterName,
  ParameterScope, StorageType, ValueString, ValueNumber, Unit)
- AutoExport opt-in con throttle (max 1 push/N min) e status indicator

### Phase 5 — PBI → Revit interaction (futuro)

In ordine di priorità tecnica:
1. **Già fatto**: protocol handler `revitcortex://` (drillthrough singolo)
2. **Da fare**: HTTP callback locale (multi-element filter)
3. **Solo se serve**: Custom Visual TypeScript (esperienza embedded premium)

## File chiave per orientarsi al ritorno

| File | Cosa contiene |
|---|---|
| `docs/powerbi-live-architecture-review.md` | Analisi tecnica/funzionale completa |
| `docs/powerbi-live-handoff.md` | Questo file |
| `WORKFLOWS.md` | Workflow operativi (Phase 0 PBI Live in fondo) |
| `tool-schemas.txt` | Signature dei tool in formato compatto |
| `src/RevitCortex.Plugin/PowerBiLive/` | Codice Phase 0 |
| `src/RevitCortex.Plugin/PowerBi/` | Wizard CSV (v2) |
| `templates/powerbi/` | Starter pack PBI Desktop (CSV) |

## Setup richiesto al ritorno

1. **Verificare licenza PBI** (confermata: l'utente ce l'ha)
2. **Decidere Tenant ID GPA**: di default usiamo "organizations" ma se
   il consent è bloccato serve override esplicito
3. **Workspace dedicato "RevitCortex Live"** su PBI Service Premium
   (può anche essere "My workspace" per il primo test)

## Comandi utili

```bash
# Build R25 + R24
cd .claude/worktrees/naughty-thompson-a6b7c9
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj

# Deploy a Revit 2025
powershell -ExecutionPolicy Bypass -File deploy.ps1 -RevitVersion 2025 -Config Debug

# Test sample data PBI (no Revit)
cd templates/powerbi
.\Generate-SampleData.ps1
```

## Decisioni aperte (da discutere lunedì)

- **App registration custom su tenant GPA?** Se l'IT collabora si guadagna
  in branding, audit, e si può andare in produzione condivisa con i
  colleghi. Decisione: probabilmente sì in Phase 2 quando il flusso è
  validato.
- **Workspace single vs per-progetto?** Single = tutti i progetti in un
  workspace, risparmio licenze; per-progetto = più ordinato ma più
  workspace da gestire. Default consigliato: single workspace, dataset
  diversi per progetto.
- **Schema versioning**: già menzionato nella review come Phase 4.
  Decisione concreta: `_schema_version` come prima colonna in ogni riga,
  + tabella `Metadata` con `(version, exported_at, project)`.

## Note

- Se MSAL.NET dà problemi su net48 con altre DLL Microsoft caricate da
  Revit, valutare downgrade a 4.61 o 4.60 — la 4.62 funziona localmente
  ma non testata in produzione cross-version Revit.
- Token cache è per-machine: spostare il PC = nuovo login richiesto.
- Settings file in JSON plain (non cifrato): non contiene segreti, solo
  ID. Token vivono nel cache MSAL DPAPI.
