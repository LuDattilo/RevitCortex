# Inventaire des outils RiveTT

> Document **généré** par `tools/audit-tool-surface.py`. Ne pas éditer à la main :
> relancer le script après toute modification de la surface d'outils.

Relevé du 2026-08-27 — connecteur 0.2.0 — **198 outils publiés**, 195 classes runtime.

## Comment lire ce document

Deux surfaces sont croisées : les attributs `[McpServerTool]` du serveur MCP et les
classes `ICortexTool` du runtime. La question posée à chaque outil est celle qui a
coûté le plus cher jusqu'ici : **un paramètre publié est-il vraiment lu**.

| Colonne | Ce qu'elle dit |
|---|---|
| Nature | `lecture` ou `écriture` selon `[ToolSafety]`. Depuis le verrou du ruban, ce classement est une frontière de permission, plus une simple étiquette |
| dryRun | l'outil accepte une prévisualisation |
| Int. | intérêt pour une agence d'architecture : **5** geste quotidien, **4** utile régulier, **3** ponctuel, **2** marginal, **1** hors périmètre. Jugement d'usage, pas une propriété du code : il vit dans les listes `TIER5`/`TIER4`/`TIER2` du script et se corrige en les éditant |
| Défaut probable | **critique** et **majeur** vérifiés dans le code ; **signal** détecté automatiquement, avec des faux positifs quand la lecture passe par un helper partagé ou un DTO typé ; **mineur** systémique |

Une flèche `→` signale une **façade** : un nom MCP qui appelle un autre outil runtime.

## Synthèse

| Mesure | Valeur |
|---|---|
| Outils publiés | **198** |
| Dont écriture | **138** (70 %) — c'est la part que le verrou du ruban gouverne |
| Écritures sans `dryRun` | **79**, alors que le contrat annonce `dryRunDefault: true` |
| Défauts critiques et majeurs corrigés | **8**, gardés par `ConfirmedDefectFixSourceTests` |
| Lacunes API comblées depuis le relevé précédent | **16** sur 19 |
| Erreurs génériques `Failed: …` sans suggestion | **129** |
| Géométrie par boîte englobante | **15** |
| Classement `[ToolSafety]` en désaccord avec le nom | **9** |
| Défauts confirmés / signaux à vérifier | **0** / **10** |

## Répartition par catégorie

| Catégorie | Outils | Part |
|---|---:|---:|
| Elements | 65 | 33 % |
| Project | 49 | 25 % |
| IFC | 20 | 10 % |
| Views | 13 | 7 % |
| LinkedFiles | 10 | 5 % |
| Annotations | 9 | 5 % |
| Parameters | 8 | 4 % |
| Documents | 7 | 4 % |
| Sheets | 5 | 3 % |
| Meta | 4 | 2 % |
| Workflows | 4 | 2 % |
| Architecture | 2 | 1 % |
| Code | 1 | 1 % |
| Interop | 1 | 1 % |

Le ferraillage et la charpente métallique — 112 outils, 38 % de la surface — ont été
retirés du dépôt, pas filtrés. Ce qui reste est le catalogue que l'agent lit à chaque
session : 198 outils dont 70 % d'écriture, tous dans le périmètre logement,
équipement, tertiaire et santé.

## Défauts corrigés

Les huit défauts critiques et majeurs du relevé précédent. Ils restent listés :
un inventaire qui oublie ce qui a cassé une fois laisse le même défaut revenir sans
que personne le reconnaisse. `ConfirmedDefectFixSourceTests` échoue si l'un revient.

| Outil | Gravité | Ce que le code faisait | Ce qu'il fait maintenant |
|---|---|---|---|
| `batch_create_sheets` | critique | fenêtres placées à (0,5 ft ; 0,5 ft) en dur, alors que l'origine de la feuille n'est pas le coin du cadre : hors cadre sur le cartouche A1 français. | Le cadre est mesuré sur l'instance de cartouche via `SheetFrame`, partagé avec `place_viewport` ; plusieurs vues sont pavées au lieu d'être empilées. |
| `workflow_sheet_set` | critique | `viewIds` était publié dans la spec et jamais lu : les feuilles sortaient vides, sans signalement. | Les `viewIds` sont lus et placés ; la réponse réconcilie `requestedViewCount` et `placedViewCount`. Outil retiré depuis (chantier de consolidation 27/08) : ce comportement vit maintenant dans `batch_create_sheets`. |
| `delete_material` | majeur | destructif sans dryRun. | `dryRun` par défaut via `DeletionPreview`, qui sonde la cascade réelle. |
| `delete_schedule` | majeur | destructif sans dryRun. | `dryRun` par défaut via `DeletionPreview`, qui sonde la cascade réelle. |
| `delete_selection` | majeur | destructif sans dryRun, alors que `delete_element` en a un par défaut. | `dryRun` par défaut via `DeletionPreview` ; la réponse précise que seule la liste enregistrée est supprimée, pas les éléments. Fusionné dans `manage_selection` (action=delete) le 27/08, avec save_selection et load_selection — même comportement. |
| `ifc_set_family_mapping_file` | majeur | classé lecture seule alors qu'il modifie un réglage d'export persistant : il traversait le verrou d'écriture du ruban. | Reclassé `[ToolSafety(false, false)]` : il passe désormais par le verrou. |
| `send_code_to_revit` | majeur | aucun dryRun sur l'outil le plus puissant, et la description annonçait une confirmation dans Revit qui n'existe pas. | `dryRun` par défaut : la sandbox est vérifiée, rien n'est exécuté ni écrit sur disque. La description ne promet plus de dialogue. |
| `workflow_clash_review` | majeur | détection par boîtes englobantes alors que `detect_clashes` utilise l'intersection solide : l'outil composé rendait plus de faux positifs que le simple. | Les deux outils appellent la même passe `ClashFinder` (pré-filtre par boîtes, puis `ElementIntersectsElementFilter`). Gardé distinct de `detect_clashes` lors du chantier de consolidation du 27/08 : il crée une vue (écriture), l'autre reste lecture seule — les fusionner aurait cassé le verrou d'écriture. Renommé `show_clashes` le 27/08 (convention `show_`, cohérent avec `show_cross_model_elements`). |

