# RevitCortex -- Flussi Operativi Collaudati

Raccolta di sequenze multi-step verificate, organizzate per obiettivo BIM.
Ogni flusso e stato ricavato dalla documentazione operativa del progetto e testato sul campo.

---

## Controllo Qualita Mattutino

**Sequenza:** `check_model_health` -> `get_warnings` -> (opzionale) `clash_detection`
**Parametri chiave:**
- `get_warnings`: usare `maxWarnings: 10` (mai il default 500)
- `clash_detection`: specificare la coppia di discipline (es. OST_StructuralFraming + OST_Walls)
**NON fare:** Non usare `workflow_model_audit` per un check veloce -- costa 3000+ token vs 500-800 per questa sequenza. Chiudere la sessione dopo il check per evitare accumulo di contesto.

**Fonte:** CLAUDE.md (Session A -- Morning Check)

---

## Aggiornamento Parametri su N Elementi

**Sequenza:** `export_elements_data` (con filtro) -> `bulk_modify_parameter_values` (dryRun: true) -> leggere solo `modifiedCount`/`skippedCount` -> `bulk_modify_parameter_values` (dryRun: false) -> `get_element_parameters` (spot check 1-2 elementi)
**Parametri chiave:**
- `export_elements_data`: specificare sempre `filterParameterName`, `filterValue`, `categories`, `parameterNames`, `maxElements`
- `bulk_modify_parameter_values`: prima `dryRun: true`, leggere SOLO i contatori (non la lista elementi), poi eseguire con `dryRun: false`
**NON fare:** Non leggere l'intero elenco elementi dal dryRun -- spreca token inutilmente. Non fare spot check su piu di 2 elementi.

**Fonte:** CLAUDE.md (Session B -- Parameter Updates, Anti-Waste Patterns)

---

## Aggiornamento Parametri Diversi per Elemento

**Sequenza:** preparare CSV con colonne ElementId + parametri -> `sync_csv_parameters`
**Parametri chiave:**
- Il CSV deve avere una colonna `ElementId` e una colonna per ogni parametro da impostare
**NON fare:** Non usare `set_element_parameters` in loop per N elementi con parametri diversi -- `sync_csv_parameters` e progettato per questo.

**Fonte:** CLAUDE.md (Modifying parameters hierarchy)

---

## Copia Parametri tra Elementi

**Sequenza:** `match_element_properties` con `parameterNames` espliciti
**Parametri chiave:**
- Specificare SEMPRE `parameterNames` -- senza, copia tutti i parametri trasferibili
**NON fare:** Non usare `match_element_properties` senza `parameterNames` -- rischio di sovrascrittura non voluta.

**Fonte:** CLAUDE.md (Modifying parameters hierarchy)

---

## Documentazione ed Esportazione

**Sequenza:** `workflow_data_roundtrip` oppure `export_to_excel` -> (opzionale) `create_preset_schedule` -> `export_schedule`
**Parametri chiave:**
- `export_schedule`: specificare `scheduleId` dello schedule appena creato
**NON fare:** Non esportare senza aver prima creato/verificato lo schedule necessario. Chiudere la sessione dopo l'export.

**Fonte:** CLAUDE.md (Session C -- Documentation / Export)

---

## Verifica Stato Modello (Costo Crescente)

**Sequenza (scegliere UNA delle seguenti in base al dettaglio richiesto):**
1. `check_model_health` (~200 token) -- solo score + issues
2. `analyze_model_statistics` con `compact: true` (~400 token)
3. `workflow_model_audit` con filtri (~800 token)
4. `workflow_model_audit` completo (~3000 token)
**Parametri chiave:**
- `analyze_model_statistics`: usare `compact: true` per risposte concise
- `workflow_model_audit`: filtrare per categorie specifiche quando possibile
**NON fare:** Non partire dal livello 4 se basta il livello 1. La differenza e 200 vs 3000+ token.

**Fonte:** CLAUDE.md (Tool Selection Hierarchy -- Model status)

---

## Ricerca Elementi

**Sequenza (scegliere in base alla complessita):**
1. Filtro semplice (1 parametro, valore esatto): `export_elements_data` con `filterParameterName`/`filterValue`
2. Filtro complesso (range, AND/OR, multi-parametro): `ai_element_filter`
3. Elementi nella vista attiva: `get_current_view_elements` con `fields` e `limit`
4. Elementi in un volume/stanza: `get_elements_in_spatial_volume` con `categoryFilter` e `maxElementsPerVolume` ridotto
**Parametri chiave:**
- `ai_element_filter`: OBBLIGATORIO wrappare i parametri in un oggetto `data`: `{"data": {"filterCategory": "OST_Walls", ...}}`
- `get_current_view_elements`: specificare `fields` per ridurre i dati restituiti
- `get_elements_in_spatial_volume`: specificare `categoryFilter` e limitare `maxElementsPerVolume`
**NON fare:** Non usare `ai_element_filter` con `maxElements: 1000` senza necessita. Non usare `audit_families` globale per trovare una singola categoria.

