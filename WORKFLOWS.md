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

## Riassegnazione Massiva del "Group Parameter Under"

**Sequenza:** `manage_project_parameters` action=`list` (discovery nomi) -> `manage_project_parameters` action=`set_group` con `dryRun: true` -> verificare `plannedCount` -> rieseguire senza `dryRun`
**Parametri chiave:**
- `parameterNames: string[]` — bulk; in alternativa `parameterName` singolo
- `targetGroup` — short name (`IdentityData`, `Data`, `Geometry`, `Constraints`, `Materials`, `Ifc`, `Construction`, `Phasing`, `Visibility`, `Graphics`, `Structural`, ...) o ForgeTypeId completo
- I parametri **built-in** vengono skippati silenziosamente (non rigruppabili da API)
- Il file Shared Parameters NON viene toccato (gruppo memorizzato nel binding del progetto)
- I valori già compilati nelle istanze sono preservati
**NON fare:** Non ricreare il parametro per cambiarne il gruppo -- si perdono i valori. Non assumere che funzioni sui built-in (Comments, Mark, ecc.) -- vengono skippati.

**Fonte:** API `InternalDefinition.SetGroupTypeId` (Revit 2022+)

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

**REGOLA FONDAMENTALE: non usare `send_code_to_revit` in autonomia.** Se un'operazione è massiva o complessa e potrebbe beneficiare di uno script, proporre entrambe le opzioni all'utente e attendere la scelta:

> "Posso procedere con i tool standard (più chiamate, più tracciabile) oppure usare send_code_to_revit con uno script C# (più efficiente, ma bypassa le protezioni native). Quale preferisci?"

**Sequenza quando l'utente sceglie lo script:** Scrivere codice C# usando `document` come variabile del documento
**Parametri chiave:**
- Variabile documento: `document` (NON `doc`, `Doc`, o `uidoc`)
- Per UIDocument: `new UIDocument(document)`
- ElementId: `.Value` su R2024+, `.IntegerValue` su R2023
- Namespace proibiti: `System.IO`, `System.Net`, `System.Diagnostics.Process`, `Microsoft.Win32`, `System.Reflection.Emit`, `System.Runtime.InteropServices`
**NON fare:** Non passare automaticamente allo script senza chiedere. Non usare namespace proibiti -- il sandbox restituisce `PermissionDenied`.

**Fonte:** CLAUDE.md (Tool-Specific Corrections, Handling User Input Situations)

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

---

## Audit SDK Revit Locali

**Sequenza:** elencare `C:\Revit * SDK` -> controllare `Samples`, `RevitAPI.chm`, `Revit Platform API Changes and Additions.docx` -> confrontare i sample tra versioni -> mappare solo gli spunti utili su backlog RevitCortex
**Parametri chiave:**
- Le SDK servono come riferimento ufficiale e fonte di pattern API; il build del progetto usa i pacchetti `Nice3point.Revit.Api.*`, non gli assembly della SDK
- Dare priorita ai sample nuovi tra versioni, per esempio R26: `CoordinationModel`, `ElectricalConductors`, `OperatingScheduleImport`; R27: `AddinsIsolation`, `HostedWall`, `ManageLinks`
- Usare i sample Autodesk come riferimento tecnico, non copiare codice direttamente nei tool RevitCortex
**NON fare:** Non cambiare i riferimenti NuGet verso DLL locali della SDK senza una ragione precisa. Non aggiungere feature R27-only senza guardie `REVIT2027_OR_GREATER` e build cross-target R24/R25.

**Fonte:** Sessione audit SDK locali 2026-05-02


---

## Export Dati Revit → Power BI via OneDrive (B2 workflow)

**Sequenza:**
1. `get_project_info` (solo prima call della sessione, tutti i campi)
2. `push_to_powerbi` con le categorie e i parametri di interesse

