# RevitCortex — Bulk Test completo (tutti i comandi)

**Data:** 2026-05-29
**Modello:** `Snowdon Towers Sample Architectural_luigi.dattilo7VWCL.rvt` (workshared, 3 fasi, ~37.000 elementi)
**Lingua rilevata:** inglese (`Comments`, `Type Name`, `Family Name`)
**Tool totali nel server:** 175
**Chiamate di test eseguite:** 94 (52 read-only + 24 write/create + 18 gruppi rimanenti)

## Metodo (importante)

Il test è stato condotto chiamando **direttamente il bridge JSON-RPC del plugin sulla porta 8080**, non attraverso il server MCP della sessione. Motivo: il server MCP attivo in questa sessione era una build **vecchia** (vedi *Drift di sessione* sotto), priva dei parametri introdotti nelle ultime settimane. Il bridge parla con il **plugin** (deployato aggiornato alle 14:00), quindi esercita il codice corrente.

Script riutilizzabili: `scripts/bulk-test-bridge.ps1` (helper), `scripts/bulk-test-all-readonly.ps1`, `scripts/bulk-test-all-write.ps1`, `scripts/bulk-test-remaining.ps1`. Risultati grezzi: `scripts/bulk2-*-results.json`.

Nessun guardrail di sicurezza è stato disabilitato. Le operazioni distruttive sono state eseguite **solo in `dryRun`**; i create reali usano il prefisso riconoscibile `RC_BULK2_*`. `send_code_to_revit` non è stato eseguito (richiede consenso esplicito per uso, da convenzione di progetto).

## Sintesi

**Esito generale: sano.** Circa **80 operazioni distinte funzionano correttamente**, incluse tutte le novità chiave delle ultime settimane verificate live (vedi *Verificato funzionante*). Sono stati trovati **3 bug/anomalie reali** (1×P1, 1×P2, 1×P3) e **1 problema operativo di drift di sessione**. La maggior parte dei primi "errori" osservati erano **input errati del mio script di test** (nomi parametro sbagliati nel test bridge-diretto), tutti riconfermati funzionanti con input corretti.

> ## ⚠️ AVVISO CRITICO — il test ha colpito codice SUPERATO
> Dopo aver scritto questo report è emerso che il **working tree del repo contiene ~22 file modificati NON committati e NON deployati** (lavoro di Codex). Il bulk test ha esercitato il **plugin deployato alle 14:00**, che è **precedente** a queste modifiche. Verificato sul sorgente attuale:
> - **P1 (`lines_per_view_count`) è GIÀ FIXATO** nel working tree: il tool ora ha `maxViews` (default 100) e `timeBudgetMs` (default 15000) — un budget di tempo cooperativo che evita il timeout a 120s qui osservato.
> - **P2 (`manage_worksets` gating)** è toccato: `DocumentAnalyzer.cs` + `DocumentCapabilities.cs` modificati attorno a `manage_worksets`/capability.
> - Altri tool modificati non testati con la versione nuova: `GetScheduleDataTool`, `OperateElementTool`, `ExportToExcelTool`, `DuplicateView/Sheet*`, filtri, IFC export.
>
> Il working tree **compila** su R25+R24 (0 errori) e i **test passano (230/1/0)**. Quindi **P1 e P2 sotto vanno considerati probabilmente già risolti** una volta deployato il codice attuale. **Azione necessaria: build + deploy del working tree, poi RI-ESEGUIRE questo test.** Non ho potuto deployare ora perché il deploy richiede Revit chiuso e il modello è aperto per i test (chiuderlo senza conferma sarebbe invasivo).

---

## Bug e anomalie

### P1 — `lines_per_view_count` va in timeout e blocca temporaneamente la coda ExternalEvent

Su questo modello la chiamata `{threshold:20, limit:3}` **non ritorna entro 120s** (timeout lato client). Mentre è in sospeso, **tutte** le chiamate successive falliscono immediatamente ("previous Revit event is still pending/running"); la coda si libera circa 10s dopo che il client rinuncia.

- Riprodotto **due volte** (in batteria e in isolamento con timeout a 120s).
- **Già segnalato nel report precedente — ancora non risolto.**
- **Impatto:** un singolo uso di questo tool tronca un'intera sessione di lavoro/test.
- **Direzione fix:** cancellazione cooperativa / chunking del conteggio, e reset dello stato ExternalEvent dopo un timeout del router, così i comandi successivi non ereditano la coda occupata. Nel frattempo: non includerlo mai in un batch.

### P2 — Capability map "stale" per i tool `IsDynamic` quando il modello è aperto prima di un deploy

