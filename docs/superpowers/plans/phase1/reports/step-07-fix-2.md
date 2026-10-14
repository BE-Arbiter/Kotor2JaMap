# Étape 07 — Correctif de revue (fix 2)

## Changement

`src/Kotor.Compiler/Compiler.cs` : le budget estimé (fix 1) reste comme première estimation, mais la borne est désormais **garantie par reconstruction**. Les polygones solides sont triés par aire décroissante ; la construction (brushes, `BspTree.Build`, surfaces, boîte de ciel, `BspWriter.Write` en mémoire) est placée dans une boucle. Si une `BspLimitException` est levée (plans de nœuds, nœuds, feuilles, brushes, côtés, etc.), la collision est réduite de 20 % (solide d'abord, plus petites aires en premier ; le sol marchable n'est réduit qu'une fois le solide vide), puis tout est reconstruit, au plus `MaxShrinkAttempts = 80` fois. Les avertissements d'une tentative ne sont reportés dans le journal qu'à la tentative réussie. Après la boucle : `COLLISION_CAPPED` (nombre final conservé) et, si le terrain a été réduit, `WALKABLE_CAPPED`. Si la limite subsiste avec plus aucune collision (ex. `MaxLeaves = 0`), l'erreur est propagée comme avant (aucun BSP écrit).

## Tests couvrants

- Nouveau : `CompilerTests.Tree_node_planes_are_enforced_by_rebuild_and_compilation_succeeds` (3000 petits triangles inclines disjoints, `MaxPlanes = 25000`) : compilation réussie, plans ≤ MaxPlanes, > 1000 brushes conservés, `COLLISION_CAPPED` journalisé, pas de `BSP_ERROR`. Vérifié discriminant : avec `MaxShrinkAttempts = 0` il échoue (`MAX_MAP_PLANES = 25000, 25412 requis`) ; avec la boucle il passe.
- Inchangés et verts : `Limit_exceeded_writes_no_bsp`, `Plane_budget_caps_*`, `Walkable_*`, `Brush_cap_logs_and_does_not_throw`.

## Commandes

- `dotnet build Kotor2Ja.sln --no-incremental` : 0 avertissement, 0 erreur.
- `dotnet test Kotor2Ja.sln` : Kotor.Compiler.Tests 53/53, Kotor.Core.Tests 76/76 (Kotor2Ja.Cli.Tests : aucun test, comme avant).

## Remarques

- Coût : chaque dépassement ajoute une reconstruction complète en mémoire (pire cas ~40 itérations pour ~11 800 brushes).
- Le fichier `Compiler.cs` a été réécrit via PowerShell `Set-Content -Encoding utf8` lors du basculement temporaire de la constante : un BOM UTF-8 a pu être ajouté.
