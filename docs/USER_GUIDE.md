# RevitCortex — Guida Utente

> Guida pratica per usare RevitCortex in modo efficiente con Claude.

> 📖 **Cerchi l'elenco completo dei comandi?** La sezione [Riferimento comandi per disciplina](#riferimento-comandi-per-disciplina) elenca tutti i **288 comandi** con un esempio di prompt in linguaggio naturale per ciascuno.

---

## Indice

1. [Avvio rapido](#avvio-rapido)
2. [Scelta del modello Claude](#scelta-del-modello-claude)
3. [Efficienza dei token](#efficienza-dei-token)
4. [Discipline di progetto e categorie Revit](#discipline-di-progetto-e-categorie-revit)
5. [Riferimento comandi per disciplina](#riferimento-comandi-per-disciplina) — tutti i 288 comandi con esempi di prompt
   - [Elementi](#elementi--lettura-ricerca-creazione-modifica) · [Progetto](#progetto--modello-materiali-abachi-impostazioni) · [IFC](#ifc--import-export-link-ricostruzione) · [Viste e Tavole](#viste-e-tavole) · [File collegati](#file-collegati-e-coordinamento) · [Power BI](#power-bi-live) · [Parametri/Annotazioni/Workflow](#parametri-annotazioni-e-workflow) · [Armatura](#armatura-rebar--reinforcement) · [Acciaio Strutturale](#acciaio-strutturale-structural-steel)
6. [Workflow consigliati](#workflow-consigliati)
7. [Impostazioni di progetto avanzate](#impostazioni-di-progetto-avanzate)
8. [Esecuzione di codice personalizzato](#esecuzione-di-codice-personalizzato)
9. [Risoluzione dei problemi comuni](#risoluzione-dei-problemi-comuni)

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


## Riferimento comandi per disciplina

> Tutti i 288 comandi di RevitCortex, raggruppati per disciplina, con un esempio di prompt in linguaggio naturale per ciascuno. Non serve conoscere il nome tecnico del comando: descrivi a Claude l'obiettivo.

### Elementi — lettura, ricerca, creazione, modifica

Questa sezione raccoglie i comandi per leggere, cercare, creare e modificare gli elementi del modello Revit. Parli a Claude in linguaggio naturale descrivendo l'obiettivo: non serve conoscere il nome tecnico del tool. Promemoria utili:

- Le **categorie** usano codici `OST_` indipendenti dalla lingua (`OST_Walls`, `OST_Doors`, `OST_StructuralFraming`, `OST_StructuralColumns`, `OST_Rooms`...). Su modelli architettonici i pilastri sono `OST_Columns`, non `OST_StructuralColumns`.
- Le **coordinate** si esprimono in **millimetri**.
- I comandi **distruttivi** (cancellazione, modifica massiva di parametri, materiali, rinomina) mostrano una **conferma nativa di Revit** prima di agire.
- I comandi con `dryRun` permettono un'**anteprima**: chieda prima "in anteprima" e poi confermi l'esecuzione.

#### Lettura e ispezione

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `get_selected_elements` | Restituisce gli elementi attualmente selezionati nella vista. | "Cosa ho selezionato adesso?" |
| `get_element_parameters` | Legge tutti i parametri (istanza ed eventualmente tipo) di uno o più elementi. | "Mostrami i parametri di questo muro" |
| `get_element_solid_geometry` | Estrae la geometria solida reale di un elemento (non il bounding box). | "Dammi la geometria solida della trave 408122" |
| `get_elements_by_unique_id` | Recupera gli elementi a partire dai loro UniqueId. | "Trova gli elementi con questi UniqueId" |
| `get_current_view_elements` | Elenca gli elementi visibili nella vista attiva, filtrabili per categoria e campi. | "Elenca le porte e le finestre della vista corrente" |
| `get_elements_in_spatial_volume` | Trova gli elementi contenuti in un volume (vano, sezione 3D o box personalizzato). | "Quali elementi strutturali stanno dentro questo vano?" |
| `get_room_openings` | Elenca le aperture (porte/finestre) dei vani, con conteggi o dettaglio. | "Quante aperture ha ogni vano del piano terra?" |
| `get_linked_elements` | Legge gli elementi dei modelli collegati, filtrabili per categoria. | "Mostrami i pilastri del modello strutturale collegato" |
| `export_room_data` | Esporta i dati dei vani (numero, nome, area, livello). | "Esportami i dati dei primi 20 vani" |
| `measure_between_elements` | Misura la distanza tra due elementi o due punti. | "Misura la distanza tra queste due colonne" |

#### Ricerca e filtro

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `ai_element_filter` | Filtra gli elementi per categoria, tipo/istanza e livello con logica combinabile. | "Trova le prime 50 travi strutturali del primo piano" |
| `export_elements_data` | Esporta gli elementi con parametri scelti e filtri semplici (1 parametro). | "Esporta tutti i muri con i loro parametri filtrando per Livello = Piano 1" |
| `filter_by_parameter_value` | Filtra per valore di parametro con condizioni (uguale, vuoto, range, AND/OR). | "Trova le porte con il parametro Contrassegno vuoto" |
| `find_undimensioned_elements` | Individua gli elementi senza quote in una vista. | "Quali muri non sono quotati in questa pianta?" |
| `find_untagged_elements` | Individua gli elementi senza cartiglio/etichetta in una vista. | "Trovami le porte non etichettate in questa vista" |

#### Creazione di elementi

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `create_room` | Crea un vano su un livello in una posizione (x, y). | "Crea un vano chiamato Ufficio sul Piano 1 in posizione 5000, 3000" |
| `create_floor` | Crea un pavimento da un contorno di punti o dal perimetro di un vano. | "Crea un pavimento seguendo il contorno di questo vano" |
| `create_filled_region` | Crea una regione riempita in una vista da un contorno (con eventuali fori). | "Disegna una regione riempita su questa area della pianta" |
| `create_grid` | Crea o gestisce griglie (singole o matrice con passo X/Y). | "Crea una griglia 5x4 con passo 6000 mm" |
| `create_level` | Crea o gestisce livelli con quota e flag di piano edificio. | "Aggiungi un livello a quota 3500 mm chiamato Piano 2" |
| `create_structural_framing_system` | Genera un sistema di travi su un livello entro un rettangolo con passo dato. | "Genera un sistema di travi sul Piano 1 con passo 1500 mm" |
| `create_array` | Crea una serie (array) lineare o radiale di elementi esistenti. | "Crea una serie di 6 colonne con passo 4000 mm in X" |
| `create_point_based_element` | Crea elementi posizionati su punto (es. famiglie puntuali, arredi, colonne). | "Posiziona questa famiglia di pilastri nei punti indicati" |
| `create_line_based_element` | Crea elementi basati su linea (es. muri, travi). | "Crea un muro da questo punto a quest'altro" |
| `create_surface_based_element` | Crea elementi basati su superficie (es. pavimenti, controsoffitti). | "Crea una superficie su questo contorno" |
| `duplicate_family_type` | Duplica un tipo di famiglia con nuovo nome ed eventuali override di parametri. | "Duplica questo tipo di porta come Porta 90x210 modificando la larghezza" |
| `load_family` | Carica famiglie nel progetto da file o cartella. | "Carica questa famiglia di finestre nel progetto" |

#### Modifica e trasformazione

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `set_element_parameters` | Imposta i parametri di uno o più elementi specifici (richiede conferma). | "Imposta il Commento di questo elemento a 'Verificato'" |
| `match_element_properties` | Copia parametri da un elemento sorgente verso più elementi destinazione. | "Copia i parametri di questo muro su tutti gli altri selezionati" |
| `change_element_type` | Cambia il tipo di uno o più elementi indicando l'ID o il nome del tipo. | "Cambia il tipo di queste finestre in Finestra 120x150" |
| `modify_element` | Sposta, ruota, specchia o copia con offset gli elementi (richiede conferma). | "Sposta queste colonne di 2000 mm in X" |
| `copy_elements` | Copia gli elementi tra viste o documenti con offset opzionale. | "Copia questi elementi nella vista del Piano 2 con offset 0,0,3500" |
| `operate_element` | Esegue azioni rapide sugli elementi (es. seleziona, isola, nascondi). | "Seleziona questi elementi nella vista" |
| `batch_rename` | Rinomina in blocco gli elementi con find/replace, prefisso o suffisso (anteprima con dryRun). | "Rinomina in anteprima i tipi sostituendo 'STD' con 'STANDARD'" |
| `renumber_elements` | Rinumera gli elementi su un parametro con numero iniziale, incremento e ordinamento (anteprima con dryRun, richiede conferma). | "Rinumera in anteprima le porte partendo da 1 ordinate per livello" |
| `rename_families` | Rinomina famiglie e/o tipi in blocco per categoria (anteprima con dryRun). | "Aggiungi il prefisso 'GPA_' a tutte le famiglie di porte, prima in anteprima" |
| `color_elements` | Colora gli elementi di una categoria per valore di parametro (solo viste modello, non Tavole). | "Colora i muri in base al loro tipo nella pianta corrente" |
| `set_element_phase` | Assegna la fase agli elementi (solo modelli con fasi; richiede conferma). | "Imposta la fase di creazione di questi elementi a 'Stato di progetto'" |
| `set_element_workset` | Assegna il workset agli elementi (solo modelli workshared; richiede conferma). | "Sposta questi muri nel workset Architettura" |
| `set_material_properties` | Modifica le proprietà dei materiali (anteprima con dryRun; richiede conferma). | "Cambia il colore di questo materiale, prima in anteprima" |
| `delete_element` | Cancella uno o più elementi (anteprima con dryRun; richiede conferma). | "Cancella in anteprima questi elementi, poi eliminali" |

#### Selezioni salvate

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `save_selection` | Salva la selezione corrente con un nome riutilizzabile. | "Salva questa selezione come 'Travi piano 1'" |
| `load_selection` | Richiama una selezione salvata e la applica nella vista. | "Carica la selezione 'Travi piano 1'" |
| `delete_selection` | Elimina una selezione salvata per nome. | "Elimina la selezione salvata 'Travi piano 1'" |

#### Import / Export e Power BI

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `export_to_excel` | Esporta gli elementi e i loro parametri in un file Excel. | "Esporta tutti i muri in Excel con i loro parametri" |
| `import_from_excel` | Aggiorna i parametri degli elementi da un file Excel (anteprima con dryRun). | "Aggiorna i parametri dei muri da questo Excel, prima in anteprima" |
| `export_families` | Esporta le famiglie del progetto in una cartella, opzionalmente per categoria. | "Esporta tutte le famiglie di porte in questa cartella" |
| `push_to_powerbi` | Pubblica elementi/parametri verso Power BI con scope configurabile. | "Pubblica tutti i muri su Power BI con i parametri scelti" |
| `push_table_to_powerbi` | Pubblica una tabella personalizzata (intestazioni + righe) verso Power BI. | "Manda questa tabella di quantità a Power BI" |
| `import_from_powerbi` | Importa dati da Power BI e li applica agli elementi tramite colonna ID (anteprima con dryRun). | "Importa da Power BI e aggiorna gli elementi via ElementId, in anteprima" |
| `select_from_powerbi` | Seleziona/evidenzia in Revit gli elementi indicati da Power BI tramite i loro ID. | "Seleziona in Revit gli elementi che ho filtrato in Power BI" |

#### Avanzato

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `send_code_to_revit` | Esegue codice C# personalizzato sul modello (uso avanzato; previa conferma esplicita dell'utente). | "Esegui questo script C# sul modello" |

#### Esempi di workflow

**1. Esportazione filtrata in Excel.**
"Esporta in Excel tutti i muri (`OST_Walls`) del Piano 1 con i parametri Area, Volume e Tipo." → Claude usa `export_elements_data`/`export_to_excel` filtrando su Livello e includendo solo i parametri richiesti, mantenendo l'`ElementId` per il successivo re-import.

**2. Aggiornamento massivo con anteprima.**
"Trova le porte (`OST_Doors`) con il Contrassegno vuoto, poi rinumerale partendo da 1 ordinate per livello." → Claude individua gli elementi con `filter_by_parameter_value` (condizione "vuoto"), mostra l'anteprima con `renumber_elements` in `dryRun: true`, e dopo la conferma esegue la rinumerazione definitiva.

**3. Round-trip Power BI e controllo visivo.**
"Pubblica le travi (`OST_StructuralFraming`) su Power BI; quando ho filtrato quelle critiche, riselezionale in Revit e coloramele per tipo." → Claude usa `push_to_powerbi` per la pubblicazione, `select_from_powerbi` per riportare la selezione filtrata nel modello e `color_elements` (su una vista modello, non Tavola) per la verifica visiva per tipo.

### Progetto — modello, materiali, abachi, impostazioni

Questa sezione raccoglie i comandi per interrogare lo stato del modello, gestire materiali e abachi, configurare unità, fasi, workset, revisioni e impostazioni di progetto, oltre alle operazioni di pulizia e ai collegamenti CAD/RVT. Parli a Claude in linguaggio naturale: lui sceglie il comando giusto. Le categorie negli esempi usano i codici `OST_` indipendenti dalla lingua (`OST_Walls`, `OST_Doors`, `OST_StructuralFraming`…), le coordinate sono in millimetri. I comandi distruttivi (eliminazione, purga, modifica struttura) mostrano una finestra di conferma in Revit prima di agire.

#### Diagnostica e lettura del modello

Comandi di sola lettura per capire com'è fatto il modello e dove sono i problemi. Sono leggeri e si possono combinare liberamente.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `check_model_health` | Restituisce un punteggio di salute del modello con la lista sintetica dei problemi principali. | "Fai un controllo rapido della salute del modello" |
| `analyze_model_statistics` | Produce statistiche complessive del modello (conteggi per categoria, elementi, viste). | "Dammi le statistiche generali del modello in versione compatta" |
| `get_warnings` | Elenca gli avvisi (warning) di Revit, limitabili in numero. | "Mostrami i primi 10 avvisi del modello" |
| `get_project_info` | Restituisce le informazioni di progetto e, su richiesta, livelli, fasi, workset e link. | "Dammi le info del progetto con livelli e fasi" |
| `get_current_view_info` | Restituisce nome, tipo e proprietà della vista attualmente attiva in Revit. | "Su quale vista sono adesso?" |
| `lines_per_view_count` | Conta le linee di dettaglio per vista (passaggio singolo, sicuro anche su modelli grandi) più il totale di linee di modello a livello progetto. | "Trova le viste con più di 50 linee" |
| `get_phases` | Elenca le fasi presenti nel progetto. | "Quali fasi ci sono nel modello?" |
| `get_worksets` | Elenca i workset del modello condiviso. | "Mostrami i workset del progetto" |

#### Famiglie, parametri condivisi e clash

Strumenti per ispezionare famiglie e tipi, esaminare i parametri condivisi e verificare le interferenze tra categorie.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `audit_families` | Verifica le famiglie del progetto (inutilizzate, di sistema, ordinabili) per categoria. | "Verifica le famiglie delle porte ed escludi quelle inutilizzate" |
| `get_available_family_types` | Elenca i tipi di famiglia disponibili, filtrabili per categoria e nome. | "Elenca i tipi di famiglia disponibili per i muri in versione compatta" |
| `list_family_sizes` | Elenca le famiglie ordinate per dimensione/peso su disco, utile per alleggerire il file. | "Mostrami le 10 famiglie più pesanti del modello" |
| `get_shared_parameters` | Elenca i parametri condivisi presenti, filtrabili per categoria. | "Quali parametri condivisi ci sono sulle porte?" |
| `export_shared_parameter_file` | Esporta il file dei parametri condivisi (.txt) in un percorso indicato. | "Esporta il file dei parametri condivisi sul desktop" |
| `clash_detection` | Rileva le interferenze tra due categorie con tolleranza e numero massimo di risultati. | "Controlla le interferenze tra `OST_StructuralFraming` e `OST_Walls`" |

#### Materiali

Lettura, creazione, duplicazione ed eliminazione dei materiali, più l'estrazione delle quantità per computo.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `get_materials` | Elenca tutti i materiali del progetto. | "Elenca i materiali del modello in versione compatta" |
| `get_material_properties` | Restituisce le proprietà di un materiale (per ID o per nome). | "Mostrami le proprietà del materiale Calcestruzzo" |
| `get_material_quantities` | Estrae le quantità di materiale per categoria o sulla selezione corrente. | "Dammi le quantità di materiale dei muri" |
| `create_material` | Crea un nuovo materiale con nome, classe e colore opzionali. | "Crea un materiale Acciaio S275 di classe metallo, colore grigio" |
| `duplicate_material` | Duplica un materiale esistente con un nuovo nome. | "Duplica il materiale Calcestruzzo chiamandolo Calcestruzzo C30/37" |
| `delete_material` | Elimina un materiale per ID o per nome (richiede conferma). | "Elimina il materiale Test inutilizzato" |

#### Abachi (schedule)

Creazione, duplicazione, lettura, modifica ed esportazione degli abachi, più la scoperta dei campi disponibili.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `list_schedulable_fields` | Elenca i campi disponibili per un abaco di una data categoria/tipo. | "Quali campi posso mettere in un abaco delle porte? Solo i nomi" |
| `create_schedule` | Crea un nuovo abaco per una categoria con i campi indicati; `scheduleType` supporta regular, material_takeoff, key_schedule, sheet_list, view_list. | "Crea un abaco delle porte con Contrassegno, Livello e Larghezza" |
| `create_preset_schedule` | Crea un abaco da un modello predefinito (preset) pronto all'uso. | "Crea l'abaco preimpostato dei locali" |
| `get_schedule_data` | Legge le righe di un abaco esistente, con limite di righe impostabile. | "Leggi le prime 20 righe dell'abaco delle finestre" |
| `modify_schedule` | Modifica un abaco (campi, ordinamenti, filtri, rinomina). | "Aggiungi un filtro all'abaco porte per mostrare solo il livello 1" |
| `duplicate_schedule` | Duplica un abaco esistente assegnandogli un nuovo nome. | "Duplica l'abaco delle porte chiamandolo Porte REI" |
| `delete_schedule` | Elimina un abaco per ID o per nome (richiede conferma). | "Elimina l'abaco di prova" |
| `export_schedule` | Esporta un abaco in un formato indicato (es. CSV/TXT). | "Esporta l'abaco dei muri in CSV" |

#### Fogli, revisioni ed esportazione

Creazione fogli, gestione revisioni ed export massivo di fogli/viste.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `create_sheet` | Crea un nuovo foglio con numero, nome e cartiglio opzionale. | "Crea il foglio A-101 chiamato Pianta Piano Terra" |
| `create_revision` | Crea o gestisce una revisione e la assegna ai fogli. | "Crea una revisione del 02/06/2026 e assegnala ai fogli selezionati" |
| `batch_export` | Esporta in blocco fogli e/o viste in una cartella di output. | "Esporta tutti i fogli in PDF nella cartella Consegna" |

#### Strutture stratificate

Lettura e modifica della struttura composta (layer) dei tipi di muro, pavimento, tetto.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `get_compound_structure` | Restituisce gli strati della struttura composta di un tipo (per ID o nome). | "Mostrami la stratigrafia del muro Tamponamento 30cm" |
| `set_compound_structure` | Modifica gli strati di un tipo (supporta anteprima con dryRun). | "Modifica la stratigrafia del tipo muro 123, prima fammi l'anteprima" |
| `duplicate_system_type` | Duplica un tipo di sistema (muro/pavimento/tetto) con un nuovo nome. | "Duplica il tipo muro Base chiamandolo Tamponamento 35cm" |

#### Impostazioni di progetto, unità, fasi e workset

Configurazione delle informazioni di progetto, unità, filtri di fase, workset e impostazioni grafiche aggiuntive.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `set_project_info` | Imposta le informazioni di progetto (nome, numero, indirizzo, committente, autore…). | "Imposta nome progetto Nuova Sede, committente GPA, autore Luigi" |
| `manage_project_units` | Gestisce le unità di progetto per tipo di grandezza (es. lunghezza, area). | "Imposta le unità di lunghezza in millimetri con due decimali" |
| `manage_phase_filters` | Gestisce i filtri di fase del progetto (creazione, stato, presentazione). | "Crea un filtro di fase che mostra solo le demolizioni" |
| `manage_worksets` | Gestisce i workset (crea, rinomina, elimina). | "Crea un workset chiamato Strutture" |
| `manage_additional_settings` | Gestisce impostazioni aggiuntive (spessori linea, pattern, colori, retino). | "Crea uno stile di linea rosso spessore 5" |
| `manage_links` | Gestisce i collegamenti del modello (ricarica, scarica, cambia percorso). | "Ricarica il link strutturale" |

#### Collegamenti CAD e pulizia del modello

Pulizia degli import/link CAD ed eliminazione degli elementi inutilizzati.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `cad_link_cleanup` | Pulisce link e import CAD (eliminazione import e/o link, anche per ID). | "Elimina tutti gli import CAD del modello" |
| `purge_unused` | Purga gli elementi inutilizzati, opzionalmente template di vista e filtri (supporta anteprima con dryRun). | "Fai un'anteprima della purga degli elementi inutilizzati" |

#### Esempi di workflow

Scenari reali che combinano più comandi in un'unica richiesta in linguaggio naturale.

- **Check del mattino completo**: "Fammi il controllo di salute del modello, poi mostrami i primi 10 avvisi e infine verifica le interferenze tra `OST_StructuralFraming` e `OST_Floors`." Claude esegue `check_model_health`, `get_warnings` (limitato a 10) e `clash_detection`, restituendo solo i numeri utili senza appesantire la sessione.

- **Setup nuovo materiale e computo**: "Crea un materiale Acciaio S355 di classe metallo, poi dammi le quantità di materiale per `OST_StructuralFraming` e creami un abaco del telaio strutturale con Tipo, Lunghezza e Volume." Qui si concatenano `create_material`, `get_material_quantities` e `create_schedule` (preceduto eventualmente da `list_schedulable_fields` per scoprire i campi giusti).

- **Pulizia e consegna**: "Fai prima l'anteprima della purga degli inutilizzati, poi elimina gli import CAD, imposta le info di progetto con committente GPA e numero 2026-014, e infine esporta tutti i fogli in PDF nella cartella Consegna." Claude usa `purge_unused` con dryRun per l'anteprima, `cad_link_cleanup`, `set_project_info` e `batch_export`; le operazioni distruttive chiedono conferma in Revit prima di agire.

### IFC — import, export, link, ricostruzione

Questa disciplina copre l'intero ciclo di vita dei dati IFC in Revit: capire cosa il modello sa fare, esportare verso IFC (con o senza configurazioni salvate), collegare o aprire file IFC ricevuti da terzi, e infine la ricostruzione "nativa" — trasformare gli elementi IFC importati (spesso oggetti generici poco editabili) in muri, solai, coperture, strutture e aperture Revit veri e propri.

Tutti i comandi accettano i tuoi prompt in linguaggio naturale: non devi conoscere i nomi tecnici qui sotto, ti basta descrivere l'obiettivo. Le coordinate sono in millimetri. I comandi di ricostruzione (`ifc_rebuild_*`) modificano il modello: supportano un'anteprima `dryRun` e mostrano una conferma prima di scrivere.

#### Capacità, configurazioni e validazione

Comandi di sola lettura per capire cosa supporta l'ambiente IFC e per controllare un file/richiesta prima di agire.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `ifc_get_capabilities` | Riporta le funzionalità IFC disponibili (versioni esportabili, supporto link/import, ricostruzione). | "Cosa puoi fare con i file IFC in questo modello?" |
| `ifc_list_export_configurations` | Elenca le configurazioni di esportazione IFC salvate nel progetto. | "Mostrami le configurazioni di export IFC disponibili" |
| `ifc_get_export_configuration` | Mostra i dettagli di una specifica configurazione di esportazione IFC. | "Fammi vedere i dettagli della configurazione di export 'IFC2x3 Coordinamento'" |
| `ifc_validate_request` | Verifica che un percorso file IFC sia valido e gestibile prima di import/link. | "Controlla se questo file IFC è valido prima di importarlo" |
| `ifc_set_family_mapping_file` | Imposta il file di mappatura famiglie usato durante import/esportazione IFC. | "Usa questo file di mapping famiglie per l'import IFC" |

#### Esportazione verso IFC

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `ifc_export_basic` | Esporta il modello (o una vista) in IFC con opzioni base come versione, suddivisione muri/pilastri e quantità. | "Esporta il modello in IFC2x3 nella cartella di progetto con le base quantities" |
| `ifc_export_with_configuration` | Esporta in IFC usando una configurazione salvata, per risultati ripetibili e conformi. | "Esporta in IFC usando la configurazione 'IFC4 Reference View' nella cartella export" |

#### Import e collegamento di file IFC

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `ifc_open_or_import` | Apre o importa un file IFC nel modello, scegliendo l'azione e l'intento (es. unione automatica geometrie). | "Importa questo file IFC nel modello unendo automaticamente le geometrie" |
| `ifc_link` | Collega un file IFC come riferimento esterno, generando il modello Revit associato. | "Collega il file strutturale IFC del fornitore come link" |
| `ifc_reload_link` | Ricarica un link IFC esistente, eventualmente puntando a un nuovo file aggiornato. | "Ricarica il link IFC strutturale con la versione aggiornata del file" |

#### Analisi della ricostruzione (sola lettura)

Prima di ricostruire, queste analisi ti dicono cosa è convertibile, con quale affidabilità, e come confrontare il risultato con l'originale.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `ifc_analyze_rebuildability` | Analizza quali elementi IFC importati possono essere ricostruiti come elementi Revit nativi. | "Analizza quali muri IFC importati posso ricostruire come elementi nativi" |
| `ifc_list_rebuild_candidates` | Elenca gli elementi candidati alla ricostruzione, filtrabili per categoria e confidenza minima. | "Elencami i candidati alla ricostruzione con confidenza almeno 0.8" |
| `ifc_compare_original_vs_rebuilt` | Confronta un elemento IFC originale con la sua versione ricostruita per verificarne la fedeltà. | "Confronta l'elemento originale 12345 con quello ricostruito 67890" |

#### Ricostruzione di elementi nativi

Questi comandi convertono gli elementi IFC importati in elementi Revit editabili. Sono operazioni di scrittura: usa prima l'anteprima `dryRun` per vedere il risultato, poi conferma per eseguire (richiede conferma).

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `ifc_rebuild_walls` | Ricostruisce gli elementi IFC selezionati come muri Revit nativi, con tipo e flag strutturale. | "Ricostruisci questi elementi IFC come muri nativi, prima in anteprima" |
| `ifc_rebuild_floors` | Ricostruisce gli elementi IFC selezionati come solai Revit nativi. | "Trasforma questi IFC importati in solai Revit, mostrami prima l'anteprima" |
| `ifc_rebuild_roofs` | Ricostruisce gli elementi IFC selezionati come coperture Revit native. | "Ricostruisci queste coperture IFC come tetti nativi" |
| `ifc_rebuild_structural_members` | Ricostruisce gli elementi IFC come membrature strutturali native (travi/pilastri). | "Ricostruisci queste travi IFC come elementi strutturali nativi in anteprima" |
| `ifc_rebuild_family_instances` | Ricostruisce gli elementi IFC come istanze di famiglia Revit native. | "Converti questi oggetti IFC in istanze di famiglia native, prima in dryRun" |
| `ifc_rebuild_openings` | Ricostruisce le aperture IFC come aperture native negli host indicati. | "Ricostruisci queste aperture IFC sui muri host indicati, in anteprima" |
| `ifc_tag_unreconstructable_elements` | Contrassegna con un valore gli elementi IFC che non è possibile ricostruire. | "Marca come non ricostruibili questi elementi IFC con l'etichetta 'NO_REBUILD'" |

#### Esempi di workflow

**1. Ricezione e validazione di un IFC di un fornitore strutturale**
> "Ho ricevuto il file IFC strutturale del fornitore. Prima controlla che sia valido, poi collegalo come link al modello e dimmi quali categorie contiene. La settimana prossima quando mi mandano l'aggiornamento ricaricheremo lo stesso link col file nuovo."
Questo combina `ifc_validate_request` per il controllo preliminare, `ifc_link` per il collegamento e, in seguito, `ifc_reload_link` per aggiornare il link senza ricrearlo.

**2. Da IFC generico a modello Revit nativo ed editabile**
> "Importa questo IFC architettonico, poi analizza quali elementi posso ricostruire come nativi con confidenza alta. Mostrami i candidati muri e solai, ricostruiscili prima in anteprima e, se la geometria è fedele rispetto agli originali, procedi. Gli elementi che non si possono convertire marcali come non ricostruibili."
Qui si concatenano `ifc_open_or_import`, `ifc_analyze_rebuildability` e `ifc_list_rebuild_candidates` per la pianificazione, poi `ifc_rebuild_walls` e `ifc_rebuild_floors` in dryRun (con `ifc_compare_original_vs_rebuilt` per la verifica), infine `ifc_tag_unreconstructable_elements` sui residui.

**3. Export IFC ripetibile e conforme per la consegna BIM**
> "Verifica quali configurazioni di export IFC abbiamo salvate, mostrami i dettagli di quella di coordinamento, e poi esporta il modello con quella configurazione nella cartella di consegna. Se non andasse bene, fai un export base in IFC2x3 con le base quantities attive."
Combina `ifc_list_export_configurations` e `ifc_get_export_configuration` per la scelta, `ifc_export_with_configuration` per l'export conforme e `ifc_export_basic` come fallback. Un `ifc_get_capabilities` iniziale conferma quali versioni IFC sono esportabili.

### Viste e Tavole

Questa sezione raccoglie i comandi per creare e gestire viste (piante, sezioni, 3D), tavole, viewport, template di vista, filtri grafici e override grafici. Parli a Claude in linguaggio naturale descrivendo il risultato che vuole ottenere: ci pensa lui a scegliere e comporre i comandi giusti. Ricordi che le coordinate sono sempre in millimetri e le categorie usano i codici `OST_` indipendenti dalla lingua del modello (es. `OST_Walls`, `OST_Doors`, `OST_StructuralFraming`). Alcune operazioni modificano il modello e mostrano una finestra di conferma in Revit prima di eseguire.

#### Creare e duplicare viste

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `create_view` | Crea una nuova vista (pianta, sezione, 3D, ecc.) su un livello, con scala, livello di dettaglio e crop opzionali. | "Crea una pianta del piano terra in scala 1:50 con livello di dettaglio Fine" |
| `duplicate_view` | Duplica una vista esistente, con opzione di duplicazione semplice, con dettagli o come dipendente. | "Duplica questa pianta mantenendo i dettagli" |
| `create_views_from_rooms` | Genera automaticamente una vista per ciascun locale selezionato, con offset, scala e schema di denominazione. | "Crea una pianta per ogni vano selezionato con un margine di 500 mm" |
| `manage_unplaced_views` | Gestisce le viste non ancora posizionate su tavola (elenca, individua, ecc.). | "Mostrami tutte le viste non ancora posizionate su nessuna tavola" |

#### Modificare e rinominare viste

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `batch_modify_view_range` | Modifica in blocco il view range (piano di taglio, offset superiore/inferiore, profondità) di più viste. | "Imposta il piano di taglio a 1200 mm su tutte queste piante" |
| `rename_views` | Rinomina le viste in blocco con prefisso, suffisso o trova-e-sostituisci. | "Aggiungi il prefisso ARCH_ a tutte le viste selezionate" |
| `section_box_from_selection` | Attiva e adatta il section box di una vista 3D agli elementi selezionati (o alla selezione corrente se non specificati). | "Limita il section box agli elementi che ho selezionato" |

#### Template e filtri di vista

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `apply_view_template` | Applica (o rimuove) un template di vista a una o più viste, identificandolo per ID o per nome. | "Applica il template 'Pianta Strutture' a queste tre viste" |
| `manage_view_templates` | Gestisce i template di vista: elenca, filtra per tipo, rinomina o elimina (le eliminazioni richiedono conferma). | "Elenca tutti i template di vista delle piante" |
| `create_view_filter` | Crea o gestisce un filtro di vista per categorie/parametri e ne imposta gli override colore (RGB) sulla vista. | "Crea un filtro che colora di rosso i muri portanti in questa vista" |

#### Override grafici

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `override_graphics` | Applica override grafici (colore, trasparenza, mezzitoni, spessore linea) agli elementi in una vista, o li resetta (richiede conferma). | "Colora di blu con trasparenza 50% questi elementi nella vista corrente" |

#### Tavole

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `batch_create_sheets` | Crea più tavole in un colpo solo da un elenco, con cartiglio predefinito. | "Crea le tavole A101, A102 e A103 con il cartiglio A1" |
| `create_placeholder_sheets` | Crea e gestisce tavole segnaposto (placeholder), assegnabili poi a tavole reali con cartiglio. | "Genera 10 tavole segnaposto numerate da S01 a S10" |
| `duplicate_sheet_with_views` | Duplica una tavola con le sue viste, scegliendo se duplicare le viste, mantenere legende e abachi. | "Duplica la tavola A101 in 3 copie duplicando anche le viste" |
| `duplicate_sheet_with_content` | Duplica una tavola con tutto il contenuto (viste, legende, abachi, revisioni) e rinumerazione con prefisso/suffisso. | "Duplica questa tavola mantenendo legende, abachi e revisioni" |

#### Viewport sulle tavole

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `place_viewport` | Posiziona una vista su una tavola come viewport, con posizione, rotazione e tipo di viewport. | "Metti la pianta del piano terra sulla tavola A101 a X=200 Y=150" |
| `align_viewports` | Allinea uno o più viewport rispetto a un viewport di riferimento secondo una modalità di allineamento. | "Allinea questi viewport al primo che ho selezionato" |

#### Esempi di workflow

- "Crea le tavole esecutive di tutti i locali del piano terra": prima genera una vista per ogni vano (`create_views_from_rooms`) in scala 1:50, poi applica a tutte il template 'Pianta Locali' (`apply_view_template`), crea le tavole con cartiglio A1 (`batch_create_sheets`) e infine posiziona ogni vista sulla rispettiva tavola (`place_viewport`).

- "Prepara la serie di tavole strutturali partendo da un prototipo": parti da una tavola già impaginata e falla duplicare in 5 copie con tutto il contenuto, rinumerando con prefisso S- (`duplicate_sheet_with_content`); poi rinomina le viste duplicate con il prefisso STR_ (`rename_views`).

- "Evidenzia visivamente le criticità in una vista 3D di coordinamento": crea un filtro che colora di rosso una categoria (`create_view_filter`), applica override grafici di trasparenza agli elementi di contorno (`override_graphics`, richiede conferma) e limita il section box agli elementi selezionati per concentrare la vista sull'area di interesse (`section_box_from_selection`).


### File collegati e coordinamento

Questa sezione raccoglie i comandi per gestire i file collegati (Revit link, IFC, modelli di coordinamento), allinearli e posizionarli, e per individuare visivamente elementi che vivono dentro i modelli collegati. Tutte le coordinate sono espresse in **millimetri**. Le categorie usano i codici `OST_` indipendenti dalla lingua (es. `OST_Walls`, `OST_Doors`, `OST_StructuralFraming`).

#### Lettura e ispezione dei collegamenti

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `get_linked_file_instances` | Elenca tutte le istanze di file collegati presenti nel modello con il loro stato. | "Mostrami tutti i file collegati in questo progetto." |
| `get_coordination_models` | Elenca i modelli di coordinamento collegati, con filtro per nome ed eventuali istanze. | "Quali modelli di coordinamento sono caricati? Filtra quelli che contengono 'MEP'." |
| `get_link_transform` | Restituisce la trasformazione (origine e rotazione) di una specifica istanza di collegamento. | "Dammi la posizione e l'orientamento del collegamento con istanza 778812." |
| `get_selected_linked_elements` | Restituisce gli elementi attualmente selezionati che appartengono a un modello collegato. | "Cosa ho selezionato nel modello collegato in questo momento?" |

#### Inserimento, posizionamento e ricarica

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `add_linked_file` | Inserisce un nuovo file collegato, opzionalmente in una posizione specifica. | "Collega il file C:\Progetti\Strutture.rvt centrandolo sull'origine." |
| `move_link_instance` | Sposta un'istanza di collegamento di un offset o in coordinate assolute, secondo la modalità scelta. | "Sposta il collegamento strutture di 1500 mm verso est." |
| `align_link_to_host` | Allinea un'istanza di collegamento al modello host secondo la modalità di allineamento indicata. | "Allinea il collegamento 778812 al modello host." |
| `pin_unpin_link_instance` | Blocca o sblocca (pin/unpin) una o più istanze di collegamento per impedirne lo spostamento accidentale. | "Blocca tutti i collegamenti architettonici così non si spostano." |
| `reload_linked_file_from` | Ricarica un tipo di collegamento da un nuovo percorso file (utile dopo spostamenti o aggiornamenti). | "Ricarica il collegamento strutture dalla nuova cartella sul server." |

#### Visualizzazione e coordinamento cross-model

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `highlight_linked_element` | Evidenzia un singolo elemento dentro un modello collegato, con opzione di sezione 3D e offset. | "Evidenziami la trave ID 50421 dentro il collegamento 778812 e creami una sezione 3D attorno." |
| `show_cross_model_elements` | Mostra/isola/seleziona insieme elementi del modello host e di quelli collegati per la revisione di coordinamento. | "Isola insieme i pilastri dell'host e le tubazioni del modello MEP collegato, con un section box attorno." |

#### Esempi di workflow

**Inserire e bloccare un nuovo collegamento strutturale**
> "Collega il file `C:\Progetti\Edificio_Strut.rvt` posizionandolo sull'origine, poi allinealo al modello host e infine bloccalo (pin) così nessuno lo sposta per errore."
Questo combina `add_linked_file`, `align_link_to_host` e `pin_unpin_link_instance`. Verifica prima la posizione con `get_link_transform` se devi controllare origine e rotazione.

**Aggiornare un collegamento spostato sul server**
> "Il file strutture è stato spostato in una nuova cartella. Mostrami prima tutti i collegamenti caricati, poi ricarica quello delle strutture dal nuovo percorso `\\Server\BIM\Strut_v3.rvt` e dimmi se la posizione è cambiata."
Qui si usano `get_linked_file_instances` per individuare il `linkTypeId` giusto, `reload_linked_file_from` per la ricarica e `get_link_transform` per confermare che la trasformazione sia rimasta invariata.

**Revisione visiva di un'interferenza cross-model**
> "Ho selezionato una trave nel modello collegato delle strutture: dimmi qual è, poi evidenziala insieme ai muri `OST_Walls` dell'host e isola tutto con un section box e un offset di 500 mm per controllare l'incrocio."
Questo flusso parte da `get_selected_linked_elements` per identificare l'elemento collegato, usa `highlight_linked_element` per metterlo in risalto e `show_cross_model_elements` per isolare host e link insieme con la vista 3D di coordinamento.

### Power BI Live

Pubblica i dati del modello Revit verso un dataset push di Power BI e, viceversa, usa Power BI per riselezionare gli elementi direttamente in Revit. Il flusso tipico è: accedi una volta, individua il workspace, crea (o riusa) il dataset, pubblica elementi/abachi/selezione, poi interroga. Dopo la prima pubblicazione riuscita il documento viene "agganciato" (ProjectBinding) al workspace e al dataset: da quel momento puoi omettere `workspaceId` e `datasetId` nelle chiamate successive.

I dati sono pubblicati in tabelle standard (`Metadata`, `Elements`, `Selection`, `Schedules`). Ogni elemento porta sempre il proprio ElementId, così Power BI può collegare automaticamente le tabelle alla master `Elements`. Le coordinate restano in millimetri come nel resto di RevitCortex.

#### Autenticazione e contesto

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `pbi_check_auth` | Verifica lo stato di accesso a Power BI; con `signIn` avvia il login device-code (URL + codice mostrati in una finestra di Revit). | "Sono collegato a Power BI? Se no, fammi accedere." |
| `pbi_sign_out` | Disconnette da Power BI revocando tutti i token memorizzati. | "Esci dal mio account Power BI." |
| `pbi_get_binding` | Mostra a quale workspace e dataset è agganciato il documento Revit attivo. | "A quale dataset Power BI è collegato questo modello?" |

#### Scoperta workspace e dataset

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `pbi_list_workspaces` | Elenca i workspace (gruppi) Power BI a cui l'utente ha accesso. | "Mostrami i miei workspace Power BI." |
| `pbi_list_datasets` | Elenca i dataset push presenti in un workspace indicato. | "Quali dataset ci sono nel workspace di coordinamento?" |
| `pbi_create_dataset` | Crea (in modo idempotente) un dataset push RevitCortex nel workspace, con le tabelle standard. | "Crea un dataset Power BI chiamato Cantiere Nord nel mio workspace." |

#### Pubblicazione dei dati

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `pbi_publish_elements` | Pubblica gli elementi del modello nella tabella `Elements`, con modalità `replace` (snapshot completo), `append` o `create`. | "Pubblica tutti i muri e i pilastri su Power BI sostituendo lo snapshot precedente." |
| `pbi_publish_schedules` | Pubblica gli abachi nella tabella `Schedules` in formato lungo (una riga per cella). | "Manda su Power BI l'abaco delle porte." |
| `pbi_publish_selection` | Pubblica la selezione corrente di Revit nella tabella `Selection`, sostituendo lo snapshot precedente. | "Pubblica su Power BI gli elementi che ho selezionato adesso." |

#### Interrogazione e selezione inversa

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `pbi_query` | Esegue una query (filtri rapidi per categoria/livello/parametro o DAX raw) sul dataset agganciato e seleziona o isola gli elementi corrispondenti in Revit. | "Seleziona in Revit tutti i muri del Livello 1 partendo da Power BI." |

#### Esempi di workflow

- "Collegami a Power BI, mostrami i miei workspace, crea un dataset chiamato Progetto Torre nel workspace di coordinamento e poi pubblica tutti gli elementi delle categorie OST_Walls e OST_StructuralFraming in modalità replace." Questo combina `pbi_check_auth` (con accesso), `pbi_list_workspaces`, `pbi_create_dataset` e `pbi_publish_elements`; dopo la pubblicazione il documento resta agganciato al dataset.

- "Pubblica su Power BI l'abaco dei pilastri e poi, sempre da Power BI, isola in Revit tutti gli elementi del Livello 2 così li rivedo nella vista corrente." Qui si usano `pbi_publish_schedules` e poi `pbi_query` con `action` impostata su isolate; non serve ripetere workspace e dataset perché il binding è già salvato.

- "Verifica a quale dataset è collegato questo modello, seleziona gli elementi con Contrassegno W-12 partendo da Power BI e poi, dopo che ho aggiustato la selezione in Revit, ripubblicala nella tabella Selection." Combina `pbi_get_binding`, `pbi_query` (filtro per parametro Mark) e `pbi_publish_selection` per chiudere il ciclo Revit ↔ Power BI.

### Parametri, Annotazioni e Workflow

Questa sezione raccoglie i comandi per gestire i parametri del modello (condivisi, di progetto, globali), applicare annotazioni (tag, quote, note di testo, legende a colori) e orchestrare i workflow combinati di documentazione, audit e coordinamento. Parla a Claude in linguaggio naturale: pensa a cosa vuoi ottenere, non al nome del tool.

Promemoria utili:
- Le **categorie** si indicano con i codici `OST_` indipendenti dalla lingua (es. `OST_Walls`, `OST_Doors`, `OST_StructuralFraming`).
- Le **coordinate** e gli offset sono in millimetri.
- I comandi che **modificano o cancellano** dati chiedono una conferma nativa in Revit prima di procedere (indicato con "richiede conferma").
- I comandi con `dryRun` permettono un'**anteprima**: chiedi sempre prima quante righe verrebbero toccate, poi conferma l'esecuzione reale.

#### Gestione parametri

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `add_shared_parameter` | Aggiunge un parametro condiviso a una o piu categorie del modello. | "Aggiungi il parametro condiviso WBS_Code ai muri e ai pavimenti, come parametro di istanza testo." |
| `manage_project_parameters` | Crea, elenca, rinomina o sposta i parametri di progetto e le loro categorie associate (richiede conferma). | "Crea un parametro di progetto 'Lotto' di tipo testo associato a porte e finestre." |
| `manage_global_parameters` | Crea, modifica, rinomina o ordina i parametri globali, anche con formule. | "Crea un parametro globale 'Altezza_Standard' impostato a 3000 mm." |
| `get_cache_stats` | Mostra le statistiche della cache interna di RevitCortex (parametri e metadati memorizzati). | "Quanti elementi ci sono in cache adesso?" |
| `clear_cache` | Svuota la cache interna per forzare una rilettura aggiornata del modello. | "Pulisci la cache, ho appena modificato il modello a mano." |
| `say_hello` | Verifica che la connessione tra Claude e Revit sia attiva. | "Sei collegato a Revit?" |

#### Modifica massiva dei valori dei parametri

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `bulk_modify_parameter_values` | Imposta, cerca-e-sostituisce o azzera lo stesso parametro su molti elementi di una categoria (richiede conferma, supporta anteprima). | "Sui muri sostituisci nel parametro Commenti il testo 'TEMP' con 'DEFINITIVO', prima fammi l'anteprima." |
| `clear_parameter_values` | Azzera il valore di un parametro su uno scope o categorie indicate (richiede conferma). | "Svuota il parametro Contrassegno su tutte le porte." |
| `add_prefix_suffix` | Aggiunge un prefisso e/o un suffisso al valore di un parametro testuale (anteprima con dryRun=true di default; impostare dryRun=false per applicare, richiede conferma). | "Aggiungi il prefisso 'A-' al parametro Contrassegno delle porte." |
| `sync_csv_parameters` | Allinea valori di parametri diversi su elementi diversi a partire da dati CSV (supporta anteprima). | "Sincronizza i parametri dal CSV che ti incollo, ogni riga ha l'ID e i valori da scrivere, prima in anteprima." |
| `transfer_parameters` | Copia i valori dei parametri da un elemento sorgente verso uno o piu elementi destinazione (supporta anteprima). | "Copia i parametri dall'elemento 606873 a questi altri tre, prima mostrami cosa cambierebbe." |

#### Annotazioni: tag, quote, note e legende

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `tag_rooms` | Inserisce i tag dei locali nella vista attiva, con o senza linea di richiamo. | "Tagga tutti i locali della vista corrente senza linea di richiamo." |
| `tag_walls` | Inserisce i tag dei muri nella vista attiva, con orientamento e tipo di tag opzionali. | "Tagga i muri della vista attiva con orientamento orizzontale." |
| `wipe_empty_tags` | Rimuove i tag vuoti (senza valore) da una vista o per categorie indicate (richiede conferma, supporta anteprima). | "Trova ed elimina i tag vuoti nella vista corrente, prima fammi vedere quanti sono." |
| `create_dimensions` | Crea quote tra elementi o riferimenti (coordinate in mm; Z deve coincidere con la quota del livello). | "Quota la distanza tra questi due pilastri sul livello 0." |
| `create_text_note` | Inserisce una o piu note di testo nella vista (coordinate in mm). | "Aggiungi una nota di testo 'Zona da verificare' a questa posizione." |
| `create_color_legend` | Colora gli elementi in base a un parametro e genera una legenda a colori. | "Colora i muri per tipo e creami una legenda a colori." |

#### Import / cross-app

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `import_table` | Importa una tabella da file (CSV/delimitato) come vista drafting o nota tabellare. | "Importa questo CSV come tabella in una vista drafting, prima riga come intestazione." |
| `cross_app_selection` | Sincronizza, isola o crea riferimenti di selezione tra applicazioni (es. con NavisCortex), con section box opzionale. | "Isola in Revit gli elementi che ho selezionato in Navisworks e creami una section box." |

#### Workflow combinati

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `workflow_model_audit` | Esegue un audit complessivo del modello (avvisi, famiglie) con output sintetico o esteso. | "Fammi un audit del modello in versione compatta, max 50 avvisi." |
| `workflow_clash_review` | Rileva le interferenze tra due categorie e prepara una vista 3D con section box per la revisione visiva. | "Controlla le interferenze tra travi strutturali e muri e preparami la vista 3D." |
| `workflow_data_roundtrip` | Esporta gli elementi con i loro parametri su file per modifica esterna e re-import. | "Esporta tutti i muri con i parametri Area e Commenti su file per il round-trip dei dati." |
| `workflow_room_documentation` | Genera la documentazione dei locali di un livello, con sezioni opzionali. | "Documenta tutti i locali del Piano Terra e creami anche le sezioni." |
| `workflow_sheet_set` | Crea in blocco un set di tavole, con cartiglio opzionale. | "Crea il set di tavole che ti elenco usando il cartiglio A1 standard." |

#### Esempi di workflow

**1. Standardizzazione dei codici sulle porte (anteprima poi esecuzione)**
"Sulle porte (`OST_Doors`): prima aggiungi il parametro condiviso 'Codice_Locale' se non esiste, poi aggiungi il prefisso 'PT-' al parametro Contrassegno e infine taggale tutte nella vista attiva. Per la modifica del Contrassegno mostrami prima l'anteprima del numero di elementi coinvolti, poi procedi."

**2. Round-trip dei dati dei muri filtrati per livello**
"Esporta tutti i muri (`OST_Walls`) con i parametri Area, Volume e Commenti su un file per il round-trip. Io li modifico in Excel, poi me li reimporti sincronizzando i valori dal CSV: prima in `dryRun` per vedere quante righe cambierebbero, poi in scrittura reale."

**3. Audit, coordinamento e documentazione di un piano**
"Inizia con un audit compatto del modello e dimmi quanti avvisi ci sono. Poi controlla le interferenze tra travi strutturali (`OST_StructuralFraming`) e muri (`OST_Walls`) preparando la vista 3D di revisione. Infine genera la documentazione di tutti i locali del Piano Primo con le sezioni e crea il set di tavole relativo usando il cartiglio standard."

### Armatura (Rebar / Reinforcement)

Questa disciplina copre la creazione, la lettura e la modifica delle armature in cemento armato: barre singole e in serie, armatura ad area e a percorso (path), reti elettrosaldate (fabric), giunti (coupler), sovrapposizioni (splice), vincoli, numerazione, arrotondamenti e impostazioni di rinforzo. Le coordinate, i vettori e le curve si esprimono sempre in millimetri. Diverse operazioni sono distruttive (creazione, modifica, rimozione, conversione) e mostrano una conferma nativa di Revit prima di eseguire.

> Suggerimento: prima di creare armatura, verifica sempre l'idoneità dell'host con `get_rebar_host_data`. Tieni presente che `isValidHost` indica solo che l'host può ospitare barre singole (Rebar), NON che sia idoneo ad armatura ad area/percorso/rete, che hanno controlli più restrittivi.

#### Lettura e scoperta (catalogo tipi)

Comandi di sola lettura per scoprire i tipi disponibili nel modello e le capacità dell'API. Sicuri da combinare in parallelo.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `get_rebar_api_capabilities` | Riporta quali funzioni dell'API armatura sono supportate dalla versione di Revit attiva. | "Quali funzioni di armatura supporta questo Revit?" |
| `list_rebar_bar_types` | Elenca i tipi di barra d'armatura disponibili (diametri). | "Mostrami i diametri di barra disponibili nel modello" |
| `list_rebar_shapes` | Elenca le forme di piega (shape) di armatura definite nel progetto. | "Quali forme di piega ferri ho a disposizione?" |
| `list_rebar_hook_types` | Elenca i tipi di uncino (hook) disponibili. | "Elenca i tipi di uncino per le armature" |
| `list_rebar_cover_types` | Elenca i tipi di copriferro definiti nel progetto. | "Che tipi di copriferro sono impostati?" |
| `list_rebar_fabric_types` | Elenca i tipi di rete elettrosaldata (fabric) disponibili. | "Mostrami le reti elettrosaldate disponibili" |
| `list_rebar_splice_types` | Elenca i tipi di sovrapposizione/giunzione (splice) disponibili. | "Quali tipi di sovrapposizione ferri posso usare?" |
| `get_reinforcement_settings` | Legge le impostazioni globali di armatura del progetto. | "Quali sono le impostazioni di armatura del progetto?" |
| `get_fabric_rounding` | Legge le regole di arrotondamento per le reti elettrosaldate. | "Come sono impostati gli arrotondamenti delle reti?" |

#### Lettura dati elementi armatura

Comandi di sola lettura su elementi specifici (per ID): geometria, vincoli, numerazione, giunti, dati di host.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `get_rebar_element_data` | Restituisce i dati completi di una barra/serie di armatura. | "Dammi i dati del ferro 451200" |
| `get_rebar_host_data` | Restituisce i dati dell'host e se può ospitare armatura. | "L'elemento 318044 può ospitare armatura?" |
| `get_rebar_geometry` | Restituisce la geometria 3D di una barra (con o senza uncini/raggi di piega). | "Mostrami la geometria del ferro 451200 senza uncini" |
| `get_rebar_constraints` | Elenca i vincoli (constraint) della barra. | "Che vincoli ha il ferro 451200?" |
| `get_rebar_constraint_candidates` | Elenca i possibili riferimenti per vincolare una specifica maniglia della barra. | "A cosa posso vincolare la maniglia 0 del ferro 451200?" |
| `get_rebar_numbering` | Legge la numerazione (mark) delle armature, con riepilogo opzionale. | "Dammi un riepilogo della numerazione dei ferri" |
| `get_rebar_rounding` | Legge le regole di arrotondamento lunghezza di una barra (o globali). | "Come è arrotondata la lunghezza del ferro 451200?" |
| `get_rebar_varying_data` | Restituisce i dati di una serie a passo variabile (varying), con lunghezze opzionali. | "Mostrami i dati della serie variabile del ferro 451200 con le lunghezze" |
| `get_rebar_coupler_data` | Restituisce i dati di un giunto meccanico (coupler). | "Dammi i dati del coupler 460010" |
| `get_rebar_splice_data` | Restituisce i dati di sovrapposizione di una barra. | "Mostrami la sovrapposizione del ferro 451200" |
| `get_rebar_splice_candidates` | Elenca le possibili sovrapposizioni applicabili a una barra. | "Dove posso sovrapporre il ferro 451200?" |
| `get_rebar_bending_detail_data` | Restituisce i dati di un dettaglio di piega (bending detail). | "Dammi i dati del dettaglio di piega 470500" |
| `get_area_reinforcement_data` | Restituisce i dati di un'armatura ad area. | "Mostrami i dati dell'armatura ad area 480000" |
| `get_path_reinforcement_data` | Restituisce i dati di un'armatura a percorso (path). | "Dammi i dati del path reinforcement 481000" |
| `get_fabric_area_data` | Restituisce i dati di un'area di rete elettrosaldata. | "Mostrami i dati della fabric area 482000" |
| `get_fabric_sheet_data` | Restituisce i dati di un foglio di rete elettrosaldata. | "Dammi i dati del foglio di rete 483000" |
| `get_fabric_wire_data` | Restituisce i fili (wire) di un foglio di rete in una direzione. | "Mostrami i fili in direzione maggiore del foglio 483000" |

#### Creazione armatura

Comandi distruttivi che aggiungono elementi al modello: tutti mostrano conferma. Per l'host usa categorie strutturali (es. `OST_StructuralFraming`, `OST_StructuralColumns`, `OST_Floors`, `OST_Walls`). Curve, vettori e origini sono in millimetri.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `create_rebar_from_curves` | Crea armatura da curve esplicite con normale, uncini e layout (richiede conferma). | "Crea staffe nel pilastro 318044 con queste curve e questa normale" |
| `create_rebar_from_shape` | Crea armatura a partire da una forma di piega posizionata con origine e assi (richiede conferma). | "Crea un ferro con la forma 'staffa 90°' nella trave 318044 in questa posizione" |
| `create_free_form_rebar` | Crea armatura free form da uno o più loop di curve (richiede conferma). | "Crea un ferro free form nella fondazione 318044 con questi loop" |
| `create_area_reinforcement` | Crea armatura ad area su una soletta/muro con direzione principale (richiede conferma). | "Aggiungi armatura ad area sulla soletta 318044 con direzione maggiore X" |
| `create_path_reinforcement` | Crea armatura a percorso lungo curve, con uncini e tipo (richiede conferma). | "Crea path reinforcement sul bordo soletta 318044 lungo questa curva" |
| `create_fabric_area` | Crea un'area di rete elettrosaldata sull'host con direzione principale (richiede conferma). | "Stendi una rete elettrosaldata sulla soletta 318044" |
| `create_fabric_sheet` | Crea un singolo foglio di rete elettrosaldata, con profilo di piega opzionale (richiede conferma). | "Crea un foglio di rete per il muro 318044" |
| `create_rebar_coupler` | Crea un giunto meccanico tra due estremità di barre (richiede conferma). | "Metti un coupler tra l'estremità del ferro 451200 e del ferro 451300" |
| `create_rebar_bending_detail` | Crea un dettaglio di piega di una barra in una vista (richiede conferma). | "Crea il dettaglio di piega del ferro 451200 nella vista 9001" |

#### Modifica armatura e proprietà

Comandi distruttivi che cambiano armature esistenti: forma, uncini, terminazioni, layout, host, visibilità, passo variabile. Mostrano conferma.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `set_rebar_shape` | Cambia la forma di piega di una barra (per ID o nome) (richiede conferma). | "Cambia la forma del ferro 451200 in 'staffa chiusa'" |
| `set_rebar_hooks` | Imposta gli uncini iniziale e/o finale della barra (richiede conferma). | "Metti uncino a 135° su entrambe le estremità del ferro 451200" |
| `set_rebar_terminations` | Imposta orientamento e rotazione delle terminazioni della barra (richiede conferma). | "Ruota la terminazione finale del ferro 451200 di 90 gradi" |
| `set_rebar_layout` | Cambia il layout della serie (singola, numero fisso, passo massimo, ecc.) (richiede conferma). | "Imposta il layout del ferro 451200 a passo massimo" |
| `set_rebar_host` | Sposta una barra su un nuovo host (richiede conferma). | "Sposta il ferro 451200 sul pilastro 318099" |
| `set_rebar_visibility` | Imposta la visibilità (nascosto/non oscurato) della barra in una vista (richiede conferma). | "Rendi non oscurato il ferro 451200 nella vista 9001" |
| `set_rebar_varying` | Attiva o disattiva il passo variabile su una serie (richiede conferma). | "Attiva la distribuzione a passo variabile sul ferro 451200" |
| `move_rebar_in_set` | Sposta una singola barra all'interno della serie, con reset opzionale (richiede conferma). | "Sposta la barra in posizione 3 della serie 451200 di 50 mm" |
| `split_rebar` | Divide una serie di barre in corrispondenza di una posizione (richiede conferma). | "Dividi la serie 451200 alla posizione 5" |
| `unify_rebars` | Unifica più barre in un'unica serie (richiede conferma). | "Unifica i ferri 451200, 451300 e 451400 in un'unica serie" |
| `propagate_rebar` | Propaga un'armatura su altri host simili (richiede conferma). | "Propaga il ferro 451200 a tutti i pilastri uguali" |
| `include_exclude_rebar_bars` | Include o esclude singole barre della serie in una vista (richiede conferma). | "Escludi la barra in posizione 2 del ferro 451200 nella vista 9001" |
| `modify_rebar_bending_detail` | Modifica posizione e rotazione di un dettaglio di piega (richiede conferma). | "Sposta e ruota il dettaglio di piega 470500" |
| `transfer_rebar_annotations` | Trasferisce le annotazioni di armatura da una vista all'altra (richiede conferma). | "Copia le annotazioni dei ferri dalla vista 9001 alla vista 9002" |

#### Vincoli, numerazione e arrotondamenti

Comandi distruttivi per gestire vincoli, mark e regole di arrotondamento. Mostrano conferma.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `manage_rebar_constraints` | Aggiunge, rimuove o modifica un vincolo di una barra tramite azione (richiede conferma). | "Vincola la maniglia 0 del ferro 451200 al candidato 1" |
| `manage_rebar_numbering` | Esegue azioni sulla numerazione (assegna/rinumera) di una barra (richiede conferma). | "Assegna il mark 'B12' al ferro 451200" |
| `manage_rebar_rounding` | Imposta le regole di arrotondamento lunghezza per una barra o globali (richiede conferma). | "Arrotonda la lunghezza dei ferri a 10 mm per eccesso" |
| `manage_fabric_rounding` | Imposta le regole di arrotondamento per le reti elettrosaldate (richiede conferma). | "Imposta l'arrotondamento delle reti a 25 mm" |

#### Sovrapposizioni (splice) e giunti

Comandi distruttivi per gestire sovrapposizioni di barre. Mostrano conferma.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `splice_rebar` | Applica una sovrapposizione (splice) a una barra in una posizione (richiede conferma). | "Aggiungi una sovrapposizione al ferro 451200" |
| `remove_rebar_splice` | Rimuove la sovrapposizione da un'estremità della barra (richiede conferma). | "Togli la sovrapposizione dal ferro 451200" |

#### Armatura ad area, a percorso e reti (modifica)

Comandi distruttivi specifici per i sistemi di armatura ad area/percorso e per le reti elettrosaldate. Mostrano conferma.

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `set_area_reinforcement_layers` | Attiva o disattiva uno strato (layer) di un'armatura ad area (richiede conferma). | "Disattiva lo strato superiore dell'armatura ad area 480000" |
| `set_path_reinforcement_options` | Imposta offset e orientamenti delle barre di un'armatura a percorso (richiede conferma). | "Imposta un offset aggiuntivo di 30 mm sul path reinforcement 481000" |
| `convert_rebar_system_to_rebars` | Converte un sistema di armatura in barre singole modificabili (richiede conferma). | "Converti il sistema di armatura 480000 in barre singole" |
| `remove_rebar_system` | Rimuove un sistema di armatura ad area o a percorso (richiede conferma). | "Elimina il sistema di armatura 480000" |
| `place_fabric_sheet` | Posiziona un foglio di rete su un host con trasformazione opzionale (richiede conferma). | "Posiziona il foglio di rete 483000 sulla soletta 318044" |
| `set_fabric_sheet_bend_profile` | Imposta il profilo di piega di un foglio di rete (richiede conferma). | "Imposta il profilo di piega del foglio di rete 483000" |
| `remove_fabric_reinforcement_system` | Rimuove un sistema di rete elettrosaldata ad area (richiede conferma). | "Elimina la rete elettrosaldata dell'area 482000" |

#### Giunti meccanici (coupler) e impostazioni

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `set_rebar_coupler_visibility` | Imposta la visibilità (non oscurato) di un coupler in una vista (richiede conferma). | "Rendi non oscurato il coupler 460010 nella vista 9001" |
| `set_reinforcement_settings` | Imposta le opzioni globali di armatura del progetto (richiede conferma). | "Imposta che le forme dei ferri definiscano gli uncini" |

#### Esempi di workflow

**1. Armare una trave dal catalogo e numerare i ferri**
> "Apri la trave `OST_StructuralFraming` con ID 318044: prima verifica che possa ospitare armatura, poi mostrami i diametri di barra e le forme di piega disponibili. Crea le staffe da curve nel web della trave con la normale corretta, aggiungi uncini a 135° su entrambe le estremità, imposta il layout a passo massimo e infine assegna automaticamente la numerazione dei ferri. Mostrami un riepilogo della numerazione alla fine."

**2. Armatura ad area su una soletta con verifica preventiva**
> "Sulla soletta `OST_Floors` con ID 318100 voglio armatura ad area con direzione principale X. Prima leggi i dati dell'host per confermare l'idoneità, poi crea l'armatura ad area scegliendo un tipo di barra dal catalogo. Dopo la creazione disattiva lo strato superiore e dammi i dati dell'armatura ad area risultante. Le operazioni che modificano il modello richiederanno conferma."

**3. Riorganizzare una serie esistente con split, sovrapposizione e coupler**
> "Sul ferro 451200 dividi la serie alla posizione 5, poi controlla dove posso applicare una sovrapposizione e aggiungila. Tra l'estremità del nuovo tratto e il ferro adiacente 451300 metti un giunto meccanico (coupler) e rendilo non oscurato nella vista di sezione 9001. Mostrami i dati del coupler creato a fine operazione."


### Acciaio Strutturale (Structural Steel)

Questa disciplina copre la modellazione e la gestione delle strutture in acciaio in Revit: connessioni (giunti) strutturali, tagli geometrici tra elementi (solid cut e instance void cut), proprietà di fabbricazione (fabrication ID, link materiali), flussi di approvazione e l'ispezione delle API di acciaio disponibili.

Le categorie acciaio rientrano tipicamente in `OST_StructuralFraming` (travi, controventi) e `OST_StructuralColumns` (pilastri). Le coordinate dei punti di input sono in millimetri. Molti comandi di scrittura supportano `dryRun` per l'anteprima e diversi comandi distruttivi richiedono conferma prima di eseguire.

#### Diagnostica e capacità del modello

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `get_structural_steel_api_capabilities` | Riporta quali funzionalità delle API acciaio sono disponibili nella versione di Revit attiva. | "Quali funzioni di acciaio strutturale supporta questo Revit?" |
| `analyze_structural_steel_model` | Fornisce una panoramica degli elementi acciaio, connessioni e tagli presenti nel modello. | "Fammi un'analisi generale dell'acciaio strutturale del modello" |
| `get_steel_element_properties` | Mostra le proprietà di un singolo elemento in acciaio. | "Mostrami le proprietà di questo elemento in acciaio" |
| `get_steel_element_warnings` | Elenca gli avvisi relativi a un elemento acciaio (o a tutti se non specificato). | "Ci sono avvisi sugli elementi in acciaio?" |

#### Connessioni strutturali — lettura

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `list_steel_connection_types` | Elenca i tipi di connessione strutturale disponibili nel progetto. | "Elenca i tipi di giunto in acciaio disponibili" |
| `list_steel_connection_handlers` | Elenca i gestori (handler) di connessione presenti nel modello. | "Mostrami i gestori di connessione acciaio del modello" |
| `list_steel_connection_handler_types` | Elenca i tipi di gestore di connessione disponibili. | "Quali tipi di handler di connessione ci sono?" |
| `list_steel_connection_providers` | Elenca i provider di connessione strutturale registrati. | "Elenca i provider di connessioni in acciaio" |
| `get_steel_connection_data` | Restituisce i dati completi di una specifica connessione. | "Dammi i dati della connessione 482910" |
| `get_steel_connection_type_data` | Restituisce i dati di un tipo di connessione. | "Mostrami i dettagli del tipo di connessione 7712" |
| `get_steel_connection_input_points` | Mostra i punti di input geometrici di una connessione. | "Quali sono i punti di input di questa connessione?" |
| `get_steel_connection_validation` | Riporta lo stato di validazione di una connessione. | "Questa connessione è valida?" |
| `get_steel_connection_applicability` | Verifica se un tipo di connessione è applicabile a certi elementi. | "Questo tipo di giunto è applicabile a questi elementi?" |
| `get_steel_connection_settings` | Mostra le impostazioni globali delle connessioni acciaio. | "Mostrami le impostazioni delle connessioni in acciaio" |
| `get_structural_connection_provider_registry` | Restituisce il registro dei provider di connessione strutturale. | "Mostrami il registro dei provider di connessione" |
| `get_structural_connection_provider_data` | Restituisce i dati di un provider di connessione (tutti o uno specifico). | "Dammi i dati del provider di connessione strutturale" |
| `get_structural_connection_validation_info` | Fornisce informazioni dettagliate di validazione di una connessione. | "Perché questa connessione non è valida?" |

#### Connessioni strutturali — creazione e modifica

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `create_steel_connection` | Crea una connessione strutturale tra gli elementi indicati usando un tipo di handler. | "Crea un giunto in acciaio tra gli elementi 401 e 402" |
| `create_generic_steel_connection` | Crea una connessione generica (senta parametri di giunto specifici) tra elementi. | "Crea una connessione generica tra questi due profili" |
| `create_steel_connection_handler_type` | Crea un nuovo tipo di gestore di connessione con nome e famiglia indicati. | "Crea un nuovo tipo di handler di connessione chiamato Giunto-Trave-Pilastro" |
| `create_default_steel_connection_handler_type` | Crea il tipo di gestore di connessione predefinito. | "Crea l'handler di connessione di default" |
| `create_steel_structural_connection_type` | Crea un tipo di connessione strutturale a partire da un family symbol. | "Crea un tipo di connessione dal family symbol 5521" |
| `set_steel_connection_type` | Assegna o cambia il tipo di handler di una connessione esistente (anteprima con dryRun). | "Cambia il tipo di handler della connessione 482910" |
| `set_steel_connection_type_family_symbol` | Imposta il family symbol di un tipo di connessione (anteprima con dryRun). | "Assegna il family symbol 5521 al tipo di connessione 7712" |
| `modify_steel_connection_inputs` | Aggiunge o rimuove elementi di input da una connessione. | "Aggiungi l'elemento 403 agli input della connessione 482910" |
| `manage_custom_steel_connection_type` | Esegue un'azione di gestione (es. promozione/conversione) su un tipo di connessione personalizzato. | "Gestisci il tipo di connessione personalizzato 7712" |
| `set_steel_connection_default_order` | Reimposta l'ordine predefinito di esecuzione di una connessione. | "Ripristina l'ordine di default della connessione 482910" |
| `delete_steel_connection` | Elimina una connessione strutturale (richiede conferma; anteprima con dryRun). | "Elimina la connessione 482910" |

#### Stato e approvazione delle connessioni

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `set_steel_connection_status` | Imposta lo stato di una connessione (es. nuovo, in lavorazione, completato). | "Segna la connessione 482910 come completata" |
| `set_steel_connection_approval` | Assegna un tipo di approvazione a una connessione. | "Approva la connessione 482910 con stato Verificato" |
| `list_steel_approval_types` | Elenca i tipi di approvazione disponibili nel progetto. | "Quali stati di approvazione sono disponibili?" |
| `manage_steel_approval_type` | Crea o gestisce un tipo di approvazione (anteprima con dryRun). | "Crea un nuovo tipo di approvazione chiamato Approvato-Direzione" |

#### Tagli geometrici (solid cut e instance void cut)

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `check_steel_cut_eligibility` | Verifica se un elemento può tagliare un altro prima di applicare il taglio. | "L'elemento 601 può tagliare l'elemento 602?" |
| `add_steel_solid_cut` | Applica un taglio solido di un elemento su un altro (anteprima con dryRun). | "Taglia la trave 602 con il solido 601" |
| `remove_steel_solid_cut` | Rimuove un taglio solido esistente tra due elementi. | "Rimuovi il taglio solido tra 601 e 602" |
| `set_steel_solid_cut_face_splitting` | Attiva o disattiva la suddivisione delle facce per un taglio solido. | "Attiva lo split delle facce sul taglio tra 601 e 602" |
| `get_steel_cut_data` | Restituisce i dati dei tagli associati a un elemento. | "Mostrami i dati dei tagli sull'elemento 602" |
| `get_solid_cut_relationships` | Elenca le relazioni di taglio solido di un elemento. | "Quali tagli solidi coinvolgono l'elemento 602?" |
| `add_steel_instance_void_cut` | Usa l'istanza di un vuoto (void) per tagliare un elemento target (anteprima con dryRun). | "Usa il vuoto 700 per tagliare la trave 602" |
| `remove_steel_instance_void_cut` | Rimuove un taglio creato da un'istanza di vuoto. | "Rimuovi il taglio del vuoto 700 dalla trave 602" |
| `get_instance_void_cut_relationships` | Elenca le relazioni di taglio tramite istanze di vuoto per un elemento. | "Quali vuoti tagliano l'elemento 602?" |

#### Fabbricazione, ID e collegamenti

| Comando | Cosa fa | Esempio di prompt naturale |
|---------|---------|----------------------------|
| `add_steel_fabrication_info` | Aggiunge informazioni di fabbricazione agli elementi indicati (anteprima con dryRun). | "Aggiungi le info di fabbricazione a questi profili in acciaio" |
| `get_steel_element_fabrication_properties` | Mostra le proprietà di fabbricazione di un elemento. | "Mostrami le proprietà di fabbricazione di questo elemento" |
| `get_steel_fabrication_unique_id` | Restituisce l'ID univoco di fabbricazione di un elemento. | "Qual è il fabrication ID di questo elemento?" |
| `set_steel_fabrication_unique_id` | Imposta l'ID univoco di fabbricazione di un elemento. | "Imposta il fabrication ID di questo elemento" |
| `get_steel_reference_by_fabrication_id` | Trova l'elemento corrispondente a un dato fabrication GUID. | "Trovami l'elemento con questo GUID di fabbricazione" |
| `get_steel_external_id_map` | Restituisce la mappa degli ID esterni di un elemento. | "Mostrami la mappa degli ID esterni di questo elemento" |
| `get_steel_material_links` | Elenca i collegamenti ai materiali di un elemento acciaio. | "Quali materiali sono collegati a questo elemento?" |

#### Esempi di workflow

**1. Creazione e verifica di un giunto trave-pilastro**

> "Seleziono la trave 401 e il pilastro 402: prima verifica con `get_steel_connection_applicability` se il tipo di connessione 7712 è applicabile a questi due elementi, poi creami la connessione con quel tipo di handler in modalità anteprima (dryRun). Se l'anteprima è ok, esegui la creazione, controlla la validazione con `get_steel_connection_validation` e infine segna lo stato come completato."

**2. Tagli geometrici sicuri tra elementi in acciaio**

> "Devo accorciare la trave 602 con il solido 601. Prima controlla l'idoneità del taglio con `check_steel_cut_eligibility`, poi applica il taglio solido attivando la suddivisione delle facce. Dopo l'esecuzione mostrami le relazioni di taglio sull'elemento 602 per confermare che sia stato applicato correttamente."

**3. Tracciabilità di fabbricazione e approvazione**

> "Per tutti gli elementi selezionati di categoria `OST_StructuralFraming` aggiungi le informazioni di fabbricazione (prima in anteprima), poi mostrami il fabrication ID e i materiali collegati di un elemento campione. Infine elenca i tipi di approvazione disponibili e approva la connessione 482910 con lo stato Verificato."


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
