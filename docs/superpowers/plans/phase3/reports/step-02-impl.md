# Étape 02 — KOTOR 2 : rapport d'implémentation

Statut : FAIT (réserves mineures en fin de rapport).

## Archives réelles de KOTOR 2 (dossier `Modules`)
82 modules, uniquement `<m>.rim`, `<m>_s.rim` et `<m>_dlg.erf` (164 `.rim`, 82 `.erf`). Aucun `.mod` installé : le support `.mod` est couvert par un test synthétique. Dossier `override` et `TexturePacks\swpc_tex_tpa.erf` présents.

## MDL de KOTOR 2 : décalages vérifiés
- Pointeurs de fonction de l'en-tête de géométrie, mesurés sur de vrais modèles K2 : **4285200** et **4216320** (K1 PC : 4273776 et 4216096). Concorde avec KotorBlender `types.py` (`MODEL_FN_PTR_1_*`) pour le premier pointeur. Détection : premier pointeur (K1 4273776, K2 4285200), sinon `NotSupportedException`.
- En-tête de maillage K2 : **+8 octets avant le décalage des données MDX et celui du tableau de sommets** (xoreos `src/graphics/aurora/model_kotor.cpp`, `ModelNode_KotOR::readMesh` : « si kotor2, sauter 8 octets » juste avant `offNodeData`). Soit, relatifs au début de l'en-tête de maillage (nœud + 80) : MDX data 324 -> 332, vertex array 328 -> 336, taille d'en-tête 332 -> 340. Tous les champs avant (faces 8, transparence 84, textures 88/120, stride MDX 252, offsets normales/UV 264/272/276, nombre de sommets 304, nombre de textures 306, drapeau lightmap 308, render 313) sont inchangés (vérifié : valeurs plausibles sur K2).
- Mesure indépendante de xoreos (sonde console jetable dans `%TEMP%`, supprimée) : pour chaque maillage, les positions du MDL (tableau de sommets) comparées aux 3 premiers floats du MDX sur les 20 premiers sommets.
  - 001ebo (18 modèles) : décalages +8 : 310 maillages concordent, 0 échec ; décalages K1 : 0 concordance, 310 échecs.
  - 101per (66 modèles) : +8 : 910/910 ; K1 : 0/910.
  - 202tel (13 modèles) : +8 : 390/390 ; K1 : 0/390.
  - Contrôle inverse sur K1 (tar_m02aa) : décalages K1 concordent, +8 échoue partout.
- Nœud lumière K2 : même en-tête de 92 octets que K1 (dump hexadécimal de 130+ lumières K2 : priorité 2 à 5, ambient_only 0/1, dynamic_type 1/2, cohérents avec le layout K1). Aucun changement ; les lumières K2 sont lues (NODE_LIGHT 15/38/7 dans le journal, aucun LIGHT_INVALID).

