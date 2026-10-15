# Phase 3 — Correction finale 1

Aucun export réel n'a été lancé : seuls des tests (dossiers temporaires, sans écriture dans `Jedi Academy - SWGL_DEV`). Le jeu n'a pas été lancé. Rien n'est commité.

## 1. T02 / m1 — configuration illisible dans `export-ja`

- Fait : `ExportJaCommand.TryLoadConfig(path, error, out options)` (nouveau, public) entoure `ConfigLoader.Load`. Sur `JsonException`, `IOException`, `UnauthorizedAccessException`, `InvalidOperationException` ou `FormatException`, il écrit sur stderr `Cannot read the configuration file '<fichier>': <raison>` et l'action renvoie 1, sans pile d'appels.
- `list-modules` et `export-obj` : ils n'ont pas d'option `--config` et passent par `GameOption.ResolveDir`, qui attrape déjà ces exceptions et se replie sur les défauts (test existant `ResolveDir_falls_back_to_defaults_on_a_broken_config`). Rien changé, comme demandé (« ne change rien d'autre »).
- Tests (`ExportJaCommandTests`) : `Truncated_config_gives_a_clear_message_and_code_1` (JSON `{ "workDir": "x", ` : message avec le fichier, pas de ligne `   at `, et `Program.BuildRoot().Parse(["export-ja", ...]).Invoke()` renvoie 1) ; `Valid_config_loads`.

## 2. T03 / m2 — pk3 reproductible

- Fait : `Pk3Builder.Add` crée chaque entrée avec `CreateEntry`, fixe `LastWriteTime` à 1980-01-01 00:00 UTC (minimum du format zip) et copie le contenu du fichier. L'ordre était déjà trié (`OrderBy` ordinal insensible à la casse) ; le niveau de compression est constant. Aucune autre source de variation (pas d'attributs ni de commentaire propres au fichier).
- Test (`Pk3BuilderTests.Same_staging_built_twice_gives_identical_bytes`) : deux constructions du même staging, avec des dates de modification différentes entre les deux, donnent les mêmes octets ; toutes les entrées ont la date 1980-01-01.

## 3. m3 — chemin MD3 et échelle

- Fait : `PlaceableConverter.ModelDirectory(prefix, scale)` : échelle 40 → `models/<dossier>/` (inchangé) ; sinon `models/<dossier>/s<échelle>/`, formaté en invariant (`0.######`) : `s36`, `s37.5`. `BuildModel` l'utilise pour les chemins des fichiers (un fichier ou `_p0`, `_p1`...). Objets et portes passent tous deux par `BuildModel`, donc les deux sont couverts.
- Les noms de shaders (`models/<dossier>/<texture>`) et les textures restent sans dossier d'échelle : ils ne dépendent pas de l'échelle et sont partagés.
- Vue Exporter : `WithStagedModels` lit les chemins directement dans les clés `model` / `model2` des entités du BSP, et la vue Importer dans `asset.Path` ; les textures et le shader sont retrouvés par le nom de shader du MD3. Aucune modification nécessaire (vérifié à la lecture du code ; le préfixe de shader est inchangé).
- Tests (`PlaceableConverterTests`) : `Md3_path_depends_on_a_non_default_scale` (40, 36, 37.5 ; chemin d'entité, chemin de l'asset, shader inchangé) ; `Md3_path_scale_folder_applies_to_kotor2_and_split_models` (KOTOR 2 et modèle découpé `_p0`/`_p1`). `DoorConverterTests.Door_md3_path_depends_on_a_non_default_scale` (`model2` et asset sous `s37.5`).
- Test existant adapté : `Model_with_more_surfaces_than_the_engine_limit_is_split_into_files` utilise l'échelle 1, ses chemins attendus deviennent `models/kotor/s1/huge_p*.md3`.

## 4. m6 — déduplication des portes

- Test seul (`DoorConverterTests.Two_doors_of_one_model_give_one_md3_and_two_distinct_submodels`) : deux portes du même modèle dans un seul `Convert` donnent un seul MD3, deux entités `func_door`, deux sous-modèles distincts (`*3`, `*4`, deux `SubModelInput`) et un même `model2`. Aucun changement de code : le test passe.

## Commandes et résultats

- `dotnet build Kotor2Ja.sln --no-incremental` : 0 avertissement, 0 erreur.
- `dotnet test Kotor2Ja.sln` : vert, aucun test sauté. Cli 22, Compiler 80, Core 165, Pipeline 80.
- Un premier passage a échoué sur le test existant du modèle découpé (chemins attendus sans dossier d'échelle, échelle 1) ; corrigé comme indiqué plus haut.

## Réserves

- Les pk3 déjà présents dans `GameData\base` (avant cette correction) contiennent d'anciens chemins `models/kotor/...` ; à régénérer pour les cartes à échelle non standard.
- Le dossier `%LOCALAPPDATA%\Kotor2Ja` créé par l'incident de la relecture finale reste à supprimer à la main.