**Fonte:** CLAUDE.md (Tool Selection Hierarchy -- Finding elements, Tool-Specific Corrections)

---

## Clash Detection

**Sequenza (due opzioni):**
- Check rapido: `clash_detection` -> restituisce conteggio + lista ID
- Review visuale: `workflow_clash_review` -> crea vista 3D con section box automatico
**Parametri chiave:**
- Specificare sempre le due categorie da verificare (es. `OST_StructuralFraming` + `OST_Walls`)
- Su modelli architettonici, i pilastri sono `OST_Columns`, NON `OST_StructuralColumns`
**NON fare:** Non usare `workflow_clash_review` per un semplice conteggio -- il check rapido costa 400-600 token vs 800+ per la review completa.

**Fonte:** CLAUDE.md (Tool Selection Hierarchy -- Clash detection)

---

## Rilevamento Lingua Revit

**Sequenza:** `get_element_parameters` (su un elemento qualsiasi) oppure `get_project_info` -> controllare i nomi dei parametri nella risposta -> usare la colonna corrispondente dalle tabelle locale
**Parametri chiave:**
- EN: "Level", "Comments", "Type Name"
- IT: "Livello", "Commenti", "Nome del tipo"
- FR: "Niveau", "Commentaires", "Nom du type"
- DE: "Ebene", "Kommentare", "Typname"
**NON fare:** Non assumere MAI la lingua. Verificare sempre all'inizio di ogni sessione.

**Fonte:** CLAUDE.md (IMPORTANT: Detect Revit Language First)

---

## Tagging Automatico Vani/Muri

**Sequenza:** `get_current_view_info` (verificare vista corretta) -> `tag_rooms` oppure `tag_walls`
**Parametri chiave:**
- I tool operano SOLO sulla vista attiva di Revit
- La vista deve contenere elementi visibili della categoria richiesta
**NON fare:** Non chiamare `tag_rooms`/`tag_walls` senza prima verificare la vista attiva -- il tool non cambia vista automaticamente.

**Fonte:** CLAUDE.md (Tool Behavioral Notes)

---

## Colorazione Elementi per Categoria

**Sequenza:** `get_current_view_info` (verificare che sia una vista modello, NON un foglio) -> `color_elements`
**Parametri chiave:**
- `color_elements` usa nomi categoria localizzati (dipende dalla lingua Revit)
- Funziona solo su viste che contengono elementi visibili di quella categoria
**NON fare:** Non chiamare su DrawingSheet/Cover Sheet -- fallira. Passare prima a una FloorPlan o vista 3D.

**Fonte:** CLAUDE.md (Tool Behavioral Notes, Tool-Specific Corrections)

---

## Quotatura Elementi

**Sequenza:** `get_project_info` (per ottenere livelli con quote) -> `create_dimensions` (usando Z esatto dalla quota del livello)
**Parametri chiave:**
- Il parametro Z deve corrispondere ESATTAMENTE alla quota del livello
- Usare l'elevazione dal risultato di `get_project_info`
**NON fare:** Non inserire Z a mano o approssimato -- la quota deve essere esatta.

**Fonte:** CLAUDE.md (Tool Behavioral Notes)

---

## Operazioni Distruttive (Pattern dryRun)

**Sequenza:** tool con `dryRun: true` -> leggere anteprima risultati -> tool con `dryRun: false` -> conferma nel dialog Revit
**Parametri chiave:**
- Tool con conferma: `delete_element`, `delete_selection`, `delete_material`, `purge_unused`, `wipe_empty_tags`, `set_element_parameters`, `set_compound_structure`, `batch_rename`, `override_graphics`, `set_element_phase`, `set_element_workset`, `change_element_type`, `load_family`
- Se l'utente annulla, il tool restituisce `CortexErrorCode.Cancelled`
**NON fare:** Non eseguire operazioni distruttive senza prima un dryRun. Se l'utente annulla, non ripetere automaticamente -- chiedere.

**Fonte:** CLAUDE.md (Confirmation Dialogs, Handling User Input Situations)

---

## IFC: Verifica Capacita

**Sequenza:** `ifc_get_capabilities`
**Parametri chiave:**
- Mostra versioni IFC supportate e se il plug-in revit-ifc e installato
**NON fare:** Non tentare operazioni IFC senza prima verificare le capacita del sistema.

**Fonte:** docs/RevitCortex_IFC_Guide.md

---

## IFC: Link File nel Progetto

