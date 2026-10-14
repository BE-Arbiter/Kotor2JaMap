# Phase 2 : application graphique — Conception

Date : 2026-10-14
Statut : rédigé d'après la description de l'utilisateur. Il a demandé que ce travail soit fait pendant son absence, **avant l'optimisation des FPS**. À relire à son retour.
Référence : `docs/superpowers/specs/2026-10-14-kotor-vers-jedi-academy-design.md` (ci-après « la spec de base »).

## 1. Intention

Remplacer la ligne de commande par une application de bureau. On choisit un module KOTOR et on voit le résultat à chaque étape de la conversion, jusqu'au pk3. Le but est de repérer ce qui manque **avant** de lancer le jeu (spec de base §4 : « des morceaux qui manquent »).

## 2. Écran (décidé par l'utilisateur)

- **Colonne de gauche**, de haut en bas :
  1. une arborescence avec un nœud **« KOTOR »**, sous lequel figure la liste des modules (`tar_m02aa — Taris - appartements sud`) ;
  2. sous la liste, un bouton **« Prévisualiser »** ;
  3. sous le bouton, le **journal**.
- **Fenêtre principale** : la vue 3D de l'étape en cours. **En bas à droite**, un **bouton unique dont le libellé change** selon l'étape.

## 3. Déroulé (décidé par l'utilisateur)

| Étape | Déclencheur | Ce que montre la vue |
|---|---|---|
| Prévisualiser | bouton de gauche | la carte KOTOR telle qu'elle est (module assemblé, textures) |
| Importer | bouton variable | la carte **au format map** (géométrie convertie pour JA) **et les entités présentes** : lumières, objets, portes, etc. |
| Exporter | bouton variable | la carte **au format BSP** (le `.bsp` compilé, relu) |
| Créer PK3 | bouton variable | aucun changement |

Règles (choix de Claude, dans l'esprit du projet TFU) :
- Le bouton variable affiche « Importer » après Prévisualiser, puis « Exporter », puis « Créer PK3 ».
- Il est désactivé pendant le travail et tant que l'étape précédente n'a pas réussi.
- Changer de module dans la liste revient à l'état initial : il faut de nouveau cliquer sur Prévisualiser.
- Une étape en échec laisse le bouton sur cette même étape, et l'erreur s'affiche dans le journal.
- Les étapes tournent en arrière-plan : l'interface ne gèle pas, et le journal se remplit au fur et à mesure.

## 4. Ce que fait chaque étape (choix de Claude)

- **Prévisualiser** : `ModuleSceneBuilder.Build`. La vue affiche les maillages en mètres KOTOR, convertis dans le repère de la vue. Elle utilise la texture diffuse multipliée par la lightmap quand il y en a une (`DualTextureEffect` de MonoGame), sinon la diffuse seule.
- **Importer** : ce qui partira dans la carte, mais en mémoire, à l'échelle JA. Cela couvre :
  - la géométrie après le choix des shaders (`ShaderPlanner`) ;
  - les collisions (case à cocher, en fil de fer) ;
  - le point d'apparition ;
  - les **entités du module**, sous forme de repères, chacun avec son type et son nom : objets placés (GIT `Placeable List`), portes (`Door List`), points de passage (`WaypointList`), déclencheurs (`TriggerList`), créatures (`Creature List`), sons (`SoundList`), et lumières (nœuds lumière des modèles de pièce).

  Les entités ne sont **pas encore converties** en entités JA (phase 3 de la spec de base). La vue les montre pour qu'on voie ce qui reste à convertir. Une case à cocher par type d'entité permet de les afficher ou de les masquer.
- **Exporter** : compile le `.bsp` dans le dossier de travail (`<WorkDir>\<carte>\<carte>.bsp`), le relit et l'affiche : surfaces de rendu avec leur shader et leurs textures, plus les brushes de collision en option.
- **Créer PK3** : construit le pk3 et l'installe dans `GameData\base\kotor_<carte>.pk3`, comme la CLI (spec de base §7).
- La CLI `export-ja` enchaîne les mêmes appels (Importer, Exporter, Créer PK3). L'application et la CLI produisent donc des fichiers identiques.

## 5. Contraintes

- C#, .NET 10, même solution `Kotor2Ja.sln`. Interface en français.
- MonoGame avec le backend Vulkan, et ImGui.NET. On copie depuis le projet TFU `Tfu2Ja.App` (`AppGame`, `ImGuiRenderer`, la caméra), `Tfu.Pipeline/Workflow/WorkflowController` et `Preview/*`, puis on les adapte. Repli sur DesktopGL si Vulkan pose problème.
- Mêmes réglages que la CLI (`kotor2ja.config.json`).
- KOTOR est lu en lecture seule. Dans `GameData`, on n'écrit que le pk3.
- Fidélité : la vue montre exactement ce qui part dans la carte (anti-objectif TFU §7). Rien n'est simplifié pour l'affichage.

## 6. Non-objectifs

- Pas d'édition (spec de base §3).
- Pas de conversion des entités (phase suivante), ni d'optimisation des FPS en jeu (mise de côté par l'utilisateur, voir `docs/superpowers/plans/phase1/reports/step-09-validation.md`).
- Pas de lancement du jeu depuis l'application.
- Pas de fenêtre de réglages : les chemins se règlent dans `kotor2ja.config.json`.

## 7. Choix de Claude (à valider)

- **Caméra** : clic droit maintenu pour regarder, ZQSD (et WASD) pour se déplacer, Maj pour aller plus vite, molette pour la vitesse. C'est la caméra du projet TFU.
- **Repère de la vue** : Z vers le haut, comme JA et KOTOR. À l'étape Prévisualiser, la vue est en mètres ; elle est multipliée par l'échelle pour que la caméra garde la même vitesse d'une étape à l'autre.
- **Liste des modules** : tous les modules sous un seul nœud « KOTOR », sans sous-groupes par planète.
- **Repères d'entités** : un petit cube de couleur par type, et le nom au survol ou à côté.
- **Relecture du BSP** : un lecteur RBSP dans `Kotor.Compiler` (`BspReader`) lit les shaders, les sommets, les index, les surfaces et le lump d'entités.
- **Projet** : `src/Kotor.Pipeline` (étapes, automate, modèles de vue sans GPU, testés) et `src/Kotor2Ja.App` (MonoGame et ImGui, vérifié à la main).

## 8. Critère de réussite

1. L'application démarre et liste les modules sous « KOTOR ».
2. Avec `tar_m02aa`, les quatre étapes s'enchaînent sans erreur bloquante, et la vue montre ce que décrit le §3 à chaque étape.
3. Le pk3 produit est identique (même liste de fichiers, BSP identique octet pour octet) à celui de `kotor2ja export-ja tar_m02aa`.
4. L'interface ne gèle pas pendant une étape.
