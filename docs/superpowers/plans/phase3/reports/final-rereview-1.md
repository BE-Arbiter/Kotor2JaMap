# Phase 3 — Re-relecture 1 de la correction finale (31ec5b2..97f7d58)

Lecture seule ; les tests n'ont pas été relancés (le rapport de correction annonce build 0/0 et tests verts, non revérifié).

## 1. T02 — config illisible : ADDRESSED
- `src/Kotor2Ja.Cli/Commands/ExportJaCommand.cs` : `TryLoadConfig` (l.45-58) attrape Json/IO/UnauthorizedAccess/InvalidOperation/Format ; message anglais `Cannot read the configuration file '<fichier>': <raison>` sur stderr ; l'action renvoie 1 (l.28). `JsonDocument.Parse` lève `JsonException` ; `GetSingle` peut lever `FormatException` : couvert (`ConfigLoader.cs:83-100`).
- Tests `ExportJaCommandTests` : message, chemin du fichier, absence de `   at `, et code 1 via `Program.BuildRoot().Parse(...).Invoke()`.

## 2. T03 — pk3 reproductible : ADDRESSED
- `src/Kotor.Core/Export/Pk3Builder.cs:40-47` : `CreateEntry` + `LastWriteTime` = 1980-01-01 UTC, contenu copié. Ordre déterministe (`OrderBy` ordinal insensible à la casse, l.22), compression constante. Le champ statique `FixedTime` est déclaré après usage mais initialisé à l'initialisation du type : sans effet.
- Test `Pk3BuilderTests.Same_staging_built_twice_gives_identical_bytes` : dates de fichiers modifiées entre deux builds, octets égaux, dates d'entrée vérifiées.

## 3. Chemin MD3 et échelle : ADDRESSED
- `PlaceableConverter.cs:153-154,160-162` : `ModelDirectory` = préfixe si échelle == `ExportOptions.DefaultScale` (40), sinon `<préfixe>s<échelle>/`. Format `ToString("0.######", CultureInfo.InvariantCulture)` : invariant à la culture (s37.5 avec point). Défaut inchangé.
- Même chemin pour l'entité et le pk3 : l'entité prend `asset.Path` (`PlaceableConverter.cs:77`, `DoorConverter.cs:73` = `info.Assets[0].Path`), et l'écriture du staging utilise `md3.Path` (`PipelineSteps.cs:154-158`) puis `Pk3Builder` zippe le staging : une seule source de vérité. Les portes passent par `BuildModel` (`DoorConverter.cs:61`). Les vues Exporter/Importer lisent `model`/`model2` ou `asset.Path` (`PreviewBuilder.Models.cs:104,140-141`). Les shaders restent sous `ModelPrefix` : les noms de surface MD3 sont écrits via `shaderPrefix` non modifié (`Md3Writer.cs:45-47`), cohérent.
- Tests : `Md3_path_depends_on_a_non_default_scale` (40/36/37.5, entité = asset), cas KOTOR 2 et découpé `_p0/_p1`, `Door_md3_path_depends_on_a_non_default_scale` (`model2` = asset). Les tests comparent entité et `Models.Path`, pas le contenu du pk3 (le chemin staging→zip n'a pas de test de bout en bout, mais le code est direct).

## 4. Déduplication de deux portes : ADDRESSED
- `DoorConverterTests.Two_doors_of_one_model_give_one_md3_and_two_distinct_submodels` : un seul MD3 (`Assert.Single(r.Models)`), deux `func_door`, `*3`/`*4`, deux sous-modèles, un `model2` commun. La déduplication par `models[model]` (`DoorConverter.cs:61`) est bien exercée dans un seul `Convert`.

## Nouvelles casses Critical/Important
Aucune.

Remarques mineures (non bloquantes) :
- Le chemin s'allonge de 6-7 caractères (`s37.5/`) ; la limite du moteur est 64 caractères (MAX_QPATH). Sans effet pour les noms de modèles KOTOR courants (<= 16 caractères), mais un nom proche de 47 caractères dépasserait ; limite déjà approchée avant ce diff.
- Le format `0.######` pourrait théoriquement donner le même dossier pour deux flottants distincts à moins de 1e-6 près : négligeable.
- Réserve du rapport (pk3 existants à régénérer) correcte.
