# Étape 07 — Rapport d'implémentation (compilateur en mémoire)

## Ce qui a été construit

- `CompileInput.cs` : `SurfaceInput` et `CompileInput` (signatures de l'étape).
- `Compiler.Compile(CompileInput, bspPath, Limits, ImportLog)` : plus d'entrée par fichiers (`Compile(mapPath…)`, `ReadCollision`, `ParseEntities`, `ReadDoubleSidedShaders`, journal fichier, `assetRoot` supprimés). Les avertissements vont dans `CompileResult.Warnings` et dans `log` (code `BSP_WARNING`). Une erreur dure est aussi journalisée (`BSP_ERROR`). Le ciel vient de `input.SkyShader`, puis de la clé worldspawn `_kotor_sky`, puis de `DefaultSkyShader`. La boîte de ciel est conservée. `VertexLighting` vient de l'entrée (pas relu depuis le worldspawn). Le brouillard reste lu dans le worldspawn (`_kotor_fog`).
- `RenderSurfaces.FromInput(SurfaceInput, BspTree)` remplace `FromObj` : même découpage par feuille et par limites (1199 / 7197), même enroulement JA (v0, v2, v1), UV1 suivent les sommets. `RenderSurface` gagne `Vector2[]? LightmapUvs`. `DoubleSided` vient uniquement de `SurfaceInput`.
- `BspWriter` : si `LightmapUvs` existe, `lightmap[0]` = UV1 et couleur de sommet (255,255,255,255) ; sinon comportement précédent. `lightmapNum` reste `-3`.
- `CollisionMerger.MergeCoplanar` : fusion gloutonne par arêtes partagées (soudure à 1/1000), critères angle/plan, test de convexité, suppression des sommets alignés, triangles dégénérés (aire < 1e-4) ignorés, polygones limités à 8 sommets (borne le nombre de côtés par brush).
- `BrushBuilder.FromPolygons` (prisme : 2 faces + 1 plan par arête). `FromCollision` est conservé (utilisé par `WalkableCollision` et les tests).
- Plafond : `CapPolygons` retire d'abord les polygones de plus petite aire au-delà de `MaxBrushes - 50` (et d'un budget de côtés de brush), journalise `COLLISION_CAPPED`, ne lève pas d'exception. Puis `WalkableCollision.Build` comme avant.
- Renommages : `Bsp/CompilerNames.cs` est la source unique de `textures/kotor/collision` et des clés `_kotor_sky`, `_kotor_fog`, `_kotor_sun_dir`, `_kotor_sun`, `_kotor_ambient` (`GlobalFog`, `VertexLighting`, `BspWriter.CollisionShader` y renvoient).

## Preuve TDD

- RED : après écriture des tests, `dotnet test tests/Kotor.Compiler.Tests` échoue à la compilation (`SurfaceInput`, `CompileInput` introuvables, plus `FromInput`, `CollisionMerger`, etc.).
- GREEN : après implémentation, 47/47 tests verts. `dotnet test Kotor2Ja.sln` : Compiler 47/47, Core 76/76, sans avertissement.
- Tests de l'étape ajoutés : `CollisionMergerTests` (`Two_coplanar_triangles_merge_into_one_polygon`, `Non_coplanar_triangles_stay_separate`, `Merge_keeps_convexity`, plus `Degenerate_triangle_is_ignored`), `CompilerTests` (`Brush_cap_logs_and_does_not_throw`, `Compile_in_memory_writes_bsp`), `BspWriterTests` (`Lightmap_uvs_written_to_drawverts`, `Surface_without_uv1_uses_vertex_lighting`, `Collision_shader_is_kotor_namespaced`), `RenderSurfacesTests` (UV1 suit les sommets).

## Fichiers modifiés

`src/Kotor.Compiler/` : `Compiler.cs`, `CompileInput.cs` (nouveau), `Bsp/{RenderSurfaces,BspWriter,BrushBuilder,GlobalFog,VertexLighting}.cs`, `Bsp/CollisionMerger.cs` et `Bsp/CompilerNames.cs` (nouveaux).
`tests/Kotor.Compiler.Tests/` : `CompilerTests`, `RenderSurfacesTests`, `SkyBoxTests`, `BspWriterTests`, `VertexLightingTests`, `GlobalFogTests`, `CollisionMergerTests` (nouveau), `TestScenes.cs` (aides, nouveau).

## Tests adaptés ou supprimés

- `CompilerTests` : réécrit en mémoire (plus de fichiers). `Limit_exceeded_writes_no_bsp` force maintenant `MaxPlanes = 1` : avec `MaxBrushes = 0` le plafond de brushes ne lève plus (c'est le but de l'étape). `Missing_model_is_warning_not_error` supprimé (plus de `misc_model`/OBJ). `Compiles_small_map_end_to_end` devient `Compile_in_memory_writes_bsp` ; le test du fichier journal disparaît avec le journal fichier.
- `RenderSurfacesTests` : grilles en mémoire via `FromInput`. `Obj_vt_is_flipped_to_Quake_st` supprimé (plus d'OBJ ni d'inversion de `vt`).
- `SkyBoxTests` : `Compile` en mémoire avec `SkyShader`. `VertexLightingTests` : `Double_sided_shaders_are_read_from_shader_file` supprimé (`ReadDoubleSidedShaders` retiré) ; clés renommées `_kotor_*`. `GlobalFogTests` : clé `_kotor_fog`.

## Concerns

1. UV0 : `FromInput` recopie `Uv0` tel quel, sans l'inversion `1 - v` de l'ancien lecteur OBJ. Convention attendue : Quake (t = 0 en haut), à garantir côté export (étape 08).
2. Il reste des littéraux `textures/tfu2ja/` dans `Kotor.Core/Export` (`ShaderNames`, `Pk3Builder`, `ShaderWriter`) : étape 08, non touchés. Le shader de collision du BSP (`textures/kotor/collision`) est indépendant de `ShaderNames`.
3. Marge de brushes : le plafond solide réserve 50 brushes ; le terrain (`MaxBrushesFor(..., margin: 0)`) peut les utiliser. Si la collision solide sature le plafond, le terrain reçoit donc au plus ces 50 brushes.
4. Les polygones de plus de 8 sommets ne sont pas fusionnés (limite volontaire, pas dans le plan).
5. La fusion exige des arêtes parcourues en sens inverse : deux triangles voisins d'enroulement opposé ne fusionnent pas (sans conséquence fonctionnelle, juste plus de brushes).
