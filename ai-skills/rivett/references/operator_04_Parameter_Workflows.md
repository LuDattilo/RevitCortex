# 04 — Parameter Workflows

**Scope:** Read/Write parametri Revit (single, bulk, CSV-based, copy).
**Sources:** RiveTT parameter tools
**Last verified:** 2026-05-25

## Decision rules

### Quale tool

| Caso | Tool | Note |
|---|---|---|
| 1 elemento, 1-3 parametri | `set_element_parameters` | |
| N elementi, stesso parametro+valore | `batch_modify_parameter_values` | dryRun obbligatorio |
| N elementi, parametri diversi per ognuno | `sync_csv_parameters` | righe con `elementId`; usare `parameterMap` per BuiltInParameter |
| Copia parametri tra elementi | `match_element_properties` | sempre con `parameterNames` esplicito |

### Discovery nomi parametri

1. Per parametri custom (WBS_*, Code_*, ecc.): mai assumere il nome.
2. `get_element_parameters` su 1 elemento campione → leggere i nomi esatti.
3. Type parameter sono prefissati con `[Type]` nella risposta.
4. In un progetto localizzato, mappare le intestazioni stabili verso un enum
   `BuiltInParameter` (`{"Numéro":"ROOM_NUMBER"}`) invece di dipendere dal
   testo visualizzato.

### Portée stable

Per una sequenza preview → verifica → scrittura, preferire nell'ordine:

1. `elementIds` espliciti;
2. `capture_selection` e il relativo `selectionToken` temporaneo;
3. `savedSelectionName` per una selezione persistente nel modello;
4. `scope: selection` solo per una singola chiamata immediata.

### Type parameter

- Per filtrare un type parameter (es. nome del tipo): `filter_by_parameter_value` con `parameterType: "type"`.
- Default `parameterType: "both"` può NON risolvere stringhe type-level.

## Required checks

- [ ] Nomi parametri verificati prima del bulk update.
- [ ] `batch_modify_parameter_values` con `dryRun: true` come prima call.
- [ ] Dal dryRun lette solo `modifiedCount` e `skippedCount`.
- [ ] Il conteggio `processed` corrisponde alla portée attesa.
- [ ] Ogni preview contiene `mutated: false`.
- [ ] `match_element_properties` sempre con `parameterNames` esplicito.

## Avoid

- Non chiamare `set_element_parameters` in loop per N elementi: usare `batch_modify_parameter_values` o `sync_csv_parameters`.
- Non assumere nomi parametri custom.
- Non eseguire `batch_modify_parameter_values` senza dryRun.
- Non leggere l'intera lista elementi dal dryRun.
