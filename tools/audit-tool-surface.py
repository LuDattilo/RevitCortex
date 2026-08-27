# -*- coding: utf-8 -*-
"""Inventorie la surface d'outils du connecteur et regenere docs/INVENTAIRE_OUTILS.md.

    python tools/audit-tool-surface.py

Le document est genere, jamais edite a la main : chaque ligne vient du code. Deux
surfaces sont croisees — les attributs [McpServerTool] du serveur MCP et les
classes ICortexTool du runtime — pour repondre a la seule question qui compte sur
un connecteur a 295 outils : un parametre publie est-il vraiment lu.

Ce que le script sait detecter, et la confiance qu'on peut lui accorder :

  * parametre transmis par le serveur, introuvable dans le runtime. Absent de
    tout le runtime = defaut ; absent du seul outil = signal, car la lecture
    passe peut-etre par un helper partage (ElementScopeResolver,
    TransactionFailureHandling) ;
  * cle imbriquee annoncee dans une description ([{number, name, viewIds}]) et
    introuvable. Ces cles echappent au test de contrat, qui ne voit que les
    parametres de premier niveau ;
  * outil d'ecriture sans dryRun, alors que le contrat annonce dryRunDefault ;
  * [ToolSafety] absent ou en desaccord avec le prefixe du nom. Depuis le verrou
    d'ecriture du ruban, ce classement est une frontiere de permission ;
  * geometrie par boite englobante, position de fenetre codee en dur, erreur
    generique sans suggestion.

Le classement d'interet (1 a 5) est un jugement d'usage pour une agence
d'architecture — logement, equipement, tertiaire, sante — pas une propriete du
code. Il vit dans les listes TIER5/TIER4/TIER2 ci-dessous et se corrige en les
editant.

Le script ecrit une seule sortie, docs/INVENTAIRE_OUTILS.md, versionnee. Une page
HTML autonome a existe en parallele : meme matiere, second format, regeneree et
versionnee en meme temps, pour un diff illisible en revue. Le Markdown est la forme
canonique parce qu'il se relit dans une pull request.
"""
import collections
import datetime
import glob
import io
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SERVER = os.path.join(ROOT, "src", "RiveTT.Server", "Tools")
RUNTIME = os.path.join(ROOT, "src", "RiveTT.Tools")
OUT = os.path.join(ROOT, "docs", "INVENTAIRE_OUTILS.md")

# Prefixes que le routeur considere comme lecture seule quand [ToolSafety] manque.
# Doit rester aligne sur CortexRouter.ReadOnlyPrefixes.
READ_ONLY_PREFIXES = ["get_", "list_", "find_", "analyze_", "check_", "measure_",
                      "audit_", "export_", "ping_revit", "detect_clashes",
                      "count_lines_per_view", "ifc_get_", "ifc_list_", "ifc_export_",
                      "ifc_validate_", "ifc_analyze_", "ifc_compare_"]

# Traites cote serveur (routeur, ToolResponseShaper) : leur absence du runtime est normale.
SERVER_SIDE_KEYS = ("dryRun", "responseMode", "compact", "summaryOnly")

# create_door / create_window sont des facades a corps d'expression qui passent
# par un helper prive : aucun ExecuteAsync dans leur bloc, d'ou la table.
FACADE_OVERRIDES = {
    "create_door": "create_point_based_element",
    "create_window": "create_point_based_element",
}

TOOL_RE = re.compile(
    r'\[McpServerTool\(Name = "([a-z_0-9]+)"\)'
    r'(?:\s*,\s*Description\(\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+)\))?\s*\]', re.S)
PARAM_RE = re.compile(
    r'\[Description\("((?:[^"\\]|\\.)*)"\)\]\s*([A-Za-z0-9_<>\[\]\?\.]+)\s+'
    r'([A-Za-z0-9_]+)\s*(=\s*[^,\)]+)?')
# Les classes d'outils peuvent etre indentees : le [ \t]* initial est obligatoire.
CLASS_RE = re.compile(
    r'(?:^|\n)[ \t]*((?:\[[^\n]*\][ \t]*\n[ \t]*)*)public (?:sealed )?class (\w+)'
    r'\s*:[^{\n]*ICortexTool', re.M)


def read(path):
    return io.open(path, encoding="utf-8", errors="replace",
                   newline="").read().replace("\r\n", "\n")


