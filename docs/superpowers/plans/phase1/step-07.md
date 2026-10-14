# Étape 07 — Compilateur : entrée en mémoire, UV de lightmap, collisions fusionnées

**But :** le compilateur copié (étape 01) reçoit la scène directement en mémoire, déjà en unités JA. Il écrit les UV de lightmap dans les sommets du BSP. Il tient la limite de brushes en fusionnant les triangles coplanaires.

**Fichiers :**
- Modifier : `src/Kotor.Compiler/Compiler.cs`, `Bsp/RenderSurfaces.cs`, `Bsp/BspWriter.cs`, `Bsp/BrushBuilder.cs`
- Créer : `src/Kotor.Compiler/CompileInput.cs`, `src/Kotor.Compiler/Bsp/CollisionMerger.cs`
- Tester : `tests/Kotor.Compiler.Tests/{CompilerTests,RenderSurfacesTests,BspWriterTests}.cs` (à adapter), `CollisionMergerTests.cs` (nouveau)

**Interfaces :**
- Consomme : `ModelMesh`/`SubMesh` (01), `ImportLog` (01), et le reste du compilateur copié (`BspTree`, `WalkableCollision`, `SkyBox`, `VertexLighting`, `Limits`, `LimitCheck`).
- Produit :
```csharp
public sealed record SurfaceInput(string Shader, Vector3[] Positions, Vector3[] Normals, Vector2[] Uv0, Vector2[]? Uv1,
    int[] Indices, bool DoubleSided = false);                        // unités JA
public sealed record CompileInput(string MapName, IReadOnlyList<IReadOnlyDictionary<string, string>> Entities /* [0] = worldspawn */,
    IReadOnlyList<SurfaceInput> Surfaces, ModelMesh Solid, ModelMesh Walkable, string SkyShader, VertexLighting Lighting);
public static class Compiler { public static CompileResult Compile(CompileInput input, string bspPath, Limits limits, ImportLog log); }
public static class CollisionMerger
{
    public static IReadOnlyList<Vector3[]> MergeCoplanar(ModelMesh mesh, float normalEpsilonDeg = 0.5f, float planeEpsilon = 0.05f); // polygones convexes
}
public static class BrushBuilder { public static IReadOnlyList<Brush> FromPolygons(IReadOnlyList<Vector3[]> polygons, float thickness = DefaultThickness, Action<string>? log = null); }
// RenderSurface (existant) gagne : Vector2[]? LightmapUvs
```
**Ce qui est fixé :**
- On **supprime** l'entrée par fichiers : `Compile(mapPath, …)`, `RenderSurfaces.FromObj`, `ReadCollision`, `ReadDoubleSidedShaders`. `CompileResult` reste inchangé. Les avertissements partent aussi dans `log` (code `BSP_WARNING`).
- `RenderSurfaces.FromInput(SurfaceInput s, BspTree tree)` reprend le découpage existant (`MaxSurfaceVerts = 1199`, `MaxSurfaceIndexes = 7197`) et le calcul de la feuille. Les UV1 suivent les sommets.
- `BspWriter` :
  - **si `LightmapUvs` existe**, il écrit `lightmap[0]` = UV1 dans chaque drawvert et une couleur de sommet blanche (255,255,255,255), parce que l'éclairage vient de la lightmap via le shader ;
  - sinon, il garde le comportement actuel (UV de lightmap à 0 et `VertexLighting`).
  - `lightmapNum` reste `-3` (par sommet). Le shader utilise `tcGen lightmap`, qui lit les coordonnées de lightmap du drawvert (vérifié dans `code/rd-vulkan/tr_shader.cpp` du dépôt `OpenJK-SWGL` : `TCGEN_LIGHTMAP`).
- Collisions : on appelle `CollisionMerger.MergeCoplanar(Solid)`, puis `BrushBuilder.FromPolygons`, puis `WalkableCollision.Build(Walkable, MaxBrushesFor(...))` comme avant.
  - Si le total dépasse `Limits.MaxBrushes - 50`, on retire d'abord les polygones de plus petite aire.
  - Le journal reçoit `COLLISION_CAPPED` avec le nombre retiré. On ne lève pas d'exception.
- Fusion : deux triangles qui partagent une arête, dont les normales diffèrent de moins de `normalEpsilonDeg` et dont les plans sont à moins de `planeEpsilon`, sont fusionnés si le résultat reste convexe. Un triangle dégénéré (aire < 1e-4) est ignoré.
- La boîte de ciel existante reste en place (elle ferme la carte). Le vrai ciel KOTOR arrive en phase 3.

- [ ] **Étape 1 : tests qui échouent**
  - `Two_coplanar_triangles_merge_into_one_polygon` : un carré fait de 2 triangles donne 1 polygone de 4 sommets.
  - `Non_coplanar_triangles_stay_separate` : 2 triangles à 90° donnent 2 polygones.
  - `Merge_keeps_convexity` : un L de 3 carrés coplanaires (6 triangles) donne au moins 2 polygones, tous convexes.
  - `Brush_cap_logs_and_does_not_throw` : avec `Limits` réduit à `MaxBrushes = 60` et 100 triangles disjoints, la compilation réussit, le nombre de brushes est inférieur ou égal à 60, et le journal contient `COLLISION_CAPPED`.
  - `Lightmap_uvs_written_to_drawverts` : une surface avec UV1 = (0.25, 0.75) donne un BSP dont le premier drawvert a `lightmap[0] == (0.25, 0.75)` et la couleur 255.
  - `Surface_without_uv1_uses_vertex_lighting` : la couleur est différente de 255 pour un éclairage par défaut non blanc.
  - `Compile_in_memory_writes_bsp` : un sol de 2 triangles, plus worldspawn et `info_player_start`, donne un BSP dont le lump d'entités contient `info_player_start`.
  - Adapter les tests copiés qui utilisaient des OBJ sur disque.
- [ ] **Étape 2 : lancer** `dotnet test tests/Kotor.Compiler.Tests`. Attendu : échec.
- [ ] **Étape 3 : implémenter.**
- [ ] **Étape 4 : `dotnet test Kotor2Ja.sln`** tout vert.
- [ ] **Étape 5 : rapport** `reports/step-07-impl.md` : tests adaptés ou supprimés, et pourquoi.