`manage_worksets` risponde `"Tool 'manage_worksets' is not available for this document"` su un modello **workshared** (dove `get_worksets` funziona e restituisce 2 worksets).

- **Causa:** `manage_worksets` è `IsDynamic=true`. Il `DocumentAnalyzer` popola le capability (e chiama `EnableTool("manage_worksets")`) **all'apertura del documento**. Il modello era aperto **prima** del deploy delle 14:00, quindi l'analisi è avvenuta con la lista `EnableTool` precedente, che non includeva il nuovo `manage_worksets`. La mappa in sessione è vecchia.
- **Conferma:** `set_project_info` (nuovo ma **non** `IsDynamic`) ha funzionato (non passa dal gating capability).
- **Non è un bug di codice** (il `DocumentAnalyzer` ha la riga corretta `caps.EnableTool("manage_worksets")`), ma è un'anomalia operativa.
- **Workaround:** **riaprire il documento** in Revit (ri-esegue il DocumentAnalyzer). Da valutare: re-analisi delle capability anche su evento di reload del plugin.

### P3 — `operation`/`action` non valido non fallisce in modo chiaro

`rename_views` con `operation:"add_prefix"` (valore inesistente; quello corretto è `"prefix"`) **non** restituisce "unknown operation": cade nel ramo `find_replace` con stringa di ricerca vuota e lancia `"The value cannot be an empty string. (Parameter 'oldValue')"`.

- Vale probabilmente anche per `batch_rename`/`rename_families` (stessa famiglia di `ApplyRename`).
- **Impatto:** errore criptico invece di un messaggio utile → l'LLM non capisce come correggersi.
- **Fix:** whitelist dell'`operation` con messaggio direttivo (es. *"Unknown operation 'add_prefix'. Use: prefix, suffix, find_replace."*).

---

## Drift di sessione (operativo, non un bug di prodotto)

