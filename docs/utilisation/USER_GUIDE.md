# Guide RiveTT

RiveTT connecte un client MCP à Autodesk Revit 2027. Il démarre avec Revit
et ne nécessite ni interrupteur de démarrage ni port réseau. Le seul élément
d'interface est le panneau **RiveTT** de l'onglet *Compléments*, qui porte le
verrou d'écriture décrit ci-dessous.

## Installation

Prérequis : Revit 2026.5 ou supérieur, ou Revit 2027, en x64.

Lancez `RiveTT-Setup-<version>.exe`, puis déclarez le serveur dans votre client :

    codex mcp add RiveTT -- "%LOCALAPPDATA%\RiveTT\server\RiveTT.Server.exe"

**Aucun droit administrateur n'est nécessaire** et Windows n'affiche pas d'invite
UAC : tout est installé dans votre profil utilisateur. Rien n'exige non plus
d'installer le runtime .NET — le serveur est autonome.

L'installateur détecte les versions de Revit présentes et n'installe que pour
celles-là. Si seul un Revit 2026.0 à 2026.4 est trouvé, il s'arrête en l'indiquant :
ces versions tournent sur .NET 8 et ne peuvent pas charger le plugin. Appliquez la
mise à jour 2026.5 depuis Autodesk Access.

L'installateur remplace l'installation précédente : **aucune désinstallation
préalable n'est nécessaire**, et il n'est **pas nécessaire de fermer Revit ni le
client MCP**. Windows interdit d'écraser un fichier chargé par un processus mais
autorise à le *renommer* : l'installateur renomme le fichier verrouillé en
`<nom>.old-<horodatage>` — le processus en cours continue de l'utiliser — et
écrit le neuf à sa place. Les copies garées sont supprimées à l'installation
suivante.

Pour désinstaller, passez par *Applications installées* dans les paramètres
Windows. Fermez Revit avant : contrairement à une mise à jour, il n'y a pas de
nouveau fichier pour prendre la place de l'ancien.

En revanche le code déjà chargé reste en mémoire. Pour utiliser la nouvelle
version :

- **redémarrer Revit** si le plugin a été remplacé pendant qu'il tournait ;
- **reconnecter le serveur MCP** dans le client si `RiveTT.Server` tournait.

Puis ouvrir un projet et attendre quelques secondes que sa session soit publiée.
Vérifier la version active avec `get_server_capabilities`
(`execution.serverVersion`).

## Le verrou d'écriture

Le connecteur se charge avec Revit et ne demande aucune autorisation par appel.
Sans interrupteur, la seule limite entre un agent connecté et la maquette serait
son propre jugement. D'où le panneau **Compléments → RiveTT** :

| Bouton | Effet |
|---|---|
| **Lecture seule** (cadenas orange) | Tout outil susceptible de modifier la maquette est refusé avec `PermissionDenied`, **avant exécution** : la maquette n'est pas touchée. Les outils de lecture répondent normalement |
| **Écriture** (cadenas vert ouvert) | Les outils d'écriture redeviennent exécutables. Chaque appel reste transactionnel et journalisé |
| **État** (pastille bleue) | Version, état du canal nommé, mode courant et son origine, document actif, nombre d'outils publiés, accès au journal d'audit |

Trois propriétés à retenir :

1. **Chaque session Revit démarre en lecture seule.** Le mode n'est pas
   persisté d'une session à l'autre : c'est une décision explicite, prise à
   chaque fois.
2. **`dryRun: true` ne contourne pas le verrou.** Une prévisualisation est une
   promesse de l'outil, pas une frontière de permission ; s'y fier rendrait le
   verrou aussi solide que le plus faible des outils.
3. **Aucun outil MCP ne peut lever le verrou** — il n'existe pas d'outil pour
   ça, et un test de contrat vérifie qu'aucun fichier d'outil n'appelle la
   politique d'écriture. Seul le bouton du ruban le fait.

Le verrou survit à l'ouverture, la fermeture et l'enregistrement sous d'un
document : il décrit la session Revit, pas le fichier ouvert.

