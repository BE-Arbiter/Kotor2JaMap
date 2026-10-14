# Étape 09 — Validation en jeu (premier passage)

Retour de l'utilisateur (variantes tar_m02aa_s32/s36/s40, SWGL SP, Vulkan) :
- Chargement : OK
- Apparition : OK
- Échelle : 40 retenue
- Collisions : OK
- Textures : NOK, carrés blancs
- Performances : environ 5 FPS

## Diagnostic (orchestrateur)

**Carrés blancs.** `qconsole.log` contient `Can't find textures/kotor/lts_pwall01i__m02aa_03a_lm0.tga`. Le moteur cherche une image du nom du shader : il ne trouve pas le shader et en crée un implicite. Le rendu Vulkan de SWGL (`code/rd-vulkan/tr_shader.cpp:3065`) liste `FS_ListFiles("shaders", ".shader")`, alors que le pk3 range le fichier dans `scripts/kotor_<carte>.shader`. Le dossier attendu est `shaders/`.

**5 FPS.** Le BSP de tar_m02aa_s40 compte 18 635 surfaces pour 123 684 index (environ 2,2 triangles par surface), 25 996 feuilles et un seul cluster. `RenderSurfaces.FromInput` découpe chaque maillage par feuille de l'arbre de collision (centroïde des triangles, `RenderSurfaces.cs:57-80`). Comme l'arbre est très fin (23 681 brushes), on obtient des milliers de petites surfaces, donc autant d'appels de dessin multipasses.

## Décision
- Échelle par défaut : 40.

## Deuxième passage (après la correction 1)
- Textures : OK.
- FPS : toujours environ 5. `com_speeds 1` donne `all` ≈ 195 ms, et presque tout tombe dans `cl` (≈ 195 ms). Les autres compteurs sont presque nuls : `sv` 0, `gm` 0-1, `tr` 0, `rf` 0-2, `bk` 0.
  - Les traces de collision ne sont pas en cause (`tr` = 0).
  - `cl` = temps client hors rendu mesuré (`code/qcommon/common.cpp:1550-1552`). Le rendu Vulkan ne remplit peut-être pas `rf`/`bk`.
- Avec `r_drawworld 0`, `cg_shadows 0` et `cg_thirdPerson 0` tous désactivés : environ 20 FPS, toujours mauvais. Les essais n'ont pas été faits un par un.

## Statut
L'optimisation est mise de côté à la demande de l'utilisateur (2026-10-14). Il veut d'abord l'interface graphique. La cause racine n'est pas trouvée. Pistes restantes : un coût côté client indépendant de la carte (comparer avec une carte SWGL standard), les sons et ambiances, les entités, l'absence de grille de lumière (lightgrid) ou de clusters de visibilité, et un mesure du rendu Vulkan.

## Résolution des FPS (2026-10-15)
L'utilisateur indique que les 5 FPS venaient du pilote NVIDIA, que Windows avait remplacé par un pilote générique. La carte n'était pas en cause : la mesure `cl` ≈ 195 ms reflétait ce pilote. Aucune optimisation n'est nécessaire de ce côté.
