# Étape 07 — Correctifs de revue (fix 1)

## Changements

1. **Plafond de collision et plans/côtés (Important)** — `Compiler.cs` : budget unique (brushes, côtés de brush, plans) calculé depuis `Limits` (marges : 50 brushes ou 10 % de `MaxBrushes`, 550 côtés, 550 plans ou 10 % de `MaxPlanes`). `CapPolygons` reçoit désormais le budget restant (brushes, côtés, plans) ; chaque polygone coûte 1 brush, `8 + n` côtés et au pire `8 + n` plans (6 axiaux écrits par `BspWriter` + 2 + n). Le terrain marchable (brush prismatique : 6 + planes du brush) est compté sur les mêmes trois budgets. `COLLISION_CAPPED` journalisé, jamais d'exception.
2. **Priorité au sol marchable (Mineur)** — le terrain est construit en premier avec tout le budget (`min(brushes, côtés/11, plans/11)`), puis le solide est plafonné avec le reste réel (brushes/côtés/plans consommés par les brushes marchables). Une troncature du terrain journalise le nouveau code `WALKABLE_CAPPED` (en plus du message de `WalkableCollision`).
3. **Triangles dégénérés (Mineur)** — `CollisionMerger.MergeCoplanar(..., ImportLog? log = null)` : paramètre optionnel ajouté en dernier (signature compatible) ; un seul `COLLISION_DEGENERATE` avec le nombre de triangles ignorés (aire < 1e-4 ou sommets soudés). `Compiler` lui passe le journal.
4. **Attributs de surface incohérents (Mineur)** — `RenderSurfaces.FromInput(s, tree, ImportLog? log = null)` : `SURFACE_ATTRIBUTE_MISMATCH` (nom du shader inclus) pour Normals, Uv0 et Uv1 de longueur différente de Positions ; le repli (normale de face, UV zéro, pas de lightmap) est conservé. `Compiler` passe le journal.

Adaptation d'un test existant : `Limit_exceeded_writes_no_bsp` forçait l'échec avec `MaxPlanes = 1`, désormais absorbé par le plafond (c'est voulu) ; il force maintenant une limite dure non plafonnable (`MaxLeaves = 0`, erreur `MAX_MAP_LEAFS`, aucun fichier écrit).

## Tests couvrants

- `tests/Kotor.Compiler.Tests/CompilerTests.cs` : `Plane_budget_caps_solid_collision_and_compilation_succeeds` (MaxPlanes 400 : compile, plans ≤ 400, COLLISION_CAPPED), `Walkable_terrain_has_priority_over_saturated_solid` (MaxBrushes 60 : 30 brushes marchables conservés + 24 solides = 54, pas de WALKABLE_CAPPED), `Walkable_truncation_logs_walkable_capped` (MaxPlanes 10 : compile, plans ≤ 10, WALKABLE_CAPPED), `Limit_exceeded_writes_no_bsp` (adapté), `Brush_cap_logs_and_does_not_throw` (inchangé).
- `tests/Kotor.Compiler.Tests/CollisionMergerTests.cs` : `Degenerate_triangles_are_counted_in_one_journal_entry`.
- `tests/Kotor.Compiler.Tests/RenderSurfacesTests.cs` : `Mismatched_attribute_lengths_are_logged_with_shader_name`.

## Commandes

- `dotnet build Kotor2Ja.sln --no-incremental` : 0 avertissement, 0 erreur.
- `dotnet test Kotor2Ja.sln` : Kotor.Compiler.Tests 52/52 réussis, Kotor.Core.Tests 76/76 réussis (Kotor2Ja.Cli.Tests : aucun test, comme avant).

## Remarques

- Le comptage des plans est un majorant (les plans identiques sont dédupliqués par `BspWriter`) : le plafond peut être un peu plus strict que nécessaire, jamais plus lâche.
- Priorité du sol : un terrain très dense peut consommer tout le budget et supprimer la collision solide (journalisée `COLLISION_CAPPED`), conformément à la règle « le joueur ne tombe pas à travers le sol ».