Côté agent, l'état est lisible partout : `execution.writesAllowed` sur chaque
réponse, et le bloc `readOnlyMode` de `get_server_capabilities`. Sur un refus,
la réponse indique où se trouve le bouton — il n'y a rien à réessayer.

Contrepartie assumée : le classement est **par outil**, pas par action. Un outil
qui peut écrire est refusé même quand on l'appelle pour lire, par exemple
`manage_model_groups` en `action=inventory`. Une permission qui dépendrait des
arguments dépendrait de 250 implémentations ; celle-ci ne dépend que du
classement `toolReadOnly` déjà publié dans chaque réponse.

## Utilisation sûre

- Faire les requêtes de découverte avant de choisir des IDs de niveaux, types
  ou familles.
- Pour une écriture qui accepte `dryRun`, commencer par la prévisualisation.
- Valider les éléments créés ou modifiés avec un outil de lecture.
- Ne recourir à `send_code_to_revit` que lorsqu'aucun outil dédié ne couvre
  l'opération.

Seuls certains outils exposent `dryRun`. Les mutateurs acier
`set_steel_connection_default_order`, `set_steel_solid_cut_face_splitting` et
`set_steel_fabrication_unique_id` n'ont pas de prévisualisation ; limiter leur
portée et vérifier leur résultat immédiatement.

## Outils spécifiques au fork

- `get_server_capabilities` : contrat effectif du serveur, mode automatique,
  audit, réponses, sélection et limitations de cycle de vie.
- `create_wall` : type et niveau de base explicites, niveau supérieur et
  offsets optionnels. Les coordonnées de `locationLine` sont des coordonnées
  projet absolues en millimètres ; `baseOffset` et `topOffset` sont relatifs à
  leurs niveaux. L'outil prévisualise par défaut et retourne la base, le sommet
  et les décalages réellement appliqués après exécution.
- `create_door` / `create_window` : type, hôte et niveau explicites.
- `create_railing` : garde-corps natif depuis un chemin horizontal.
- `set_wall_host` : API de mur hôte de Revit 2027.
- `capture_selection` : capture des IDs explicites ou de la sélection Revit
  dans un jeton temporaire réutilisable. Les outils bulk acceptent aussi
  `savedSelectionName`, `last_filter` et `elementIds`.
- `duplicate_storey` : analyse puis duplication transactionnelle d'un étage,
  avec catégories, groupes, niveau haut des murs et déplacement optionnel des
  niveaux supérieurs.
- `detach_wall_constraint` : retrait d'une contrainte haute de niveau ou d'une
  attache haute/basse en conservant la hauteur non contrainte.
- `manage_model_groups` : inventaire, duplication de type et dissociation
  contrôlée des groupes de modèle.
- `save_document` / `save_as_document` : sauvegarde du document actif, avec
  `dryRun` (chemins, existence de la cible, écrasement, accessibilité du
  dossier, verrou, modifications non enregistrées). `save_as_document`
  **duplique le document ouvert** : ce n'est pas un nouveau projet vierge.
- `list_system_types` : types système (murs, sols, plafonds, toits,
  garde-corps, escaliers, cartouches…), inaccessibles autrement. Sans
  catégorie, retourne l'inventaire par catégorie avec les codes `OST_*`.
- `create_detail_line` / `create_model_line` /
  `create_room_separation_line` : lignes 2D, 3D et séparations de pièces.
  Couper une pièce sans mur physique se fait avec la troisième.
