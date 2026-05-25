# 07 — IFC Workflows

**Scope:** Link, rebuild, export IFC. Pattern dryRun obbligatorio per rebuild.
**Sources:** docs/RevitCortex_IFC_Guide.md, WORKFLOWS.md §"IFC"
**Last verified:** 2026-05-25

## Decision rules

### Verifica capacità

Prima di qualsiasi operazione IFC: `ifc_get_capabilities`. Mostra versioni IFC supportate e se `revit-ifc` plugin è installato.

### Sequenza canonica rebuild

1. `ifc_open_or_import` oppure `ifc_link`
2. `ifc_analyze_rebuildability` con `compact: true`
3. `ifc_list_rebuild_candidates` con `compact: true` (filtrato per categoria)
4. Per categoria: `ifc_rebuild_walls` / `ifc_rebuild_floors` / `ifc_rebuild_roofs` / `ifc_rebuild_openings` / `ifc_rebuild_structural_members` / `ifc_rebuild_family_instances`
5. `ifc_compare_original_vs_rebuilt` per verifica.
6. `ifc_tag_unreconstructable_elements` per gli elementi non ricostruibili.

### Export

- Basic: `ifc_export_basic`
- Con configurazione: `ifc_get_export_configuration` → `ifc_export_with_configuration`

### Mapping famiglie

`ifc_set_family_mapping_file` consente di caricare un mapping custom prima del rebuild.

## Required checks

- [ ] `ifc_get_capabilities` chiamato come prima call IFC della sessione.
- [ ] Rebuild eseguito categoria per categoria, non in blocco.
- [ ] `ifc_validate_request` chiamato prima di rebuild costosi.
- [ ] `compact: true` per i tool di analisi (rebuildability, candidates).

## Avoid

- Non tentare rebuild senza prima `ifc_analyze_rebuildability`.
- Non chiamare rebuild su tutte le categorie in parallelo.
- Non importare IFC pesanti senza verificare prima le capacità.
