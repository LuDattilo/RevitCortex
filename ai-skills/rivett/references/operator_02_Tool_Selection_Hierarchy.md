# 02 — Tool Selection Hierarchy

**Scope:** Scegliere il tool con minor costo token che risolve il task.
**Sources:** RiveTT tool catalog and model-operation guidance
**Last verified:** 2026-05-25

## Decision rules

### Stato del modello (costo crescente)

| Step | Tool | Token cost | Quando |
|---|---|---|---|
| 1 | `check_model_health` | ~200 | Quick check |
| 2 | `analyze_model_statistics` (compact: true) | ~400 | Statistiche basilari |
| 3 | `workflow_model_audit` con filtri | ~800 | Audit mirato |
| 4 | `workflow_model_audit` completo | ~3000 | Audit completo (raro) |

### Trovare elementi

| Caso | Tool | Note |
|---|---|---|
| 1 parametro, valore esatto | `export_elements_data` con `filterParameterName`/`filterValue` | Veloce |
| Range / AND-OR / multi-param | `filter_elements` | Wrappare in `{"data": {...}}` |
| Elementi vista attiva | `get_current_view_elements` con `fields` e `limit` | |
| Volume/stanza | `get_elements_in_spatial_volume` con `categoryFilter` | `containment: inside` (default) = contenuti; `containment: boundary` = elementi che DELIMITANO la stanza |
| Elementi precisi per id | `export_elements_data` con `elementIds` | Applicato prima della paginazione |
| Stanze di un livello | `export_room_data` con `levelName` o `levelId` | Filtro eseguito in Revit |
| Parametro custom vuoto | NON guess: prima `get_element_parameters` su 1 elemento campione per scoprire i nomi | I nomi standard si risolvono in EN o nella lingua del documento; un nome non risolto arriva in `unresolvedParameterNames` |

### Trovare un tipo

| Caso | Tool | Note |
|---|---|---|
| Tipo di famiglia caricabile (porta, finestra, cartiglio) | `list_family_types` | `kind: loadable` |
| Tipo di sistema (muro, solaio, parapetto, scala, cartiglio) | `list_system_types(category)` | Senza categoria restituisce l'inventario con i codici `OST_*` |
| Duplicare un tipo | `duplicate_family_type` (caricabile) / `duplicate_system_type` (sistema) | `duplicate_family_type` fallisce sui tipi di sistema |

### Ciclo di vita del documento e circolazioni

| Caso | Tool | Note |
|---|---|---|
| Nuovo progetto vuoto | `create_document(templatePath?, targetPath)` | `save_as_document` duplica il modello aperto, NON crea un progetto vuoto |
| Aprire/attivare un file | `open_document(filePath)` | Cambia il documento attivo; i cache vengono svuotati. Salvare prima il documento corrente |
| Scala tra due livelli | `create_stair(baseLevelId, topLevelId, runs)` | Volate rette + pianerottoli automatici; verificare `reachesTopLevel` nella risposta |
| Rimuovere un membro | `delete_element` sul membro, o `edit_group_members` con solo `removeElementIds` | E' una ESCLUSIONE: solo quell'istanza, tipo e altre istanze intatti, istanza rinominata "(membre exclu)" |
| Aggiungere un membro | `edit_group_members` con `addElementIds` | Richiede sgruppa/rigruppa: NUOVO tipo, le altre istanze restano sulla vecchia definizione |
| Ripristinare un membro escluso | Nessun tool | Solo dal ruban Revit (Restore Excluded Members) |
| Istanze che differiscono | Normale | Esclusioni o vincoli di livello propri; leggere `hasExcludedMembers` per istanza, non fidarsi della prima |

### Disegnare linee e dividere stanze

| Caso | Tool |
|---|---|
| Linea 2D di vista | `create_detail_line` |
| Linea 3D di modello | `create_model_line` |
| Dividere una stanza senza muro fisico | `create_room_separation_line` |
| Cartiglio su una tavola esistente | `place_title_block` |

### Modifica parametri

| Caso | Tool |
|---|---|
| 1 elemento, 1-3 parametri | `set_element_parameters` |
| N elementi, stesso parametro/valore | `batch_modify_parameter_values` (dryRun prima) |
| N elementi, parametri diversi | `sync_csv_parameters` |
| Copia tra elementi | `match_element_properties` con `parameterNames` esplicito |

### Clash

| Caso | Tool |
|---|---|
| Conteggio + lista ID | `detect_clashes` |
| Review visuale 3D | `show_clashes` |

## Required checks

- [ ] Verificato che `check_model_health` non basti prima di salire al livello 3-4.
- [ ] `filter_elements` chiamato con il wrapper `data` obbligatorio.
- [ ] `batch_modify_parameter_values` eseguito con `dryRun: true` come prima call.
- [ ] Su modelli architettonici, ricordare che colonne = `OST_Columns` (non `OST_StructuralColumns`).

## Avoid

- Non partire dal livello 4 (`workflow_model_audit` completo) se basta il livello 1.
- Non usare `filter_elements` con `maxElements: 1000` di default.
- Non chiamare `audit_families` globale per cercare una singola categoria.
- Non assumere nomi parametri custom (WBS_*, Code_*): scoprirli prima.
- Non leggere una colonna vuota come un valore vuoto: senza
  `unresolvedParameterNames` il nome è stato risolto, con esso no.
- Non leggere un numero senza la sua `unit`: Revit conserva piedi, piedi² e
  piedi³ qualunque siano le unità del progetto (`internalValue`).
- Non cercare un tipo di sistema con `list_family_types` sperando in un
  `familyName`: usare `list_system_types`.
- Non usare `save_as_document` per ottenere un progetto vuoto: usare
  `create_document`.
- Non cercare un tool per aprire il documento di una famiglia: non esiste
  (deadlock di `Document.EditFamily`). Modificare il `.rfa` fuori da Revit e poi
  `load_family`.
