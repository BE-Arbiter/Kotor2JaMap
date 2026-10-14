# Étape 02 — Correction 1 (suite à la revue)

## Changements (`src/Kotor.Core/Entities/ModuleEntitiesReader.cs`)
1. **Liste absente** : `GIT_LIST_MISSING` (informatif, un par liste) quand le libellé est absent, ou présent mais pas une liste de structures (message distinct). Aucune exception.
2. **Position d'un autre type** : le type `float` est vérifié (`Fields[x] is float`) ; sinon `GIT_ENTRY_INVALID` (« type inattendu ») et entrée sautée, plus de 0 silencieux.
3. **Orientation absente** : `Bearing` (placeable/porte) ou `XOrientation`/`YOrientation` (autres) absents ou non float → Yaw 0 conservé et un `GIT_ORIENTATION_MISSING` par liste avec le nombre d'entrées concernées.
4. **Filtres d'exceptions élargis** : GIT → `InvalidDataException`, `ArgumentException` (couvre `ArgumentOutOfRangeException`), `OverflowException`, `EndOfStreamException`, `IndexOutOfRangeException` ; le code devient `GIT_UNREADABLE` (distinct de `GIT_MISSING`, réservé au fichier absent). MDL : mêmes exceptions + `NotSupportedException` → `ROOM_MODEL_UNREADABLE`.

## Tests ajoutés (`tests/Kotor.Core.Tests/Entities/ModuleEntitiesReaderTests.cs`)
- `Git_without_lists_logs_one_entry_per_list` (9 `GIT_LIST_MISSING`, aucune entité, pas d'exception)
- `Non_list_field_logs_list_missing`
- `Position_of_wrong_type_is_invalid_not_zero`
- `Missing_orientation_is_logged_and_yaw_zero`
- `Truncated_git_is_logged_not_thrown` (5 longueurs de troncature → `GIT_UNREADABLE`, aucune entité)

## Commandes
`dotnet test Kotor2Ja.sln` : Cli 5/5, Compiler 68/68, Pipeline 5/5, Core 108/108, 0 échec, 0 ignoré. `dotnet build --no-incremental` : 0 avertissement.

## tar_m02aa inchangé
`out/entities-tar_m02aa.txt` : Placeable 26, Door 8, Waypoint 44, Trigger 6, Creature 14, Sound 1, Store 1, Camera 9, Light 71 (test réel passé, 71 lumières = somme NODE_LIGHT). Le journal peut désormais contenir des `GIT_LIST_MISSING` (ex. Encounter vide/absente) et des `GIT_ORIENTATION_MISSING` (ex. Sound/Store sans orientation) : informatif, sans effet sur les entités.

KOTOR lu en lecture seule ; rien commité.
