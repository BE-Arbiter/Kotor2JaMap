# Étape 09 — Correctif 1 (shaders, performances, échelle 40)

## 1. Shaders dans `shaders/`
- `ModuleExporter.Export` écrit désormais `shaders/kotor_<carte>.shader` dans le dossier de préparation du pk3 (au lieu de `scripts/`). Le moteur SWGL ne liste que `shaders/*.shader` (tr_shader.cpp:3065).
- Tests : `Synthetic_export_writes_pk3_with_expected_files_and_journal` attend `shaders/kotor_mymap.shader`; `Pk3_contains_expected_entries` (Pk3BuilderTests) attend `shaders/kotor_x.shader` (ce dernier test ne dépend que des données semées, `Pk3Builder` étant générique : il passait déjà).
- RED : `Synthetic_export_writes_pk3...` échoue avant correction. GREEN après.
- Vérifié sur le pk3 réel : `shaders/kotor_tar_m02aa.shader` présent, plus aucune entrée `scripts/`.

## 2. Surfaces fusionnées par shader
- `RenderSurface.Leaf` (int) devient `Leaves` (`IReadOnlyList<int>`).
- Nouvelle `RenderSurfaces.FromInputs(inputs, tree, log)` : groupes par (shader, DoubleSided, présence d'UV lightmap), triangles en ordre d'entrée (donc chunks compacts : mêmes entrées contiguës), découpage seulement par `MaxSurfaceVerts` (1199) / `MaxSurfaceIndexes` (7197), partage de sommets par (entrée, indice). `FromInput` (une entrée) est conservée en enveloppe. Compiler.cs appelle `FromInputs`.
- `RenderSurfaces.LeavesTouching(tree, points)` : descente de l'arbre avec la boîte englobante (deux côtés si elle chevauche un plan, convention `n.p >= dist` = avant comme `FindLeaf`). Retourne les feuilles non solides touchées ; **repli sur les feuilles solides touchées s'il n'y en a aucune**, pour qu'aucune surface ne reste sans feuille.
- `BspWriter` : leafsurfaces écrit depuis la relation plusieurs-à-plusieurs ; les bornes de feuille sont agrandies avec la boîte de la surface (calculée une fois par surface). `SkyBox` : une seule feuille (liste à un élément, comportement inchangé).
- Tous les triangles sont conservés ; UV lightmap, couleurs de sommet, enroulement inchangés.
- Tests ajoutés (RenderSurfacesTests) : `Inputs_with_same_shader_merge_into_one_surface` (a), `Mesh_spanning_several_leaves_is_one_surface_referenced_by_each_overlapped_leaf` (b), `Merged_chunks_respect_limits_and_keep_every_triangle` (c), `Different_shaders_never_merge` (d), `DoubleSided_and_lightmap_presence_split_groups`. Adaptés : `CompilerTests` (`Leaves == [0]`), `GlobalFogTests`, `VertexLightingTests`.
- RED : la compilation des tests échouait (`FromInputs`, `Leaves` inexistants). GREEN : 60/60 tests du compilateur.
- Limite du test (b) : dans `BspTree`, une feuille est « solide » dès qu'un brush recoupe sa boîte, donc dans toute scène de test avec brushes toutes les feuilles sont solides et le repli est exercé ; l'exclusion des feuilles non solides n'est pas testable (arbre non constructible à la main) mais elle est triviale dans `LeavesTouching`.

## 3. Échelle 40
`ExportOptions.DefaultScale = 40f`, `kotor2ja.config.json` (`"scale": 40`), aide de `--scale` (« defaut : config, 40 »), `Config_defaults_when_missing` (40). Les deux tests de ModuleExporter qui comptaient sur 36 passent maintenant `Scale = 36f` explicitement. Les docs du plan (context.md, plan général) mentionnent encore 36 : non modifiées (historique).

## Export de contrôle (dossier scratch, pas le jeu)
```
dotnet run -c Release --project src/Kotor2Ja.Cli -- export-ja tar_m02aa --config <scratch>/cfg.json
```
Résultat : 574 maillages, 41 216 triangles, export OK (aucune limite dépassée).

| | Avant (s40) | Après |
|---|---|---|
| Surfaces (lump 13, 148 o) | 18 635 | **296** |
| Index | 123 684 | 123 684 (identique : aucun triangle perdu) |
| Sommets | n/d | 91 184 |
| Feuilles | 25 996 | 12 907 |
| Leafsurfaces (lump 5, 4 o) | ~18 635 | **128 066** |

Note : le nombre de feuilles vient de la génération de l'arbre (échelle et collision différentes entre les deux exports), pas de ce correctif.
Pk3 : `maps/tar_m02aa.bsp`, `shaders/kotor_tar_m02aa.shader`, textures.

## Tests
`dotnet test Kotor2Ja.sln` : Cli 5/5, Compiler 60/60, Core 99/99, 0 avertissement.

## Réserves
- Leafsurfaces = 128 066 pour une limite de 131 072 (`MAX_MAP_LEAFFACES`) : marge de 2 %. Presque toutes les feuilles contenant un brush (donc « solides »), la règle « feuilles non solides seulement » retombe quasi toujours sur le repli. Une carte plus grosse dépasserait la limite (échec propre, pas de BSP partiel). Pistes si besoin : tri spatial des triangles avant découpage (chunks plus compacts, donc moins de feuilles par surface) ou ne référencer que les feuilles dont la région recoupe réellement les triangles.
