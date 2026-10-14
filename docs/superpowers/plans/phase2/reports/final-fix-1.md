# Correction finale 1 (relecture de la branche phase2-app)

Rien n'est commité. Aucune écriture dans le dossier du jeu (tests dans `%TEMP%`), ni jeu ni interface lancés.

## I1 - limites de feuilles et réduction de la collision
- `Limits` : nouveau champ `MaxLeafSurfaces` (dernier paramètre, défaut `0x20000`). `Limits.Vanilla` = `0x20000`, `Limits.Swgl` = `0x100000` (R_LoadMarksurfaces, rd-vulkan/tr_bsp.cpp, alloue d'après le lump sans contrôle). `LimitCheck.MaxLeafSurfaces` supprimée ; `CheckRenderLimits` reçoit la limite (défaut vanilla) et `BspWriter` passe `limits.MaxLeafSurfaces`.
- `BspLimitException` porte `CollisionRelated`. Vrai pour brushes, plans, nœuds, feuilles (`Limits.Check*`), côtés et leafbrushes (`BspWriter.CheckMax`). Faux pour le reste (shaders, surfaces, sommets, index, leafsurfaces, limites par surface).
- `Compiler.cs` : la boucle de réduction n'intercepte que `bx.CollisionRelated`. Toute autre limite sort au premier tour : `CompileResult` en échec, `BSP_ERROR` au journal, aucun BSP écrit, aucune reconstruction inutile (le mineur « 80 reconstructions » de l'étape 07 disparaît).
- Tests : `CompilerTests.Non_collision_limit_fails_immediately_without_reducing_collision` (profil vanilla avec `MaxLeafSurfaces = 0` : échec, `MAX_MAP_LEAFFACES` dans l'erreur, pas de fichier, `BSP_ERROR`, ni `COLLISION_CAPPED` ni `WALKABLE_CAPPED`) ; `LimitsProfileTests` vérifie les deux valeurs de profil ; les tests existants de réduction (limites de collision) passent toujours.

## I2 - validation du nom de carte
- Nouveau `Kotor.Core/Export/MapNames.cs` : `Normalize` (trim + minuscules) puis `Validate` (`[a-z0-9_]`, 1 à 48 caractères).
- `PipelineSteps.Import` : `PipelineStepException("MAP_NAME_INVALID")`, journalisé. `Export` et `CreatePk3` revalident (défense en profondeur avant `Directory.Delete` et le chemin du pk3).
- `ModuleExporter.Export` (deux surcharges) : échec propre sans écriture, pas même de journal (le nom ne doit pas devenir un chemin).
- CLI `ExportJaCommand.Run` : nom `--name` invalide, code de sortie 1 avec message, avant même le contrôle de l'installation.
- Tests : `FinalReviewFixTests` (`..`, `..\..\x`, `a/b`, espaces, accent, vide, 49 caractères refusés, 48 acceptés, `My_Map_2` normalisé en `my_map_2`, `ModuleExporter` ne crée pas le dossier de travail) et `ExportJaCommandTests.Invalid_map_name_returns_1`.

## M1 - dossier base du jeu
`PipelineSteps.RequireJaBaseDir` : `JA_BASE_MISSING` (journalisé) en début d'`Export` (après le contrôle du nombre de shaders, avant toute écriture) et dans `CreatePk3`. `Directory.CreateDirectory(JaBaseDir)` est supprimé. Test sur chemin temporaire : ni `base` ni `work` créés ; `CreatePk3` échoue aussi si `base` disparaît. Les tests existants créent maintenant `base` dans leur dossier temporaire.

## M2 - TestEnv
`TestEnv.JaBaseDir` supprimé de `tests/Kotor.Core.Tests/TestEnv.cs` (plus aucun usage ; les tests qui s'appuyaient sur la création automatique de `base` la créent eux-mêmes sous `%TEMP%`).

## Vue Exporter
`KotorWorkflowSteps.WarnIfCollisionReduced` (appelée par l'étape Exporter) ajoute au journal `EXPORT_VIEW_COLLISION_STALE` : « Attention : collisions reduites par le compilateur, la vue Exporter montre les collisions d'avant reduction. » quand le journal contient `COLLISION_CAPPED` ou `WALKABLE_CAPPED`. Tests : cas réel (36 000 triangles de collision isolés en profil vanilla, vraie compilation qui produit le plafonnement, puis avertissement présent) et cas sans réduction (aucun avertissement). L'appel dans `Export()` lui-même n'est couvert que par le helper (l'étape complète exige KOTOR).

## Commandes
- `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur.
- `dotnet test Kotor2Ja.sln` : Cli 8, Compiler 69, Core 108, Pipeline 34 réussis ; 0 échec, 0 ignoré (219 en tout, tests KOTOR réels inclus).
