# RevitCortex — Guida Utente

> Guida pratica per usare RevitCortex in modo efficiente con Claude.

---

## Indice

1. [Avvio rapido](#avvio-rapido)
2. [Scelta del modello Claude](#scelta-del-modello-claude)
3. [Efficienza dei token](#efficienza-dei-token)
4. [Discipline di progetto e categorie Revit](#discipline-di-progetto-e-categorie-revit)
5. [Strumenti principali](#strumenti-principali)
6. [Workflow consigliati](#workflow-consigliati)
7. [Esecuzione di codice personalizzato](#esecuzione-di-codice-personalizzato)
8. [Risoluzione dei problemi comuni](#risoluzione-dei-problemi-comuni)

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

### Lettura e ricerca

| Strumento | Scopo |
|-----------|-------|
| `get_element_parameters` | Legge i parametri di uno o più elementi |
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
| `add_shared_parameter` | Aggiunge parametro condiviso a categorie |
| `sync_csv_parameters` | Importa valori da CSV |
| `transfer_parameters` | Copia parametri tra elementi |
| `manage_project_parameters` | Lista/rimuovi parametri di progetto |

### Script e automazione

| Strumento | Scopo |
|-----------|-------|
| `send_code_to_revit` | Esegue codice C# direttamente in Revit *(Revit 2025+)* |

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
