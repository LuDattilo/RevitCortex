# RevitCortex Main Commands Bulk Test - 2026-05-29

## Sintesi

Campagna eseguita su Revit aperto con modello `Snowdon Towers`.

- File modello: `C:\Users\luigi.dattilo\Documents\Snowdon Towers Sample Architectural_luigi.dattilo7VWCL.rvt`
- RevitCortex: porta `8080`, `ReadOnlyMode=false`, locale runtime `en`
- Vista attiva durante la suite: `Cover` (`DrawingSheet`)
- Copertura: oltre 120 invocazioni, circa 85 comandi unici o varianti significative
- Modalita: tool nativi RevitCortex via JSON-RPC locale; destructive/write massivi solo in `dryRun`
- Non usato: `send_code_to_revit`
- Post-timeout: RevitCortex tornato responsivo con `say_hello` dopo 12 secondi

La suite non ha disabilitato sandbox, audit, conferme native o guardrail di sicurezza. La richiesta di "rimuovere limitazioni" e stata interpretata come massima autonomia operativa entro il safety layer del progetto.

## Stato Modello

- `check_model_health`: `grade=B`, `healthScore=80`, `warningCount=49`
- `analyze_model_statistics`: `38360` elementi, `1877` tipi, `286` famiglie, `371` viste, `57` tavole
- `get_warnings maxWarnings=10`: ok, 10 warning restituiti su 49
- Worksharing: attivo, 2 workset rilevati
- Link Revit: 6 link type/instance rilevati
- Power BI auth: utente gia autenticato; `pbi_list_workspaces` read-only ok, 4 workspace

## Comandi Passati

Passati senza anomalie bloccanti:

- Sessione/stato: `say_hello`, `get_project_info`, `check_model_health`, `analyze_model_statistics`, `workflow_model_audit`, `get_warnings`
- Elementi/parametri: `ai_element_filter`, `get_element_parameters`, `get_elements_by_unique_id`, `export_elements_data`, `filter_by_parameter_value` single condition, `get_compound_structure`, `measure_between_elements`, `get_elements_in_spatial_volume`
- Ambienti/link: `get_phases`, `get_worksets`, `get_linked_file_instances`, `manage_links`, `get_linked_elements`, `get_coordination_models`
- Materiali/famiglie: `get_materials`, `get_material_properties`, `get_material_quantities`, `list_family_sizes`, `audit_families`, `get_available_family_types`
- Project settings: `get_shared_parameters`, `manage_project_parameters list`, `manage_global_parameters list`, `manage_project_units`, `manage_phase_filters`, `manage_additional_settings`
- Viste/tavole: `create_view floorplan`, `create_view drafting`, `place_viewport rotation=clockwise`, `create_text_note`, `create_view_filter list/create/apply`, `manage_view_templates`, `apply_view_template list`
- Schedules: `create_schedule`, `get_schedule_data`, `duplicate_schedule`, `export_schedule`
- Operazioni dry-run: `bulk_modify_parameter_values`, `sync_csv_parameters`, `transfer_parameters`, `renumber_elements`, `rename_families`, `batch_rename`, `delete_element`, `purge_unused`, `wipe_empty_tags`, `set_material_properties`
- Selezione/export: `save_selection`, `load_selection`, `section_box_from_selection`, `export_to_excel` con chiavi runtime, `import_from_excel dryRun`

## Artifact Creati

Artifact principali lasciati nel file di test:

- Level `RC_MAIN_LVL_0529142902` id `2667023`
- Grid id `2667024`
- Sheet `RCM-0529142902` id `2667025`
- Revision id `2667039`
- Schedules `2667040`, `2667047`, duplicate schedule `2667054`
- Floor plan `RC_MAIN_FP_0529142902` id `2667061`
- Drafting view `RC_MAIN_DRAFT_0529142902` id `2667071`
- Text note id `2667078`
- Viewport id `2667079`
- Materials `2667086`, `2667087`
- Wall type `RC_MAIN_WALLTYPE_0529142902` id `2667088`
- View filter `RC_MAIN_FILTER_0529143149` id `2667141`
- Section box view `SectionBox_143232` id `2667145`
- Selection filter `RC_MAIN_SELECTION_0529143149`

