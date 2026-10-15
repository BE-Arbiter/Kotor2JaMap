# Assemblage de plusieurs modules en une carte JA : conception

Date : 2026-10-15. Demande de l'utilisateur, après séance de questions.

## 1. Intention

Dans KOTOR, une « zone » du jeu est souvent découpée en plusieurs modules reliés par des portes ou des téléporteurs de transition (ex. `end_m01aa` et `end_m01ab` sur l'Endar Spire). Aujourd'hui un module = une carte JA. L'utilisateur veut pouvoir **sélectionner un ou plusieurs modules (KOTOR ou KOTOR 2) et obtenir une seule carte JA** (un seul .bsp, un seul pk3), dans laquelle on passe de l'un à l'autre à pied, sans chargement.

Pourquoi : le but du projet est la fidélité (« pièces manquantes » = échec principal). Un joueur qui explore dans JA doit pouvoir traverser une zone cohérente, et non quitter la carte à chaque porte de transition.

## 2. Comportement voulu

1. **Sélection.** Dans la liste des modules (arbre « KOTOR » / « KOTOR 2 »), l'utilisateur peut sélectionner plusieurs modules du même jeu (sélection multiple). Un seul module sélectionné = comportement actuel, inchangé.
2. **Liaison automatique.** Si une transition relie deux modules sélectionnés (porte du GIT dont `LinkedToModule` est un autre module de la sélection ; la cible est donnée par `LinkedTo`, le tag d'une porte ou d'un waypoint du module de destination), les deux modules sont placés l'un par rapport à l'autre automatiquement.
3. **Liaison manuelle.** Si deux modules (ou deux groupes déjà liés) n'ont aucune transition entre eux, l'application ouvre un **éditeur visuel** : les deux modules sont affichés en 3D (vue de dessus et vue libre), l'utilisateur déplace et fait tourner l'un par rapport à l'autre avec précision (position X/Y/Z, rotation fine en degrés, saisie numérique et manipulation à la souris), choisit la porte de jonction de chaque côté, et voit en direct les chevauchements.
4. **Jonction (« stitch »).** À chaque liaison, le programme **comble l'écart** entre les deux géométries (un couloir de raccord, sol, murs et plafond, de la taille du cadre de la porte) et **garde une porte** entre les deux : un seul `func_door` à la jonction, ouvrable (la porte de transition n'est plus verrouillée). Les deux portes de transition d'origine sont remplacées par cette porte unique.
5. **Ciel et environnement.** La carte assemblée a un seul ciel. Le ciel, l'éclairage ambiant et le point d'apparition viennent d'un module « de référence » (le premier de la sélection par défaut, modifiable dans l'interface). La boîte de ciel est calculée autour de toute la géométrie assemblée.
6. **Sortie.** Un seul pk3 `kotor_<carte>.pk3` ou `kotor2_<carte>.pk3` (règle d'écriture inchangée). Le nom de carte par défaut est celui du module de référence suivi du nombre de modules, modifiable.

## 3. Objectifs, hors-objectifs, anti-objectifs

**Objectifs**
- Sélection multiple, liaison auto par transition, éditeur de placement manuel, jonction géométrique avec porte conservée, ciel unique, un seul pk3.
- Chaînes de plus de deux modules : chaque module est raccordé à un module déjà placé (arbre de placement, pas forcément une ligne).
- Aperçu de la carte assemblée dans les vues Preview et Import existantes.
- Journal : un code par événement (liaison auto, liaison manuelle, chevauchement, jonction comblée, module sans liaison).

**Hors-objectifs**
- Correction automatique des chevauchements (l'éditeur les signale, l'utilisateur ajuste).
- Mélange de KOTOR et de KOTOR 2 dans une même carte.
- Fusion de deux skyboxes, redécoupage de la visibilité, sauvegarde d'un projet d'assemblage entre deux sessions (le placement est refait à chaque fois, sauf si le placement automatique le donne).
- Transitions d'un module vers un module non sélectionné : la porte reste verrouillée comme aujourd'hui.

**Anti-objectifs (échec même si ça marche techniquement)**
- Pièces ou objets manquants dans le résultat par rapport à l'export de chaque module séparé.
- Écart visible, mur manquant ou trou à la jonction, porte qui s'ouvre sur le vide.
- Carte assemblée qui dépasse les limites du BSP sans avertissement clair (le compilateur réduit déjà la collision en cas de dépassement ; l'assemblage ne doit pas contourner cette protection).

## 4. Contraintes

- Réutiliser le pipeline existant : `ModuleSceneBuilder` produit une `ModuleScene` par module ; `ModuleExporter.BuildPlan` convertit une scène en `CompileInput` ; `PipelineSteps` enchaîne Preview, Import, Export, Create PK3 ; `DoorConverter` convertit les portes ; `Compiler` construit le BSP et la boîte de ciel (`SkyBox`).
- L'approche attendue est d'**assembler des `ModuleScene` en une seule scène** (mailles, walkmeshes, lumières, entités, textures) après application d'une transformation rigide par module (rotation autour de Z + translation), avant `BuildPlan`. Le reste du pipeline reste valable. C'est le choix de l'orchestrateur, que le builder peut contester s'il trouve mieux.
- Unités : les modules sont en mètres KOTOR dans leur propre repère ; l'assemblage se fait en mètres KOTOR, la mise à l'échelle JA reste dans `BuildPlan`.
- Limites JA : `MaxShaders` = 1024, limites BSP (`Limits`), budget de triangles (`TriangleBudget`) : à vérifier sur l'ensemble de la carte, pas par module.
- Textes visibles par l'utilisateur en anglais (règle de la phase 3). Documents et commentaires en français.
- Écriture dans `GameData\base` : seulement `kotor_<carte>.pk3` et `kotor2_<carte>.pk3`.
- Les vues (Preview, Import, Export) et les options de ligne de commande d'automatisation (`--run-to`, `--screenshot`, …) doivent fonctionner pour une carte assemblée ; la CLI gagne un moyen d'exporter plusieurs modules (placement automatique seulement).

## 5. Décisions de conception (à l'initiative du concepteur, à valider à la lecture)

- **Cible d'une transition.** `LinkedTo` désigne un objet du module de destination (porte ou waypoint). Le point d'ancrage est la porte ou le waypoint de ce tag dans le module de destination, et la porte source dans le module source. Si la cible est une porte, les deux cadres de porte sont superposés dos à dos (orientations opposées). Si c'est un waypoint, la porte du module de destination la plus proche de ce waypoint sert de cadre ; à défaut, le waypoint est posé à la jonction et l'avertissement `LINK_NO_TARGET_DOOR` est journalisé.
- **Couloir de raccord.** Si les deux cadres de porte se superposent, l'écart est nul ; sinon (placement manuel, ou transitions dont les cadres sont décalés) on comble un prisme de la largeur et de la hauteur du cadre de porte, avec sol marchable, murs et plafond. Largeur et hauteur du cadre viennent de la boîte englobante de la porte (déjà calculée par `DoorConverter`).
- **Porte retenue.** La porte du module source est conservée à la jonction, orientée vers la jonction ; la porte du module de destination est retirée. La porte retenue n'est jamais verrouillée.
- **Ordre de placement.** Le module de référence est placé à l'identité ; les autres sont placés par parcours en largeur du graphe de transitions. Les modules sans transition vers un module déjà placé passent par l'éditeur manuel.
- **Ciel.** Un seul `SkyShader` (celui de la carte actuelle, `JaDefaults.SkyShader`, ou celui du module de référence si le projet gère un ciel par module à ce moment-là).
- **Point d'apparition.** Celui du module de référence (`SpawnOrigin` existant).

## 6. Laissé au builder

- Architecture des types (par exemple un `ModuleAssembly` dans `Kotor.Core/Scene` ou `Kotor.Pipeline`) et découpage en étapes du plan.
- Détail du calcul de chevauchement (boîtes englobantes d'abord, triangles si nécessaire) et de son affichage.
- Réalisation de l'éditeur visuel dans l'application ImGui/MonoGame existante (nouvel écran, ou panneau dans `MainScreen`), manipulation à la souris, aimantation éventuelle.
- Génération de la géométrie du couloir de raccord (UV, texture, lightmap, shader : choix raisonnable, par exemple réutiliser la texture du mur voisin), collision et walkmesh associés.
- Format des codes de journal et tests (tests unitaires sur la transformation, la liaison et le raccord ; essai visuel en jeu avec `end_m01aa` + `end_m01ab`, comme dans les phases précédentes).

## 7. Précisions issues de la relecture (calls du concepteur, marqués comme tels)

- **Rotation.** Libre autour de Z (degrés flottants), pas de contrainte à 90°. Le placement automatique aligne les cadres de porte : la rotation est déduite des orientations (yaw) des deux portes, la translation de leurs positions. Une porte dont la normale n'est pas horizontale est traitée comme horizontale (projection sur XY). Pas d'inclinaison hors axe Z.
- **Géométrie des portes remplacées.** Le modèle de porte (maillage, collision) du module de destination n'est simplement pas émis ; ses murs et son cadre de décor restent. Le couloir de raccord est ouvert à l'endroit du cadre ; si le mur du module de destination ferme l'ouverture, l'avertissement `LINK_WALL_BLOCKS_DOOR` est journalisé et l'éditeur le signale. Aucune maille n'est supprimée au triangle près dans cette version.
- **Limites JA.** Le contrôle porte sur la carte assemblée entière. Réduction de collision existante conservée ; dépassement irréductible : export bloqué avec le message du compilateur. Les shaders identiques de modules différents sont dédupliqués par nom (déjà le cas via `shaders.TryAdd`). Les lightmaps restent propres à chaque module (noms déjà distincts).
- **Éditeur manuel.** Complet dès la première version (saisie numérique, souris, choix des portes, chevauchements en direct), car c'est la demande. La CLI n'a pas d'éditeur : si une liaison manuelle serait nécessaire, elle échoue avec un message qui nomme les modules non reliés.
- **Entités uniques.** Points d'apparition des modules non-référence : supprimés (un seul `info_player_start`). Ambiante, ciel, brouillard : ceux du module de référence. Lumières, portes, objets de tous les modules : conservés. Les tags en double ne posent pas de problème (JA n'en dépend pas ici).
- **Cycles.** Une arête de transition en surplus (A-B, B-C, C-A) n'est pas une seconde jonction : sa porte reste verrouillée et `LINK_EXTRA_EDGE` est journalisé.
