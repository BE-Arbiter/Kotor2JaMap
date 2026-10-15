# Phase 3 - Re-relecture 2 de la correction finale 2 (97f7d58..a0049ec)

Lecture seule. Aucun build ni test relancé (les tests écrivent dans le dossier de sortie et le test réel dépend de l'installation de KOTOR). Les verdicts reposent sur la lecture du code et du diff.

## 1. Porte UTD `Static=1` -> `spawnflags 16` : ADDRESSED
- `src/Kotor.Pipeline/Entities/DoorConverter.cs:84-88` : `if (e.Locked || isTransition || e.Static)` pose `spawnflags` = `SpawnflagLocked` (16, l.30).
- `src/Kotor.Core/Entities/ObjectModelResolver.cs:29` (`IsDoorStatic` -> `DoorFlag(template, "Static")`, l.31-40 : lecture de `Static` dans l'UTD, faux si absent ou illisible).
- `src/Kotor.Core/Entities/ModuleEntitiesReader.cs:98` : `Static = models.IsDoorStatic(e.Template)`.
- Test : `tests/Kotor.Pipeline.Tests/DoorConverterTests.cs:119-127` (porte synthétique `Static`, `spawnflags` 16).
- Réserve : la lecture UTD -> `Static` (`IsDoorStatic`, `ModuleEntitiesReader.cs:98`) n'est couverte par aucun test ; le test part directement d'un `ModuleEntity` avec `Static = true`.

## 2. Bilan `DOORS` en anglais, cause unique dans le total : ADDRESSED
- `DoorConverter.cs:51` : compteurs `byLocked`, `byTransition`, `byStatic`.
- `DoorConverter.cs:84-91` : `locked++` une seule fois par porte, quelle que soit la cause ; chaque cause incrémente son compteur.
- `DoorConverter.cs:104` : `N door(s) converted (L locked: a locked, b transition, c static), S skipped.`
- Tests : `DoorConverterTests.cs:115` (format adapté, `1 locked: 1 locked, 0 transition, 0 static`) et `DoorConverterTests.cs:126` (porte cumulant deux causes comptée une fois : `3 locked: 1 locked, 1 transition, 2 static` sur 4 portes).

## 3. Tiret long de la liste des modules remplacé par ` - ` : ADDRESSED
- `src/Kotor2Ja.App/Ui/MainScreen.cs:40` : `$"{m.Module} - {m.AreaName}"`.
- `grep "—" src/Kotor2Ja.App --include=*.cs` : aucune occurrence restante.

## 4. Tests couvrent les points 1 à 3, dont le test réel tar_m02aa (8 portes) : NOT ADDRESSED
- Points 1 et 2 : couverts (`DoorConverterTests.cs:115, 119-127`).
- Point 3 (tiret) : aucun test. Rien ne vérifie le format de `MainScreen.SetModules`.
- Test réel : `DoorConverterTests.cs:226-263`, `[SkippableFact]` ; il est ignoré si KOTOR n'est pas installé (l.229). Les assertions `Assert.Equal(8, doors.Count)` et `Assert.All(... "spawnflags" "16")` sont aux l.256-257. L'exécution n'a pas été vérifiée ici : le rapport affirme « test réel exécuté », sans preuve de sortie.

## Casses introduites par le diff
Aucune trouvée.
- `ModuleEntity` : `Static` ajouté en dernier avec valeur par défaut ; les constructions positionnelles existantes restent valides (`src/Kotor.Core/Entities/ModuleEntity.cs:13`).
- Aucun autre test ni rapport n'attend l'ancien format `(N locked)` du bilan `DOORS`.
- Le partage `DoorFlag` ne change pas le comportement de `IsDoorLocked`.