**Sequenza:** `ifc_validate_request` -> `ifc_link`
**Parametri chiave:**
- `ifc_validate_request`: verifica percorso, estensione (.ifc/.ifczip/.ifcxml), schema
- `ifc_link`: crea un file intermedio .ifc.RVT accanto all'originale
- Usare `recreateLink: false` per riutilizzare un .RVT esistente
**NON fare:** Non tentare il link senza prima validare il file. Il percorso deve essere assoluto e il file deve esistere su disco.

**Fonte:** docs/RevitCortex_IFC_Guide.md

---

## IFC: Ricostruzione Nativa Completa (7 Passi)

**Sequenza:**
1. `ifc_validate_request` -- verifica file
2. `ifc_open_or_import` -- apri come nuovo progetto (crea DirectShape)
3. `ifc_analyze_rebuildability` -- analizza cosa puo essere ricostruito
4. `ifc_rebuild_walls` / `ifc_rebuild_floors` / `ifc_rebuild_structural_members` (tutti con `dryRun: true`) -- anteprima
5. Stessi tool con `dryRun: false` -> `ifc_rebuild_openings` -> `ifc_rebuild_family_instances` -- esecuzione
6. `ifc_compare_original_vs_rebuilt` -- verifica qualita (score 0-100)
7. `ifc_tag_unreconstructable_elements` -- marca elementi non ricostruibili
**Parametri chiave:**
- Tutte le misure Revit API sono in piedi (feet). IFC usa millimetri. Conversione: 1 ft = 304.8 mm
- Score qualita: 90-100 eccellente, 70-89 buono, 50-69 discreto (verifica manuale), 0-49 scarso
- Muri curvi/inclinati vengono saltati automaticamente
- `ifc_rebuild_family_instances`: se non trova muro host entro 600mm, posiziona senza host
**NON fare:** Non saltare il dryRun al passo 4. Non saltare il passo 6 (verifica qualita). Dopo il passo 7, creare un abaco filtrato per Commenti = 'IFC_UNRECONSTRUCTABLE' per la lista completa.

**Fonte:** docs/RevitCortex_IFC_Guide.md (Workflow IFC Completo)

---

## IFC: Esportazione con Configurazione

**Sequenza:** `ifc_list_export_configurations` -> `ifc_get_export_configuration` (per dettagli) -> `ifc_export_with_configuration`
**Parametri chiave:**
- Configurazioni disponibili: IFC4 Reference View, IFC4 Design Transfer View, IFC2x3 Coordination View 2.0, IFC2x3 COBie 2.4, IFC4x3
- Override possibili tramite parametri aggiuntivi
**NON fare:** Non esportare con configurazione senza prima verificare quali sono disponibili.

**Fonte:** docs/RevitCortex_IFC_Guide.md

---

## IFC: Esportazione Rapida

**Sequenza:** `ifc_export_basic`
**Parametri chiave:**
- `fileVersion`: Default, IFC2x2, IFC2x3, IFC2x3CV2, IFC4, IFC4RV, IFC4DTV, IFC4x3
- `exportBaseQuantities`: include quantita IFC (volume, area, lunghezza)
- `wallAndColumnSplitting`: divide muri/pilastri multi-livello per piano
- `filterViewId`: esporta solo elementi visibili in una vista specifica
**NON fare:** Non usare senza specificare almeno `fileVersion` -- il default potrebbe non essere quello desiderato.

**Fonte:** docs/RevitCortex_IFC_Guide.md

---

## Prima Chiamata di Sessione: get_project_info

**Sequenza:** `get_project_info` (completo, tutti gli include = true) -> annotare livelli, fasi, link, workset -> chiamate successive con filtri
**Parametri chiave:**
- Prima chiamata: tutti gli `include*` a `true` per stabilire il contesto del modello
- Chiamate successive: `{"includeLevels": false, "includeLinks": false, "includePhases": false, "includeWorksets": false}`
**NON fare:** Non richiamare `get_project_info` senza filtri dopo la prima volta -- spreca token. Le informazioni sono gia nel contesto.

**Fonte:** CLAUDE.md (Default Parameters to Override)

---

## Audit Famiglie per Categoria

**Sequenza:** `audit_families` con `categoryFilter` specifico
**Parametri chiave:**
- Usare sempre `categoryFilter` (es. `"OST_Doors"`)
- `includeUnused: false` in operazioni normali
**NON fare:** Non lanciare `audit_families` globale (senza filtro) per trovare informazioni su una singola categoria -- il costo token e molto superiore.

**Fonte:** CLAUDE.md (Default Parameters to Override, Anti-Waste Patterns)

---

## Gestione Contesto e Sessioni