## Bug e Criticita

| Sev | Tool | Evidenza | Impatto | Azione consigliata |
| --- | --- | --- | --- | --- |
| Critical | `lines_per_view_count` | Timeout a 45s anche isolato, con `threshold=20`, `limit=5`; eccezione socket `ReadLine`. In un test precedente della stessa sessione aveva saturato l'ExternalEvent per circa 120s. | Tool non affidabile su modelli con molte viste; puo bloccare la pipeline MCP. | Aggiungere timeout per vista, cancellation token reale, limite prima della scansione e fallback progressivo. |
| High | `duplicate_view` | `viewId` -> errore `viewIds array is required`; `viewIds:[2667061]` -> ok. | Wrapper/schema server non corrisponde al runtime plugin. | Allineare wrapper a `viewIds[]` o accettare anche `viewId` nel tool. |
| High | `get_link_transform` | `linkInstanceId` -> errore `instanceId is required`; `instanceId=2403932` -> ok. | Chiamata MCP pubblica rischia fallimento sistematico. | Rinominare parametro nel wrapper o supportare alias `linkInstanceId` nel plugin. |
| High | `export_to_excel` | Con chiavi pubbliche `category/outputPath` ignora `outputPath` ed esporta `10000` elementi sul Desktop; con `categories/filePath/maxElements=5` esporta correttamente nel path richiesto. | Export non controllabile, rischio file fuori destinazione e dataset troppo grande. | Allineare wrapper a `categories`, `filePath`, `maxElements` o supportare alias nel plugin. |
| High | `duplicate_sheet_with_content` | `newNumber/newName` -> errore sheet number gia in uso; `sheetNumberPrefix` + `keepSchedules=false` -> ok. | Wrapper/schema passa parametri ignorati dal runtime. | Allineare contratto o supportare `newNumber/newName` nel tool. |
| Medium | `duplicate_sheet_with_views` | Con `keepSchedules=true` fallisce su schedule interno non aggiungibile; con `keepSchedules=false` passa. | Duplicazione tavole fragile quando ci sono schedule non piazzabili. | Filtrare schedule interne/non placeable e restituire warning per schedule saltate. |
| Medium | `ifc_export_basic` | Export filtrato su vista creata: Revit riporta success ma nessun file viene scritto. Con `fileName` contenente `.ifc`, verifica path cerca `*.ifc.ifc`. | Export IFC produce errore ambiguo e path verification fragile. | Normalizzare `fileName`, gestire estensione e distinguere "no exportable elements" da file mancante. |
| Medium | `manage_worksets` | `get_worksets` rileva 2 workset, ma `manage_worksets action=list` ritorna `Tool 'manage_worksets' is not available for this document`. | Dynamic availability incoerente su modello workshared. | Verificare `DocumentAnalyzer.EnableTool("manage_worksets")` e gating runtime. |
| Medium | `ai_element_filter` | `levelFilter='L1 - Block 35'` su `OST_Walls` restituisce ancora `1132` muri, stesso totale senza filtro. | Filtro livello non utile per muri con level null/base constraint. | Usare fallback su parametri base/top constraint o dichiarare limitazione. |
| Medium | `operate_element` / `get_selected_elements` | `operate_element select` risponde `Selected 1 element(s)`, ma subito dopo `get_selected_elements` riporta `selectedCount=0`. | Conferma di selezione non verificata o UI selection non persistente. | Dopo `SetElementIds`, rileggere la selezione e restituire count reale. |
| Low | `get_schedule_data` | Risposta usa `columnHeaders` e non include `scheduleId`. | Contratto leggibile ma meno stabile per client che si aspettano `headers` o correlazione id. | Documentare/standardizzare `columnHeaders` e includere `scheduleId`. |
| Low | `filter_by_parameter_value` direct JSON-RPC | `conditions` come stringa JSON fallisce; array gia parsato passa. Il wrapper MCP sembra parsare la stringa prima di inviarla. | Solo rischio per chiamate dirette al bridge/plugin. | Documentare differenza o accettare entrambe le forme nel plugin. |

