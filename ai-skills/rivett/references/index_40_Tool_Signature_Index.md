# 40 — Tool Signature Index

**Scope:** Indice storico non più mantenuto in RiveTT.
**Sources:** Attributi `McpServerTool` nel progetto serveur C#.
**Last verified:** 2026-08-21

## Come consultare

La fonte canonica delle signatures est le code du serveur C# ; aucun schéma généré n'est conservé.

## Categorie

| Prefisso | Categoria | Esempi |
|---|---|---|
| `get_`, `list_`, `find_`, `analyze_`, `check_`, `export_`, `measure_`, `audit_` | Read-only | `get_project_info`, `analyze_model_statistics` |
| `set_`, `batch_`, `sync_`, `create_`, `delete_`, `purge_`, `rename_`, `modify_`, `override_`, `change_` | Write | `set_element_parameters`, `batch_modify_parameter_values` |
| `ifc_*` | IFC integration | `ifc_link`, `ifc_rebuild_walls`, `ifc_export_basic` |
| `workflow_*` | Workflow composti | `workflow_model_audit`, `workflow_room_documentation` |
| `sync_navisworks_selection` | NavisCortex bridge | (nessun prefisso comune, nome unico) |
| `ping_revit`, `get_*` | Meta | Diagnostica, capabilities |

## Contrats RiveTT ajoutés

- `get_server_capabilities()`
- `capture_selection(elementIds?, ttlMinutes?)`
- `filter_elements(..., pageSize?, cursor?, responseMode?)`
- `duplicate_storey(sourceLevelId|sourceLevelName, targetElevationMm, ..., dryRun=true)`
- `detach_wall_constraint(wallIds, mode, dryRun=true)`
- `manage_model_groups(action, ..., dryRun=true)`
- `list_system_types(category?, nameFilter?, includeLoadable?, limit?)`
- `create_detail_line(path, viewId?, lineStyleName?, dryRun=true)`
- `create_model_line(path, lineStyleName?, dryRun=true)`
- `create_room_separation_line(path, viewId?, dryRun=true)`
- `place_title_block(sheetId, titleBlockId?, dryRun=true)`
- `create_sheet(sheetNumber, sheetName, titleBlockId?, titleBlockFamilyName?, titleBlockTypeName?, dryRun?)`
- `save_document(dryRun?)` / `save_as_document(targetPath, overwrite?, dryRun?)`
- `export_elements_data(..., elementIds?, countOnly?)`
- `export_room_data(..., levelName?, levelId?, nameFilter?)`
- `get_elements_in_spatial_volume(..., containment?)`
- `get_element_parameters(elementIds, includeTypeParameters?, parameterNames?, compact?)`
- `get_schedule_data(scheduleId, maxRows?, includeAvailableFields?)`
- `list_materials(nameFilter?, materialClass?, compact?)`
- `create_door(..., zMode?)` / `create_window(..., zMode?)`
- `create_document(targetPath, templatePath?, overwrite?, activate?, dryRun?)`
- `open_document(filePath, detachFromCentral?, dryRun?)`
- `create_stair(baseLevelId, topLevelId, runs, stairsTypeId?, widthMm?, railingTypeId?, dryRun?)`
- `edit_group_members(groupId, addElementIds?, removeElementIds?, newTypeName?, allowMultiInstance?, dryRun?)`

## Contrat de réponse (v0.2.0)

Tout succès porte `execution.{connector, serverVersion, revitVersion, mode,
toolReadOnly, toolDestructive, writesAllowed, cached}`. `toolReadOnly` classe
l'outil, pas la session. Les anciens noms `readOnly`/`destructive` n'existent
plus.

## Aggiornamento

Mettre à jour les descriptions directement sur les attributs `McpServerTool`.
