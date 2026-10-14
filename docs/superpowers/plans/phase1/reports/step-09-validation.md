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