## Défauts confirmés

Aucun défaut critique ou majeur ouvert.


### Arbitrages ouverts

Deux outils classés lecture seule écrivent sur le disque. Le modèle n'est pas touché,
donc le classement se défend — mais le verrou du ruban ne les arrête pas, et c'est une
décision à prendre, pas un oubli : `batch_export` et `workflow_data_roundtrip`.

## Signaux à vérifier

Détection automatique. Un signal n'est pas un défaut : la lecture passe peut-être
par un helper partagé ou un DTO typé, ou la clé annoncée n'est qu'un exemple de
documentation.

| Outil | Signal |
|---|---|
| `batch_rename_affix` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds, savedSelectionName, scope, selectionToken |
| `clear_parameter_values` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds, savedSelectionName, scope, selectionToken |
| `color_elements` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : viewId |
| `detach_wall_constraint` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : allowedWarningIds, warningPolicy |
| `duplicate_family_type` | clé imbriquée annoncée, absente du runtime : paramName |
| `duplicate_storey` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : allowedWarningIds, warningPolicy |
| `filter_by_parameter_value` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds |
| `sync_csv_parameters` | clé imbriquée annoncée, absente du runtime : paramName1 |
| `sync_navisworks_selection` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : append, createLinkedMarkers, createSectionBox, isolate, usePostCommandIsolate |
| `tag_rooms` | paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : viewId |

## Inventaire complet