Il processo server MCP attivo (PID osservato, avviato 14:43) gira da `~/.revitcortex/**server-v4**\RevitCortex.Server.dll` datato **10:23** (vecchio), mentre il deploy è andato in `~/.revitcortex/**server**\` (14:00), che è ciò che **sia** `.codex/config.toml` **sia** `.claude.json` indicano. Esistono 7 cartelle `server*` (server, server-next, server-v2..v4, .bak). `server-v4` è **lockata** dal processo vivo, quindi non sostituibile a caldo.

**Effetto:** gli schemi MCP esposti a questa sessione Claude sono vecchi (mancano `combineWith`, `useSolidGeometry`, `conditions`, `useRoomSolid`, `invert`, `levelFilter`, azioni estese…). Per questo il test è passato dal bridge diretto.

**Azione raccomandata:** (1) riavviare il client MCP così rilancia il server da `~/.revitcortex/server`; (2) riaprire il modello (risolve anche il P2); (3) riconciliare la divergenza `server-v4` vs `server` — capire cosa crea le cartelle `server-vN` e quale lancia davvero il client.

---

## Verificato FUNZIONANTE (novità delle ultime settimane, live)

Tutte queste sono state confermate sul modello via bridge:

| Tool / azione | Fase | Esito |
|---|---|---|
| `create_view` viewType=**Drafting** | 0 | OK (era il bug "Use: floorplan…") |
| `create_view` FloorPlan + name + level | — | OK |
| `create_text_note` con **rotation** + **leader** | 3e | OK |
| `create_array` **associative=true** (vero `ArrayElement`) | 4e | OK (~3.6s) |
| `set_compound_structure` **set_wrapping** (openingWrapping) | 4b | OK (dryRun) |
| `modify_schedule` **set_filter** | 0 | OK |
| `ai_element_filter` **invert** / **combineWith=or** / **levelFilter** | 3b | OK |
| `filter_by_parameter_value` **conditions** multi + logic | 3b | OK |
| `duplicate_system_type` duplicate (typeId reale) | 3c | OK |
| `set_project_info` (write) | 3a | OK |
| `create_level` / `create_grid` / `create_sheet` / `create_revision` / `create_schedule` | vari | OK |
| `purge_unused` (dryRun) | 4c | OK |
| `export_to_excel` / `export_schedule` / `export_room_data` / `export_elements_data` | — | OK |
| `clash_detection` / `get_elements_in_spatial_volume` (solid-geometry) | 3d | OK |
| `workflow_model_audit` | — | OK |

## Read-only: 52/52 OK

Tutti i tool di lettura/discovery testati hanno risposto correttamente: `say_hello`, `get_cache_stats`, `clear_cache`, `get_project_info`, `get_current_view_info`, `check_model_health`, `analyze_model_statistics`, `get_warnings`, `get_phases`, `get_worksets`, `get_selected_elements`, `ai_element_filter` (×3), `export_elements_data`, `filter_by_parameter_value` (×2), `get_current_view_elements`, `get_elements_in_spatial_volume`, `get_room_openings`, `find_untagged_elements`, `find_undimensioned_elements`, `measure_between_elements`, `audit_families`, `get_available_family_types`, `list_family_sizes`, `clash_detection`, `get_materials`, `get_material_quantities`, `get_shared_parameters`, `manage_global_parameters` list, `manage_project_parameters` list, `manage_project_units` get, `list_schedulable_fields`, `manage_view_templates` list, `apply_view_template` list, `manage_phase_filters` list, `manage_unplaced_views` list, `manage_additional_settings`, `create_view_filter` list, `create_revision` list, `manage_links` list, `get_linked_file_instances`, `get_coordination_models`, `get_selected_linked_elements`, `ifc_get_capabilities`, `ifc_list_export_configurations`, `ifc_analyze_rebuildability`, `ifc_list_rebuild_candidates`, `pbi_check_auth`, `pbi_list_workspaces`, `load_selection`.

> Nota: `get_materials` ha dato un timeout apparente nella **prima** batteria — era un effetto collaterale della coda avvelenata da `lines_per_view_count` eseguito subito prima. In isolamento risponde in **16ms**.

## Falsi allarmi (errori del mio script di test, NON del prodotto)

Riconfermati funzionanti con input corretti:

- `duplicate_system_type`, `set_compound_structure`: avevo passato `typeId=0`/nome tipo inventato → "not found". OK con `typeId=221` reale.
- `match_element_properties`: param reale `sourceElementId` (non `sourceId`) e serve un `targetElementIds` reale. OK.
- `get_link_transform`: il **plugin** vuole `instanceId` (il wrapper MCP mappa `linkInstanceId`→`instanceId`; chiamando il bridge diretto serve `instanceId`). OK.
- `batch_rename` / `renumber_elements`: il parametro corretto è `targetCategory` (non `category`). Verificato nel sorgente: wrapper e plugin **concordano** su `targetCategory`. Nessun bug.
- `create_preset_schedule`: voleva un nome preset (door_by_room…), non `action:list`.
- `ifc_validate_request`: valida solo `.ifc/.ifczip/.ifcxml`, non `.rvt` (validazione corretta).
- `batch_export` PDF: avevo passato set di viste vuoto → "required" (validazione corretta).

## Non testati (di proposito)

- `send_code_to_revit` — sandbox; richiede consenso esplicito per uso.
- `pbi_publish_*` / `pbi_create_dataset` / `push_to_powerbi` — scrivono su workspace Power BI esterni (auth+dataset reali); evitati per non produrre effetti fuori dal modello.
- `ifc_rebuild_*`, `ifc_open_or_import`, `ifc_link`, `ifc_reload_link` — richiedono un file IFC importato nel modello.
- `delete_element` / `delete_material` / `delete_schedule` / `delete_selection` reali — eseguiti solo concettualmente via dryRun dove disponibile.
- `cross_app_selection` / `show_cross_model_elements` lato Navisworks — richiedono NavisCortex.
- `workflow_clash_review` / `workflow_room_documentation` / `workflow_sheet_set` / `workflow_data_roundtrip` — workflow pesanti, evitati nel batch per non saturare la coda.
- `lines_per_view_count` — testato (è il P1), ma escluso dalle batterie per via del blocco coda.

## Raccomandazioni

1. **P1** Mettere in sicurezza `lines_per_view_count`: cancellazione cooperativa/chunking + reset dell'ExternalEvent dopo timeout del router. È il problema più impattante.
2. **P2** Riaprire il modello dopo ogni deploy del plugin (ri-esegue il DocumentAnalyzer e abilita i tool `IsDynamic` nuovi come `manage_worksets`); valutare una re-analisi capability su reload.
3. **P3** Aggiungere whitelist+messaggio direttivo per gli `operation`/`action` non validi (rename_views/batch_rename/rename_families).
4. **Drift** Riavviare il client MCP per usare il server `~/.revitcortex/server` (14:00) e riconciliare le cartelle `server-vN`; poi rieseguire questo test **via MCP** (non solo bridge) per validare anche lo strato server.

## Artefatti creati nel modello (prefisso RC_BULK2_, da eliminare)

Livelli, griglie, tavole, viste, schedule, revisione, tipo muro, text note, array e set di selezione con stamp `RC_BULK2_0529xxxx`, più gli artefatti `RC_BULK_*`/`RC_MAIN_*` dei test precedenti. Tutti rimovibili (es. `purge_unused` per i tipi inutilizzati, eliminazione manuale per gli elementi).
