# RevitCortex — Guida Utente

> Guida pratica per usare RevitCortex in modo efficiente con Claude.

> 📖 **Cerchi l'elenco completo dei comandi?** Vedi il **[Catalogo Comandi](COMMANDS.md)** — tutti i 175 comandi con un esempio di prompt in linguaggio naturale per ciascuno.

---

## Indice

1. [Avvio rapido](#avvio-rapido)
2. [Scelta del modello Claude](#scelta-del-modello-claude)
3. [Efficienza dei token](#efficienza-dei-token)
4. [Discipline di progetto e categorie Revit](#discipline-di-progetto-e-categorie-revit)
5. [Strumenti principali](#strumenti-principali)
6. [Rebar / Reinforcement](#rebar--reinforcement)
7. [Workflow consigliati](#workflow-consigliati)
8. [Impostazioni di progetto avanzate](#impostazioni-di-progetto-avanzate)
9. [Esecuzione di codice personalizzato](#esecuzione-di-codice-personalizzato)
10. [Risoluzione dei problemi comuni](#risoluzione-dei-problemi-comuni)

---

## Avvio rapido

1. Aprire Revit e caricare un progetto
2. Nel ribbon **RevitCortex** cliccare **Cortex Switch** (icona grigia → diventa verde)
3. Avviare Claude Desktop o Claude Code
4. Iniziare a dare istruzioni in linguaggio naturale

**Indicatori di stato:**
- Icona **verde** nel ribbon = server attivo, connessione pronta
- Icona **grigia** = server fermo

---

## Scelta del modello Claude

Scegliere il modello giusto riduce tempi e costi mantenendo la qualità necessaria.

| Modello | Quando usarlo | Esempi |
|---------|--------------|--------|
| **Claude Haiku** | Query semplici, lettura dati, query veloci | "Quanti muri ci sono?", "Qual è il nome del progetto?", "Elenca le viste esistenti" |
| **Claude Sonnet** *(default consigliato)* | Creazione e modifica elementi, analisi, workflow multipli | Creare pavimenti da PDF, rinominare viste, modificare parametri in batch |
| **Claude Opus** | Ragionamento architetturale complesso, script avanzati, analisi strutturata di grandi modelli | Analisi completa di un edificio, generazione di famiglie complesse via script, debug di workflow articolati |

**Regola pratica:**
- **Lettura → Haiku**
- **Scrittura / batch → Sonnet**
- **Script complessi / ragionamento profondo → Opus**

In Claude Code si può specificare il modello con `--model claude-haiku-4-5` o nelle impostazioni.

---

## Efficienza dei token

### Richieste atomiche vs. richieste composite

Preferire richieste specifiche piuttosto che richieste vaghe e poi correggere:

```
# Meno efficiente
"Crea dei pavimenti"
"No, intendo quelli architettonici"
"Aggiungi lo strato di massetto"
"Spessore 80mm"

# Più efficiente
"Crea un tipo di pavimento architettonico 'PV_CA_001'
 con due strati: Finish (ceramica 10mm) + Substrate (massetto 80mm)"
```

### Verificare prima di creare

Prima di creare elementi in blocco, verificare cosa esiste già:

```
"Quali tipi di pavimento esistono nel progetto?"
"Ci sono materiali con 'massetto' nel nome?"
```

Questo evita duplicati e creazioni inutili.

### Fornire ID quando si conosce l'elemento

Se si conosce già l'ID di un elemento, fornirlo direttamente:

```
# Meno efficiente (richiede una ricerca)
"Modifica il pavimento che si chiama 'PV_Calcestruzzo_200'"

# Più efficiente (va diretto)
"Modifica il pavimento con id 123456"
```

### Raggruppare operazioni simili

```
# 3 chiamate separate
"Crea un muro da A a B"
"Crea un muro da B a C"
"Crea un muro da C a D"

# 1 chiamata con data array
"Crea tre muri: A→B, B→C, C→D [coordinate...]"
```

`create_line_based_element` accetta un array `data` per creare più elementi in una singola operazione.

### Usare `send_code_to_revit` per operazioni batch complesse

Quando si devono creare/modificare centinaia di elementi, uno script C# eseguito in Revit è molto più veloce di decine di tool call:

```
"Usa send_code_to_revit per creare tutti i tipi di pavimento da questo elenco: [...]"
```

### Risposte compatte (`compact: true`) — *novità v1.0.18*

Alcuni tool con payload pesante accettano un flag `compact: true` che rimuove i metadati per-elemento (uniqueId, storageType, isReadOnly, transparency, ecc.) mantenendo identificatori, contatori e nomi. Riduzione tipica: 30-50% dei token.

```
"Elenca i materiali del progetto in formato compatto"
→ Claude chiamerà get_materials con compact: true
```

I tool che supportano `compact`:

| Tool | Strippa |
|------|--------|
| `get_element_parameters` | hasValue, isReadOnly, storageType, groupName, isShared |
| `get_available_family_types` | uniqueId, fullName, isReadOnly |
| `audit_families` | isInPlace, isEditable, isUnused, kind |
| `list_schedulable_fields` | parameterId, fieldType verbose |
| `get_room_openings` | metadati apertura per elemento |
| `get_shared_parameters` | description testuale |
| `get_linked_file_instances` | matrice trasformazione (origin/basisX/basisY) |
| `get_elements_in_spatial_volume` | extras per elemento |
| `get_materials` | transparency, shininess, smoothness numerici |
| `export_room_data` | department, perimeterMm |
| `ifc_list_export_configurations` | description per configurazione |
| `ifc_analyze_rebuildability` | extras per risultato |
| `ifc_list_rebuild_candidates` | extras per candidato |
| `workflow_model_audit` | dettagli warnings/famiglie verbosi |

**Garanzie di sicurezza** (contratto rispettato dal `ToolResponseShaper`):
- Nessun elemento viene mai rimosso dalle liste
- I contatori top-level (`count`, `totalRooms`, `materialCount`, `instanceCount`, ecc.) sono sempre veritieri
- ID, nomi e categorie restano intatti

Default: `compact: false` (payload pieno). Chiedere a Claude esplicitamente "in formato compatto" per attivarlo.

---

## Discipline di progetto e categorie Revit

**Regola fondamentale**: ogni elemento in Revit appartiene a una disciplina specifica. Quando si chiede di creare elementi senza specificare la disciplina, si assume **architettonico** come default.

### Tabella delle discipline

| Elemento richiesto | Categoria default (Architettonico) | Categoria Strutturale |
|-------------------|-----------------------------------|----------------------|
| Pavimento / Floor | `OST_Floors` (FloorType) | `OST_StructuralFoundation` |
| Muro / Wall | `OST_Walls`, `WallKind.Basic` | `OST_StructuralWall` |
| Trave | `OST_StructuralFraming` | `OST_StructuralFraming` |
| Pilastro | `OST_Columns` (architettonico) | `OST_StructuralColumns` |
| Fondazione | — | `OST_StructuralFoundation` |

**Quando specificare la disciplina:**
- Per elementi strutturali: aggiungere "strutturale" o "structural" alla richiesta
- Esempio: "Crea una soletta di fondazione strutturale" → usa `create_surface_based_element` con categoria `OST_StructuralFoundation`
- Esempio: "Crea un pavimento" → usa `create_floor` con `OST_Floors` (architettonico)

### Strumenti per disciplina

| Strumento | Disciplina | Categorie |
|-----------|-----------|-----------|
| `create_floor` | Architettonico | `OST_Floors` |
| `create_surface_based_element` | Strutturale/MEP | `OST_StructuralFoundation`, `OST_Roofs`, ecc. |
| `create_line_based_element` | Tutte | Muri, travi, pilastri, ecc. |
| `create_point_based_element` | Tutte | Porte, finestre, apparecchiature, ecc. |

---

## Strumenti principali

> Per la firma completa (parametri, default, enum) di ogni tool consulta [`tool-schemas.txt`](../tool-schemas.txt) nella root del repository. Il file è aggiornato ad ogni release e viene rigenerato con `node server/generate-tool-schemas-csharp.mjs`.

### Lettura e ricerca

| Strumento | Scopo |
|-----------|-------|
| `get_element_parameters` | Legge i parametri di uno o più elementi |
| `get_element_solid_geometry` | Estensione del solido REALE di un elemento (bounding box, baricentro, volume, conteggio facce/spigoli) in mm e coordinate modello — riflette il solido dopo giunzioni/tagli, a differenza del bounding box dell'elemento |
| `get_current_view_elements` | Lista tutti gli elementi nella vista attiva |
| `filter_by_parameter_value` | Filtra elementi per valore di parametro |
| `get_room_openings` | Porte e finestre di una stanza |
| `get_elements_in_spatial_volume` | Elementi in un volume 3D |
| `ai_element_filter` | Filtro semantico in linguaggio naturale |

### Creazione elementi

| Strumento | Scopo |
|-----------|-------|
| `create_floor` | Pavimento architettonico da punti o stanza |
| `create_line_based_element` | Muri, travi, elementi lineari |
| `create_point_based_element` | Porte, finestre, arredi, apparecchiature |
| `create_surface_based_element` | Solette fondazione, tetti, pannelli |
| `create_room` | Stanze con nome e numero |
| `create_level` | Nuovi livelli |
| `create_grid` | Griglie strutturali |

### Modifica elementi

| Strumento | Scopo |
|-----------|-------|
| `bulk_modify_parameter_values` | Modifica parametri su più elementi contemporaneamente |
| `change_element_type` | Cambia tipo a uno o più elementi |
| `copy_elements` | Copia con offset |
| `delete_element` | Eliminazione singolo elemento |
| `color_elements` | Colorazione per override grafico |
| `batch_rename` | Rinomina in massa con pattern |

### Viste e fogli

| Strumento | Scopo |
|-----------|-------|
| `create_view` | Piante, sezioni, alzati, 3D |
| `duplicate_view` | Duplica con/senza dettagli |
| `create_views_from_rooms` | Genera piante per ogni stanza |
| `apply_view_template` | Applica template a più viste |
| `rename_views` | Rinomina con find/replace o pattern |
| `place_viewport` | Posiziona vista su foglio |

### Parametri

| Strumento | Scopo |
|-----------|-------|
| `add_shared_parameter` | Aggiunge un parametro condiviso a categorie. Il `dataType` di una definizione nuova viene rispettato (Text \| Integer \| Number \| Length \| Area \| Volume \| Angle \| YesNo \| URL); default Text. |
| `sync_csv_parameters` | Importa valori da CSV |
| `transfer_parameters` | Copia parametri tra elementi |
| `manage_project_parameters` | Lista, crea, elimina, modifica, raggruppa, cambia binding o (tenta di) rinomina parametri di progetto. Azioni: `list \| create \| delete \| modify \| set_group \| set_binding_type \| rename`. `delete` ora rimuove correttamente anche i parametri **non-shared** (workaround del bug Revit REVIT-136670: elimina il `ParameterElement` e verifica la rimozione). `modify` supporta `categoriesMode: add\|remove\|replace`. `set_group` cambia massivamente il "Group Parameter Under" (`parameterNames[]` + `targetGroup`, es. `IdentityData`, `Data`, `Geometry`, ...; built-in ignorati; `dryRun: true`). `set_binding_type` commuta un parametro tra istanza e tipo (`isInstance: true\|false`). `rename` **non è supportato dall'API Revit** per i parametri di progetto bound e restituisce una guida con il workaround (solo i Global Parameters sono rinominabili). |
| `manage_global_parameters` | Gestisce i Global Parameters. Azioni: `list \| get \| create \| set \| delete \| rename \| set_formula \| move_up \| move_down \| sort`. A differenza di project/shared, i global **possono** essere rinominati (`rename` + `newName`). `set_formula` pilota il valore con una formula (`formula: ""` la rimuove). `move_up`/`move_down` riordinano nel gruppo; `sort` ordina (`order: ascending\|descending`). |

### Impostazioni Progetto

| Strumento | Scopo |
|-----------|-------|
| `manage_project_units` | Legge o imposta le unità di progetto (lunghezza, area, volume, angolo, ecc.) |
| `manage_additional_settings` | Stili di linea, pesi linea, pattern linea, pattern di riempimento, halftone/underlay |

### Script e automazione

| Strumento | Scopo |
|-----------|-------|
| `send_code_to_revit` | Esegue codice C# direttamente in Revit *(Revit 2025+)* |

### Power BI Live

Integrazione diretta con Power BI push datasets (senza file intermedi).

| Strumento | Scopo |
|-----------|-------|
| `pbi_check_auth` | Verifica stato login; con `signIn=true` avvia il flusso device-code MSAL |
| `pbi_list_workspaces` | Elenca i workspace Power BI accessibili |
| `pbi_list_datasets` | Elenca i dataset push in un workspace |
| `pbi_create_dataset` | Crea un dataset push RevitCortex (idempotente per nome) |
| `pbi_publish_elements` | Pubblica snapshot elementi → tabella Elements (replace/append/create) |
| `pbi_publish_schedules` | Pubblica schedule → tabella Schedules long-form (una riga per cella) |
| `pbi_get_binding` | Mostra il binding workspace/dataset salvato per il documento attivo |
| `pbi_publish_selection` | Pubblica la selezione Revit corrente nella tabella Selection del dataset PBI | workspaceId?, datasetId?, datasetName?, clearIfEmpty? |
| `pbi_query` | Esegue una query DAX sul dataset PBI e seleziona/isola in Revit gli elementi corrispondenti | datasetId?, workspaceId?, exportRunId?, category?, ostCode?, action?, maxElements? |
| `pbi_sign_out` | Revoca il token MSAL; necessario per cambiare account |

**Visual Power BI Desktop (Phase 2C):** Il file `powerbi-visual/dist/revitcortexselectionvisual1A2B3C4D.1.0.0.4.pbiviz` è un custom visual importabile in Power BI Desktop con 5 azioni:

- **Seleziona in Revit** (pulsante grande primario) — invia gli ElementId filtrati / evidenziati a Revit
- **Isola in Revit** — `IsolateElementsTemporary` sulla vista attiva
- **Colora in Revit** — override grafico (linea di proiezione + pattern di riempimento solido) usando la colonna opzionale **Color (hex)**: una misura DAX o colonna che ritorna `#RRGGBB`
- **Crea vista 3D da selezione** — nuova `View3D` con section box sugli elementi; aggiunta al Project Browser, la vista corrente resta invariata
- **Reset override** — pulisce gli `OverrideGraphicSettings` della vista attiva

Comunica via HTTP POST su `localhost:27016` (4 endpoint: `/pbi-select`, `/pbi-color`, `/pbi-reset-overrides`, `/pbi-create-view`). Niente autenticazione MSAL, niente confirmation dialog (per annullare: Ctrl+Z in Revit o "Reset override"). Field well **Element ID** (obbligatorio, `kind: Grouping` → niente aggregazione "Count of"), **Color (hex)** (opzionale). **Palette e lingua:** UI in teal `#00838F` come il Settings; locale auto-rilevato (italiano/inglese). Dettagli tecnici in `docs/powerbi-live-phase2c-handoff.md`.

**Flusso tipico (primo utilizzo):**
1. `pbi_check_auth(signIn=true)` → copia il codice mostrato e aprilo su `https://microsoft.com/devicelogin`
2. `pbi_list_workspaces()` → copia il `workspaceId` target
3. `pbi_publish_elements(workspaceId="...", categoryFilter=["OST_Walls","OST_Floors"], mode="replace")` → il dataset viene creato automaticamente e il binding salvato
4. Publish successivi: `pbi_publish_elements()` senza parametri — workspace e dataset vengono risolti dal binding

**Note:**
- Token MSAL cifrato DPAPI in `%LOCALAPPDATA%\.revitcortex\msal_cache.bin`, valido tra le sessioni Revit
- Il binding documento→dataset è salvato in `~/.revitcortex/powerbi-live.json` (chiave stabile: UniqueId o SHA256 del path)
- I dataset push sono ottimizzati per Power BI Service (browser); su Power BI Desktop limitare a qualche centinaia di righe con `categoryFilter` o `maxElements`
- `AllowExternalWrites` deve essere `true` in `powerbi-live.json` (o `false` ma `readOnlyMode=false` nelle impostazioni generali)

---

## Rebar / Reinforcement

Categoria **`Rebar`** — 62 strumenti per l'armatura strutturale (barre, sistemi area/percorso, rete elettrosaldata, accoppiatori, sovrapposizioni, impostazioni). Coprono Revit 2023→2027. Tutti gli input dimensionali sono in **millimetri** e gli angoli in **gradi**; ID, codici `OST_*` e nomi enum sono **indipendenti dalla lingua** (non usare nomi di parametro localizzati). Prima di posare qualsiasi armatura, verificare sempre due cose: chiamare `get_rebar_host_data` sull'elemento ospite e controllare `isValidHost: true` (l'armatura può essere posata **solo in ospiti in calcestruzzo strutturale** — travi, pilastri, muri, pavimenti, fondazioni), e chiamare `get_rebar_api_capabilities` per sapere quali strumenti la versione di Revit in esecuzione supporta (alcuni sono gated per versione, vedi sotto). Gli strumenti di scrittura mostrano un dialog di conferma nativo: se l'utente annulla, restituiscono `CortexErrorCode.Cancelled`.

### Discovery (lettura)

| Strumento | Scopo |
|-----------|-------|
| `list_rebar_bar_types` | Tipi di barra disponibili nel modello |
| `list_rebar_hook_types` | Tipi di uncino |
| `list_rebar_shapes` | Forme barra (Rebar Shape) |
| `list_rebar_cover_types` | Tipi di copriferro |
| `list_rebar_splice_types` | Tipi di sovrapposizione/giunzione |
| `list_rebar_fabric_types` | Tipi di rete elettrosaldata (fabric) |
| `get_rebar_host_data` | Valida l'ospite (`isValidHost`) e ne legge i dati di armatura |
| `get_rebar_element_data` | Dati di una singola barra / set |
| `get_rebar_geometry` | Geometria delle barre (con/senza uncini, raggio di piega) |
| `get_rebar_constraints` | Vincoli correnti di una barra |
| `get_reinforcement_settings` | Impostazioni globali di armatura del documento |
| `get_rebar_api_capabilities` | Quali funzioni l'API supporta sulla versione Revit attiva |

### Barre (scrittura)

| Strumento | Scopo |
|-----------|-------|
| `create_rebar_from_shape` | Crea barre da una forma + sistema di assi (origin/xVec/yVec) |
| `create_rebar_from_curves` | Crea barre da curve esplicite |
| `create_free_form_rebar` | Crea armatura free-form da loop |
| `set_rebar_layout` | Imposta il layout del set (Single, FixedNumber, MaximumSpacing, ...) |
| `set_rebar_shape` | Cambia la forma della barra |
| `set_rebar_hooks` | Imposta uncino iniziale/finale |
| `set_rebar_terminations` | Imposta le terminazioni di estremità *(Revit 2026+)* |
| `set_rebar_host` | Sposta la barra su un nuovo ospite |
| `set_rebar_visibility` | Visibilità della barra in una vista |
| `move_rebar_in_set` | Trasla una singola barra all'interno del set |
| `include_exclude_rebar_bars` | Includi/escludi singole barre del set in una vista |
| `split_rebar` | Divide un set a una posizione |

### Sistemi Area / Percorso (scrittura + lettura)

| Strumento | Scopo |
|-----------|-------|
| `create_area_reinforcement` | Armatura ad area su un ospite (richiede `majorDirection`) |
| `create_path_reinforcement` | Armatura a percorso lungo curve |
| `set_area_reinforcement_layers` | Attiva/disattiva i layer dell'armatura ad area |
| `set_path_reinforcement_options` | Opzioni dell'armatura a percorso (offset, orientamento barre) |
| `convert_rebar_system_to_rebars` | Converte un sistema in barre singole |
| `remove_rebar_system` | Rimuove un sistema area/percorso |
| `get_area_reinforcement_data` | Dati di un'armatura ad area |
| `get_path_reinforcement_data` | Dati di un'armatura a percorso |

### Rete elettrosaldata / Fabric (scrittura + lettura)

| Strumento | Scopo |
|-----------|-------|
| `create_fabric_area` | Crea un'area di rete elettrosaldata sull'ospite |
| `create_fabric_sheet` | Crea un foglio di rete |
| `place_fabric_sheet` | Posiziona un foglio di rete su un ospite |
| `set_fabric_sheet_bend_profile` | Imposta il profilo di piega del foglio |
| `remove_fabric_reinforcement_system` | Rimuove un sistema di rete |
| `get_fabric_area_data` | Dati di un'area di rete |
| `get_fabric_sheet_data` | Dati di un foglio di rete |
| `get_fabric_wire_data` | Dati dei fili di un foglio (per direzione) |

### Avanzati: accoppiatori, vincoli, sovrapposizioni (scrittura + lettura)

| Strumento | Scopo |
|-----------|-------|
| `create_rebar_coupler` | Crea un accoppiatore tra estremità barra |
| `set_rebar_coupler_visibility` | Visibilità dell'accoppiatore in una vista |
| `manage_rebar_constraints` | Gestisce i vincoli di una barra (handle/candidate) |
| `propagate_rebar` | Propaga l'armatura ad altri ospiti — **vedi Limitazioni note** |
| `unify_rebars` | Unifica più barre in un set *(Revit 2025+)* |
| `transfer_rebar_annotations` | Ricrea le annotazioni dell'armatura tra viste (best-effort) |
| `get_rebar_coupler_data` | Dati di un accoppiatore |
| `get_rebar_constraint_candidates` | Candidati di vincolo per un handle |
| `splice_rebar` | Crea una sovrapposizione *(Revit 2025+)* |
| `remove_rebar_splice` | Rimuove una sovrapposizione *(Revit 2025+)* |
| `get_rebar_splice_data` | Dati di una sovrapposizione *(Revit 2025+)* |
| `get_rebar_splice_candidates` | Candidati di sovrapposizione *(Revit 2025+)* |

### Impostazioni, numerazione, dettagli di piega (scrittura + lettura)

| Strumento | Scopo |
|-----------|-------|
| `set_reinforcement_settings` | Imposta le opzioni globali di armatura del documento |
| `manage_rebar_rounding` | Arrotondamento lunghezza barre (vedi Limitazioni note) |
| `manage_fabric_rounding` | Arrotondamento lunghezza rete (vedi Limitazioni note) |
| `manage_rebar_numbering` | Numerazione barre — solo `set_number` (vedi Limitazioni note) |
| `create_rebar_bending_detail` | Crea un dettaglio di piega in una vista *(Revit 2024+)* |
| `modify_rebar_bending_detail` | Modifica un dettaglio di piega *(Revit 2024+)* |
| `get_rebar_rounding` | Regole di arrotondamento barre correnti |
| `get_fabric_rounding` | Regole di arrotondamento rete correnti |
| `get_rebar_numbering` | Numeri/marche di numerazione correnti |
| `get_rebar_bending_detail_data` | Dati di un dettaglio di piega *(Revit 2024+)* |

### Esempi pratici

> I parametri vettoriali (`origin`, `xVec`, `yVec`, `majorDirection`, `normal`, `translation`) sono **oggetti JSON** `{"x":..,"y":..,"z":..}` con coordinate in millimetri (non array). Le curve (`curves`/`bendProfile`) sono **array JSON** di `{"type":"line"|"arc","start":{x,y,z},"end":{x,y,z},"mid"?:{x,y,z}}`; `loops` è un array di array di curve. Il `layout` è un **oggetto JSON** `{"rule":"single|fixed_number|maximum_spacing|number_with_spacing|minimum_clear_spacing", "number"?, "arrayLengthMm"?, "spacingMm"?, "barsOnNormalSide"?, "includeFirstBar"?, "includeLastBar"?}`. Far prima `get_project_info` per i livelli e individuare l'`hostId` (l'ID dell'elemento strutturale ospite).

> **Posizionare le barre dentro il solido REALE dell'ospite.** Per calcolare le coordinate (specialmente staffe e copriferro) usare `get_element_solid_geometry` sull'`hostId`, **non** il bounding box dell'elemento. Travi e pilastri vengono tagliati/uniti da pilastri e solette: il solido armabile è più piccolo e spesso traslato rispetto al bounding box dell'elemento. Posando le barre dal bounding box si finisce in parte nel vuoto → Revit segnala *"Rebar is placed completely outside of its host"*. Inserire le coordinate dal `boundingBox`/`centroid` (in mm, coordinate modello) restituiti da `get_element_solid_geometry`, poi rientrare del copriferro (`commonCover` da `get_rebar_host_data`) più Ø staffa + metà Ø barra. Verificare ogni barra con `get_rebar_geometry` prima di scalare a N elementi.

**(a) Discovery del setup di armatura**
```
"Elenca i tipi di barra disponibili"
→ list_rebar_bar_types()

"Elenca le forme di armatura disponibili"
→ list_rebar_shapes()

"Verifica se la trave con id 998877 può ospitare armatura"
→ get_rebar_host_data con hostId: 998877
   (controllare isValidHost: true nella risposta; se false, l'elemento
    non è in calcestruzzo strutturale → marcarlo strutturale o assegnare
    un materiale calcestruzzo prima di procedere)

"Quali funzioni rebar supporta questa versione di Revit?"
→ get_rebar_api_capabilities()
```

**(b) Posa di una barra da forma (shape-driven)**
```
"Crea barre nella trave 998877 usando la forma 1234 e il tipo barra 5678,
 con origine (0,0,0), asse X (1,0,0), asse Y (0,1,0), spaziatura max 200 mm"
→ create_rebar_from_shape con
   hostId: 998877,
   shapeId: 1234,
   barTypeId: 5678,
   origin: "{\"x\":0,\"y\":0,\"z\":0}",
   xVec: "{\"x\":1,\"y\":0,\"z\":0}",
   yVec: "{\"x\":0,\"y\":1,\"z\":0}",
   layout: "{\"rule\":\"maximum_spacing\",\"spacingMm\":200,\"arrayLengthMm\":3000}"
```
(in alternativa a `shapeId`/`barTypeId` si possono usare `shapeName`/`barTypeName`)

**(c) Armatura ad area su un pavimento/muro**
```
"Crea un'armatura ad area sul pavimento 445566 con direzione principale
 (1,0,0) e tipo barra 5678"
→ create_area_reinforcement con
   hostId: 445566,
   majorDirection: "{\"x\":1,\"y\":0,\"z\":0}",
   barTypeId: 5678
```
(opzionali: `curves`, `areaTypeId`, `hookTypeId`; se l'ospite non è valido la chiamata viene rifiutata)

**(d) Ispezione di una barra creata**
```
"Mostrami i dati della barra 778899"
→ get_rebar_element_data con rebarId: 778899

"Dammi la geometria della barra 778899 senza uncini"
→ get_rebar_geometry con rebarId: 778899, suppressHooks: true
```

### Funzioni gated per versione

Chiamare sempre `get_rebar_api_capabilities` per confermare la versione di Revit attiva. Su target più vecchi questi strumenti restituiscono un **errore strutturato che indica la versione minima** richiesta:

| Funzione | Versione minima | Strumenti |
|----------|-----------------|-----------|
| Sovrapposizioni (splice) | **Revit 2025+** | `splice_rebar`, `remove_rebar_splice`, `get_rebar_splice_data`, `get_rebar_splice_candidates` |
| Unificazione barre | **Revit 2025+** | `unify_rebars` |
| Terminazioni di estremità | **Revit 2026+** | `set_rebar_terminations` |
| Dettagli di piega | **Revit 2024+** | `create_rebar_bending_detail`, `modify_rebar_bending_detail`, `get_rebar_bending_detail_data` |

### Limitazioni note

Queste sono limitazioni reali dell'API Revit emerse in fase di implementazione — non sono difetti aggirabili lato RevitCortex:

- **`propagate_rebar`**: l'API Revit **non espone** la propagazione dell'armatura in nessuna versione. Lo strumento restituisce un errore strutturato "non supportato" che rimanda al comando interattivo **Propagate** di Revit. Da considerare una limitazione nota, non una funzione operativa.
- **`manage_rebar_numbering`**: solo l'azione `set_number` funziona (scrive la marca/numero a schedario). Le azioni `renumber` e `remove_gaps` **non sono supportate** (nessuna API per lo schema di numerazione) e restituiscono un errore strutturato.
- **`transfer_rebar_annotations`**: **ricrea** (best-effort) le annotazioni dell'armatura tra viste, **non** copia gli stili di tag esatti. Gli elementi saltati sono riportati in un array `warnings` nella risposta.
- **`create_rebar_bending_detail` / `create_rebar_coupler`**: richiedono un tipo di dettaglio di piega / un tipo di famiglia accoppiatore **già presente nel modello**. Lo strumento ne risolve uno; se non ne esiste alcuno fallisce con un messaggio chiaro → caricare prima la famiglia/tipo appropriato.
- **Arrotondamento (`manage_rebar_rounding` / `manage_fabric_rounding`)**: esiste solo l'arrotondamento della **lunghezza** (length-rounding); non c'è un'opzione di arrotondamento del volume. Un eventuale input `volumeRounding` viene riportato in `warnings`, non applicato.

---

## Workflow consigliati

### Creazione pavimenti da specifiche tecniche (PDF/Excel)

1. Estrarre l'elenco dei tipi di pavimento e dei loro strati
2. Verificare i materiali esistenti: `"Elenca i materiali nel progetto"`
3. Creare i materiali mancanti con `send_code_to_revit`
4. Creare i tipi di pavimento con strati
5. Verificare: `"Mostrami i tipi di pavimento creati con i loro strati"`

**Attenzione**: specificare sempre "pavimento architettonico" o "architectural floor" per evitare di creare solette fondazione per errore.

### Rinomina massiva di elementi

```
"Rinomina tutte le viste che iniziano con 'Copia di' 
 rimuovendo il prefisso 'Copia di '"
```

Oppure con pattern:
```
"Rinomina i fogli usando il pattern: A{numero:D3} - {nome}"
```

### Colorazione per analisi

```
"Colora gli elementi per valore del parametro 'Fase':
 - Fase 1 → verde
 - Fase 2 → giallo  
 - Fase 3 → rosso"
```

### Export dati per revisione

```
"Esporta tutti i muri con area, tipo e livello in un Excel"
```

---

---

## Impostazioni di progetto avanzate

### Unità di progetto (`manage_project_units`)

```
"Quali sono le unità di progetto attuali?"
→ manage_project_units con action: "get"

"Imposta le unità di lunghezza in millimetri"
→ manage_project_units con action: "set", specType: "length", unit: "millimeters"

"Quali unità sono disponibili per le aree?"
→ manage_project_units con action: "list_valid_units", specType: "area"
```

Specifiche supportate: `length`, `area`, `volume`, `angle`, `slope`, `number`, `currency`, `mass`, `force`, `speed`, `temperature`.

### Global Parameters (`manage_global_parameters`)

I Global Parameters sono valori nominati a livello di progetto che possono pilotare quote e vincoli.

```
"Elenca tutti i parametri globali del progetto"
→ manage_global_parameters con action: "list"

"Crea un parametro globale 'AltezzaInterpiano' di tipo lunghezza con valore 3.0"
→ manage_global_parameters con action: "create", name: "AltezzaInterpiano", dataType: "length", value: "3.0"

"Imposta il valore di 'AltezzaInterpiano' a 3.2"
→ manage_global_parameters con action: "set", name: "AltezzaInterpiano", value: "3.2"

"Rinomina 'AltezzaInterpiano' in 'H_Interpiano'"
→ manage_global_parameters con action: "rename", name: "AltezzaInterpiano", newName: "H_Interpiano"

"Lega il valore a una formula"
→ manage_global_parameters con action: "set_formula", name: "H_Totale", formula: "H_Interpiano * 3"
   (formula: "" rimuove la formula)

"Riordina i parametri globali"
→ manage_global_parameters con action: "move_up" | "move_down", name: "H_Interpiano"
→ manage_global_parameters con action: "sort", order: "ascending"
```

> **Nota sui limiti dell'API Revit**: una definizione di **shared parameter** non può essere rinominata né eliminata dal file `.txt` via API (solo aggiunta). Un **parametro di progetto** bound non può essere rinominato via API (solo dalla UI di Revit) — usa il workaround: crea il nuovo, copia i valori con `transfer_parameters`, elimina il vecchio. I **Global Parameters**, invece, si rinominano senza problemi.

### Impostazioni aggiuntive (`manage_additional_settings`)

Corrisponde al menu **Manage → Additional Settings** di Revit.

```
"Elenca tutti gli stili di linea del progetto"
→ manage_additional_settings con action: "list_line_styles"

"Crea uno stile di linea 'RC_Taglio' rosso, peso 3"
→ manage_additional_settings con action: "create_line_style",
   name: "RC_Taglio", colorR: 255, colorG: 0, colorB: 0, lineWeight: 3

"Elenca tutti i pattern di riempimento (drafting e model)"
→ manage_additional_settings con action: "list_fill_patterns"

"Elenca i pattern di linea disponibili"
→ manage_additional_settings con action: "list_line_patterns"

"Imposta la luminosità halftone al 70%"
→ manage_additional_settings con action: "set_halftone", halftonePercent: 70
```

---

## Esecuzione di codice personalizzato

`send_code_to_revit` *(Revit 2025+)* compila ed esegue C# direttamente nel processo Revit.

**Quando usarlo:**
- Operazioni batch su centinaia di elementi
- Logica personalizzata non coperta dagli strumenti standard
- Creazione di tipi complessi con `CompoundStructure`

**Variabili disponibili nel codice:**
```csharp
Document document       // documento Revit attivo
UIDocument uiDocument   // UIDocument (per selezione, viste, ecc.)
Application app         // applicazione Revit
```

**Esempio — creazione tipo pavimento con strati:**
```csharp
var collector = new FilteredElementCollector(document)
    .OfClass(typeof(FloorType))
    .OfCategory(BuiltInCategory.OST_Floors)
    .Cast<FloorType>();

var baseType = collector.First();
var cs = ((HostObjAttributes)baseType).GetCompoundStructure();

// Modifica strati...
var layer = new CompoundStructureLayer(0.1 / 0.3048, MaterialFunctionAssignment.Finish1, materialId);
cs.SetLayers(new List<CompoundStructureLayer> { layer });

var newType = baseType.Duplicate("PV_Custom_001") as FloorType;
((HostObjAttributes)newType).SetCompoundStructure(cs);

return new { typeId = newType.Id.Value, name = newType.Name };
```

**Transazioni:**
- `transactionMode: "auto"` (default) — la transazione è gestita automaticamente
- `transactionMode: "none"` — nessuna transazione (per sola lettura o quando il codice gestisce le transazioni)

---

## Acciaio Strutturale (Structural Steel)

48 strumenti nella categoria `StructuralSteel` per connessioni strutturali, tipi/approvazioni, metadati di fabbricazione e tagli geometrici. Coordinate sempre in **millimetri**, angoli in gradi.

**Prima di iniziare:** chiama `get_structural_steel_api_capabilities` (nessun parametro) per sapere cosa supporta il Revit in esecuzione (versione, API custom-connection, presenza provider). Quando non sai se è installato un provider di connessioni (Autodesk Steel Connections, IDEA StatiCa), preferisci `create_generic_steel_connection`: funziona sempre, senza provider.

### Strumenti per modulo

| Modulo | Strumenti |
|--------|-----------|
| **1 – Discovery** (15, sola lettura) | `get_structural_steel_api_capabilities`, `list_steel_connection_handlers`, `list_steel_connection_types`, `list_steel_connection_handler_types`, `list_steel_approval_types`, `list_steel_connection_providers`, `get_steel_connection_data`, `get_steel_connection_type_data`, `get_steel_connection_settings`, `get_steel_element_properties`, `get_steel_external_id_map`, `get_steel_material_links`, `get_steel_element_warnings`, `get_steel_cut_data`, `analyze_structural_steel_model` |
| **2 – Connessioni** (8, scrittura) | `create_generic_steel_connection`, `create_steel_connection`, `modify_steel_connection_inputs`, `set_steel_connection_type`, `set_steel_connection_approval`, `set_steel_connection_status`, `set_steel_connection_default_order`, `delete_steel_connection` |
| **3 – Tipi & approvazioni** (9) | `create_steel_structural_connection_type`, `create_steel_connection_handler_type`, `create_default_steel_connection_handler_type`, `set_steel_connection_type_family_symbol`, `manage_steel_approval_type`, `manage_custom_steel_connection_type`, `get_steel_connection_input_points`, `get_steel_connection_applicability`, `get_steel_connection_validation` |
| **4 – Fabbricazione** (5) | `add_steel_fabrication_info`, `get_steel_element_fabrication_properties`, `set_steel_fabrication_unique_id`, `get_steel_fabrication_unique_id`, `get_steel_reference_by_fabrication_id` |
| **5 – Tagli** (8) | `add_steel_solid_cut`, `remove_steel_solid_cut`, `set_steel_solid_cut_face_splitting`, `add_steel_instance_void_cut`, `remove_steel_instance_void_cut`, `check_steel_cut_eligibility`, `get_solid_cut_relationships`, `get_instance_void_cut_relationships` |
| **6 – Provider/validazione** (3, sola lettura) | `get_structural_connection_provider_registry`, `get_structural_connection_provider_data`, `get_structural_connection_validation_info` |

### Esempi

**Capacità + elenco connessioni esistenti:**
```json
get_structural_steel_api_capabilities {}
list_steel_connection_handlers {"maxResults": 50, "summaryOnly": false}
```

**Connessione generica fra due o più elementi** (`elementIds` è un array JSON di interi):
```json
create_generic_steel_connection {"elementIds": [606883, 606885], "connectionName": "Giunto trave-pilastro"}
```

**Leggere le proprietà di fabbricazione di un elemento:**
```json
get_steel_element_properties {"elementId": 606883, "summaryOnly": false}
```

**Aggiungere un taglio solido** (l'elemento `cutElementId` taglia `targetElementId`):
```json
add_steel_solid_cut {"cutElementId": 700100, "targetElementId": 700200, "splitFaces": false, "dryRun": true}
```

**Forma degli input:** i parametri-array (`elementIds`) sono array JSON di interi; gli `inputPoints` (in `create_steel_connection`) sono oggetti `{"x":..,"y":..,"z":..}` in mm, **non** array piatti. Gli strumenti di scrittura accettano `dryRun: true` per un'anteprima senza modifiche.

### Limitazioni note (verificate sull'API Revit reale)

- **Connessioni tipizzate** (`create_steel_connection`) e approvazioni dipendono da un **provider installato** e da famiglie compatibili: senza provider restituiscono un errore strutturato "provider non disponibile". Usa `create_generic_steel_connection` quando il provider è incerto.
- **`set_steel_connection_type`** non è un setter in-place (l'API non lo espone): **ricrea** la connessione (elimina + ricrea con il nuovo tipo, preservando gli elementi connessi).
- **`manage_custom_steel_connection_type`**: la mutazione delle connessioni custom richiede Reference/Subelement selezionati interattivamente (non esprimibili via JSON) → lo strumento valida e restituisce un Fail onesto; in **Revit 2027** le API legacy `AddElementsToCustomConnection`/`RemoveMainSubelementsFromCustomConnection` sono state rimosse.
- **`manage_steel_approval_type`** supporta solo `create` e `list`: l'API non espone rename/delete.
- **Metadati di fabbricazione**: l'API `SteelElementProperties` espone solo `UniqueID` + l'associazione fabbricazione/Reference. **Non esistono** API pubbliche per code di warning, link materiali o "external id" sugli elementi steel.
- **Validazione/provider** (`get_structural_connection_validation_info`, `get_structural_connection_provider_registry/_data`): l'infrastruttura provider non è interrogabile pubblicamente → questi strumenti riportano `available: false` con una nota e, per la validazione, lo `CodeCheckingStatus` dell'handler (NotCalculated/OkChecked/CheckingFailed).
- **Tagli** (`add_steel_solid_cut`, `add_steel_instance_void_cut`, ...): sono operazioni di **geometria Revit generica** (SolidSolidCutUtils / InstanceVoidCutUtils), non specifiche dell'acciaio; la risposta lo segnala esplicitamente.

---

## Risoluzione dei problemi comuni

### Il plugin non si connette

1. Verificare che l'icona nel ribbon sia **verde** (non grigia)
2. Verificare che un documento sia aperto in Revit
3. Controllare che la porta 8080 non sia bloccata dal firewall
4. Riavviare Revit e ricliccare **Cortex Switch**

### `send_code_to_revit` restituisce errori di compilazione

- Verificare che la variabile `document` sia usata (non `doc` o `Document`)
- Usare `return` esplicito per restituire valori
- Non usare `await` / `async` — il codice è sincrono
- Per Revit 2024 e precedenti: gli ID elemento sono `int`, non `long`

### Elementi creati nella categoria sbagliata

- "Pavimento" → deve usare `create_floor` (non `create_surface_based_element`)
- "Soletta fondazione" → usare `create_surface_based_element` con `OST_StructuralFoundation`
- "Muro architettonico" → `create_line_based_element` con `OST_Walls`
- "Muro strutturale" → `create_line_based_element` con `OST_StructuralWall`

### La modalità Read-Only blocca le operazioni

Se RevitCortex è in modalità read-only, le operazioni di creazione/modifica sono bloccate.
Aprire **Settings** → disattivare **Read-Only Mode**.

### Errore "No active document"

Il server è partito ma nessun documento è aperto, oppure il documento è stato aperto dopo che il server era già in esecuzione. Soluzione: chiudere e riaprire il documento, oppure fare click su **Cortex Switch** due volte (stop → start).
