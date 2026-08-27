# Changelog — RiveTT 0.3.0

Ce document remplace `AUDIT_OUTILS.md`, `CONSOLIDATION_SURFACE.md`, `CONVENTION_NOMMAGE.md`,
`PLAN_CORRECTION.md` et `PROTOCOLE_TEST.md` : les cinq couvraient un même chantier (audit du
24/08, campagne de test du 26-27/08, renommage et consolidation de la surface du 27/08) et sont
maintenant exécutés. Ce qui suit condense les décisions et l'état livré ; le détail de mesure
(appels exacts, réponses JSON, tableaux ligne à ligne) reste dans l'historique git de ces
fichiers si besoin de le retrouver. `docs/INVENTAIRE_OUTILS.md`, généré par
`tools/audit-tool-surface.py`, reste la source de vérité vivante sur la surface actuelle.

---

## 1. Ferraillage et charpente métallique — retirés

Les modules Rebar et StructuralSteel (112 outils, 38 % de la surface à l'origine) ont été
retirés entièrement du dépôt, pas seulement filtrés de l'inventaire.

## 2. Défauts corrigés

Chaque entrée garde son identifiant `P#.#` : c'est celui que les commentaires du code citent
encore (`// see P1.1 in PLAN_CORRECTION.md`, etc.) — ce fichier ayant remplacé
`PLAN_CORRECTION.md`, l'identifiant est la seule chose qui relie encore ces commentaires à
leur contexte. Ne pas renuméroter.

### Critiques et majeurs (audit du 24/08)

- **`workflow_sheet_set` ignorait `viewIds`** — les feuilles sortaient vides, sans
  signalement. Fusionné depuis dans `batch_create_sheets` (§4).
- **`batch_create_sheets` plaçait les fenêtres à un point codé en dur** (0,5 ft ; 0,5 ft),
  hors du cadre réel sur le cartouche A1 français dont l'origine est à 650 mm à l'intérieur
  du cadre. Le cadre est maintenant mesuré sur l'instance de cartouche (`SheetFrame`, partagé
  avec `place_viewport`) ; plusieurs vues sur une feuille sont pavées, pas empilées.
- **`workflow_clash_review` détectait par boîtes englobantes** quand `clash_detection`
  utilisait l'intersection solide — l'outil composé rendait plus de faux positifs. Les deux
  appellent maintenant la même passe (`ClashFinder`). Fusionné depuis dans `detect_clashes`
  puis re-séparé en `show_clashes` (§4 — raison de sécurité, pas de régression).
- **`send_code_to_revit` sans `dryRun`** — l'outil le plus puissant du connecteur n'avait
  aucun aperçu, et sa description promettait une confirmation Revit qui n'existe pas.
  `dryRun` par défaut ; la sandbox est vérifiée, rien n'est exécuté ni écrit sur disque.
- **`delete_selection`, `delete_material`, `delete_schedule` destructifs sans `dryRun`** —
  les trois s'appuyaient sur `session.RequestConfirmation`, qui ne bloque rien. `dryRun` par
  défaut via `DeletionPreview`, qui sonde la cascade réelle par transaction annulée.
- **`ifc_set_family_mapping_file` classé lecture seule** alors qu'il modifie un réglage
  d'export persistant : reclassé en écriture, il traverse maintenant le verrou du ruban.

### Contrats faux ou trompeurs (campagne du 26/08, plan P0-P2)

- **P0.1 — `create_stair` construisait la volée à z = 0 absolu** au lieu de l'élévation du
  niveau de base : toute création au-dessus du niveau 0 échouait (`locationPath` invalide) ou
  produisait une géométrie fausse. Corrigé — la volée est ancrée sur `baseLevel.Elevation`.