def strip_literal(raw):
    """Concatene un litteral C# multi-lignes en une chaine."""
    text = re.sub(r'"\s*\+\s*"', '', (raw or "").strip())
    if text.startswith('"'):
        text = text[1:]
    if text.endswith('"'):
        text = text[:-1]
    return text.replace('\\"', '"')


def load_server():
    tools = {}
    for path in sorted(glob.glob(os.path.join(SERVER, "*.cs"))):
        text = read(path)
        hits = list(TOOL_RE.finditer(text))
        for i, match in enumerate(hits):
            end = hits[i + 1].start() if i + 1 < len(hits) else len(text)
            block = text[match.end():end]
            # Borne le bloc a la fin de la methode, sinon les cles d'un outil
            # voisin sont attribuees a celui-ci.
            close = block.find("\n    }\n")
            if close > 0:
                block = block[:close]
            sig_end = block.find("CancellationToken")
            sig = block[:sig_end if sig_end > 0 else len(block)]
            forwarded = set(re.findall(r'\["([A-Za-z0-9_]+)"\]\s*=', block))
            forwarded |= set(re.findall(r'p\["([A-Za-z0-9_]+)"\]', block))
            called = re.search(r'ExecuteAsync\(\s*"([a-z_0-9]+)"', block)
            tools[match.group(1)] = {
                "name": match.group(1),
                "description": strip_literal(match.group(2)),
                "params": [{"desc": p.group(1), "name": p.group(3)}
                           for p in PARAM_RE.finditer(sig)],
                "forwarded": sorted(forwarded),
                "runtimeTool": called.group(1) if called else match.group(1),
            }
    return tools


def load_runtime():
    tools = {}
    for path in glob.glob(os.path.join(RUNTIME, "**", "*.cs"), recursive=True):
        if os.sep + "obj" + os.sep in path or os.sep + "bin" + os.sep in path:
            continue
        text = read(path)
        classes = list(CLASS_RE.finditer(text))
        for i, cls in enumerate(classes):
            end = classes[i + 1].start() if i + 1 < len(classes) else len(text)
            block = text[cls.start():end]
            named = re.search(r'public string Name => "([a-z_0-9]+)"', block)
            if not named:
                continue
            attrs = cls.group(1) or ""
            pair = re.search(r'\[ToolSafety\((true|false)\s*,\s*(true|false)\)\]', attrs)
            single = re.search(r'\[ToolSafety\((true|false)\)\]', attrs)
            category = re.search(r'public string Category => "([^"]+)"', block)
            tools[named.group(1)] = {
                "file": os.path.relpath(path, ROOT).replace("\\", "/"),
                "category": category.group(1) if category else "",
                "hasDryRun": "dryRun" in block,
                "safetyDeclared": bool(pair or single),
                "readOnly": (pair.group(1) == "true") if pair else
                            ((single.group(1) == "true") if single else None),
                "destructive": (pair.group(2) == "true") if pair else False,
                "block": block,
            }
    return tools


NOISE = set("""number name value true false null string int double bool json array object
elementId elementIds id ids e g m n p x y z etc mm deg optional default""".split())


def nested_keys(text):
    """Cles annoncees dans une description du type [{number, name, viewIds?}]."""
    keys = set()
    for group in re.findall(r'\{([^{}]{2,200})\}', text or ""):
        for token in re.split(r'[,\|]', group):
            token = token.split(":")[0].strip().strip("?.[]* ")
            if re.match(r'^[a-zA-Z][A-Za-z0-9_]{2,}$', token or "") and token not in NOISE:
                keys.add(token)
    return keys


def action_values(text):
    values = set()
    for group in re.findall(r'action\s*=\s*([a-z_]+(?:\|[a-z_]+)+)', text or ""):
        values |= set(group.split("|"))
    return values


# ── interet pour une agence d'architecture : 5 quotidien, 1 hors perimetre
TIER5 = """create_wall create_door create_window create_floor create_room create_level
create_grid create_view create_sheet place_viewport place_title_block batch_create_sheets
batch_export create_schedule get_schedule_data export_schedule list_system_types
list_family_types get_element_parameters set_element_parameters get_project_info
get_current_view_info get_current_view_elements filter_by_parameter_value filter_elements
modify_element copy_elements delete_element manage_model_groups duplicate_view
duplicate_storey create_room_separation_line tag_rooms create_text_note list_warnings
open_document save_document save_as_document create_document get_server_capabilities
list_schedulable_fields create_stair create_railing apply_view_template
manage_view_templates create_view_filter override_graphics export_elements_data
export_room_data export_to_excel import_from_excel create_dimensions batch_rename
renumber_elements batch_modify_parameter_values manage_project_parameters list_materials
purge_unused check_model_health list_worksets manage_links add_linked_file
get_linked_elements create_revision get_selected_elements edit_group_members
manage_area_plans synchronize_with_central""".split()

