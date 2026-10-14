# Étape 07 — Re-revue du correctif 1 (df4c043..3f1352b)

## Verdict par constat

1. **Budget brushes / côtés / plans (Important) — PARTIELLEMENT TRAITÉ (NOT ADDRESSED pour la garantie « borne supérieure »).**
   - Acquis : `Compiler.cs:205-212` calcule un budget unique (brushes, côtés, plans) et `CapPolygons` (`Compiler.cs:274-295`) le respecte ; le test `Plane_budget_caps_solid_collision_and_compilation_succeeds` (`CompilerTests.cs:418`) exerce un `MaxPlanes` réduit.
   - Vérification demandée « le budget est-il un majorant de ce que BspWriter émet par brush ? » (comparaison avec `BspWriter.cs:68-93` et `BspTree.cs:36-37,57`) :
     - Côtés : exact. `BspWriter.cs:85-91` écrit 6 côtés axiaux + `b.Planes.Count` côtés par brush, soit `6 + n` pour le terrain et `6 + (2 + n)` pour un polygone solide : le coût `8 + n` est exact. OK.
     - Plans émis par brush : majorés par 6 + `b.Planes.Count` (dédup uniquement à la baisse). OK.
     - **Plans non budgétés** : la liste des plans écrite est `tree.Planes` + plans axiaux + plans du brush, et `tree.Planes` contient aussi **un plan de séparation axial par nœud interne** (`BspTree.cs:57`, position = médiane des centres de brushes, donc rarement confondue avec un plan de brush). Ces plans de nœuds ne sont comptés nulle part ; seule la marge `planeMargin = min(550, MaxPlanes/10)` (`Compiler.cs:206`) les absorbe. Avec ~11 800 brushes, `LeafBrushTarget = 4` donne plusieurs milliers de nœuds, donc bien plus de 550 plans possibles. La déduplication (plans axiaux partagés) peut compenser en pratique mais ne le garantit pas : le plafond n'est pas une vraie borne supérieure, et `limits.CheckPlanes` (`BspTree.cs:45`, `BspWriter.cs:142`) peut encore lever `MAX_MAP_PLANES` sur une carte dense saturant le budget. Le scénario du constat d'origine (~11 800 brushes à `MaxPlanes` par défaut) n'est donc pas démontré corrigé.
     - Le test à `MaxPlanes = 400` (marge 40, ≤ ~32 brushes) ne discrimine pas ce défaut : le nombre de nœuds y est trop faible.
   - Correctif suggéré : retrancher du budget de plans une estimation du nombre de nœuds (par ex. `≤ brushes` conservateur, ou `kept/2 + 1` plans de séparation) ou, mieux, vérifier après `BspTree.Build` puis réduire/ré-itérer ; ajouter un test à grand nombre de petits brushes disjoints à `MaxPlanes` réduit.

2. **Sol marchable prioritaire — ADDRESSED.** `Compiler.cs:211-222` construit le terrain en premier avec tout le budget (`walkCap`), puis le solide reçoit le reste (`Compiler.cs:228-229`) ; la troncature journalise `WALKABLE_CAPPED` (`Compiler.cs:214-219`) ; tests `Walkable_terrain_has_priority_over_saturated_solid` (`CompilerTests.cs:434`, 30 marchables conservés, 54 au total) et `Walkable_truncation_logs_walkable_capped` (`CompilerTests.cs:450`). Le cas `walkCap = 0` aboutit bien à `LimitReached` (`WalkableCollision.cs:47-48`). Réserve : le terrain est borné par la même estimation de plans incomplète (constat 1).

3. **COLLISION_DEGENERATE — ADDRESSED.** `CollisionMerger.cs` (compteur `degenerate`, une seule entrée `Warn` avec le nombre, après la boucle d'enregistrement), journal transmis par `Compiler.cs:225` ; test `Degenerate_triangles_are_counted_in_one_journal_entry` (`CollisionMergerTests.cs:329`, vérifie « 2 triangle »). Signature rétrocompatible (paramètre optionnel final).

4. **SURFACE_ATTRIBUTE_MISMATCH — ADDRESSED.** `RenderSurfaces.cs` (3 avertissements avec `s.Shader` pour Normals, Uv0, Uv1), journal transmis par `Compiler.cs:244` ; test `Mismatched_attribute_lengths_are_logged_with_shader_name` (`RenderSurfacesTests.cs:490`, 3 entrées, nom du shader). Repli inchangé.

## Régression / nouvelle casse

- **Important (résiduel du constat 1)** — `Compiler.cs:206,209,212,228-229` : plans de séparation de nœuds (`BspTree.cs:57`) hors budget ; compilation encore susceptible d'échouer sur `MAX_MAP_PLANES` pour une carte très dense. Détail ci-dessus.
- Test modifié `Limit_exceeded_writes_no_bsp` (`CompilerTests.cs:370-381`) : le passage à `MaxLeaves = 0` est légitime (l'ordre des paramètres de `Limits` est brushes, plans, nœuds, feuilles, cohérent avec l'ancien appel) ; il préserve l'intention (limite dure → pas de BSP écrit). Aucune casse.
- Autre : aucune.

## Observations hors périmètre

- `WalkableCollision.Weld` (`WalkableCollision.cs:75`) écarte aussi des triangles dégénérés sans journal (même classe de silence que le constat 3, côté terrain).
- Le rapport de correctif affirme que le comptage « est un majorant, jamais plus lâche » (`step-07-fix-1.md`, Remarques) : exact pour les plans de brush, faux une fois les plans de nœuds comptés.
- Résultats de tests du rapport (Compiler 52/52, Core 76/76) non rejoués (revue limitée au diff).

## Verdict

**CHANGES REQUESTED** — constats 2, 3, 4 traités ; constat 1 partiellement traité : le budget de plans n'est pas une borne supérieure stricte car il ignore les plans de séparation de l'arbre BSP.
