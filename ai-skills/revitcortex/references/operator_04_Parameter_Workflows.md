# 04 — Parameter Workflows

**Scope:** Read/Write parametri Revit (single, bulk, CSV-based, copy).
**Sources:** CLAUDE.md §"Modifying parameters", WORKFLOWS.md §"Aggiornamento Parametri"
**Last verified:** 2026-05-25

## Decision rules

### Quale tool

| Caso | Tool | Note |
|---|---|---|
| 1 elemento, 1-3 parametri | `set_element_parameters` | |
| N elementi, stesso parametro+valore | `bulk_modify_parameter_values` | dryRun obbligatorio |
| N elementi, parametri diversi per ognuno | `sync_csv_parameters` | CSV con colonna `ElementId` |
| Copia parametri tra elementi | `match_element_properties` | sempre con `parameterNames` esplicito |

### Discovery nomi parametri

1. Per parametri custom (WBS_*, Code_*, ecc.): mai assumere il nome.
2. `get_element_parameters` su 1 elemento campione → leggere i nomi esatti.
3. Type parameter sono prefissati con `[Type]` nella risposta.

### Type parameter

- Per filtrare un type parameter (es. nome del tipo): `filter_by_parameter_value` con `parameterType: "type"`.
- Default `parameterType: "both"` può NON risolvere stringhe type-level.

## Required checks

- [ ] Nomi parametri verificati prima del bulk update.
- [ ] `bulk_modify_parameter_values` con `dryRun: true` come prima call.
- [ ] Dal dryRun lette solo `modifiedCount` e `skippedCount`.
- [ ] `match_element_properties` sempre con `parameterNames` esplicito.

## Avoid

- Non chiamare `set_element_parameters` in loop per N elementi: usare `bulk` o `sync_csv`.
- Non assumere nomi parametri custom.
- Non eseguire `bulk_modify_parameter_values` senza dryRun.
- Non leggere l'intera lista elementi dal dryRun.