TIER4 = """duplicate_system_type duplicate_family_type change_element_type
match_element_properties transfer_parameters batch_rename_affix clear_parameter_values
create_filled_region create_detail_line create_model_line import_table create_color_legend
color_elements create_views_from_rooms create_placeholder_sheets
duplicate_sheet_with_content duplicate_sheet_with_views align_viewports
manage_unplaced_views batch_modify_view_range create_section_box_from_selection
measure_between_elements get_elements_in_spatial_volume get_room_openings
get_material_quantities set_compound_structure get_compound_structure create_material
duplicate_material set_material_properties list_phases set_element_phase
manage_phase_filters manage_worksets set_element_workset manage_project_units
set_project_info list_shared_parameters add_shared_parameter manage_global_parameters
find_untagged_elements find_undimensioned_elements delete_empty_tags tag_walls audit_families
analyze_model_statistics detect_clashes workflow_model_audit
workflow_room_documentation create_preset_schedule modify_schedule duplicate_schedule
delete_schedule export_families load_family create_array manage_view_display capture_selection
manage_selection clean_cad_links ifc_export_basic ifc_link
manage_additional_settings create_surface_based_element create_point_based_element
create_line_based_element create_structural_framing_system
delete_material get_element_solid_geometry count_lines_per_view show_clashes
workflow_data_roundtrip clear_cache get_cache_stats list_linked_file_instances
get_link_transform create_ramp create_opening create_spot_dimension manage_sheet_sets
create_key_schedule get_curtain_grid_info add_curtain_grid_line add_curtain_mullions
create_toposolid manage_images manage_scope_boxes list_design_options""".split()

TIER2 = """ping_revit send_code_to_revit sync_csv_parameters get_elements_by_unique_id
sync_navisworks_selection show_cross_model_elements highlight_linked_element
list_coordination_models get_selected_linked_elements pin_unpin_link_instance
move_link_instance align_link_to_host list_family_sizes
detach_wall_constraint set_wall_host create_assembly""".split()

# The Rebar and StructuralSteel tool folders (112 tools, 38% of the surface at the
# time) were removed from the repository entirely rather than filtered — there is
# no longer a folder name to exclude here.
OUT_OF_SCOPE = ()

# ── defauts confirmes a la lecture du code, qui priment sur les detections
#
# Les huit defauts critiques et majeurs du releve du 2026-08-24 sont corriges ; ils
# sont listes dans FIXED plus bas plutot que supprimes, pour que la regression soit
# visible si l'un d'eux revient. Ne restent ici que les deux arbitrages ouverts.
CONFIRMED = {
    "batch_export": ("mineur",
        "classé lecture seule et écrit sur le disque. Volontaire (le modèle n'est pas "
        "touché) mais à arbitrer : le verrou n'empêche pas cet écrit."),
    "workflow_data_roundtrip": ("mineur",
        "même cas que `batch_export` : écrit un .xlsx en mode lecture seule."),
}

