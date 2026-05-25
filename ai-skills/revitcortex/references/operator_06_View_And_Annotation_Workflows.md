# 06 — View & Annotation Workflows

**Scope:** Tag, color, dimensions, view templates, viewports.
**Sources:** CLAUDE.md §"Tool Behavioral Notes", WORKFLOWS.md §"Tagging/Colorazione/Quotatura"
**Last verified:** 2026-05-25

## Decision rules

### Tag rooms / Tag walls

- Operano SOLO sulla vista attiva di Revit.
- La vista deve contenere elementi visibili della categoria richiesta.
- Sequenza: `get_current_view_info` → verifica → `tag_rooms` / `tag_walls`.

### Color elements

- Usa nomi categoria **localizzati** (dipende dalla lingua Revit).
- Funziona solo su viste con elementi visibili di quella categoria.
- FALLISCE su DrawingSheet / Cover Sheet: passare prima a FloorPlan o 3D.

### Create dimensions

- Il parametro Z deve corrispondere ESATTAMENTE alla quota del livello.
- Sequenza: `get_project_info` (livelli con quote) → `create_dimensions` con Z esatto.

### Naming viste

I seguenti caratteri sono **vietati** nei nomi vista:
`:` `\` `/` `{` `}` `[` `]` `|` 

Per timestamp, usare `HH-mm-ss` (mai `HH:mm:ss`).

## Required checks

- [ ] Vista attiva verificata con `get_current_view_info` prima di tag/color.
- [ ] Per `color_elements`: vista NON è sheet/cover.
- [ ] Per `create_dimensions`: Z preso da `get_project_info` (non a mano).
- [ ] Per nomi vista: nessun carattere vietato.

## Avoid

- Non chiamare `tag_rooms`/`tag_walls` senza verificare la vista attiva.
- Non usare `color_elements` su sheet (fallirà).
- Non approssimare Z in `create_dimensions`.
- Non includere `:` o `/` nei nomi vista.
