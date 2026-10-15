# Correction finale 2 (essais visuels)

## Portes statiques
- `ModuleEntity` : nouveau champ `Static` (en dernier, valeur par défaut `false`).
- `ObjectModelResolver.IsDoorStatic` (lecture de `Static` dans l'UTD, comme `Locked`, mutualisée dans `DoorFlag`) ; `ModuleEntitiesReader` renseigne `Static`.
- `DoorConverter` : une porte `Static` reçoit `spawnflags 16`, comme les portes verrouillées et les transitions.
- Bilan `DOORS` par cause, une porte cumulant plusieurs causes compte une fois dans le total : `N door(s) converted (L locked: a locked, b transition, c static), S skipped.`
- Tests : porte synthétique `Static=1` -> `spawnflags 16` (et cumul des causes, bilan) ; test réel tar_m02aa : 8 portes, toutes `spawnflags 16`. Le test existant du bilan est adapté au nouveau format.

## Tiret des noms de modules
- `MainScreen.cs` : `module — zone` devient `module - zone`. Aucun autre glyphe hors ASCII dans les textes affichés de l'application (le `→` du titre de fenêtre est rendu par l'OS, pas par ImGui, laissé tel quel). Le README ne montre pas ce format.

## Vérifications
- `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur.
- `dotnet test Kotor2Ja.sln` : 22 + 80 + 165 + 81 réussis, 0 échec, 0 ignoré (test réel exécuté).