## Changements
- `KotorGame.cs` (enum + `KotorGames`), `ResourceResolver.ForModule(dir, module, game)` : `.mod` (remplace les `.rim`), `_dlg.erf` pour K2. `ModuleCatalog` liste `.rim` (hors `_s`) et `.mod` (l'ancien test excluant le `.mod` est mis à jour).
- `MdlReader` : détection K1/K2 et décalages. `ShaderNames` (`PrefixFor`, `LightmapPrefixFor`, `StripPrefix`, `Short/FullName` par jeu : 43 caractères utiles pour le matériau en K2), `ShaderPlanner.Plan/Write(…, game)`.
- `ExportOptions` : `Game`, `Kotor2Dir`, `ForGame(game)` ; `ConfigLoader` : clé `kotor2Dir`, variable `KOTOR2_PATH`, défaut Steam ; `kotor2ja.config.json` : `kotor2Dir`.
- `PipelineSteps`/`ModuleExporter` : `<prefix>_<carte>.pk3`, `shaders/<prefix>_<carte>.shader`, `textures/<dossier>/`. `PreviewBuilder` reconnaît les deux dossiers.
- Workflow : `ModuleListItem(Game, Module, AreaName)`, `IWorkflowSteps.Preview(game, module, log)` et `IsInstalled(game)` (méthode d'interface par défaut), `WorkflowController.SelectModule(game, module)` / `SelectedGame` / `IsInstalled`. `KotorWorkflowSteps` liste les deux jeux, le nom de zone K2 vient du `dialog.tlk` de K2 (absent : nom du module) ; l'état n'est modifié qu'après réussite de l'aperçu. Jeu absent : `GAME_NOT_FOUND`.
- Application : deux nœuds « KOTOR » / « KOTOR 2 », grisé et libellé `KOTOR 2 (not found)` si le dossier est absent (aucun plantage). Non lancée, non cliquée (consigne).
- CLI : `--game kotor|kotor2` sur `list-modules`, `export-ja`, `export-obj` ; valeur inconnue : message anglais, code 1. Dossier K2 par `--kotor`, sinon config / `KOTOR2_PATH` / défaut.
- `TestEnv.Kotor2Dir` dans les deux projets de test.

## Tests
- `Kotor2Tests.cs` : `MapPrefix_and_folders`, `Resolver_prefers_mod_over_rim`, `Resolver_reads_dlg_erf_only_for_kotor2`, `ForGame_selects_the_game_folder`, `Config_reads_kotor2Dir`, `Shader_planner_uses_the_game_folder`, `Real_k2_catalog_lists_modules` (82 modules, `001ebo` présent), `Real_k2_room_models_read` (001ebo, 101per, 202tel : >= 3 modèles de pièce chacun, maillages texturés et UV de lightmap), `Real_k2_preview_and_export` (001ebo -> `kotor2_001ebo.pk3` avec `shaders/kotor2_001ebo.shader`, `textures/kotor2/`, aucun `textures/kotor/`).
- `WorkflowControllerTests` : `Select_takes_game`, `Missing_game_folder_is_reported_not_installed`, `Real_modules_list_both_games`.
- `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur. `dotnet test` : tout vert (Core 139, Compiler 76, Pipeline 37, Cli 8 ; aucun ignoré).

## Export d'essai (CLI, dossier temporaire, JA non touché)
- 001ebo : 292 maillages, 79346 triangles, 1 texture manquante. Journal : BSP_WARNING 2, COLLISION_DEGENERATE 1, LIGHTMAP_REPAIRED 1, MDL_NO_UV 2, MESH_NOT_RENDERED 18, MESH_NO_TEXTURE 3, NODE_EMITTER 1, NODE_LIGHT 15, TEXTURE_ANIMATED 3, TEXTURE_ENVMAP 9.
- 101per : 846 maillages, 74174 triangles, 1 manquante. BSP_WARNING 2, LIGHTMAP_REPAIRED 1, MESH_NOT_RENDERED 64, MESH_NO_TEXTURE 1, NODE_EMITTER 2, NODE_LIGHT 38, TEXTURE_ANIMATED 9.
- 202tel : 377 maillages, 55081 triangles, 0 manquante. BSP_WARNING 2, COLLISION_DEGENERATE 1, LIGHTMAP_REPAIRED 1, MESH_NOT_RENDERED 13, NODE_EMITTER 1, NODE_LIGHT 7, TEXTURE_ANIMATED 1, TEXTURE_ENVMAP 24.
- pk3 produits : `kotor2_001ebo.pk3`, `kotor2_101per.pk3`, `kotor2_202tel.pk3`. `list-modules --game kotor2` : 82 lignes, noms de zone via `dialog.tlk` K2 (accents corrects).

## Réserves
- Aucun essai visuel de l'application (interdit) : les deux nœuds et l'état grisé ne sont vérifiés que par compilation et tests du contrôleur.
- Le test de modèles K2 ne couvre que les pièces (maillages MDL/MDX des LYT) ; skin/dangly/saber de K2 ne sont pas sondés (inutilisés par les pièces).
- Le `.mod` K2 n'a pas de fichier réel à tester (aucun installé) : test synthétique seulement.
- Messages existants (`Echelle invalide`, etc.) laissés en français : leur traduction relève de l'étape anglais. Les nouveaux textes (CLI `--game`, `GAME_NOT_FOUND`, `NotSupportedException` du MDL, UI) sont en anglais.
- `kotor2Dir` du `kotor2ja.config.json` livré écrase `KOTOR2_PATH` (contrairement à `kotorDir`, volontairement absent du fichier livré).
- Références : xoreos `https://github.com/xoreos/xoreos/blob/master/src/graphics/aurora/model_kotor.cpp` ; KotorBlender `types.py`.
