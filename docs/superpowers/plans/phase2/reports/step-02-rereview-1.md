# Étape 02 (phase 2) — Seconde revue, correction 1

Base b36e481, tête d5291a6. Revue du diff uniquement (aucune relance de la suite ; les chiffres de tests sont ceux du rapport de correction).

## Verdict par constat

1. **Liste GIT absente ou champ non-liste : ADDRESSED.**
   `ModuleEntitiesReader.cs` `ReadGit` (bloc `TryGetValue` / `raw is not IReadOnlyList<GffStruct>`) émet `GIT_LIST_MISSING`, avec un message distinct selon que le champ est absent ou présent mais pas une liste, puis `continue` sans exception. Tests : `Git_without_lists_logs_one_entry_per_list` (9 entrées, 0 entité) et `Non_list_field_logs_list_missing`.

2. **Position d'un autre type devenue 0 : ADDRESSED.**
   `ReadEntry` (branche non-caméra) teste `Fields[x] is float` pour X, Y et Z. Sinon `GIT_ENTRY_INVALID` « type inattendu » et l'entrée est sautée. Le test `Position_of_wrong_type_is_invalid_not_zero` utilise un X de type DWORD (code 5) et vérifie 0 entité et le message.

3. **Orientation absente donnant Yaw 0 en silence : ADDRESSED.**
   `ReadEntry` renseigne `orientMissing` quand `Bearing` (placeable/porte) ou `XOrientation`/`YOrientation` est absent ou non float. `ReadGit` agrège alors un `GIT_ORIENTATION_MISSING` par liste, avec le nombre d'entrées concernées, et le Yaw reste à 0. Test `Missing_orientation_is_logged_and_yaw_zero` : 2 entités, Yaw 0, 2 avertissements (un par liste). Le cas caméra (Orientation absente) reste une entrée invalide, ce qui est cohérent.

4. **Filtres d'exceptions trop étroits : ADDRESSED.**
   - GIT : `InvalidDataException`, `ArgumentException` (couvre `ArgumentOutOfRangeException`), `OverflowException`, `EndOfStreamException`, `IndexOutOfRangeException`, avec le code `GIT_UNREADABLE`, distinct de `GIT_MISSING` qui reste réservé au fichier absent.
   - MDL : mêmes exceptions plus `NotSupportedException`, avec `ROOM_MODEL_UNREADABLE`.
   - Test `Truncated_git_is_logged_not_thrown` : 5 longueurs de troncature, 0 entité, `GIT_UNREADABLE`.

## Nouvelle casse

Aucune. Le changement de code `GIT_MISSING` vers `GIT_UNREADABLE` pour un GIT illisible est voulu. Le test `Missing_git_is_logged` couvre toujours `GIT_MISSING`. Le rapport indique que les comptes de `tar_m02aa` sont inchangés.

## Observations hors périmètre

- Pas de test de MDL tronqué : le filtre MDL élargi n'est pas couvert directement.
- Dans `ReadEntry`, `s.GetList("Geometry")` et la lecture des sous-champs de géométrie restent hors de tout `try`. Un champ `Geometry` d'un autre type ou tronqué pourrait lever une exception non rattrapée. À vérifier lors d'une prochaine passe.
- Un couple `XOrientation`/`YOrientation` présent mais nul (0, 0) n'est pas signalé comme orientation manquante. C'est acceptable, car la valeur existe.

## Verdict

**APPROUVÉ.** Les 4 constats sont corrigés, avec des tests dédiés, et aucune régression n'est visible dans le diff.