**Parametri chiave:**
- `categories`: usa codici OST_* (es. `["OST_Walls","OST_Floors"]`). Ometti per esportare tutto.
- `parameterNames`: lista dei parametri da includere come colonne. Ometti per auto-discover dai primi 100 elementi.
- `outputFolder`: default `OneDrive - GPA Ingegneria Srl\RevitCortex\<NomeDocumento>\`. Puoi sovrascrivere con un path assoluto.
- `fileName`: default `elements_<timestamp>.csv`. Usa un nome fisso (es. `walls.csv`) se vuoi che PBI sovrascriva lo stesso file ad ogni refresh.
- Viene scritto anche `last_refresh.json` nella stessa cartella (utile come sorgente di una card "Ultimo aggiornamento" in PBI).

**Setup Power BI Desktop (una tantum):**
1. Get Data → Cartella (o SharePoint Folder se usi la versione cloud OneDrive)
2. Punta a `OneDrive - GPA Ingegneria Srl\RevitCortex\<NomeProgetto>\`
3. Combina i file CSV con Power Query → espandi le colonne
4. Pubblica nel workspace Premium GPA → abilita scheduled refresh (fino a 48x/giorno con Premium)

**NON fare:**
- Non usare un `fileName` con timestamp fisso e poi impostare scheduled refresh in PBI: PBI aggiungerebbe nuove righe ad ogni refresh invece di sostituirle. Usa un nome fisso oppure usa Power Query per filtrare sul file più recente tramite `last_refresh.json`.
- Non esportare tutto il modello senza filtrare categorie su modelli grandi (>50k elementi): usa sempre `categories` e `maxElements`.

**Fonte:** Implementazione sessione 2026-05-09 (architettura B2: plugin → OneDrive → PBI scheduled refresh)

---

## Wizard UI Power BI + Bidirezionalita Drillthrough (B2 + Protocol Handler)

**Sequenza:**
1. Ribbon RevitCortex -> bottone "Power BI Export"
2. Wizard step 1: scegli categorie con count (filtro testuale, profili salvabili)
3. Wizard step 2: scegli parametri con copertura % (Istanza/Tipo, hide-empty, search)
4. Wizard step 3: imposta cartella output OneDrive, nome file, sovrascrittura, auto-export al salvataggio, registrazione protocol handler -> "Esporta"
5. Power BI Desktop -> Get Data -> Cartella -> punta a OneDrive\RevitCortex\<Modello>\
6. In PBI: drillthrough su una matrice elemento -> URL `revitcortex://select?ids=ID1,ID2,...` -> Revit attiva e seleziona

**Parametri chiave:**
- I profili salvati sono in `~/.revitcortex/profiles/<Nome>.json` (uno per cliente/progetto)
- "Sovrascrivi file" = ON e indispensabile per scheduled refresh PBI con un singolo dataset
- "Auto-export al salvataggio" si applica solo durante la sessione Revit corrente (no persistence cross-restart)
- Il protocol handler viene registrato in HKCU (no admin); `~/.revitcortex/protocol/protocol.log` contiene il log delle invocazioni
- Performance: la discovery dei parametri sample 200 elementi per categoria, fa coverage % e raggruppa per gruppo Revit

**Setup PBI per drillthrough:**
1. Crea una colonna "Drillthrough URL" nel modello PBI: `"revitcortex://select?ids=" & [ElementId]`
2. Inserisci un visual "Action" o un bottone con campo URL = quella misura
3. Per multi-selezione: `"revitcortex://select?ids=" & CONCATENATEX(SELECTEDVALUES(...), ",")`

**NON fare:**
- Non usare `parameterNames` con nomi inventati: la wizard scopre i nomi reali via campionamento, fidati di quel risultato
- Non disabilitare la coverage % filter su modelli grandi (>50 categorie + 1000 parametri possibili) — il rumore in lista diventa eccessivo
- Non usare protocol handler con porte diverse da quelle della sessione attiva: il registrar usa `RevitCortexApp.Instance.Port` corrente al momento della registrazione

**Fonte:** Implementazione sessione 2026-05-09 (B2 completo: WPF wizard + ProtocolHandlerRegistrar + SelectFromPowerBiTool)

---

## Wizard Power BI v2 - Tab categorie + Mapping editor + Round-trip

**Sequenza completa:**
1. Ribbon RevitCortex -> "Power BI Export"
2. Step 1 (Sorgente): tab Model/Annotation/Analytical/Altre, scope Tutto/Vista/Selezione, oppure "Schedule esistenti del modello"
3. Step 2 (Parametri): dual-pane Available/Selected con frecce, color legend, search globale
4. Step 3 (Output): cartella + nome file + opzioni; **bottone "Mappa colonne..."** per definire alias/formula; **bottone "Applica da CSV..."** per round-trip Revit<-CSV
5. Esporta = `push_to_powerbi` invocato in-process via `RevitCortexApp.Instance.Router.Route(...)` (no TCP, no Cortex Switch necessario)