### Elements — 65 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `filter_by_parameter_value` | lecture | — | 5 | Filter elements by one parameter condition, or several combined with AND/OR via the conditions array. Conditions: equals, not_equals, contains, not_co… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds |
| `batch_rename` | écriture destructif | oui | 5 | Batch rename elements or system types in the Revit project. Supports both loadable-family elements and system types (wall/floor/ceiling/roof types). | **mineur** — erreur générique sans suggestion |
| `copy_elements` | écriture | — | 5 | Copy elements with optional mm offset. Can target a different view (sourceViewId+targetViewId) or another OPEN document (targetDocumentTitle). | **mineur** — pas de dryRun |
| `create_door` → `create_point_based_element` | écriture | oui | 5 | Place a door family type in a host wall. ELEVATION: locationPoint.z is an ABSOLUTE project elevation by default - pass zMode=relativeToLevel to give z… | **mineur** — géométrie par boîte englobante |
| `create_floor` | écriture | oui | 5 | Create an architectural floor from a boundary (or a room), optionally with holes. Provide boundaryPoints OR roomId. Previews by default: the dry run r… | **mineur** — erreur générique sans suggestion |
| `create_grid` | écriture destructif | — | 5 | Create a grid system (X and/or Y grids by count + spacing), or rename/delete an existing grid. action=create\|rename\|delete. Spacing/extent values are… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_level` | écriture destructif | oui | 5 | Create, edit, rename, or delete a level. action=create\|set\|rename\|delete. For set/rename/delete identify the level by levelId or name. | **mineur** — erreur générique sans suggestion |
| `create_room` | écriture | oui | 5 | Create a room at a point on a level. x/y are plan coordinates in mm; the level sets the elevation. A point that is not inside a closed loop of room-bo… | **mineur** — erreur générique sans suggestion |
| `create_room_separation_line` | écriture | oui | 5 | Draw room separation lines in a plan view to split or bound a room without building a physical wall. path is a JSON array [{x,y,z}, ...] in mm. This i… | **mineur** — erreur générique sans suggestion |
| `create_stair` | écriture | oui | 5 | Create a native component stair between two levels. runs is a JSON array [{p0:{x,y}, p1:{x,y}}, ...] in mm plan coordinates — the levels drive the ele… | **mineur** — erreur générique sans suggestion |
| `create_window` → `create_point_based_element` | écriture | oui | 5 | Place a window family type in a host wall. ELEVATION: locationPoint.z is an ABSOLUTE project elevation by default - pass zMode=relativeToLevel to give… | **mineur** — géométrie par boîte englobante |
| `delete_element` | écriture destructif | oui | 5 | Delete elements. The dryRun preview reports the real cascade (dependent tags, sketches, railings...) and any group membership. Deleting a group MEMBER… | **mineur** — erreur générique sans suggestion |
| `edit_group_members` | écriture destructif | oui | 5 | Add or remove members of a model group. The Revit API cannot edit group members in place, so this ungroups the instance, changes the member set and cr… | **mineur** — erreur générique sans suggestion |
| `export_room_data` | lecture | — | 5 | Export room data (area in m2, perimeter, level, department). Filter inside Revit with levelName/levelId and nameFilter instead of returning every room… | **mineur** — géométrie par boîte englobante ; erreur générique sans suggestion |
| `export_to_excel` | lecture | — | 5 | Export element data from a Revit category to an Excel file. | **mineur** — erreur générique sans suggestion |
| `filter_elements` | lecture | — | 5 | Paginated element query by category, class, family symbol, bounding box, or level. Returns totalCount, returnedCount, appliedLimit and nextCursor. res… | **mineur** — classement déclaré (lecture) différent du préfixe du nom ; géométrie par boîte englobante |
| `get_current_view_elements` | lecture | — | 5 | List elements visible in the currently active view. categoryFilter is a single-category shortcut (OST code, English name or localized label); modelCat… | **mineur** — erreur générique sans suggestion |
| `get_linked_elements` | lecture | — | 5 | Query elements from linked Revit models with optional filtering. parameterNames is additive — without it only basic fields are returned. | **mineur** — erreur générique sans suggestion |
| `get_selected_elements` | lecture | — | 5 | Get currently selected elements in Revit. | **mineur** — erreur générique sans suggestion |
| `import_from_excel` | écriture destructif | oui | 5 | Import parameter values from an Excel file into Revit elements. | **mineur** — erreur générique sans suggestion |
| `manage_area_plans` | écriture | — | 5 | Builds regulatory area surfaces (SHAB/SU/SDP): area schemes, area plan views, area boundary lines, and Area elements. action=list_schemes\|duplicate_sc… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `modify_element` | écriture | — | 5 | Move, rotate, mirror, or copy elements. Vectors are {"x":mm,"y":mm,"z":mm} JSON objects. move needs translation; rotate needs rotationCenter + rotatio… | **mineur** — pas de dryRun |
| `create_wall` → `create_line_based_element` | écriture | oui | 5 | Create one native Revit wall. wallTypeId and baseLevelId are required. Set topLevelId to constrain the wall to a level; topOffset is in mm and may be… | — |
| `export_elements_data` | lecture | — | 5 | Export element data as JSON or CSV, by category and/or by explicit elementIds. Parameter names may be given in English or in the document language (Ma… | — |
| `get_element_parameters` | lecture | — | 5 | Get parameters of elements by Revit element ID. Numeric values come back in PROJECT display units with an explicit unit plus the Revit internal value… | — |
| `manage_model_groups` | écriture destructif | oui | 5 | Inventory model groups, duplicate a group type and optionally swap selected instances, or ungroup selected model groups. Write actions preview by defa… | — |
| `renumber_elements` | écriture destructif | oui | 5 | Renumber rooms/doors/windows by location or name. Writes into the specified parameter; supports prefix/suffix and start/increment. | — |
| `set_element_parameters` | écriture destructif | oui | 5 | Set parameter values on one or more elements. Pass requests as a JSON-encoded array string. Supports parameterName by display name and builtInParamete… | — |
| `color_elements` | écriture | — | 4 | Color a view's elements of a category by grouping them on a parameter value, or reset (clear) those color overrides. action=color\|reset. Pass viewId t… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : viewId |
| `duplicate_family_type` | écriture | — | 4 | Duplicate a loadable family type with a new name and optional parameter overrides. | **signal** — clé imbriquée annoncée, absente du runtime : paramName |
| `add_curtain_grid_line` | écriture | — | 4 | Adds a grid line to an existing curtain wall/system's grid (create the wall itself with create_line_based_element and a curtain wall type). hostElemen… | **mineur** — pas de dryRun |
| `add_curtain_mullions` | écriture | — | 4 | Adds mullions to an existing curtain wall/system's grid lines. hostElementId and mullionTypeId are required; applies to every ungridded segment unless… | **mineur** — pas de dryRun |
| `capture_selection` | lecture | — | 4 | Capture explicit element IDs or the current Revit selection as a reusable temporary token. Tokens expire and are scoped to the active document session… | **mineur** — classement déclaré (lecture) différent du préfixe du nom |
| `change_element_type` | écriture destructif | — | 4 | Change the type of one or more elements to a target type specified by ID or name. | **mineur** — pas de dryRun |
| `create_array` | écriture | — | 4 | Create a linear or radial array. Default builds a real associative Revit ArrayElement (editable count); set associative=false for loose copies. linear… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_detail_line` | écriture | oui | 4 | Draw 2D detail lines in a view (view-owned, not visible in other views). path is a JSON array [{x,y,z}, ...] in mm; consecutive points become segments… | **mineur** — erreur générique sans suggestion |
| `create_filled_region` | écriture | — | 4 | Create a filled region in a view from a closed boundary, optionally with holes (inner loops). | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_model_line` | écriture | oui | 4 | Draw 3D model lines on a horizontal sketch plane. path is a JSON array [{x,y,z}, ...] in mm; all points must share the same z, which sets the plane el… | **mineur** — erreur générique sans suggestion |
| `create_opening` | écriture | — | 4 | Cuts an opening or a vertical shaft. openingType=shaft\|host\|wall. shaft: baseLevelId+topLevelId+curves (closed loop, mm) — a vertical shaft through ev… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_point_based_element` | écriture | oui | 4 | Create point-based elements. Pass [{category, locationPoint:{x,y,z}, typeId?, levelId?, baseLevel?, hostWallId?, facingFlipped?, handFlipped?, rotatio… | **mineur** — géométrie par boîte englobante |
| `create_ramp` | écriture | oui | 4 | Create a native component ramp between two levels (accessibility/PMR). runs is a JSON array [{p0:{x,y}, p1:{x,y}}, ...] in mm plan coordinates — the l… | **mineur** — erreur générique sans suggestion |
| `create_structural_framing_system` | écriture | — | 4 | Create a beam system on a level over a rectangular area. Default builds a real associative Revit BeamSystem (editable layout); set associative=false f… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_surface_based_element` | écriture | — | 4 | Create surface-based elements: floors, ceilings, or roofs (OST_Floors, OST_Ceilings, OST_Roofs — a roof is a real FootPrintRoof, Document.Create.NewFo… | **mineur** — pas de dryRun |
| `create_toposolid` | écriture | — | 4 | Creates a Toposolid (site/ground surface) from a closed boundary loop (Toposolid.Create). toposolidTypeId and levelId are required — list types with l… | **mineur** — pas de dryRun |
| `export_families` | lecture | — | 4 | Export loaded families as .rfa files into a target directory. | **mineur** — erreur générique sans suggestion |
| `find_undimensioned_elements` | lecture | — | 4 | Find elements not referenced by dimensions | **mineur** — erreur générique sans suggestion |
| `find_untagged_elements` | lecture | — | 4 | Find elements without tags in a view | **mineur** — erreur générique sans suggestion |
| `get_element_solid_geometry` | lecture | — | 4 | Get an element's REAL solid geometry (bounding box, centroid, volume m3, face/edge counts AND inferred cross-section shape: circular/rectangular/compl… | **mineur** — erreur générique sans suggestion |
| `get_elements_in_spatial_volume` | lecture | — | 4 | Find elements within a 3D bounding box or room volume. volumeType=room uses volumeIds; volumeType=custom uses customMinX..customMaxZ. | **mineur** — erreur générique sans suggestion |
| `load_family` | écriture | — | 4 | Load a family into the Revit project, or reload one already there (e.g. after editing its .rfa outside Revit). Also lists loaded families or duplicate… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `manage_selection` | écriture destructif | oui | 4 | CRUD on named saved selections (SelectionFilterElement). action=save\|load\|list\|delete. name is required for save/load/delete (ignored for list). save:… | **mineur** — erreur générique sans suggestion |
| `manage_view_display` | écriture | — | 4 | Select, highlight, isolate, hide, or zoom to elements in the active view. Actions: select, selectionbox, setcolor, settransparency, hide, temphide, is… | **mineur** — pas de dryRun |
| `match_element_properties` | écriture destructif | — | 4 | Copy parameter values from one source element to one or more target elements. | **mineur** — pas de dryRun |
| `measure_between_elements` | lecture | — | 4 | Measure distance between two elements or two points in mm. Provide either elementId1/elementId2, or point1/point2 (as JSON arrays [x,y,z]). | **mineur** — géométrie par boîte englobante |
| `set_element_phase` | écriture | — | 4 | Assign created/demolished phase to elements. Pass a JSON array of requests: [{elementId, createdPhaseId?, demolishedPhaseId?}]. The older names phaseC… | **mineur** — pas de dryRun |
| `set_element_workset` | écriture | — | 4 | Move elements to a different workset. Pass a JSON array of requests: [{elementId, worksetName}]. Worksets are resolved by name only. | **mineur** — pas de dryRun |
| `set_material_properties` | écriture destructif | oui | 4 | Set identity, appearance, product info, and asset assignments on Revit materials. Each request is a FLAT object keyed by materialId plus any of: name,… | **mineur** — erreur générique sans suggestion |
| `create_line_based_element` | écriture | oui | 4 | Create line-based elements (walls, beams). Pass a JSON array of specs: [{category, locationLine:{p0:{x,y,z}, p1:{x,y,z}, pMid?:{x,y,z}}, typeId?, heig… | — |
| `get_curtain_grid_info` | lecture | — | 4 | Reads an existing curtain wall/system grid: U/V grid line ids, panel ids, mullion ids. hostElementId is the curtain wall or curtain system element. | — |
| `get_room_openings` | lecture | — | 4 | Get doors/windows adjacent to rooms with dimensions. Filter by roomIds, roomNumbers, or levelName. | — |
| `edit_family` | écriture destructif | oui | 3 | Edits a loaded family's type parameters in the background - no window opens. Pass familyId or familyName, and changes as JSON: [{typeName, parameters:… | **mineur** — erreur générique sans suggestion |
| `rename_families` | écriture destructif | oui | 3 | Rename loaded families (and optionally their types) with find/replace, prefix, or suffix operations. | **mineur** — erreur générique sans suggestion |
| `detach_wall_constraint` | écriture destructif | oui | 2 | Preview or detach wall top-level constraints or Revit 2027 top/base attachments. Grouped walls are reported and skipped instead of rolling back unrela… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : allowedWarningIds, warningPolicy |
| `create_assembly` | écriture | — | 2 | Groups elements into an AssemblyInstance (prefabrication/shop drawings), or splits them into Parts (demolition/phasing sequencing). action=create_asse… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `get_elements_by_unique_id` | lecture | — | 2 | Resolve Revit UniqueId strings to ElementId records for cross-app workflows. | — |

### Project — 49 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `duplicate_storey` | écriture destructif | oui | 5 | Preview or transactionally duplicate model elements from one level to a target elevation. Reports view-specific, grouped, and constrained dependencies… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : allowedWarningIds, warningPolicy |
| `batch_export` | lecture | — | 5 | Export views/sheets to DWG, DXF, DGN, PDF, or image (PNG) formats. | **mineur** — classé lecture seule et écrit sur le disque. Volontaire (le modèle n'est pas touché) mais à arbitrer : le verrou n'empêche pas cet écrit. |
| `check_model_health` | lecture | — | 5 | Run a model health check and return a health score. | **mineur** — erreur générique sans suggestion |
| `create_revision` | écriture | — | 5 | List, create, update, or assign revisions to sheets, and draw revision clouds. action=list\|create\|set\|add_to_sheets\|create_cloud. 'set' updates an exi… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_schedule` | écriture | — | 5 | Create a new schedule view in Revit. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_sheet` | écriture | oui | 5 | Create a sheet, with a title block. Pass titleBlockId (an OST_TitleBlocks family type id, from list_system_types or list_family_types) or a family/typ… | **mineur** — erreur générique sans suggestion |
| `export_schedule` | lecture | — | 5 | Export a schedule as JSON, or write it to a CSV/TSV file. Without exportPath the data comes back inline; with exportPath the file is written using del… | **mineur** — erreur générique sans suggestion |
| `get_current_view_info` | lecture | — | 5 | Get information about the currently active view in Revit. | **mineur** — erreur générique sans suggestion |
| `get_project_info` | lecture | — | 5 | Get project name, address, levels, phases, worksets, and links from the active Revit document. | **mineur** — erreur générique sans suggestion |
| `get_schedule_data` | lecture | — | 5 | Export schedule data as JSON from an existing schedule view. availableFields is omitted unless includeAvailableFields=true: it lists every schedulable… | **mineur** — erreur générique sans suggestion |
| `list_family_types` | lecture | — | 5 | List available family types in the Revit project. | **mineur** — erreur générique sans suggestion |
| `list_materials` | lecture | — | 5 | List materials in the active Revit document. nameFilter and materialClass narrow the list inside Revit - a real project carries 200+ materials. | **mineur** — erreur générique sans suggestion |
| `list_schedulable_fields` | lecture | — | 5 | Discover available schedulable fields for a category. | **mineur** — erreur générique sans suggestion |
| `list_system_types` | lecture | — | 5 | List the system types of a category: walls, floors, ceilings, roofs, railings, stairs, ramps, viewports, text, dimensions, sheets, title blocks. Syste… | **mineur** — erreur générique sans suggestion |
| `list_warnings` | lecture | — | 5 | Get model warnings from the active Revit document. | **mineur** — erreur générique sans suggestion |
| `list_worksets` | lecture | — | 5 | List all worksets in the active Revit document. | **mineur** — erreur générique sans suggestion |
| `manage_links` | écriture destructif | — | 5 | List, reload, reload-from-path, unload, or remove linked files. To add a NEW link use add_linked_file instead. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `place_title_block` | écriture | oui | 5 | Place a title block instance on an existing sheet. Use it to repair a sheet that has no frame. Call it without titleBlockId to get the list of title b… | **mineur** — erreur générique sans suggestion |
| `purge_unused` | écriture destructif | oui | 5 | Purge unused families/types and materials, and optionally unreferenced view templates and view filters, from the project. | **mineur** — erreur générique sans suggestion |
| `synchronize_with_central` | écriture destructif | oui | 5 | Synchronizes the local model with the workshared central file. AFFECTS THE WHOLE TEAM, not just this session, and cannot be undone from here. Requires… | — |
| `analyze_model_statistics` | lecture | — | 4 | Analyze element counts by category in the active Revit document. | **mineur** — erreur générique sans suggestion |
| `audit_families` | lecture | — | 4 | Audit families in the Revit project. Lists loadable (.rfa) families by default; set includeSystemFamilies=true to also list system-family types (wall/… | **mineur** — erreur générique sans suggestion |
| `clean_cad_links` | écriture destructif | — | 4 | Analyze and clean up imported/linked CAD files. action=list\|delete. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `count_lines_per_view` | lecture | — | 4 | Count detail lines per view (single document pass, safe on any model size) plus a project-wide model line count. Model lines have no owner view, so th… | **mineur** — erreur générique sans suggestion |
| `create_key_schedule` | écriture | — | 4 | Creates a key schedule (ViewSchedule.CreateKeySchedule) — a reusable finish/typology key table (room finish keys, dwelling-unit typologies), different… | **mineur** — pas de dryRun |
| `create_material` | écriture | — | 4 | Create a new material in the Revit project. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_preset_schedule` | écriture | — | 4 | Create a schedule from a predefined template. preset = door_by_room \| window_by_room \| room_finish \| material_takeoff \| sheet_list \| view_list. materi… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `delete_material` | écriture destructif | oui | 4 | Delete a material from the project by ID or name. Previews by default: the dry run names the material and reports the deletion cascade. Set dryRun=fal… | **mineur** — erreur générique sans suggestion |
| `delete_schedule` | écriture destructif | oui | 4 | Delete a schedule by ID or name. Previews by default: the dry run names the schedule and reports the cascade, including the viewports that placed it o… | **mineur** — erreur générique sans suggestion |
| `duplicate_material` | écriture | — | 4 | Duplicate an existing material with a new name. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `duplicate_schedule` | écriture | — | 4 | Duplicate a schedule with a new name | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `duplicate_system_type` | écriture destructif | — | 4 | Duplicate, rename, or delete a system type (wall, floor, roof, ceiling). action=duplicate\|rename\|delete. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `get_compound_structure` | lecture | — | 4 | Get wall/floor/roof/ceiling layer structure by type ID or name. | **mineur** — erreur générique sans suggestion |
| `get_material_quantities` | lecture | — | 4 | Calculate material area and volume across elements, optionally filtered by category or restricted to the current selection. | **mineur** — erreur générique sans suggestion |
| `list_phases` | lecture | — | 4 | List all project phases in the active Revit document. | **mineur** — erreur générique sans suggestion |
| `list_shared_parameters` | lecture | — | 4 | List all project parameters with their bindings and categories, optionally filtered by category. | **mineur** — erreur générique sans suggestion |
| `manage_additional_settings` | écriture | — | 4 | Manage Additional Settings (Manage tab): line styles, line weights, line patterns, fill patterns, halftone/underlay. | **mineur** — pas de dryRun |
| `manage_phase_filters` | écriture | — | 4 | List, set, or create Revit Phase Filters. Actions: list \| set \| create. The 'set' action changes one presentation (New \| Demolished \| Existing \| Tempo… | **mineur** — pas de dryRun |
| `manage_project_units` | écriture | — | 4 | Get or set project units (length, area, volume, angle, etc.). Actions: get, set, list_valid_units. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `manage_sheet_sets` | écriture | — | 4 | List, create, or delete named view/sheet sets (ViewSheetSet), so batch_export/printing can reuse a saved list instead of one passed on every call. act… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `manage_worksets` | écriture destructif | — | 4 | Create, rename, delete, or set the active workset (workshared models only). To LIST worksets use list_worksets. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `modify_schedule` | écriture destructif | — | 4 | Modify schedule fields, sorting, filters, or rename the schedule. Supported actions: add_field, remove_field, set_sorting, clear_sorting, set_filter,… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `set_compound_structure` | écriture destructif | oui | 4 | Modify compound structure on a wall/floor/roof/ceiling type. action=replace\|add\|remove\|modify\|set_wrapping. set_wrapping sets openingWrapping (none\|ex… | **mineur** — erreur générique sans suggestion |
| `set_project_info` | écriture | — | 4 | Set editable Project Information fields. Only the fields you pass are changed; others are left untouched. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `detect_clashes` | lecture | — | 4 | Detect clashes between two element categories. Uses true solid-geometry intersection by default (fewer false positives than bounding boxes). | — |
| `list_design_options` | lecture | — | 4 | Lists existing design option sets and their options, and (with elementId) reports which option an element belongs to. Creating a design option set/opt… | — |
| `export_shared_parameter_file` | lecture | — | 3 | Export shared parameter file contents | **mineur** — erreur générique sans suggestion |
| `get_material_properties` | lecture | — | 3 | Get detailed material properties (physical, thermal, appearance) by material ID or name. | **mineur** — erreur générique sans suggestion |
| `list_family_sizes` | lecture | — | 2 | List loaded families with type/instance counts and, when includeSize=true, the family file size in KB measured by exporting each family to a temp file… | **mineur** — erreur générique sans suggestion |

### IFC — 20 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `ifc_export_basic` | écriture | — | 4 | Export the active document to IFC. First-class flags cover the common options; use overrides for any other IFCExportOptions key. | **mineur** — pas de dryRun ; classement déclaré (écriture) différent du préfixe du nom |
| `ifc_link` | écriture | — | 4 | Link an IFC file into the active document (creates a .ifc.RVT sidecar file managed by Revit). | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `ifc_compare_original_vs_rebuilt` | lecture | — | 3 | Compare volume/geometry between the original DirectShape and its native rebuild. | **mineur** — géométrie par boîte englobante |
| `ifc_export_with_configuration` | écriture | — | 3 | Export using a named configuration (built-in or custom) with optional key/value overrides. | **mineur** — pas de dryRun ; classement déclaré (écriture) différent du préfixe du nom |
| `ifc_open_or_import` | écriture destructif | — | 3 | Open or import an IFC file as a native Revit project (actions: open \| import). | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `ifc_rebuild_family_instances` | écriture | oui | 3 | Place family instances (doors, windows, furniture) from IFC DirectShapes. | **mineur** — géométrie par boîte englobante |
| `ifc_rebuild_openings` | écriture | oui | 3 | Cut openings in rebuilt walls/floors based on IFC opening DirectShapes. | **mineur** — géométrie par boîte englobante |
| `ifc_reload_link` | écriture destructif | — | 3 | Reload an existing IFC link, optionally from a new file. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `ifc_set_family_mapping_file` | écriture | — | 3 | Set the family mapping file used by subsequent IFC exports. | **mineur** — pas de dryRun |
| `ifc_tag_unreconstructable_elements` | écriture destructif | — | 3 | Tag IFC DirectShapes that cannot be rebuilt by writing a marker parameter. | **mineur** — pas de dryRun |
| `ifc_analyze_rebuildability` | lecture | — | 3 | Analyze IFC DirectShapes and score feasibility of rebuilding them as native Revit elements. | — |
| `ifc_get_capabilities` | lecture | — | 3 | Detect IFC version support and revit-ifc add-in presence | — |
| `ifc_get_export_configuration` | lecture | — | 3 | Get full details of a specific export configuration by name. | — |
| `ifc_list_export_configurations` | lecture | — | 3 | List available built-in export configurations | — |
| `ifc_list_rebuild_candidates` | lecture | — | 3 | List elements above a rebuild confidence threshold. | — |
| `ifc_rebuild_floors` | écriture | oui | 3 | Rebuild native floors from IFC DirectShapes. dryRun defaults to true. | — |
| `ifc_rebuild_roofs` | écriture | oui | 3 | Rebuild native roofs from IFC DirectShapes. dryRun defaults to true. | — |
| `ifc_rebuild_structural_members` | écriture | oui | 3 | Rebuild columns and beams from IFC DirectShapes. dryRun defaults to true. | — |
| `ifc_rebuild_walls` | écriture | oui | 3 | Rebuild native walls from IFC DirectShapes. dryRun defaults to true. | — |
| `ifc_validate_request` | lecture | — | 3 | Validate IFC file path, extension, and schema version. | — |

### Views — 13 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `apply_view_template` | écriture | — | 5 | List, apply, or remove view templates from views. action=list\|apply\|remove. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_view` | écriture | — | 5 | Create a new view in Revit: floor plan, ceiling plan, section, elevation, drafting, callout, or 3D view. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_view_filter` | écriture | — | 5 | Create, apply, or list parameter-based view filters. action=create\|apply\|list. A filter carries one rule (parameterName/filterRule/filterValue) or sev… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `duplicate_view` | écriture | — | 5 | Duplicate an existing view in Revit. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `manage_view_templates` | écriture destructif | — | 5 | List, duplicate, delete, or rename view templates. action=list\|duplicate\|delete\|rename. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `override_graphics` | écriture | — | 5 | Override element graphics in a view (colors, transparency, halftone, line weight). | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `place_viewport` | écriture | — | 5 | Place a view on a sheet as a viewport. positionX/positionY are the viewport CENTRE in mm in sheet coordinates; omit both to centre it on the sheet. Th… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `batch_modify_view_range` | écriture | — | 4 | Modify view range offsets (top, cut plane, bottom, view depth) for multiple views. Offsets are in mm. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_section_box_from_selection` | écriture | — | 4 | Create a 3D section box from selected elements | **mineur** — pas de dryRun ; géométrie par boîte englobante ; erreur générique sans suggestion |
| `create_views_from_rooms` | écriture | — | 4 | Create callout, section, or elevation views from rooms with a naming pattern. | **mineur** — pas de dryRun ; géométrie par boîte englobante ; erreur générique sans suggestion |
| `manage_scope_boxes` | écriture | — | 4 | Inventory, rename, move, or assign-to-views existing scope boxes (OST_VolumeOfInterest). The Revit API has no method to create one from scratch — draw… | **mineur** — pas de dryRun ; géométrie par boîte englobante ; erreur générique sans suggestion |
| `manage_unplaced_views` | écriture destructif | oui | 4 | List or delete views that are not placed on any sheet | **mineur** — erreur générique sans suggestion |
| `rename_views` | écriture destructif | oui | 3 | Batch rename views using find/replace, prefix, or suffix operations. | **mineur** — erreur générique sans suggestion |

### LinkedFiles — 10 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `add_linked_file` | écriture | — | 5 | Adds a new Revit linked file from a file path and optionally places an instance at the given position. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `get_link_transform` | lecture | — | 4 | Returns the full transform of a linked file instance. | **mineur** — erreur générique sans suggestion |
| `list_linked_file_instances` | lecture | — | 4 | Lists all linked Revit files grouped by type, with transforms and load status. | **mineur** — erreur générique sans suggestion |
| `align_link_to_host` | écriture | — | 2 | Aligns a link instance to the host project's internal origin, shared coordinates, or project base point. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `get_selected_linked_elements` | lecture | — | 2 | Returns info about currently selected link instances. | **mineur** — erreur générique sans suggestion |
| `highlight_linked_element` | écriture | — | 2 | Highlights an element inside a linked model with an optional section box. | **mineur** — pas de dryRun ; géométrie par boîte englobante ; erreur générique sans suggestion |
| `list_coordination_models` | lecture | — | 2 | Read-only listing of Autodesk Revit Coordination Models with type metadata and optional instances. | **mineur** — erreur générique sans suggestion |
| `move_link_instance` | écriture | — | 2 | Moves a linked file instance. mode=delta applies (x,y,z) as an offset; mode=absolute places the origin at (x,y,z). Values are in mm. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `pin_unpin_link_instance` | écriture | — | 2 | Pins or unpins linked file instances. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `show_cross_model_elements` | écriture | — | 2 | Select host elements plus elements in linked Revit models. Two strategies for visibility: (a) default — create red DirectShape markers in the host doc… | **mineur** — pas de dryRun ; erreur générique sans suggestion |

### Annotations — 9 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `tag_rooms` | écriture | — | 5 | Tag rooms in a view. Pass viewId to target a specific view; without it the active view is used. Nothing in this surface can activate a view, so viewId… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : viewId |
| `create_dimensions` | écriture | — | 5 | Create dimension annotations in a view. Pass a JSON array of dimension specs. Element mode: [{viewId, elementIds:[...], linePoint:{x,y,z}, dimensionSt… | **mineur** — pas de dryRun |
| `create_text_note` | écriture | — | 5 | Create text notes in a view. Pass a JSON array: [{text, position:{x,y,z}, viewId?, textNoteTypeId?, width?, horizontalAlignment?, verticalAlignment?,… | **mineur** — pas de dryRun |
| `create_color_legend` | écriture | — | 4 | Color elements by parameter value and optionally create a legend view. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_spot_dimension` | écriture | — | 4 | Create a spot elevation annotation (a level/coordinate callout) at a point on an element's geometry. create_dimensions only builds linear dimensions;… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `delete_empty_tags` | écriture destructif | oui | 4 | Find and remove empty or orphaned tags | **mineur** — erreur générique sans suggestion |
| `import_table` | écriture | — | 4 | Import a CSV/TSV file as a formatted table in a drafting or legend view. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `manage_images` | écriture | — | 4 | Imports a raster/PDF file as an image and places it in a view (survey scan, surveyor underlay). action=list\|place. place needs filePath (bmp/jpg/jpeg/… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `tag_walls` | écriture | — | 4 | Tag walls at their midpoints in the active view. Operates on the active view only. Tags all walls by default, or a subset via wallIds. | **mineur** — pas de dryRun ; erreur générique sans suggestion |

### Parameters — 8 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `batch_modify_parameter_values` | écriture destructif | oui | 5 | Bulk modify parameter values across elements by category. Supports set, find-and-replace, and other operations. | **mineur** — erreur générique sans suggestion |
| `manage_project_parameters` | écriture destructif | oui | 5 | Manage project parameters. Actions: list \| create \| delete \| modify \| set_group \| set_binding_type \| rename. 'delete' now correctly removes non-shared… | **mineur** — erreur générique sans suggestion |
| `batch_rename_affix` | écriture destructif | oui | 4 | Add a prefix and/or suffix to parameter values across the model or a selection. Runs as a dry-run preview by default; set dryRun=false to apply the ch… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds, savedSelectionName, scope, selectionToken |
| `clear_parameter_values` | écriture destructif | oui | 4 | Clear parameter values on elements by category or scope | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : elementIds, savedSelectionName, scope, selectionToken |
| `add_shared_parameter` | écriture | — | 4 | Add a shared parameter to project categories. The data type of a newly created definition is honored (a typed shared parameter, not always Text). | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `manage_global_parameters` | écriture destructif | — | 4 | Manage global parameters (project-level named values). Actions: list \| get \| create \| set \| delete \| rename \| set_formula \| move_up \| move_down \| sort… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `transfer_parameters` | écriture destructif | oui | 4 | Copy parameter values from source element to one or more target elements. | **mineur** — erreur générique sans suggestion |
| `sync_csv_parameters` | écriture destructif | oui | 2 | Synchronize parameter values from CSV data into Revit elements. | **signal** — clé imbriquée annoncée, absente du runtime : paramName1 |

### Documents — 7 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `create_document` | écriture | oui | 5 | Create a NEW EMPTY project from a Revit template (.rte) and save it to targetPath. This is the real 'new project': save_as_document duplicates the ope… | — |
| `open_document` | écriture | oui | 5 | Open a .rvt file and make it the ACTIVE document in Revit. Every later tool call targets that document and all caches are flushed. Save the current do… | — |
| `save_as_document` | écriture | oui | 5 | Save the active Revit project to an absolute .rvt path (parameter name: targetPath). This DUPLICATES the open document - it does not create a blank pr… | — |
| `save_document` | écriture | oui | 5 | Save the active Revit project at its current path. dryRun reports the path, the unsaved-changes state and any predictable blocker without writing. | — |
| `close_document` | écriture destructif | oui | 3 | Closes an open document (project, family, or template). Defaults to the active document; pass filePath to close a different one open in the background… | — |
| `open_family` | écriture | oui | 3 | Opens a .rfa family file and makes it the active document in Revit, for visual editing (type parameters, geometry). The active document CHANGES - ever… | — |
| `open_template` | écriture | oui | 3 | Opens a .rte template file and makes it the active document in Revit, to edit the TEMPLATE itself (levels, types, view templates). To start a new PROJ… | — |

### Sheets — 5 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `batch_create_sheets` | écriture | oui | 5 | Create multiple sheets with title blocks and optional view placement. sheets is a JSON array: [{number, name, titleBlockName?, viewIds?}]. Each sheet'… | **mineur** — erreur générique sans suggestion |
| `align_viewports` | écriture | — | 4 | Align viewports across sheets. 'placement' matches box centers; 'model' matches the box outline min-corner so equal-scale views of the same region lin… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `create_placeholder_sheets` | écriture destructif | — | 4 | Create, list, convert, or delete placeholder sheets. action=create\|list\|convert\|delete. | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `duplicate_sheet_with_content` | écriture | — | 4 | Duplicate a sheet including annotations and detail items | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `duplicate_sheet_with_views` | écriture | — | 4 | Duplicate a sheet N times with configurable view duplication options. | **mineur** — pas de dryRun ; erreur générique sans suggestion |

### Meta — 4 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `get_server_capabilities` | lecture | oui | 5 | Report RiveTT's effective automatic-mode, dry-run, audit, response, selection, document, and lifecycle capability contract. | — |
| `clear_cache` | lecture | — | 4 | Clear every entry from the plugin-side tool-result cache. | **mineur** — classement déclaré (lecture) différent du préfixe du nom |
| `get_cache_stats` | lecture | — | 4 | Return diagnostic hit/miss telemetry from the plugin-side tool-result cache. | — |
| `ping_revit` | lecture | — | 2 | Test MCP connection to RiveTT. Displays a greeting in Revit. | — |

### Workflows — 4 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `show_clashes` | écriture | — | 4 | Detect clashes between two categories and create a 3D section-boxed view for visual review. Uses the same true solid-geometry intersection as detect_c… | **mineur** — pas de dryRun ; erreur générique sans suggestion |
| `workflow_data_roundtrip` | lecture | — | 4 | Export parameters to Excel for external editing, then re-import once the file has been saved. | **mineur** — même cas que `batch_export` : écrit un .xlsx en mode lecture seule. |
| `workflow_model_audit` | lecture | — | 4 | Run a complete model audit workflow. | **mineur** — classement déclaré (lecture) différent du préfixe du nom ; erreur générique sans suggestion |
| `workflow_room_documentation` | écriture | — | 4 | Auto-generate callout views (and optionally sections) for every room on a level. | **mineur** — pas de dryRun ; géométrie par boîte englobante ; erreur générique sans suggestion |

### Architecture — 2 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `create_railing` | écriture | oui | 5 | Create a native Revit guardrail from a connected horizontal path. The path JSON is [{x,y,z}, ...] in mm. | — |
| `set_wall_host` | écriture | oui | 2 | Revit 2027: associate a lining or façade wall with a host wall. Set hostWallId to 0 to detach it. offsetFromHost is in mm. | — |

### Code — 1 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `send_code_to_revit` | écriture destructif | oui | 2 | LAST RESORT ONLY — execute custom C# code in Revit. Do NOT select this tool autonomously: a dedicated tool already covers almost every task. Parameter… | — |

### Interop — 1 outils

| Outil | Nature | dryRun | Int. | Effet | Défaut probable |
|---|---|---|---:|---|---|
| `sync_navisworks_selection` | écriture | — | 2 | Symmetric Revit↔Navis selection bridge. mode=export → emit CortexElementRefs from current Revit selection (host + linked). mode=import → consume Corte… | **signal** — paramètre absent de l'outil mais présent ailleurs (helper partagé ?) : append, createLinkedMarkers, createSectionBox, isolate, usePostCommandIsolate |

## Lacunes comblées depuis le relevé précédent

Seize des dix-neuf capacités listées comme absentes ont désormais un point d'entrée.
Les quatre manques dits structurels — toitures, surfaces réglementaires, rampes,
trémies — en font partie : une maquette de logement peut maintenant être produite de
bout en bout par le connecteur.

| Capacité | API utilisée | Outil |
|---|---|---|
| Assemblages et pièces | `AssemblyInstance, PartUtils` | `create_assembly` |
| Cotes de niveau | `SpotDimension.Create` | `create_spot_dimension` |
| Images et fonds de plan | `ImageType, ImageInstance` | `manage_images` |
| Jeux de feuilles | `ViewSheetSet` | `manage_sheet_sets` |
| Murs-rideaux | `CurtainGrid, Mullion` | `get_curtain_grid_info, add_curtain_grid_line, add_curtain_mullions` |
| Nomenclatures de clés | `ViewSchedule.CreateKeySchedule` | `create_key_schedule` |
| Nuages de révision | `RevisionCloud.Create` | `create_revision (action=create_cloud)` |
| Options de conception | `DesignOption` | `list_design_options (lecture seule, voir API_LIMITS)` |
| Plans de surface | `Area, AreaScheme` | `manage_area_plans (SHAB, SU, SDP)` |
| Rampes | `StairsEditScope sur un type OST_Ramps` | `create_ramp` |
| Synchronisation centrale | `Document.SynchronizeWithCentral` | `synchronize_with_central` |
| Toitures | `FootPrintRoof` | `create_surface_based_element (OST_Roofs)` |
| Toposolides | `Toposolid` | `create_toposolid` |
| Trémies et réservations | `Document.Create.NewOpening` | `create_opening (shaft \| host \| wall)` |
| Vues de détail | `ViewSection.CreateCallout` | `create_view (type=callout)` |
| Zones de délimitation | `OST_VolumeOfInterest` | `manage_scope_boxes` |

## Exposé par l'API Revit, pas encore outillé

Vérifié par recherche de l'API dans `src/RiveTT.Tools` sur les 198 outils : aucune de
ces capacités n'a de point d'entrée. Effort : **S** de l'ordre de la journée, **M** de
la semaine, **L** au-delà.

| Capacité absente | API concernée | Priorité | Ce que ça coûte aujourd'hui | Effort |
|---|---|---|---|---|
| Lignes de raccord | `Matchline, ViewBreak` | basse | Grands linéaires découpés sur plusieurs feuilles. Aucune occurrence de `Matchline`. | S |
| Plateformes de construction | `BuildingPad` | basse | `create_toposolid` couvre le terrain, pas la plateforme décaissée qui s'y inscrit. | S |
| Repères de texte | `KeynoteTag et table de repères` | basse | Annotation normalisée par référence plutôt que texte libre. Aucune occurrence de `Keynote` dans le runtime. | M |

Trois manques de priorité basse. Aucun ne bloque une production courante.

## Ce que l'API Revit ne permet pas

Ni lacune ni dette : une frontière. Ces capacités ont été réinscrites comme des
manques à chaque relecture ; elles sont ici pour qu'on cesse de les chercher.

| Capacité | API | Pourquoi c'est fermé |
|---|---|---|
| Légendes | `ViewType.Legend` | L'API ne crée pas de vue de légende de zéro : seul `View.Duplicate()` sur une légende existante fonctionne. `create_view` le signale explicitement plutôt que d'échouer. |
| Options de conception | `DesignOption, DesignOptionSet` | Ni jeu ni option ne se créent par l'API, et `DesignOptionSet` n'est même pas un type public. `list_design_options` lit ce que la boîte de dialogue Revit a créé. |
| Zones de délimitation | `OST_VolumeOfInterest` | Aucune méthode de création : `manage_scope_boxes` inventorie, renomme, déplace et affecte aux vues des boîtes dessinées dans Revit. |
