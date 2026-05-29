# RevitCortex Bulk Test - Comandi recenti

Data: 2026-05-29  
Modello: `Snowdon Towers` (`C:\Users\luigi.dattilo\Documents\Snowdon Towers Sample Architectural_luigi.dattilo7VWCL.rvt`)  
Runtime: RevitCortex plugin via socket JSON-RPC locale, porta `8080`  
Lingua rilevata: inglese (`Comments`, `Type Name`, `Family Name`)

## Sintesi

Eseguiti smoke test sui comandi introdotti o modificati nelle ultime settimane: filtri avanzati, query spaziali/clash, project/global parameters, creazione livelli/griglie/viste/schedule/tavole/revisioni, text note e viewport.

Risultato generale: molti comandi base funzionano, ma ci sono anomalie importanti su tool recenti o sul deploy/runtime caricato in Revit. Il caso piu critico e `lines_per_view_count`, che va in timeout e blocca temporaneamente la coda ExternalEvent.

Non sono stati disattivati sandbox, audit o conferme native. Le operazioni write eseguite sono state piccole e riconoscibili con prefisso `RC_BULK*`.

## Bug e anomalie

### P1 - `lines_per_view_count` timeout e blocco temporaneo coda

Chiamata:

```json
{"threshold":20,"limit":5,"includeDetailLines":true,"includeModelLines":true}
```

Risultato: timeout dopo 120000 ms. Le chiamate successive hanno fallito con:

```text
Tool '<name>' could not start because a previous Revit event is still pending or running
```

Dopo circa 10 secondi Revit ha liberato la coda. Da trattare come bug di resilienza: timeout lato router non deve lasciare l'ExternalEvent in stato occupato per i comandi successivi.

### P1 - `ai_element_filter` ignora `invert` e `levelFilter`

`invert=true` con `filterCategory=OST_Walls` ha restituito ancora muri:

```text
Found 1132 element(s), returning 5 ... builtInCategory=OST_Walls
```

`levelFilter` su `L1 - Block 43` e su `levelId=593142` ha restituito lo stesso totale dei muri senza filtro (`1132`). Anche su porte il filtro livello ha restituito elementi da livelli diversi (`Parking`, `L1 - Block 35`, `L4`).

### P1 - `filter_by_parameter_value` multi-condition non attivo nel runtime

La chiamata con `conditions` + `logic=or`:

```json
{
  "categories":["OST_Walls"],
  "conditions":[
    {"parameterName":"Mark","condition":"equals","value":"RC-TEST-001","parameterType":"instance"},
    {"parameterName":"Comments","condition":"contains","value":"RC-STYLE-CHECK","parameterType":"instance"}
  ],
  "logic":"or"
}
```

ha fallito con `InvalidInput: parameterName is required`, come se il runtime non leggesse `conditions`.

### P1 - Drift runtime/schema/sorgente sui tool recenti

Segnali osservati:

- `create_view` ha rifiutato `viewType=Drafting` con suggerimento `Use: floorplan, ceilingplan, section, 3d`, mentre il sorgente corrente include `drafting`.
- La metadata MCP esposta a Codex omette parametri recenti (`useSolidGeometry`, `useRoomSolid`, `rotation`, `viewportTypeId`, azioni estese di livelli/griglie).
- `clash_detection` accetta `useSolidGeometry`, ma la risposta runtime non espone `method`, quindi non e verificabile se stia usando solid geometry o bounding box.
- `place_viewport` accetta la chiamata con `rotation=clockwise`, ma non applica la rotazione.

Questo indica con alta probabilita un disallineamento tra DLL caricate in Revit, sorgente repo e metadata MCP.

### P2 - `place_viewport` non applica `rotation`

Test:

```json
{"sheetId":2666993,"viewId":2667007,"positionX":200,"positionY":150,"rotation":"clockwise"}
```

Risultato: viewport creato (`2667018`), ma `Rotation on Sheet` rimane `0`. La risposta non include il campo `rotation`.

### P2 - `get_schedule_data` contratto risposta incoerente

Su schedule creato `2666967`, `get_schedule_data` ritorna `rowCount=1126`, ma:

- `headers` non risulta valorizzato;
- la prima riga del body contiene `["Type","Length"]`;
- `scheduleId` non risulta nella risposta sagomata.

Probabile mismatch tra contratto documentato e payload effettivo.

### P3 - `filter_by_parameter_value` extra `returnParameters` non risolve type params