**Sequenza:** Quando il contesto accumula >50 righe di JSON (export, audit, schedule) e serve un task non correlato -> aprire una nuova conversazione
**Parametri chiave:**
- Soglia pratica: ~15,000 token cumulativi nella sessione
- Il costo di ripartire e vicino a zero; il costo di trascinare 200K+ token di contesto e reale
**NON fare:** Non mischiare task QA con task di authoring nella stessa sessione lunga. Non continuare una sessione con contesto pesante per task non correlati.

**Fonte:** CLAUDE.md (Session Patterns -- Dirty context rule, Token Estimates)

---

## Workaround: filter_by_parameter_value su Parametri di Tipo

**Sequenza:** `filter_by_parameter_value` con `parameterType: "type"`
**Parametri chiave:**
- Quando si filtra su parametri di tipo (es. nome tipo), impostare `parameterType: "type"`
- Il default `parameterType: "both"` potrebbe NON risolvere correttamente parametri stringa a livello di tipo
- Il parametro nome tipo a livello di istanza ha spesso `hasValue: false`
**NON fare:** Non usare il default "both" per filtri su nome tipo -- usare esplicitamente "type".

**Fonte:** CLAUDE.md (Tool-Specific Corrections -- filter_by_parameter_value)

---

## Workaround: operate_element e ai_element_filter Richiedono Wrapper data

**Sequenza:** Passare i parametri dentro un oggetto `data`
**Parametri chiave:**
- `operate_element`: `{"data": {"elementIds": [123], "action": "select"}}`
- `ai_element_filter`: `{"data": {"filterCategory": "OST_StructuralFraming", "includeInstances": true, "maxElements": 5}}`
**NON fare:** Non passare i parametri al livello root -- il tool fallira.

**Fonte:** CLAUDE.md (Tool-Specific Corrections)

---

## Workaround: send_code_to_revit

**Sequenza:** Scrivere codice C# usando `document` come variabile del documento
**Parametri chiave:**
- Variabile documento: `document` (NON `doc`, `Doc`, o `uidoc`)
- Per UIDocument: `new UIDocument(document)`
- ElementId: `.Value` su R2024+, `.IntegerValue` su R2023
- Namespace proibiti: `System.IO`, `System.Net`, `System.Diagnostics.Process`, `Microsoft.Win32`, `System.Reflection.Emit`, `System.Runtime.InteropServices`
**NON fare:** Non usare variabili diverse da `document`. Non usare namespace proibiti -- il sandbox li blocchera con `PermissionDenied`.

**Fonte:** CLAUDE.md (Tool-Specific Corrections, Security Requirements)

---

## Trovare Elementi con Parametri Personalizzati Vuoti (WBS / Codici)

**Obiettivo:** trovare elementi di una o più categorie dove un parametro personalizzato (es. WBS_Activity, WBS_Phase) non è popolato, e creare un abaco Revit.

**Sequenza:**
1. `ai_element_filter` su una categoria con `maxElements: 1` → ottieni un ID campione
2. `get_element_parameters` sull'ID campione con `includeTypeParameters: false` → scopri i nomi esatti dei parametri WBS_* presenti nel modello
3. `export_elements_data` con `categories: [tutte le categorie richieste]`, `parameterNames: [lista nomi WBS_*]`, `maxElements: 500` → ottieni tutti gli elementi con i valori WBS
4. Filtra lato client (o usa `filter_by_parameter_value` con `condition: "is_empty"`) per identificare quali elementi hanno WBS vuoti
5. `create_schedule` con i campi WBS scoperti + filtro `is_empty` → abaco nativo in Revit già filtrato

**Parametri chiave:**
- Step 2: usare un elemento reale per scoprire i nomi — NON indovinare "WBS_Activity"
- Step 3: specificare `parameterNames` espliciti — senza, `export_elements_data` restituisce tutti i parametri (troppi token)
- Step 5: il filtro `is_empty` in `create_schedule` funziona anche per parametri condivisi personalizzati

**NON fare:**
- Non usare `send_code_to_revit` per questa operazione — non necessario e rischia conflitti DLL (archintelligence, BIM360)
- Non usare solo `ai_element_filter` aspettandosi i parametri personalizzati — restituisce solo i campi base (Name, Id, Level)
- Non creare l'abaco manualmente in Revit se è possibile usare `create_schedule`

**Fonte:** Bug report sessione 2026-04-16 (modello Rd6, categorie OST_StructuralFraming + OST_StructuralColumns + OST_Walls + OST_Floors)

---

## Workaround: get_compound_structure su Muri non Strutturali

**Sequenza:** `get_compound_structure` restituisce `structuralLayerIndex: -1`
**Parametri chiave:**
- Su muri rainscreen o non strutturali, `structuralLayerIndex: -1` e architettonicamente corretto
- NON e un errore dati
**NON fare:** Non richiamare il tool per verificare -- il risultato e corretto. Accettare -1 come valore valido.

**Fonte:** CLAUDE.md (Anti-Waste Patterns)
