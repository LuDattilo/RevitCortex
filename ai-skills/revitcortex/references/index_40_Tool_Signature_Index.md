# 40 — Tool Signature Index

**Scope:** Lookup veloce delle firme dei 157 tool MCP di RevitCortex.
**Sources:** tool-schemas.txt (generato da server/generate-tool-schemas-csharp.mjs)
**Last verified:** 2026-05-25

## Come consultare

La fonte canonica delle firme e `tool-schemas.txt` nella root del repo. Una riga per tool, formato compatto.

Per cercare un tool specifico:
```bash
grep "^get_element_parameters" tool-schemas.txt
```

Per listare tutti i tool di una categoria:
```bash
grep -E "^(ifc_|pbi_|workflow_)" tool-schemas.txt
```

## Categorie

| Prefisso | Categoria | Esempi |
|---|---|---|
| `get_`, `list_`, `find_`, `analyze_`, `check_`, `export_`, `measure_`, `audit_` | Read-only | `get_project_info`, `analyze_model_statistics` |
| `set_`, `bulk_`, `sync_`, `create_`, `delete_`, `purge_`, `wipe_`, `rename_`, `modify_`, `override_`, `change_` | Write | `set_element_parameters`, `bulk_modify_parameter_values` |
| `ifc_*` | IFC integration | `ifc_link`, `ifc_rebuild_walls`, `ifc_export_basic` |
| `pbi_*` | PowerBI | `pbi_publish_elements`, `pbi_query` |
| `workflow_*` | Workflow composti | `workflow_model_audit`, `workflow_clash_review` |
| `cross_app_*` | NavisCortex bridge | `cross_app_selection` |
| `say_hello`, `get_*` | Meta | Diagnostica, capabilities |

## Aggiornamento

Dopo ogni modifica a un tool:
```bash
node server/generate-tool-schemas-csharp.mjs
git add tool-schemas.txt
git commit -m "chore: regenerate tool-schemas.txt"
```

Questo file (`index_40`) NON deve essere riscritto a ogni cambio: cita solo le categorie e le convenzioni. Il dettaglio sta in `tool-schemas.txt`.