**Mapping editor (Commit 2):**
- Riga = una colonna CSV
- Tipo: `param` (nome parametro), `field` (ElementId|Category|Family|Type), `formula` (espressione con token)
- Formula tokens: `{ParamName}`, `{[Type] ParamName}`, `{ElementId|Category|Family|Type}`. Esempio: `{Family} - {Type} ({Mark})`
- Header CSV: alias custom per la colonna, vuoto = nome sorgente
- Riordina con su/giu, rimuovi, aggiungi calcolata

**Round-trip Excel<-CSV->Revit (Commit 3):**
1. `push_to_powerbi` -> CSV
2. Aprire in Excel, modificare valori (no formule, solo testi puri rispettando le unita di progetto)
3. Salvare come CSV UTF-8
4. Wizard -> "Applica da CSV..." oppure tool `import_from_powerbi`
5. Anteprima dryRun con conteggi, conferma TaskDialog, scrittura in transazione singola
6. Skipped automaticamente: `ElementId`, `Category`, `Family`, `Type` (built-in non scrivibili) + parametri read-only
7. Header `[Type] Foo` -> parametro di tipo, altrimenti istanza

**NON fare:**
- Non modificare la colonna `ElementId` nel CSV: e la chiave per ritrovare l'elemento, modificarla = riga ignorata
- Non aspettarsi che il round-trip ricostruisca formule Excel: vengono ignorate; calcoli devono essere generati nel CSV via "Mappa colonne" prima dell'export
- Non usare comma decimale in CSV se la cultura del CSV e en-US: il tool prova prima `SetValueString` (unit-aware) poi `double.Parse` (Invariant + Current), ma colonne ambigue meglio normalizzarle