# ── defauts corriges, gardes par src/RiveTT.Tests/Tools/ConfirmedDefectFixSourceTests.cs
# Le tableau du document les rappelle : un inventaire qui oublie ce qui a casse une fois
# laisse le meme defaut revenir sans que personne le reconnaisse.
FIXED = [
    ("workflow_sheet_set", "critique",
     "`viewIds` était publié dans la spec et jamais lu : les feuilles sortaient vides, sans "
     "signalement.",
     "Les `viewIds` sont lus et placés ; la réponse réconcilie `requestedViewCount` et "
     "`placedViewCount`. Outil retiré depuis (chantier de consolidation 27/08) : ce "
     "comportement vit maintenant dans `batch_create_sheets`."),
    ("batch_create_sheets", "critique",
     "fenêtres placées à (0,5 ft ; 0,5 ft) en dur, alors que l'origine de la feuille n'est pas "
     "le coin du cadre : hors cadre sur le cartouche A1 français.",
     "Le cadre est mesuré sur l'instance de cartouche via `SheetFrame`, partagé avec "
     "`place_viewport` ; plusieurs vues sont pavées au lieu d'être empilées."),
    ("workflow_clash_review", "majeur",
     "détection par boîtes englobantes alors que `detect_clashes` utilise l'intersection "
     "solide : l'outil composé rendait plus de faux positifs que le simple.",
     "Les deux outils appellent la même passe `ClashFinder` (pré-filtre par boîtes, puis "
     "`ElementIntersectsElementFilter`). Gardé distinct de `detect_clashes` lors du chantier "
     "de consolidation du 27/08 : il crée une vue (écriture), l'autre reste lecture seule — "
     "les fusionner aurait cassé le verrou d'écriture. Renommé `show_clashes` le 27/08 "
     "(convention `show_`, cohérent avec `show_cross_model_elements`)."),
    ("send_code_to_revit", "majeur",
     "aucun dryRun sur l'outil le plus puissant, et la description annonçait une confirmation "
     "dans Revit qui n'existe pas.",
     "`dryRun` par défaut : la sandbox est vérifiée, rien n'est exécuté ni écrit sur disque. "
     "La description ne promet plus de dialogue."),
    ("delete_selection", "majeur",
     "destructif sans dryRun, alors que `delete_element` en a un par défaut.",
     "`dryRun` par défaut via `DeletionPreview` ; la réponse précise que seule la liste "
     "enregistrée est supprimée, pas les éléments. Fusionné dans `manage_selection` "
     "(action=delete) le 27/08, avec save_selection et load_selection — même comportement."),
    ("delete_material", "majeur", "destructif sans dryRun.",
     "`dryRun` par défaut via `DeletionPreview`, qui sonde la cascade réelle."),
    ("delete_schedule", "majeur", "destructif sans dryRun.",
     "`dryRun` par défaut via `DeletionPreview`, qui sonde la cascade réelle."),
    ("ifc_set_family_mapping_file", "majeur",
     "classé lecture seule alors qu'il modifie un réglage d'export persistant : il traversait "
     "le verrou d'écriture du ruban.",
     "Reclassé `[ToolSafety(false, false)]` : il passe désormais par le verrou."),
]

# Les trois outils de suppression appelaient `session.RequestConfirmation`, une methode de
# compatibilite qui retourne toujours true : la « confirmation » annoncee dans leur
# description n'a jamais rien bloque. C'est le dryRun qui la remplace.

SEV_ORDER = {"critique": 0, "majeur": 1, "signal": 2, "mineur": 3, "": 4}

# ── capacites exposees par l'API Revit et non outillees
#
# Seize des dix-neuf lacunes du releve precedent sont comblees ; elles sont listees dans
# COVERED, avec l'outil qui les couvre. Ne restent ici que celles qui n'ont toujours
# aucun point d'entree — verifiees par recherche de l'API dans src/RiveTT.Tools.
GAPS = [
    ("Repères de texte", "KeynoteTag et table de repères", "basse",
     "Annotation normalisée par référence plutôt que texte libre. Aucune occurrence de "
     "`Keynote` dans le runtime.", "M"),
    ("Lignes de raccord", "Matchline, ViewBreak", "basse",
     "Grands linéaires découpés sur plusieurs feuilles. Aucune occurrence de `Matchline`.", "S"),
    ("Plateformes de construction", "BuildingPad", "basse",
     "`create_toposolid` couvre le terrain, pas la plateforme décaissée qui s'y inscrit.", "S"),
]

# ── lacunes comblees depuis le releve precedent, avec l'outil qui les couvre
COVERED = [
    ("Toitures", "FootPrintRoof", "create_surface_based_element (OST_Roofs)"),
    ("Plans de surface", "Area, AreaScheme", "manage_area_plans (SHAB, SU, SDP)"),
    ("Rampes", "StairsEditScope sur un type OST_Ramps", "create_ramp"),
    ("Trémies et réservations", "Document.Create.NewOpening", "create_opening (shaft | host | wall)"),
    ("Nuages de révision", "RevisionCloud.Create", "create_revision (action=create_cloud)"),
    ("Cotes de niveau", "SpotDimension.Create", "create_spot_dimension"),
    ("Zones de délimitation", "OST_VolumeOfInterest", "manage_scope_boxes"),
    ("Vues de détail", "ViewSection.CreateCallout", "create_view (type=callout)"),
    ("Murs-rideaux", "CurtainGrid, Mullion",
     "get_curtain_grid_info, add_curtain_grid_line, add_curtain_mullions"),
    ("Jeux de feuilles", "ViewSheetSet", "manage_sheet_sets"),
    ("Toposolides", "Toposolid", "create_toposolid"),
    ("Options de conception", "DesignOption", "list_design_options (lecture seule, voir API_LIMITS)"),
    ("Synchronisation centrale", "Document.SynchronizeWithCentral", "synchronize_with_central"),
    ("Nomenclatures de clés", "ViewSchedule.CreateKeySchedule", "create_key_schedule"),
    ("Images et fonds de plan", "ImageType, ImageInstance", "manage_images"),
    ("Assemblages et pièces", "AssemblyInstance, PartUtils", "create_assembly"),
]

