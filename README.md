# RiveTT

MCP local pour **Autodesk Revit 2026.5+ et 2027**, basé sur RevitCortex (licence MIT).

## Principes

- deux cibles, un seul code source : Revit 2026.5+ ou 2027 / .NET 10 / x64,
  sélectionnées au build via `-p:RevitVersion=2026|2027` (voir « Compiler et installer » ci-dessous) ;
- transport local par **Windows Named Pipe**, jamais par port TCP ;
- démarrage automatique de l'add-in avec Revit, sans interrupteur de démarrage ;
- verrou d'écriture au ruban (*Compléments → RiveTT*) : chaque session Revit
  démarre en lecture seule, aucun outil ne peut lever le verrou ;
- mode automatique permanent : aucune boîte d'autorisation ou licence ;
- pas de Power BI, télémétrie, mise à jour automatique ni compte commercial ;
- serveur MCP standard sur `stdio`, nommé `RiveTT`.

Les appels transitent uniquement entre le client MCP, le serveur stdio et le
processus Revit de l'utilisateur courant. Les écritures Revit restent dans des
transactions et sont consignées dans `%LOCALAPPDATA%\RiveTT\audit.jsonl`.

## Contrat de réponse

- noms de paramètres résolus en anglais **ou** dans la langue du document
  (`Mark`/`Repère`, `Level`/`Niveau`) ; un nom non résolu est signalé, jamais
  rendu par une valeur vide ;
- valeurs numériques accompagnées de leur unité et de la valeur interne Revit ;
- catégories accompagnées de leur code `OST_*` (`categoryBic`), les libellés
  localisés étant ambigus ;
- `execution.toolReadOnly` classe l'outil, pas la session ;
  `execution.writesAllowed` donne l'état du verrou d'écriture du ruban — faux au
  démarrage de chaque session Revit, et aucun outil ne peut le lever ;
  `execution.cached` signale une réponse issue du cache.

## Documentation

Séparée par usage. Vous n'avez besoin que d'une colonne à la fois.

**Se servir du connecteur**

| Document | Quand |
|---|---|
| [docs/utilisation/USER_GUIDE.md](docs/utilisation/USER_GUIDE.md) | Installation, verrou d'écriture, gestes courants |
| [docs/utilisation/IFC.md](docs/utilisation/IFC.md) | Export, liaison, reconstruction d'IFC en éléments natifs |
| [docs/utilisation/SECURITY.md](docs/utilisation/SECURITY.md) | Ce que le connecteur s'autorise, et ce qui l'en empêche |

**Développer dessus**

| Document | Quand |
|---|---|
| [AGENTS.md](AGENTS.md) | **À lire en premier.** Architecture, contrat à deux faces, verrou d'écriture. Les agents de code le chargent automatiquement comme instructions projet |
| [docs/developpement/CHANGELOG_0.3.0.md](docs/developpement/CHANGELOG_0.3.0.md) | Défauts corrigés, renommage et consolidation de la surface, ce qui reste à vérifier sur maquette |

**Référence commune**

[docs/INVENTAIRE_OUTILS.md](docs/INVENTAIRE_OUTILS.md) — les outils, effet par
effet, avec les défauts connus et les capacités API non outillées. Généré par
`tools/audit-tool-surface.py`, jamais édité à la main.

La liste des outils n'est pas recopiée ici. Elle l'a été, sous forme de section
« Fonctions ajoutées » tenue à la main, et elle a cessé d'être tenue : il y manquait
seize capacités. L'inventaire généré est la seule liste exacte par construction.

## Installer

Téléchargez `RiveTT-Setup-<version>.exe` et lancez-le. **Aucun droit administrateur
n'est demandé** : le manifeste de l'installateur est `asInvoker`, Windows n'affiche
donc jamais d'invite UAC.

Il détecte les Revit présents et n'installe que pour ceux-là :

| Revit | Pris en charge |
|---|---|
| 2026.5 et supérieur | oui |
| 2026.0 à 2026.4 | **non** — tourne sur .NET 8, l'installateur le dit et s'arrête |
| 2027.x | oui |

La détection lit la version de `Revit.exe`, pas le registre : la valeur `Version`
du registre garde celle de l'installation d'origine et affiche encore
`26.0.4.409` sur un poste réellement en 2026.5.

Pour préparer un poste où Revit n'est pas encore installé :
`RiveTT-Setup-<version>.exe /REVIT=2026,2027`.

## Compiler

Prérequis : .NET SDK 10, et [Inno Setup 6](https://jrsoftware.org/isdl.php) pour
produire l'installateur (`winget install JRSoftware.InnoSetup --scope user`).

```powershell
cd RiveTT
.\build.ps1                              # les deux cibles Revit + l'installateur
.\build.ps1 -RevitVersion 2027           # une seule cible
.\build.ps1 -SkipInstaller               # binaires seuls, sans Inno Setup
```

Tout ce qui est généré atterrit dans `dist\` (ignoré par git) :

    dist\2026\plugin\   add-in compilé contre Revit 2026.5
    dist\2027\plugin\   add-in compilé contre Revit 2027
    dist\server\        RiveTT.Server.exe, autonome, partagé par les deux
    dist\RiveTT-Setup-<version>.exe

Le serveur ne référence pas l'API Revit : il est compilé une fois et partagé. Il est
**autonome** (~38 Mo) et n'exige aucun runtime .NET installé — c'était la seule pièce
qui aurait imposé des droits administrateur.

L'installation est par utilisateur dans
`%APPDATA%\Autodesk\Revit\Addins\<2026|2027>\RiveTT`, le serveur dans
`%LOCALAPPDATA%\RiveTT\server`.

Pour enregistrer le serveur dans Codex :

```powershell
codex mcp add RiveTT -- "%LOCALAPPDATA%\RiveTT\server\RiveTT.Server.exe"
```

Revit n'a **pas** besoin d'être fermé pour une mise à jour : l'installateur renomme
les fichiers verrouillés en `.old-<horodatage>` et écrit les neufs à leur place.
Redémarrez Revit ensuite — l'instance en cours garde l'ancien code en mémoire. Il faut
en revanche le fermer pour **désinstaller**.

Ouvrez Revit et un projet : la session est publiée automatiquement et le serveur MCP
la découvre sans configuration de port.

## Vérifier

```powershell
dotnet test .\src\RiveTT.Tests\RiveTT.Tests.csproj -c Release
dotnet build .\RiveTT.sln -c Release
```

13 échecs sont attendus hors poste Revit : ces tests chargent `RevitAPI.dll` à
l'exécution, et le paquet NuGet ne fournit qu'un assembly de référence.

`.uild.ps1` compile les deux cibles, publie le serveur et produit l'installateur
dans `dist\`.

## Contribuer

Règles d'architecture, contrat outils, verrou d'écriture et commandes de
vérification : voir [AGENTS.md](AGENTS.md).

## Licence

Ce dérivé conserve la licence [MIT](LICENSE) du projet source RevitCortex.
