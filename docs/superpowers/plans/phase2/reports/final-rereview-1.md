# Relecture de la correction finale 1 (8e87677..316b3a1)

Portée : diff seul (lecture seule, ni git, ni suite de tests, ni dossier du jeu). Build et tests non rejoués : les chiffres du rapport final-fix-1.md (0 avertissement, 219 tests) sont repris tels quels.

## Verdict par constat

1. **I1 (réduction de collision pour toute limite) : ADDRESSED.**
   - `src/Kotor.Compiler/Bsp/Limits.cs` : `BspLimitException.CollisionRelated`. Le champ `MaxLeafSurfaces` est ajouté au profil : Vanilla = 0x20000, Swgl = 0x100000. `Limits.Check*` (brushes, plans, nœuds, feuilles) lève avec `collisionRelated: true`.
   - `src/Kotor.Compiler/Bsp/BspWriter.cs:77-79` : côtés et leafbrushes marqués liés à la collision. `CheckRenderLimits` reçoit `limits.MaxLeafSurfaces`.
   - `src/Kotor.Compiler/Bsp/LimitCheck.cs` : les limites de rendu (shaders, surfaces, sommets, index, leaffaces, par surface) restent non liées à la collision.
   - `src/Kotor.Compiler/Compiler.cs:227` : la boucle ne réagit qu'à `bx.CollisionRelated`. Les autres limites sortent au premier tour, sans BSP, avec `BSP_ERROR` au journal (chemin d'échec existant).
   - Tests : `CompilerTests.Non_collision_limit_fails_immediately_without_reducing_collision`, `LimitsProfileTests` (les deux valeurs de profil).

2. **I2 (validation du nom de carte) : ADDRESSED.**
   - `src/Kotor.Core/Export/MapNames.cs` : normalisation (trim, minuscules) puis validation `[a-z0-9_]`, 1 à 48 caractères. Les caractères Unicode qui se replient en ASCII (par exemple le signe Kelvin) sont normalisés avant la validation, donc sans risque.
   - `PipelineSteps.cs` : `Import`, `Export` et `CreatePk3` lèvent `MAP_NAME_INVALID`. `Export` et `CreatePk3` revalident avant `Directory.Delete` et avant le chemin du pk3.
   - `ModuleExporter.cs` : refus dans les deux surcharges, avant toute écriture.
   - `ExportJaCommand.cs:493` : code de sortie 1, avant le contrôle de l'installation.
   - Tests : `..`, `..\..\x`, `a/b`, espaces, accent, vide, 49 caractères refusés / 48 acceptés, normalisation de la casse, `ModuleExporter` sans trace sur le disque, et le test CLI.

3. **Création fantôme de JaBaseDir : ADDRESSED.**
   - `PipelineSteps.cs` : `RequireJaBaseDir` lève `JA_BASE_MISSING`, appelé au début d'`Export` (après le contrôle des shaders, avant toute écriture) et dans `CreatePk3`. `Directory.CreateDirectory(JaBaseDir)` est supprimé.
   - Test : `Export_and_CreatePk3_refuse_missing_ja_base_dir_and_create_nothing`. Les tests existants créent maintenant `base` sous `%TEMP%`.

4. **TestEnv.JaBaseDir : ADDRESSED.**
   - `tests/Kotor.Core.Tests/TestEnv.cs` : la propriété vers `GameData` est supprimée. Les tests créent leur propre `base` sous `%TEMP%` (`ModuleExporterTests`, `PipelineStepsTests`, `PreviewBuilderTests`, `WorkflowControllerTests`).

5. **Avertissement de la vue Exporter : ADDRESSED.**
   - `src/Kotor.Pipeline/Workflow/KotorWorkflowSteps.cs:443` (appel dans `Export`) et `WarnIfCollisionReduced`, qui ajoute `EXPORT_VIEW_COLLISION_STALE` quand le journal contient `COLLISION_CAPPED` ou `WALKABLE_CAPPED`. Le journal est ensuite affiché par `Flush`.
   - Tests : `Export_view_warns_when_compiler_reduced_collision` (réduction réelle en profil vanilla) et `Export_view_does_not_warn_without_reduction`. L'appel dans `Export()` n'est couvert que via le helper, ce que le rapport signale.

## Nouvelle casse
Aucune.

## Observations hors périmètre (aucune bloquante)
- `ModuleExporter.Export(module, …)` et la CLI ne vérifient pas `JA_BASE_MISSING` avant `Preview` (chargement coûteux) : l'échec arrive après le chargement du module. Un contrôle plus tôt économiserait du temps.
- `ModuleExporter.BuildPlan` (publique) normalise le nom mais ne le valide pas. Les chemins d'écriture sont couverts par `PipelineSteps`.
- Le nom de module (`module`) n'est pas validé par `MapNames`. Il n'est utilisé que comme clé de catalogue. Il sert de nom de carte par défaut, et celui-là est validé.
- Si le `ImportLog` est réutilisé entre plusieurs exports, `WarnIfCollisionReduced` peut signaler une réduction ancienne.

## Verdict
**APPROVED** : les 5 constats sont traités, sans nouvelle casse.