# ── ce que l'API Revit ne permet pas : ni lacune ni dette, une frontiere
# A distinguer des lacunes : rien a outiller ici, seulement a dire au bon endroit pour
# que la capacite ne soit pas re-inscrite comme un manque a chaque relecture.
API_LIMITS = [
    ("Légendes", "ViewType.Legend",
     "L'API ne crée pas de vue de légende de zéro : seul `View.Duplicate()` sur une légende "
     "existante fonctionne. `create_view` le signale explicitement plutôt que d'échouer."),
    ("Options de conception", "DesignOption, DesignOptionSet",
     "Ni jeu ni option ne se créent par l'API, et `DesignOptionSet` n'est même pas un type "
     "public. `list_design_options` lit ce que la boîte de dialogue Revit a créé."),
    ("Zones de délimitation", "OST_VolumeOfInterest",
     "Aucune méthode de création : `manage_scope_boxes` inventorie, renomme, déplace et "
     "affecte aux vues des boîtes dessinées dans Revit."),
]


def interest(row):
    if row["category"] in OUT_OF_SCOPE:
        return 1
    if row["name"] in TIER5:
        return 5
    if row["name"] in TIER4:
        return 4
    if row["name"] in TIER2:
        return 2
    return 3


def analyse(server, runtime, corpus):
    called = set(v["runtimeTool"] for v in server.values())
    rows = []
    for name in sorted(set(server) | (set(runtime) - called)):
        srv = server.get(name)
        target = FACADE_OVERRIDES.get(name, srv["runtimeTool"]) if srv else name
        run = runtime.get(target)
        block = run["block"] if run else ""
        flags = []

        if srv and not run:
            flags.append(("critique", "publié par le serveur, aucun outil runtime correspondant"))
        if run and not srv:
            flags.append(("info", "outil runtime non publié sur la surface MCP"))

        if srv and run:
            missing = [k for k in srv["forwarded"]
                       if k not in SERVER_SIDE_KEYS and ('"%s"' % k) not in block]
            nowhere = [k for k in missing if ('"%s"' % k) not in corpus]
            elsewhere = [k for k in missing if k not in nowhere]
            if nowhere:
                flags.append(("critique",
                              "paramètre transmis, lu nulle part : " + ", ".join(nowhere)))
            if elsewhere:
                flags.append(("signal", "paramètre absent de l'outil mais présent ailleurs "
                                        "(helper partagé ?) : " + ", ".join(elsewhere)))

            text = srv["description"] + " " + " ".join(p["desc"] for p in srv["params"])
            keys = [k for k in sorted(nested_keys(text))
                    if ('"%s"' % k) not in block
                    and not re.search(r'\b%s\b' % re.escape(k), block, re.I)
                    and k not in srv["forwarded"]]
            if keys:
                flags.append(("signal", "clé imbriquée annoncée, absente du runtime : "
                                        + ", ".join(keys)))
            actions = [v for v in sorted(action_values(text)) if ('"%s"' % v) not in block]
            if actions:
                flags.append(("signal", "valeur d'action annoncée, absente du runtime : "
                                        + ", ".join(actions)))

        if run:
            prefix_ro = any(name.startswith(p) for p in READ_ONLY_PREFIXES)
            if run["readOnly"] is False and not run["hasDryRun"]:
                flags.append(("mineur", "pas de dryRun"))
            if not run["safetyDeclared"]:
                flags.append(("mineur", "[ToolSafety] absent, classement déduit du préfixe"))
            elif run["readOnly"] is not None and run["readOnly"] != prefix_ro:
                flags.append(("mineur", "classement déclaré (%s) différent du préfixe du nom"
                              % ("lecture" if run["readOnly"] else "écriture")))
            if re.search(r'Viewport\.Create\([^)]*new XYZ\(\s*[0-9]', block, re.S):
                flags.append(("mineur", "position de fenêtre codée en dur"))
            if "get_BoundingBox(" in block and "Solid" not in block and name != "detect_clashes":
                flags.append(("mineur", "géométrie par boîte englobante"))
            if re.search(r'CortexErrorCode\.Unknown,\s*\$"Failed', block):
                flags.append(("mineur", "erreur générique sans suggestion"))

        row = {
            "name": name,
            "runtimeTool": target,
            "facade": bool(srv and target != name),
            "category": (run or {}).get("category", ""),
            "file": (run or {}).get("file", ""),
            "description": (srv or {}).get("description", ""),
            "readOnly": (run or {}).get("readOnly"),
            "destructive": (run or {}).get("destructive", False),
            "hasDryRun": (run or {}).get("hasDryRun", False),
            "flags": flags,
        }
        row["interest"] = interest(row)

        if name in CONFIRMED:
            row["sev"], row["defect"] = CONFIRMED[name]
        else:
            hard = [m for lvl, m in flags if lvl == "critique"]
            soft = [m for lvl, m in flags if lvl == "signal"]
            small = [m for lvl, m in flags if lvl == "mineur"]
            if hard:
                row["sev"], row["defect"] = "majeur", " ; ".join(hard)
            elif soft:
                row["sev"], row["defect"] = "signal", " ; ".join(soft)
            elif small:
                row["sev"], row["defect"] = "mineur", " ; ".join(small)
            else:
                row["sev"], row["defect"] = "", ""
        rows.append(row)
    return rows