- **P0.2 — le journal d'audit n'enregistrait rien** en usage réel (0 entrée pour ~60 appels
  dont une trentaine d'écritures pendant la campagne). Écriture rétablie depuis le chemin
  d'exécution réel des outils.
- **P1.1 — `create_preset_schedule` : trois presets sur six inexploitables** —
  `sheet_list` et `view_list` étaient routés vers un schedule de catégorie ordinaire au lieu
  de `ViewSchedule.CreateSheetList`/`CreateViewList`, et `material_takeoff` créait une
  nomenclature vide en annonçant un succès. Corrigé : bon routage, et tout preset qui
  produirait zéro champ échoue explicitement plutôt que de rendre un id.
- **P1.3 — `create_sheet` sans `titleBlockId` posait un cartouche** malgré une description
  annonçant une feuille nue. Aligné sur la description.
- **P1.5 — `create_view_filter` résolvait les paramètres sur un seul élément témoin** — un
  paramètre porté par une partie seulement de la catégorie était déclaré introuvable.
  Corrigé : résolution sur l'ensemble des éléments et types de la catégorie.
- **P1.6 — le refus sous verrou nommait le mauvais outil** (le handler interne, pas l'outil
  appelé). Le nom public est maintenant propagé jusqu'à la couche de permission.
- **P1.7 — `load_family` ne pouvait pas mettre à jour une famille existante** — l'overload
  sans `IFamilyLoadOptions` rendait `false` sans jamais écraser. Corrigé avec l'overload
  complet et un paramètre `overwriteExisting`.
- **P2.1 — lots intolérants à un identifiant invalide** — un seul ID invalide dans un lot
  (`manage_view_display`/ex-`operate_element` : hide, select, unhide…) faisait échouer tout
  le lot. Généralisé le contrat déjà présent sur `delete_element` : les IDs invalides sont
  écartés et rapportés (`requestedCount`, `skippedIds`), le reste du lot aboutit.
- **P2.2 — `get_current_view_elements` : pas de curseur, compteurs ambigus** — aligné sur
  le contrat pagination de `filter_elements` (`pageSize`/`nextCursor`), et les deux compteurs
  (vue entière vs résultat filtré) nommés sans ambiguïté.
- **P2.3 — `create_room` créait des pièces non encloisonnées** silencieusement. Refusé par
  défaut, avec `allowUnenclosed` pour les cas légitimes.
- **P2.4 — `color_elements` : libellé de paramètre non résolu** (`"None"` au lieu du vrai
  nom de type). Passe maintenant par le même résolveur localisé que `get_element_parameters`.
- **P2.5 — `create_dimensions` mode éléments : entraxe rendu, nus annoncés** — contrat
  clarifié.
- **Erreurs opaques `get_element_parameters(parameterNames:[...])`** sur un tableau JSON natif
  — 55 paramètres sur 41 outils affectés, corrigés en acceptant l'array natif
  (`JsonArrayParam`), pas seulement une chaîne JSON encodée.
- **Bug array-shaped optional MCP parameters** rejetant un tableau JSON natif côté hôte MCP —
  corrigé séparément (commit `902beee`).

### Hygiène (passe dédiée, faite)

- **P3.1** — suppression des 194 branches mortes `REVIT2024_OR_GREATER` (la macro est
  définie inconditionnellement — aucune n'était un vrai défaut, juste du bruit de lecture).
- **P3.2** — centralisation de la constante de conversion pied↔mm (304,8), auparavant en
  dur dans 68 fichiers.

### Évolution livrée

- **P4.1 — `open_family`, `open_template`, `close_document`, `edit_family`** — l'ouverture
  de gabarits (`.rte`) et de familles (`.rfa`) en édition, avec fermeture propre.
  L'affirmation d'un interblocage sur `Document.EditFamily` (qui bloquait cette évolution)
  s'est révélée fausse à la mesure — quatre segments du chemin d'ouverture exercés sans
  blocage.

### Non corrigé, connu

- **P0.3 — le serveur ne redécouvre pas une session Revit apparue après lui** : mesuré,
  reproductible (démarrer Revit après le serveur), mais cause racine non confirmée. Piste
  ouverte : un appel resté bloqué dans `pipe.ConnectAsync` sans atteindre son `finally`
  ferait attendre indéfiniment `RevitConnectionManager._mutex` sur tout appel suivant,
  remontant comme un timeout MCP générique plutôt qu'un code structuré. À instrumenter en
  session réelle avant tout correctif.
- **P1.2 — `place_viewport` sur feuille sans cadre** : le défaut décrit (cadre `[0,0]×[0,0]`,
  centrage implicite dérivé du propre viewport posé) contredit le code actuel de
  `SheetFrame.cs`/`PlaceViewportTool`, qui semble déjà porter le bon contrat. Écart entre
  build testé et source à revérifier en session Revit avant de rouvrir ou refermer.
- **P1.4 — invalidation du cache aux transitions de document** : rejoué le 27/08 en session
  réelle avec un protocole précis (bascule via un document intermédiaire, imposée par
  `Document.Close(false)` qui refuse le document actif) — **non reproduit**. Le protocole
  exact ayant produit le défaut à l'origine reste inconnu ; premier fait négatif, pas une
  clôture certaine.

---

## 3. Convention de nommage — appliquée

Décision : les noms d'outils restent en anglais (le consommateur du nom est le modèle, pas
l'architecte ; cohérence avec l'API Revit ; précision de sélection sur une grande surface).
Le français vit dans la résolution bilingue des paramètres et catégories, et dans les
libellés localisés des réponses — pas dans le nom de l'outil.

Règles appliquées : `verbe_objet` en tête (R1), `list_` énumère une collection / `get_` cible
un élément désigné (R2), `create_` fabrique / `add_` fait entrer l'existant (R3), `batch_`
seul pour le traitement en lot — `bulk_` proscrit (R4), `delete_` supprime / `clear_` vide
sans supprimer le porteur (R5), `manage_<domaine>` réservé aux outils à répartition par
`action` avec la liste énumérée dans la description (R6), `ifc_`/`workflow_` restent des
préfixes de domaine légitimes (R7), aucun préfixe décoratif (R8).

**Renommages livrés (sans alias — décision assumée : aucun consommateur ne porte de nom
d'outil en dur, sauf des configurations stockées à corriger à la note de version) :**

| Ancien | Nouveau | Raison |
|---|---|---|
| `get_materials`, `get_worksets`, `get_phases`, `get_shared_parameters`, `get_warnings`, `get_coordination_models`, `get_linked_file_instances`, `get_available_family_types` | `list_materials`, `list_worksets`, `list_phases`, `list_shared_parameters`, `list_warnings`, `list_coordination_models`, `list_linked_file_instances`, `list_family_types` | R2 |
| `clash_detection` | `detect_clashes` | R1 |
| `cad_link_cleanup` | `clean_cad_links` | R1 |
| `lines_per_view_count` | `count_lines_per_view` | R1 |
| `section_box_from_selection` | `create_section_box_from_selection` | R1 |
| `bulk_modify_parameter_values` | `batch_modify_parameter_values` | R4 |
| `wipe_empty_tags` | `delete_empty_tags` | R5 |
| `ai_element_filter` | `filter_elements` | R8 (préfixe décoratif) |
| `say_hello` | `ping_revit` | sonde de connexion assumée, pas de valeur métier |
| `cross_app_selection` | `sync_navisworks_selection` | le nom ne disait pas le pont Revit↔Navisworks |
| `add_prefix_suffix` | `batch_rename_affix` | chevauchement partiel avec `batch_rename`, pas une fusion propre |
| `operate_element` | `manage_view_display` | action `delete` retirée (doublon de `delete_element`) ; reste homogène (état d'affichage de vue) |
| `manage_curtain_grid` | scindé en `get_curtain_grid_info`, `add_curtain_grid_line`, `add_curtain_mullions` | R6 ne s'appliquait pas — un read et deux writes géométriques de nature différente, pas du CRUD |
| `workflow_clash_review` | `show_clashes` | aligné sur le préfixe `show_` déjà utilisé par `show_cross_model_elements` |

Restent en `get_` (ciblent une entité désignée, pas une collection) : `get_element_parameters`,
`get_project_info`, `get_current_view_info`, `get_material_properties`, `get_schedule_data`,
`get_compound_structure`, `get_link_transform`.

---

## 4. Consolidation de la surface

### Principe retenu

« Moins d'outils » n'est pas un objectif en soi : fusionner ne supprime pas une décision, il
la déplace (du choix du nom au choix d'une valeur d'`action`), et l'aggrave si les paramètres
deviennent conditionnels sans être énumérés explicitement. Mesuré deux fois sur
`create_preset_schedule` (3 presets sur 6 cassés derrière l'énuméré) et `list_system_types`
(deux pièges de paramètre conditionnel). Le bon critère : le nombre de décisions à réussir
avant qu'un appel aboutisse, pas le nombre d'outils. Trois leviers, dans l'ordre de
rendement : supprimer les vrais doublons ; différer le chargement des schémas côté hôte MCP
(gratuit, à vérifier avant tout refactoring) ; fusionner par `action` en dernier recours, et
seulement pour du CRUD réel sur un même objet.

### Retraits confirmés par mesure

- **`workflow_sheet_set`** — sur-ensemble strict et bogué de `batch_create_sheets` (même
  cadre, même centrage, ajoutait juste deux compteurs). Retiré ; les compteurs
  `requestedViewCount`/`placedViewCount` remontés dans `batch_create_sheets`.
- **`workflow_clash_review`** — mesuré identique à `clash_detection` sur les clashes eux-mêmes
  (mêmes IDs, même ordre), n'ajoutant qu'une vue 3D en boîte de section. **Gardé séparé** de
  `detect_clashes` (et non fusionné comme d'abord envisagé) : il crée une vue — une écriture —
  quand `detect_clashes` reste lecture seule ; les fusionner aurait donné un outil dont le
  comportement sous le verrou d'écriture du ruban dépendrait d'un paramètre, ce que le verrou
  ne sait pas exprimer (il gate par outil, pas par appel). Renommé `show_clashes` (§3).

### Fusions exécutées après lecture du comportement

- **Sélections (4 → 2).** `save_selection`, `load_selection`, `delete_selection`
  manipulaient le même `SelectionFilterElement`, résolu par le même nom — fusionnés en
  `manage_selection(action=save|load|list|delete)`. `capture_selection` reste séparé : jeton
  de session éphémère (TTL 1-120 min), pas un élément persisté.
- **Liens — partiel.** `reload_linked_file_from` absorbé dans `manage_links(action=reload_from)`,
  qui identifiait déjà sa cible par `linkId` (instance) — plus cohérent que le `linkTypeId`
  que demandait l'outil retiré. La validation de chemin (`PathSafety.TryResolveSafe`) que
  l'outil retiré avait a été portée dans `manage_links` pour ne pas perdre ce garde-fou.
  `pin_unpin_link_instance`, `move_link_instance`, `align_link_to_host`, `add_linked_file`
  **restent séparés** : chacun a un jeu de paramètres propre et mutuellement exclusif des
  actions existantes de `manage_links` — les absorber aurait fait passer l'outil de 2
  paramètres conditionnels à 8+.

### Clôturé sans fusion, après lecture du code (la prémisse de fusion ne tenait pas)

- **Matériaux.** `get_material_properties` (1 matériau, lecture, expose des blocs d'assets
  détaillés) et `set_material_properties` (lot `requests[]`, écriture, ne peut assigner que
  l'*id* d'un asset entier) n'ont ni la même cardinalité ni le même schéma de sortie —
  fusionner recréerait le cas get/set qu'on refuse déjà de fusionner ailleurs.
- **Duplicateurs de feuilles.** `duplicate_sheet_with_content` et `duplicate_sheet_with_views`
  ne diffèrent pas d'un booléen : le premier copie annotations libres + révisions, le second
  copie les paramètres de cartouche + diagnostics d'échec de nomenclature + mode de
  duplication de vue. Aucun des deux ne couvre l'autre — six comportements indépendants.
  Fusionner exigerait de ré-implémenter l'union, un chantier de fonctionnalité à ouvrir
  séparément si la duplication de surface gêne plus que le risque de régression ne coûte.
- **Nomenclatures.** `create_schedule`, `create_key_schedule`, `create_preset_schedule`
  restent trois opérations légitimement distinctes (factory générique, constructeur API
  différent, gabarits nommés) — pas de doublon réel, une fois les presets corrigés (§2).
- **Créateurs génériques d'éléments.** `create_point_based_element`, `create_line_based_element`,
  `create_surface_based_element` ne sont pas une strate redondante : ils sont le moteur réel
  derrière `create_door`/`create_window`/`create_wall` (façades pures) et le seul chemin pour
  plafonds, toits, mobilier, poteaux et ossature structurelle.
- **`manage_curtain_grid`** — traité au §3 (scindé, pas fusionné davantage).

### Ce qui n'a jamais été candidat

Les 20 `ifc_` (chaîne technique cohérente, le préfixe fait le regroupement) ; lire et écrire
(`get_element_parameters`/`set_element_parameters` n'ont ni les mêmes paramètres ni la même
classification de sécurité) ; les créateurs d'éléments par catégorie (`create_wall`,
`create_door`, `create_room` — un `create_element(category=...)` serait le contre-exemple du
principe retenu).

---

## 5. Chiffres

| Mesure | 24/08 | 27/08 (fin de chantier) |
|---|---|---|
| Outils publiés | 196 (après retrait Rebar/StructuralSteel, §1) | 198 |
| Renommages | — | 14 |
| Fusions / retraits | — | 4 outils retirés (`workflow_sheet_set`, `save_selection`, `load_selection`, `delete_selection` → `manage_selection`), 1 absorbé (`reload_linked_file_from`), 1 scindé en 3 (`manage_curtain_grid`) |

Le nombre d'outils n'a volontairement pas baissé de façon spectaculaire : le gain visé est la
réduction du nombre de décisions à réussir par appel (§4), pas le nombre de lignes dans la
liste.

---

## 6. Vérifications qui restent à faire en session Revit réelle

Cet environnement de développement n'a pas Revit installé — 13+ tests de la suite ne peuvent
pas s'y exécuter (`RevitAPI.dll` introuvable), et tout ce qui touche géométrie, transactions
et messages d'erreur Revit n'est prouvable que sur maquette. À consigner dans la PR qui closes
ces points :

- **P0.3** — instrumenter `RevitConnectionManager._mutex` (horodatage prise/relâche) pour
  confirmer ou écarter la piste du mutex bloqué.
- **P1.2** — rejouer `place_viewport` sur une feuille sans cartouche pour confirmer que le
  contrat actuel du code (`SheetFrame.cs`) est bien ce qui tourne en production.
- **`manage_links(action=reload_from)`** — confirmer que `RevitLinkType.LoadFrom` ne lève
  pas d'erreur transactionnelle quand appelé dans une `Transaction` (l'outil retiré
  affirmait le contraire en commentaire ; l'implémentation gardée l'enveloppe sans échec
  documenté, mais n'a pas été revérifiée après le retrait de l'alternative).
- **`create_stair`** — recette de non-régression : une volée de 3 840 mm entre niveaux
  distants de 2 720 mm, rejouée pour un niveau de base négatif, nul, et positif ; critère :
  création réussie et `actualRiserCount == desiredRiserCount == 16` dans les trois cas.
- **`manage_curtain_grid`** (désormais 3 outils) sur un `CurtainSystem` multi-faces — seule
  la première face est adressée, documenté comme limite, à confirmer que ça ne plante pas.
- **`list_design_options`** sur un modèle portant de vraies variantes de conception.
- Cadre A1 français et pavage multi-vues (`batch_create_sheets`), parité `detect_clashes`/
  `show_clashes` sur le même modèle, aperçus de suppression (`delete_material`,
  `delete_schedule`, `manage_selection(action=delete)`), verrou d'écriture dans les deux sens
  — tous mesurés conformes le 26/08, à reconfirmer si l'un de ces chemins est retouché.

## 7. Lacunes API connues, pas des dettes

Trois capacités sont des frontières de l'API Revit, pas des manques à outiller : les
**légendes** (l'API ne crée pas de vue de légende de zéro, seul `View.Duplicate()` sur une
légende existante fonctionne), les **options de conception** (ni jeu ni option ne se créent
par l'API, `DesignOptionSet` n'est même pas un type public), les **zones de délimitation**
(aucune méthode de création — `manage_scope_boxes` inventorie ce que Revit a créé). Trois
manques subsistent, priorité basse : repères de texte (Keynote), lignes de raccord
(Matchline), plateformes de construction (BuildingPad).