- `place_title_block` : pose un cartouche sur une feuille existante (réparation
  d'une feuille sans cadre).
- `create_document` : **nouveau projet vierge** depuis un gabarit `.rte`,
  enregistré au chemin demandé. C'est le vrai « nouveau projet » :
  `save_as_document` duplique le modèle ouvert avec tout son historique.
  `activate: true` l'ouvre ensuite dans Revit.
- `open_document` : ouvre un `.rvt` et en fait le document actif. Tous les
  appels suivants le ciblent et les caches sont vidés. Enregistrer le document
  courant avant : le changement ne le sauvegarde pas.
- `create_stair` : escalier par composant entre deux niveaux, volées droites
  (`runs`) et paliers automatiques. La réponse compare `actualRiserCount` à
  `desiredRiserCount` et donne `reachesTopLevel` : une volée trop courte produit
  un escalier qui n'atteint pas l'étage.
- `edit_group_members` : ajout/retrait de membres d'un groupe. L'API Revit ne
  sait pas modifier un groupe en place : l'outil dégroupe, modifie, regroupe, et
  **crée donc un nouveau type de groupe**. Refuse un type à plusieurs occurrences
  sauf `allowMultiInstance: true`, car les autres occurrences gardent l'ancienne
  définition.

### Groupes, exclusions et occurrences divergentes

Deux occurrences d'un **même type** de groupe peuvent légitimement différer — ce
n'est pas une anomalie du modèle :

- **exclusion de membre** : un élément retiré d'une occurrence seulement. Revit
  suffixe alors le nom de cette occurrence par « (membre exclu) ». Le type, sa
  définition et les autres occurrences ne bougent pas ;
- **contraintes propres** : un mur groupé peut être plus haut dans une occurrence
  parce que ses contraintes de niveau y sont différentes.

Ce que cela implique pour le pilotage :

| Opération | Ce que fait le connecteur |
|---|---|
| Retirer un membre | `delete_element` sur ce membre, ou `edit_group_members` avec `removeElementIds` seuls : c'est une **exclusion**. Le type garde son id et ses autres occurrences leurs éléments. La réponse liste `groupExclusionIds` |
| Ajouter un membre | Seule voie possible : dégrouper / modifier / regrouper, donc **création d'un nouveau type** ; les autres occurrences gardent l'ancienne définition. Refusé sur un type à plusieurs occurrences sauf `allowMultiInstance: true` |
| Rétablir un membre exclu | **Impossible par l'API.** Sélectionner l'occurrence dans Revit puis « Rétablir les éléments exclus » du ruban |
| Dissoudre | `manage_model_groups action=ungroup` sur les occurrences visées |
| Détecter les exclusions | `manage_model_groups` retourne, par occurrence, `memberCount`, `excludedCount` et `hasExcludedMembers`, et lit la définition complète depuis l'occurrence la plus fournie |

Chaque occurrence possède **ses propres copies** des membres, avec ses propres
identifiants : un id relevé sur une occurrence n'a aucun sens dans une autre.

### Changer la hauteur d'un niveau sans casser les groupes

Modifier l'altimétrie d'un niveau sous des groupes contraints les déforme ou les
éclate en types divergents. Procédure recommandée, qui préserve les symétries et
l'identité du type :

1. créer deux niveaux temporaires au-dessus du bâtiment, à l'écart
   (`create_level`), en conservant **le même écart** que les niveaux d'origine ;
2. copier les groupes vers ces niveaux (`copy_elements` avec `offsetZ`, qui
   réassocie les contraintes haute et basse aux niveaux réels) ;
3. faire les manipulations d'altimétrie sur les niveaux d'origine
   (`create_level action=set`) ;
4. recopier les groupes depuis les niveaux temporaires vers les niveaux
   redéfinis, puis supprimer les niveaux temporaires.

Les groupes reviennent avec leurs miroirs et leur type d'origine, ce qu'aucun
dégroupage/regroupage ne sait faire.

### Conventions à connaître

- **Unités.** Entrées en millimètres. En sortie, toute valeur numérique de
  paramètre porte `value` (unités du projet), `unit`, `displayValue` et
  `internalValue` (unités internes Revit : pieds, pieds², pieds³).
- **Altimétrie.** `create_wall` ignore le `z` de `locationLine` : `baseLevelId`
  et `baseOffset` font foi. `create_door`/`create_window` attendent un `z`
  **absolu projet**, sauf avec `zMode: "relativeToLevel"` où `z` s'ajoute à
  l'altitude du niveau. Un point hors de la plage verticale de l'hôte est
  refusé avec les bornes en mm.
- **Noms de paramètres.** Ils se résolvent en anglais comme dans la langue du
  document (`Mark`/`Repère`, `Level`/`Niveau`, `Width`/`Largeur`). Un nom non
  résolu est signalé dans `unresolvedParameterNames` avec des suggestions,
  jamais rendu par une colonne vide.
- **Catégories.** Les libellés sont localisés et parfois ambigus (Revit FR
  nomme la catégorie des vues portées « Fenêtres », comme les fenêtres) :
  préférer le code `OST_*` retourné dans `categoryBic`.

### Réponses et pagination

Chaque succès contient `execution.connector`, `serverVersion`, `revitVersion`,
`mode`, `toolReadOnly`, `toolDestructive`, `writesAllowed` et `cached`. Un
aperçu d'écriture contient toujours `dryRun:true` et `mutated:false`.

`toolReadOnly` classe **l'outil qui répond**, ce n'est pas un état de session :
RiveTT n'a pas de mode lecture seule, `writesAllowed` vaut toujours `true`.
`cached: true` signale une réponse servie par le cache. Tout cache est vidé
après `save_document`/`save_as_document`.

`filter_elements` utilise `responseMode: summary | idsOnly | details` et
retourne `totalCount`, `returnedCount`, `appliedLimit` et `nextCursor`. Un
curseur devient invalide dès que le document Revit change, afin d'éviter de
mélanger deux états du modèle.

Les erreurs de transaction fournissent `warnings`, `errors`, `rolledBack`,
`failedElementIds` et `repairHints`. Les nouveaux workflows complexes acceptent
`warningPolicy: suppress_all | allow_list` ; avec `allow_list`, tout
avertissement non autorisé provoque un rollback silencieux.

### Limites de cycle de vie

L'interdiction d'activer un document vise les **gestionnaires d'événements API**
(`Idling`, `DocumentChanged`), pas un `ExternalEvent` — le contexte dans lequel
tourne chaque outil de ce connecteur. `open_document`, `create_document`,
`open_family` et `open_template` sont donc disponibles, conformément à la
recommandation Autodesk (External Event = « supported and safe » pour
ouvrir/activer).

`open_family` (.rfa) et `open_template` (.rte) activent le fichier dans
l'interface Revit — le document actif change, donc tout appel suivant cible ce
fichier jusqu'au retour sur le projet. `close_document` referme un document
ouvert (projet, famille ou gabarit) ; fermer le document **actif** exige qu'un
autre document soit ouvert pour y basculer d'abord — `Document.Close(false)`
refuse le document actif (mesuré le 27/08/2026,
`docs/developpement/CHANGELOG_0.3.0.md` P1.4), c'est une contrainte réelle de
l'API, pas un défaut à contourner par un thread d'arrière-plan.

`edit_family` modifie les valeurs de paramètres de type d'une famille **en
arrière-plan** (aucune fenêtre ne s'ouvre) : `Document.EditFamily` → modifier →
`LoadFamily` dans le projet → fermeture, sur le patron déjà en production dans
`export_families` et `list_family_sizes`. Limité aux types existants et à
leurs valeurs de paramètre (cotes, matériaux, oui/non, texte) — ni nouveaux
types, ni géométrie. Pour la géométrie ou de nouveaux types : `open_family`
puis édition visuelle dans Revit.

Restent indisponibles, et `get_server_capabilities` le déclare :

- **escaliers esquissés**, volées hélicoïdales et balancements :
  `create_stair` couvre l'escalier par composant (volées droites + paliers) ;
- **édition de groupe en place** : l'API ne le permet pas,
  `edit_group_members` dégroupe/regroupe et ne propage pas aux autres
  occurrences du type ;
- **propagation d'armatures** : absente de l'API Revit sur toutes les versions
  supportées.

Les autres outils sont exposés par les wrappers C# de
`src/RiveTT.Server/Tools`.

## Diagnostic

- « No RiveTT Revit 2027 session » : démarrer Revit 2027 et ouvrir un
  projet.
- Plugin absent : vérifier
  `%APPDATA%\Autodesk\Revit\Addins\2027\RiveTT.addin`.
- Journal : `%LOCALAPPDATA%\RiveTT\audit.jsonl`.
- Réinstaller après avoir fermé Revit si une DLL est verrouillée.