## Skip Motivati

- `color_elements`: vista attiva `DrawingSheet`; il tool richiede vista modello attiva.
- `tag_walls` / `tag_rooms`: operano solo sulla vista attiva; test su sheet sarebbe falso negativo.
- `create_dimensions`: richiede riferimenti geometrici e quota Z esatta; non creato per non introdurre annotazioni arbitrarie.
- `send_code_to_revit`: non usato; il bulk test era coperto dai tool nativi e lo script bypasserebbe dry-run/conferme senza consenso specifico.
- Operazioni distruttive reali (`delete_material`, `delete_schedule`, purge reale, set parametri reale): non eseguite oltre dry-run.

## Verifiche Build/Test

Eseguite dopo la campagna:

- `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`: ok, 0 errori, 0 warning
- `dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`: ok, 0 errori, 20 warning gia presenti
- `dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj`: ok, 0 errori, 0 warning
- `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"`: ok, 224 passati, 1 skipped

## Priorita Fix

1. Sistemare subito i mismatch wrapper/plugin: `duplicate_view`, `get_link_transform`, `export_to_excel`, `duplicate_sheet_with_content`.
2. Mettere in sicurezza `lines_per_view_count` con timeout/cancel e test su modello con 300+ viste.
3. Rendere robusta la duplicazione sheet con schedule non piazzabili.
4. Correggere path/estensione e diagnostica di `ifc_export_basic`.
5. Aggiungere test di integrazione che chiamano i wrapper MCP reali contro il bridge plugin, non solo unit test del tool isolato.

## Fix Applicati - 2026-05-29

Correzioni implementate nel codice sorgente, da distribuire nel plugin Revit con deploy/restart prima di rieseguire il bulk test nel modello aperto.

- Allineati wrapper e runtime per `duplicate_view`, `get_link_transform`, `export_to_excel`, `create_schedule` e `duplicate_sheet_with_content`; mantenuti alias legacy nel plugin dove possibile.
- Aggiunte opzioni pubbliche mancanti agli schemi MCP per `duplicate_sheet_with_content`, `export_to_excel` e `lines_per_view_count`; rigenerato `tool-schemas.txt`.
- `duplicate_sheet_with_views` e `duplicate_sheet_with_content` ora saltano schedule non piazzabili e riportano contatori/warning invece di fallire tutta la duplicazione.
- `ifc_export_basic` e `ifc_export_with_configuration` normalizzano il nome file `.ifc`, evitando il caso `*.ifc.ifc`.
- `manage_worksets` viene abilitato dal `DocumentAnalyzer` quando il modello e workshared.
- `ai_element_filter` accetta `levelFilter` come nome livello, id numerico/stringa o oggetto strutturato.
- `operate_element select` rileggge la selezione effettiva prima di riportare il conteggio.
- `get_schedule_data` include anche `scheduleId` e alias `headers`.
- `filter_by_parameter_value` accetta `conditions` sia come array JSON sia come stringa JSON parsabile.
- `lines_per_view_count` ha limiti espliciti `maxViews` e `timeBudgetMs`, conteggio piu mirato delle detail lines e uscita parziale con `timedOut`.

Verifiche post-fix:

- Regression test mirati: 6 passati.
- `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`: ok, 0 errori.
- `dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`: ok, 0 errori.
- `dotnet build src/RevitCortex.Server/RevitCortex.Server.csproj`: ok, 0 errori.
- `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R26"`: ok, 230 passati, 1 skipped.