Filtro type-level su `Type Name contains MUR` funziona (`1131` match su `1132` muri), ma `returnParameters:["Type Name","Mark"]` restituisce `Type Name=""` nel blocco `parameters`, anche se `matchedValue` e `typeName` sono corretti.

## Test passati

Contesto e read-only:

- `get_project_info`: ok, modello workshared con 3 fasi e livelli.
- `get_current_view_info`: ok, vista attiva `Cover` (`DrawingSheet`).
- `check_model_health`: ok, health score `80`, warnings `49`.
- `get_warnings maxWarnings=10`: ok.
- `get_worksets`: ok, `Workset1`, `Shared Levels and Grids`.
- `manage_links action=list`: ok, 6 link.
- `create_revision action=list`: ok, 3 revisioni iniziali.
- `ifc_get_capabilities`: ok.
- `ifc_list_export_configurations compact=true`: ok, ma il payload include ancora descrizioni.

Filtri/query:

- `ai_element_filter` basic `OST_Walls`: ok, 1132 muri, 3 restituiti.
- `filter_by_parameter_value` single condition type-level: ok, 1131 match.
- `get_elements_in_spatial_volume` custom bbox: ok, 9 muri trovati, 5 restituiti.
- `get_elements_in_spatial_volume` room solid: ok, 12 elementi trovati, 5 restituiti.
- `clash_detection` walls/floors: ok, 5 clash restituiti con `maxResults=5`.
- `manage_project_parameters list`: ok, 94 parametri.
- `manage_project_parameters set_group dryRun`: ok.
- `manage_global_parameters list`: ok, 13 parametri.

Write smoke test:

- `create_level`: ok, creato `RC_BULK_LVL_20260529-132333`, id `2666950`.
- `create_grid`: ok, creata griglia `1032333`, id `2666951`.
- `create_sheet`: ok, creata tavola `RC-0529132333`, id `2666952`.
- `create_revision`: ok, creata revisione `2666966`.
- `create_schedule` con `categoryName=OST_Walls`: ok, schedule `2666967`, 2 campi aggiunti.
- `duplicate_system_type`: ok, tipo muro `RC_BULK_WALLTYPE_20260529-132333`, id `2666974`.
- `create_sheet` fase 3e: ok, tavola `R3E-0529132521`, id `2666993`.
- `create_view FloorPlan`: ok, vista `RC_BULK_FP_20260529-132521`, id `2667007`.
- `create_text_note`: ok, nota `2667017`; allineamenti visibili nei parametri (`Horizontal Align`, `Vertical Align`).
- `place_viewport`: viewport creato, id `2667018`; rotazione non applicata.

## Verifica build/test

- `dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`: pass, 20 warning.
- `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`: pass quando eseguito serialmente.
- `dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj`: pass.
- `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"`: pass, 224 passed, 1 skipped.

Nota: il primo tentativo di build R25 in parallelo con R24 ha generato `NETSDK1005` su `project.assets.json`. Rerun seriale riuscito. Non e un failure prodotto, ma il workflow di build multi-target non va parallelizzato sullo stesso workspace.

## Artefatti lasciati nel modello

Creati intenzionalmente per smoke test:

- Level `RC_BULK_LVL_20260529-132333` (`2666950`)
- Grid `1032333` (`2666951`)
- Sheets `RC-0529132333` (`2666952`) e `R3E-0529132521` (`2666993`)
- Revision `RC bulk revision 20260529-132333` (`2666966`)
- Schedule `RC_BULK_WALLS_20260529-132333` (`2666967`)
- Schedule alias probe `RC_BULK_ALIAS_20260529-132333` (`2666986`)
- Wall type `RC_BULK_WALLTYPE_20260529-132333` (`2666974`)
- Floor plan `RC_BULK_FP_20260529-132521` (`2667007`)
- Text note `2667017`
- Viewport `2667018`

## Raccomandazioni

1. Allineare deploy Revit, MCP server metadata e sorgente corrente; poi rieseguire i test P1/P2.
2. Aggiungere regression test runtime per `ai_element_filter` su `invert`, `levelFilter`, `combineWith=or`.
3. Proteggere `lines_per_view_count` con timeout cooperativo/cancellazione o chunking, e assicurare reset dello stato ExternalEvent dopo timeout.
4. Verificare `place_viewport` applicando `Viewport.Rotation` e restituendo il valore finale in response.
5. Uniformare contratto `get_schedule_data`: `scheduleId`, `headers`, `rows` senza header duplicato nel body.