**Fonte:** Commit 2 + 3 implementati 2026-05-09 ispirati a SheetLink (https://docs.dirootsone.diroots.com/docs/sheetlink-user-guide)

---

## push_table_to_powerbi - tabelle generate da Claude verso PBI

**Sequenza:**
1. Claude analizza dati Revit / clash / WBS / qualunque cosa
2. Claude chiama `push_table_to_powerbi(headers: [...], rows: [...])`
3. Il tool scrive un CSV nella stessa cartella OneDrive degli export `push_to_powerbi`
4. PBI legge la cartella unica e vede tutti i file insieme

**Esempio chiamata:**
```json
{
  "headers": ["Level", "WallVolume_m3", "FloorArea_m2", "DoorCount"],
  "rows": [
    ["Level 1", 145.6, 320.4, 12],
    ["Level 2", 132.1, 305.8, 14],
    ["Roof", 0, 280.0, 0]
  ],
  "subfolder": "Analyses",
  "fileName": "summary_by_level.csv"
}
```

**Quando usarlo:**
- L'analisi e' fatta da Claude (non e' un dump di elementi Revit)
- Multi-documento (push_to_powerbi opera solo sul documento attivo)
- Aggregati per livello/disciplina/fase
- Computi ricavati da formule custom

**Quando NON usarlo:**
- Dati che escono direttamente da Revit -> usa `push_to_powerbi` (mantiene ElementId per drillthrough)
- Schedule esistenti del modello -> `push_to_powerbi` con `scheduleIds`

**Templates di test (non richiede Revit):**
- `templates/powerbi/Generate-SampleData.ps1` genera 125 righe di test in OneDrive
- `templates/powerbi/RevitCortex-PowerQuery.pq` script M gia' configurato
- `templates/powerbi/RevitCortex-DAX-Measures.txt` misure DAX base
- `templates/powerbi/Build-Report-Steps.md` procedura passo-passo

**Fonte:** Implementato 2026-05-09 (commit 4: Claude-generated tables to PBI)

---

## PBI Live - Phase 0 (Auth + Discovery)

**Sequenza:**
1. Da Claude o tool: `pbi_check_auth` -> verifica se gia loggato (silent)
2. Se non loggato: `pbi_check_auth(signIn=true)` -> avvia MSAL device-code in background e ritorna subito (`Starting` o `AwaitingUser`), senza bloccare il main thread Revit
3. Richiamare `pbi_check_auth(signIn=false)` dopo pochi secondi per leggere `userCode` + `verificationUrl` se il primo risultato era `Starting`
4. Utente apre browser su `https://login.microsoft.com/device`, incolla codice, completa login
5. Token cache salvato cifrato in %LOCALAPPDATA%\.revitcortex\msal_cache.bin (DPAPI)
6. `pbi_list_workspaces` -> elenco gruppi PBI accessibili

**Settings:**
- `~/.revitcortex/powerbi-live.json` contiene clientId/tenantId/defaultWorkspaceId
- Per GPA usare app registration custom, non il ClientId well-known:
  - `ClientId = 05d231e9-d720-4c54-8ecd-93a85dbef40b`
  - `TenantId = 53372e72-8a4d-4a86-8745-257d91a1aafc`
- ClientId default storico = "871c010f-5e61-4fb1-83ac-98610a7e9110" (PBI Embedded Sample, well-known), ma sul tenant GPA fallisce con AADSTS65002
- AllowExternalWrites = false (push PBI bloccato in read-only mode by default)

**App registration Entra GPA:**
- Supported account types: Single tenant - GPA Ingegneria Srl
- Platform: Mobile and desktop applications / InstalledClient
- Redirect URI:
  - `http://localhost`
  - `https://login.microsoftonline.com/common/oauth2/nativeclient`
- API permissions delegated Power BI Service:
  - `Dataset.ReadWrite.All`
  - `Report.Read.All`
  - `Workspace.Read.All`
- Admin consent deve risultare `Granted for GPA Ingegneria Srl`
- Nel Manifest `allowPublicClient` deve essere esplicitamente `true`; se e' `null`, il device-code arriva alla conferma browser ma poi il token exchange fallisce con AADSTS7000218 (`client_assertion` o `client_secret` richiesto)

**Token cache:**
- Cifrato DPAPI per-user-per-machine
- Refresh automatico via MSAL
- SignOut: cancella accounts + file cache

**Architettura:**
- `PowerBiAuthService`: MSAL public-client, device-code, cache DPAPI
- `PowerBiServiceClient`: REST wrapper minimale Phase 0 (solo ListWorkspaces)
- `PowerBiSettings`: persistenza config
- `PbiCheckAuthTool` / `PbiListWorkspacesTool`: tool ICortexTool registrati via assembly scan del Plugin (oltre a Tools)
- `PbiCheckAuthTool` usa stato auth in memoria e thread background: non usare `Thread.Sleep`, `Join` o polling MSAL sul main thread Revit per aspettare il completamento login

**NON fare:**
- Non hardcodare segreti client (e' public client, niente secret necessario)
- Non chiamare HTTP REST sul main thread Revit (gia' giro su Task.Run)
- Non assumere che il device-code completi: utente puo' annullare, mostrare URL+codice in modo chiaro
- Non lasciare `allowPublicClient` a `null` nel manifest Entra

**Esito test validato 2026-05-11:**
- `pbi_check_auth(signIn=true)` ritorna in ~20 ms, Revit non si congela
- Login riuscito con account `luigi.dattilo-co@gpapartners.com`
- Token cache MSAL/DPAPI letto correttamente
- `pbi_list_workspaces` ritorna 4 workspace: `GPA BIM`, `24HBS`, `AutomatedML`, `Test`

**Fonte:** Phase 0 del piano PBI Live, allineato con docs/powerbi-live-architecture-review.md (2026-05-09), validato end-to-end 2026-05-11

---

## PBI Live - Phase 1 (Publish Elements + Schedules)

**Primo publish (workspaceId esplicito, dataset auto-creato):**
1. `pbi_check_auth(signIn=false)` → verifica token valido
2. `pbi_list_workspaces()` → trova workspaceId target (es. `Test`)
3. `pbi_publish_elements(workspaceId="...", mode="replace", categoryFilter=["OST_Walls","OST_Doors"], maxElements=2000)` → snapshot filtrato, dataset auto-creato se non esiste, binding salvato in `~/.revitcortex/powerbi-live.json`
4. Risposta: `{success:true, datasetId:"...", rowCount:N, batchCount:N}`

**Publish successivi (binding auto-risolto, no parametri necessari):**
1. `pbi_publish_elements()` → workspaceId/datasetId/datasetName risolti dal binding del documento aperto
2. `pbi_publish_schedules()` → stesso binding, tabella Schedules long-form

**Publish schedules:**
1. `pbi_publish_schedules(mode="replace")` → esporta tutti gli schedule non-template (max 5000 righe/schedule), long-form: una riga per cella
2. Con filtro: `pbi_publish_schedules(scheduleIds=[123456, 789012])` → solo gli schedule specificati

**Ispezione binding:**
- `pbi_get_binding()` → mostra il binding corrente per il documento aperto (workspaceId, datasetId, datasetName, docKey, updatedAt)

**Sign-out / cambio account:**
1. `pbi_sign_out()` → revoca cache MSAL, ritorna `previousAccount`
2. `pbi_check_auth(signIn=true)` → nuovo login device-code

**ProjectBindings — chiave documento:**
- Priorità: cloud GUID > `ProjectInformation.UniqueId` > SHA256(path normalizzato)
- Salvato in `~/.revitcortex/powerbi-live.json` sotto `ProjectBindings[docKey]`
- Aggiornato automaticamente ad ogni publish riuscito (elements o schedules)
- Schema binding: `{WorkspaceId, DatasetId, DatasetName, ProjectName, DocumentGuid, LastPathHash, SchemaVersion, UpdatedAtUtc}`

**Schema dataset (v1.0):**
- **Metadata**: ExportRunId, ExportedAt, SchemaVersion, ProjectName, ProjectNumber, RevitVersion, ElementCount, ScheduleCount
- **Elements**: ElementId, UniqueId, Category, Family, Type, Level, Phase, Area, Volume, Length, IsStructural, ... + _ExportRunId, _ExportedAt
- **Schedules**: ScheduleId, ScheduleName, RowIndex, ColumnName, ValueString, ValueNumber, _ExportRunId, _ExportedAt
- **Selection**: ElementId, UniqueId, Category, SelectedAt (Phase 2)
- Per i muri, `Elements[Level]` deve derivare dal parametro Revit `WALL_BASE_CONSTRAINT` (Base Constraint), non dai soli parametri generici di level-hosting.

**Limiti e best practice:**
- PBI push dataset ottimizzato per PBI Service (browser), non PBI Desktop → usare `categoryFilter` per limitare le righe su Desktop
- Max 10.000 righe per batch POST (gestito automaticamente dal client)
- `pbi_publish_elements` senza filtri su modelli grandi (>5000 elementi) → usare `maxElements` o `categoryFilter`
- `mode="append"` non richiede dataset esistente solo per elements (lo crea); per schedules il dataset deve esistere già

**Architettura:**
- `PowerBiElementExporter`: snapshot Revit→DTO sul main thread, detached (no Revit API dopo)
- `PowerBiScheduleExporter`: idem, long-form, salta template e titleblock revision schedules
- `PowerBiServiceClient`: REST wrapper (ListWorkspaces, ListDatasets, GetDatasetByName, CreatePushDataset, DeleteRows, PostRows)
- `ProjectDocumentKey.Compute(doc)`: chiave stabile per binding
- `RunWithoutContext<T>`: thread dedicato senza SynchronizationContext per evitare deadlock MSAL/WPF

**Esito test validato 2026-05-11:**
- Dataset creato automaticamente in workspace `Test`
- 1191 elementi (filtro OST_Walls + OST_Doors + OST_StructuralColumns) pubblicati, 29 colonne
- Tabella Elements visibile in PBI Service e PBI Desktop
- Conteggi per categoria verificati vs `analyze_model_statistics`

**NON fare:**
- Non chiamare API REST sul main thread Revit (usare sempre RunWithoutContext)
- Non aprire dataset push pesanti (~10k righe) in PBI Desktop → usare PBI Service
- Non usare `mode="append"` per un refresh completo → usare `replace`

**Fonte:** Phase 1 del piano PBI Live, implementato 2026-05-11, validato end-to-end sullo stesso giorno

---

### PBI Live — Phase 2A: Publish Selection

Publish the current Revit selection to the Power BI Selection table.

**Prerequisite:** A ProjectBinding must exist (run `pbi_publish_elements` once first).

**Flow:**
1. Select elements in Revit
2. Call `pbi_publish_selection` (no params needed if binding exists)
3. In Power BI, filter/visualize the Selection table by `SelectionSetId` or `ElementId`

**Tool:** `pbi_publish_selection(clearIfEmpty?)`

**Key behaviors:**
- Replace semantics: each call DELETEs previous rows and POSTs the new snapshot
- Empty selection with `clearIfEmpty=false` (default): returns warning, table unchanged
- Empty selection with `clearIfEmpty=true`: clears the table
- Stale binding (dataset manually deleted): falls back to name-lookup (same as publish_elements)

---

### PBI Live — Phase 2B: Query Power BI -> Select in Revit

Query the bound Power BI dataset and select matching Revit elements by returned `ElementId`.

**Prerequisite:** A ProjectBinding must exist (run `pbi_publish_elements` once first).

**Flow manuale validato:**
1. `pbi_publish_elements()` -> pubblica/aggiorna la tabella `Elements`
2. `pbi_query(exportRunId="...", maxElements=10)` -> riseleziona un publish run precedente
3. `pbi_publish_selection()` -> pubblica la selezione corrente nella tabella `Selection`
4. In Power BI creare visual o filtri su `Elements`/`Selection`

**Filtri categoria:**
- `category` filtra `Elements[Category]` con nome display, es. `pbi_query(category="Walls")`
- `ostCode` filtra `Elements[OstCode]` con codice stabile, es. `pbi_query(ostCode="OST_Walls")`
- Preferire `ostCode` nei test automatici e nei workflow multi-lingua; usare `category` solo quando si lavora consapevolmente sul nome display salvato nel dataset.

**Comandi utili:**
- `pbi_query(exportRunId="...", action="select", maxElements=10)`
- `pbi_query(ostCode="OST_Walls", action="select", maxElements=20)`
- `pbi_query(category="Walls", action="isolate", maxElements=50)`

**Esito test validato 2026-05-11 su Snowdon Towers:**
- `OST_Walls`, `OST_Floors`, `OST_StructuralColumns`, `OST_StructuralFraming`, `OST_StructuralFoundation`, `OST_GenericModel` -> elementi trovati e selezionabili via DAX su `Elements[OstCode]`
- `OST_Doors`, `OST_Windows`, `OST_Rooms`, `OST_Columns`, `OST_Ceilings`, `OST_Roofs`, `OST_Stairs`, `OST_Railings`, `OST_CurtainWallPanels`, `OST_CurtainWallMullions` -> query valida, nessun elemento nel dataset corrente

### PBI Live — Phase 2C: PBI Desktop visual → Revit selection

**Quando usare:** Selezionare elementi Revit direttamente da un report Power BI Desktop senza passare da Claude.

**Prerequisiti:**
- Plugin RevitCortex installato e Cortex Switch attivo (porta 27016 si apre automaticamente)
- Power BI Desktop (non Service) con il visual `revitcortexselectionvisual1A2B3C4D.1.0.0.0.pbiviz` importato

**Installazione del visual:**
1. Aprire Power BI Desktop
2. Nel pannello Visualizzazioni → "…" → Importa un visual da un file
3. Selezionare `powerbi-visual/dist/revitcortexselectionvisual1A2B3C4D.1.0.0.0.pbiviz`
4. Trascinare il visual sulla pagina del report
5. Nel pannello Campi, trascinare `Elements[ElementId]` nel ruolo **Element ID**

**Utilizzo:**
- **Seleziona filtrati (N)** — seleziona tutti gli elementi visibili nel report (rispetta cross-filter e slicer)
- **Seleziona highlighted (N)** — seleziona solo le righe evidenziate da un altro visual; pulsante disabilitato se count=0
- Indicatore connessione: verde = RevitCortex attivo su porta 27016, grigio = plugin non avviato

**Porta:** Il listener si apre sulla porta `27016` (una sopra il TCP bridge su `27015`). Se due istanze Revit sono aperte, la seconda salta il listener silenziosamente.