def cell(text, limit=None):
    text = re.sub(r'\s+', ' ', text or "").strip()
    if limit and len(text) > limit:
        text = text[:limit].rstrip() + "…"
    return text.replace("|", "\\|")


def emit(rows):
    total = len(rows)
    by_cat = collections.Counter(r["category"] or "(sans)" for r in rows)
    off = sum(1 for r in rows if r["category"] in OUT_OF_SCOPE)
    writes = sum(1 for r in rows if r["readOnly"] is False)
    no_dry = [r for r in rows if r["readOnly"] is False and not r["hasDryRun"]]
    no_dry_archi = [r for r in no_dry if r["category"] not in OUT_OF_SCOPE]
    generic = [r for r in rows if any(m == "erreur générique sans suggestion"
                                      for _, m in r["flags"])]
    bbox = [r for r in rows if any(m == "géométrie par boîte englobante" for _, m in r["flags"])]
    mismatch = [r for r in rows if any(m.startswith("classement déclaré")
                                       for _, m in r["flags"])]
    confirmed = [r for r in rows if r["sev"] in ("critique", "majeur")]
    signals = [r for r in rows if r["sev"] == "signal"]

    version = "inconnue"
    props = os.path.join(ROOT, "Directory.Build.props")
    if os.path.exists(props):
        found = re.search(r'<Version>([^<]+)</Version>', read(props))
        if found:
            version = found.group(1)

    out = []
    add = out.append
    add("# Inventaire des outils RiveTT\n")
    add("> Document **généré** par `tools/audit-tool-surface.py`. Ne pas éditer à la main :\n"
        "> relancer le script après toute modification de la surface d'outils.\n")
    add("Relevé du %s — connecteur %s — **%d outils publiés**, %d classes runtime.\n"
        % (datetime.date.today().isoformat(), version, total,
           len(set(r["runtimeTool"] for r in rows))))

    add("## Comment lire ce document\n")
    add("Deux surfaces sont croisées : les attributs `[McpServerTool]` du serveur MCP et les\n"
        "classes `ICortexTool` du runtime. La question posée à chaque outil est celle qui a\n"
        "coûté le plus cher jusqu'ici : **un paramètre publié est-il vraiment lu**.\n")
    add("| Colonne | Ce qu'elle dit |")
    add("|---|---|")
    add("| Nature | `lecture` ou `écriture` selon `[ToolSafety]`. Depuis le verrou du ruban, "
        "ce classement est une frontière de permission, plus une simple étiquette |")
    add("| dryRun | l'outil accepte une prévisualisation |")
    add("| Int. | intérêt pour une agence d'architecture : **5** geste quotidien, "
        "**4** utile régulier, **3** ponctuel, **2** marginal, **1** hors périmètre. "
        "Jugement d'usage, pas une propriété du code : il vit dans les listes "
        "`TIER5`/`TIER4`/`TIER2` du script et se corrige en les éditant |")
    add("| Défaut probable | **critique** et **majeur** vérifiés dans le code ; **signal** "
        "détecté automatiquement, avec des faux positifs quand la lecture passe par un "
        "helper partagé ou un DTO typé ; **mineur** systémique |")
    add("")
    add("Une flèche `→` signale une **façade** : un nom MCP qui appelle un autre outil "
        "runtime.\n")

    add("## Synthèse\n")
    add("| Mesure | Valeur |")
    add("|---|---|")
    add("| Outils publiés | **%d** |" % total)
    add("| Dont écriture | **%d** (%.0f %%) — c'est la part que le verrou du ruban gouverne |"
        % (writes, writes / total * 100))
    add("| Écritures sans `dryRun` | **%d**, alors que le contrat annonce "
        "`dryRunDefault: true` |" % len(no_dry))
    add("| Défauts critiques et majeurs corrigés | **%d**, gardés par "
        "`ConfirmedDefectFixSourceTests` |" % len(FIXED))
    add("| Lacunes API comblées depuis le relevé précédent | **%d** sur 19 |" % len(COVERED))
    add("| Erreurs génériques `Failed: …` sans suggestion | **%d** |" % len(generic))
    add("| Géométrie par boîte englobante | **%d** |" % len(bbox))
    add("| Classement `[ToolSafety]` en désaccord avec le nom | **%d** |" % len(mismatch))
    add("| Défauts confirmés / signaux à vérifier | **%d** / **%d** |"
        % (len(confirmed), len(signals)))
    add("")

    add("## Répartition par catégorie\n")
    add("| Catégorie | Outils | Part |")
    add("|---|---:|---:|")
    for cat, count in by_cat.most_common():
        add("| %s | %d | %.0f %% |" % (cat, count, count / total * 100))
    add("")
    add("Le ferraillage et la charpente métallique — 112 outils, 38 %% de la surface — ont été\n"
        "retirés du dépôt, pas filtrés. Ce qui reste est le catalogue que l'agent lit à chaque\n"
        "session : %d outils dont %.0f %% d'écriture, tous dans le périmètre logement,\n"
        "équipement, tertiaire et santé.\n" % (total, writes / total * 100))

    add("## Défauts corrigés\n")
    add("Les huit défauts critiques et majeurs du relevé précédent. Ils restent listés :\n"
        "un inventaire qui oublie ce qui a cassé une fois laisse le même défaut revenir sans\n"
        "que personne le reconnaisse. `ConfirmedDefectFixSourceTests` échoue si l'un revient.\n")
    add("| Outil | Gravité | Ce que le code faisait | Ce qu'il fait maintenant |")
    add("|---|---|---|---|")
    for name, sev, was, now in sorted(FIXED, key=lambda f: (SEV_ORDER[f[1]], f[0])):
        add("| `%s` | %s | %s | %s |" % (cell(name), sev, cell(was), cell(now)))
    add("")

    add("## Défauts confirmés\n")
    if confirmed:
        add("Lus dans le code, pas déduits.\n")
        add("| Outil | Gravité | Ce que le code fait |")
        add("|---|---|---|")
        for row in sorted(confirmed, key=lambda r: (SEV_ORDER[r["sev"]], r["name"])):
            add("| `%s` | %s | %s |" % (row["name"], row["sev"], cell(row["defect"])))
    else:
        add("Aucun défaut critique ou majeur ouvert.\n")
    add("")

    add("### Arbitrages ouverts\n")
    add("Deux outils classés lecture seule écrivent sur le disque. Le modèle n'est pas touché,\n"
        "donc le classement se défend — mais le verrou du ruban ne les arrête pas, et c'est une\n"
        "décision à prendre, pas un oubli : `batch_export` et `workflow_data_roundtrip`.\n")

    add("## Signaux à vérifier\n")
    add("Détection automatique. Un signal n'est pas un défaut : la lecture passe peut-être\n"
        "par un helper partagé ou un DTO typé, ou la clé annoncée n'est qu'un exemple de\n"
        "documentation.\n")
    add("| Outil | Signal |")
    add("|---|---|")
    for row in sorted(signals, key=lambda r: r["name"]):
        add("| `%s` | %s |" % (row["name"], cell(row["defect"])))
    add("")

    add("## Inventaire complet\n")
    for cat, count in by_cat.most_common():
        add("### %s — %d outils\n" % (cat, count))
        add("| Outil | Nature | dryRun | Int. | Effet | Défaut probable |")
        add("|---|---|---|---:|---|---|")
        group = [r for r in rows if (r["category"] or "(sans)") == cat]
        for row in sorted(group, key=lambda r: (-r["interest"], SEV_ORDER[r["sev"]], r["name"])):
            kind = "lecture" if row["readOnly"] else (
                "écriture" + (" destructif" if row["destructive"] else "")
                if row["readOnly"] is False else "?")
            name = "`%s`" % row["name"]
            if row["facade"]:
                name += " → `%s`" % row["runtimeTool"]
            add("| %s | %s | %s | %d | %s | %s |" % (
                name, kind, "oui" if row["hasDryRun"] else "—", row["interest"],
                cell(row["description"], 150) or "—",
                ("**%s** — %s" % (row["sev"], cell(row["defect"], 170)))
                if row["sev"] else "—"))
        add("")

    add("## Lacunes comblées depuis le relevé précédent\n")
    add("Seize des dix-neuf capacités listées comme absentes ont désormais un point d'entrée.\n"
        "Les quatre manques dits structurels — toitures, surfaces réglementaires, rampes,\n"
        "trémies — en font partie : une maquette de logement peut maintenant être produite de\n"
        "bout en bout par le connecteur.\n")
    add("| Capacité | API utilisée | Outil |")
    add("|---|---|---|")
    for name, api, tool in sorted(COVERED, key=lambda c: c[0]):
        # cell() escapes the pipes: "create_opening (shaft | host | wall)" would otherwise
        # split into three columns.
        add("| %s | `%s` | `%s` |" % (cell(name), cell(api), cell(tool)))
    add("")

    add("## Exposé par l'API Revit, pas encore outillé\n")
    add("Vérifié par recherche de l'API dans `src/RiveTT.Tools` sur les %d outils : aucune de\n"
        "ces capacités n'a de point d'entrée. Effort : **S** de l'ordre de la journée, **M** de\n"
        "la semaine, **L** au-delà.\n" % total)
    add("| Capacité absente | API concernée | Priorité | Ce que ça coûte aujourd'hui | Effort |")
    add("|---|---|---|---|---|")
    order = {"haute": 0, "moyenne": 1, "à arbitrer": 2, "basse": 3}
    for name, api, prio, why, effort in sorted(GAPS, key=lambda g: (order[g[2]], g[0])):
        add("| %s | `%s` | %s | %s | %s |" % (cell(name), cell(api), prio, cell(why), effort))
    add("")
    add("Trois manques de priorité basse. Aucun ne bloque une production courante.\n")

    add("## Ce que l'API Revit ne permet pas\n")
    add("Ni lacune ni dette : une frontière. Ces capacités ont été réinscrites comme des\n"
        "manques à chaque relecture ; elles sont ici pour qu'on cesse de les chercher.\n")
    add("| Capacité | API | Pourquoi c'est fermé |")
    add("|---|---|---|")
    for name, api, why in sorted(API_LIMITS, key=lambda a: a[0]):
        add("| %s | `%s` | %s |" % (cell(name), cell(api), cell(why)))
    add("")

    io.open(OUT, "w", encoding="utf-8", newline="\n").write("\n".join(out))
    return {"total": total, "writes": writes, "off": off, "noDry": len(no_dry),
            "noDryArchi": len(no_dry_archi), "generic": len(generic),
            "confirmed": len(confirmed), "signals": len(signals)}


def main():
    server = load_server()
    runtime = load_runtime()
    corpus = "\n".join(
        read(f) for f in glob.glob(os.path.join(RUNTIME, "**", "*.cs"), recursive=True)
        if os.sep + "obj" + os.sep not in f and os.sep + "bin" + os.sep not in f)
    rows = analyse(server, runtime, corpus)
    stats = emit(rows)
    print("serveur %d / runtime %d" % (len(server), len(runtime)))
    print(json.dumps(stats, indent=1))
    print("ecrit : %s" % os.path.relpath(OUT, ROOT))


if __name__ == "__main__":
    main()
