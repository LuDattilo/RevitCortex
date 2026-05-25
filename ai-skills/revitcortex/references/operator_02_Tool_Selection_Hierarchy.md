# 02 — Tool Selection Hierarchy

**Scope:** Scegliere il tool con minor costo token che risolve il task.
**Sources:** CLAUDE.md §"Tool Selection Hierarchy", WORKFLOWS.md §"Verifica Stato Modello", WORKFLOWS.md §"Ricerca Elementi"
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
| Range / AND-OR / multi-param | `ai_element_filter` | Wrappare in `{"data": {...}}` |
| Elementi vista attiva | `get_current_view_elements` con `fields` e `limit` | |
| Volume/stanza | `get_elements_in_spatial_volume` con `categoryFilter` | |
| Parametro custom vuoto | NON guess: prima `get_element_parameters` su 1 elemento campione per scoprire i nomi | Mai assumere il formato del nome |

### Modifica parametri

| Caso | Tool |
|---|---|
| 1 elemento, 1-3 parametri | `set_element_parameters` |
| N elementi, stesso parametro/valore | `bulk_modify_parameter_values` (dryRun prima) |
| N elementi, parametri diversi | `sync_csv_parameters` |
| Copia tra elementi | `match_element_properties` con `parameterNames` esplicito |

### Clash

| Caso | Tool |
|---|---|
| Conteggio + lista ID | `clash_detection` |
| Review visuale 3D | `workflow_clash_review` |

## Required checks

- [ ] Verificato che `check_model_health` non basti prima di salire al livello 3-4.
- [ ] `ai_element_filter` chiamato con il wrapper `data` obbligatorio.
- [ ] `bulk_modify_parameter_values` eseguito con `dryRun: true` come prima call.
- [ ] Su modelli architettonici, ricordare che colonne = `OST_Columns` (non `OST_StructuralColumns`).

## Avoid

- Non partire dal livello 4 (`workflow_model_audit` completo) se basta il livello 1.
- Non usare `ai_element_filter` con `maxElements: 1000` di default.
- Non chiamare `audit_families` globale per cercare una singola categoria.
- Non assumere nomi parametri custom (WBS_*, Code_*): scoprirli prima.
