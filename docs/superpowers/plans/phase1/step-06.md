# Étape 06 — Maillages de marche, scène du module et export OBJ de contrôle

**But :**
- assembler toutes les pièces d'un module en une scène unique, en mètres KOTOR, avec la liste de tout ce qui n'a pas été pris ;
- l'exporter en OBJ pour un premier contrôle visuel par l'utilisateur dans Blender.

**Fichiers :**
- Créer : `src/Kotor.Core/Walkmesh/{Walkmesh,WokReader}.cs`, `src/Kotor.Core/TwoDa/TwoDaFile.cs`
- Créer : `src/Kotor.Core/Scene/{ModuleScene,SceneMesh,SceneWalkmesh,ModuleSceneBuilder}.cs`, `src/Kotor.Core/Export/ObjExporter.cs`
- Créer : `src/Kotor2Ja.Cli/Commands/ExportObjCommand.cs`
- Tester : `tests/Kotor.Core.Tests/Walkmesh/WokReaderTests.cs`, `TwoDa/TwoDaFileTests.cs`, `Scene/ModuleSceneBuilderTests.cs`, `Export/ObjExporterTests.cs`

**Interfaces :**
- Consomme :
  - `ResourceResolver` (02) ;
  - `ModuleCatalog.Load`, `ModuleInfo`, `TlkFile` (03) ;
  - `LayoutFile`, `TextureLookup`, `KotorTexture`, `TxiInfo` (04) ;
  - `MdlReader`, `KotorModel.Walk`, `NodeKind` (05) ;
  - `ImportLog`, `PngWriter` (01).
- Produit :
```csharp
public sealed record Walkmesh(Vector3[] Vertices, int[] Triangles, int[] Materials);   // un matériau par triangle
public static class WokReader { public static Walkmesh Read(byte[] data); }            // "BWM V1.0"
public sealed class TwoDaFile { public static TwoDaFile Read(byte[] data); public string? Get(int row, string column); public int RowCount { get; } } // "2DA V2.b"
public sealed record SceneMesh(string Room, string Node, string Texture, string? Lightmap, TxiInfo Txi, int TransparencyHint,
    Vector3[] Positions, Vector3[] Normals, Vector2[] Uv0, Vector2[]? Uv1, int[] Triangles);    // positions monde du module, mètres
public sealed record SceneWalkmesh(string Room, Vector3[] Positions, int[] Triangles, bool[] Walkable);
public sealed record ModuleScene(ModuleInfo Info, IReadOnlyList<SceneMesh> Meshes, IReadOnlyList<SceneWalkmesh> Walkmeshes,
    IReadOnlyDictionary<string, KotorTexture?> Textures /* clé = nom de texture ou de lightmap ; null = introuvable */);
public static class ModuleSceneBuilder { public static ModuleScene Build(ResourceResolver res, string module, TlkFile? tlk, ImportLog log); }
public static class ObjExporter { public static void Export(ModuleScene scene, string outDir); }   // <outDir>/<module>.obj/.mtl, textures/*.png
```
**Ce qui est fixé :**
- Pour chaque `LayoutRoom` : le monde du maillage est `ModelTransform(nœud) * Translation(room.Position)`.
- On garde les nœuds avec `Mesh != null && Render`. Normales absentes : calculées à partir des triangles. `Uv0` absent : zéros, avec le code `MDL_NO_UV`.
- **Journal obligatoire, avec des comptes par pièce** :

| Code | Cas |
|---|---|
| `ROOM_MODEL_MISSING` | MDL ou MDX introuvable (la pièce est sautée, l'export continue) |
| `MESH_NOT_RENDERED` | maillage avec `Render == false` (y compris `Aabb`) |
| `NODE_LIGHT`, `NODE_EMITTER`, `NODE_REFERENCE`, `NODE_CAMERA` | nœuds non convertis en phase 1 |
| `TEXTURE_MISSING` | texture ou lightmap introuvable (la clé reste dans `Textures` avec la valeur null) |
| `TEXTURE_ANIMATED` | `TxiInfo.IsAnimated` (exportée immobile) |
| `TEXTURE_ENVMAP` | `EnvMapTexture` non nul (reflet non converti) |
| `WALKMESH_MISSING` | WOK introuvable pour une pièce |

- **WOK** : `"BWM V1.0"`, `type` u32 @8 (1 = WOK de pièce). Les offsets des sommets, faces et matériaux sont dans l'en-tête (voir les références).
  - Les sommets d'un WOK de pièce sont peut-être déjà en coordonnées du module. **À vérifier** avec le test `Walkmesh_aligns_with_room` ci-dessous, puis décider s'il faut ajouter `room.Position` (le choix va dans le rapport).
  - `Walkable` : colonne `Walk` de `surfacemat.2da` (valeur `1`), lu via le résolveur.
- **OBJ de contrôle** : en mètres, Z vers le haut, un groupe par `Room/Node`, UV0 seulement, matériau = texture.
  - Les textures sont écrites en PNG. Une texture manquante donne un PNG magenta 8×8.
  - Le walkmesh est écrit dans `<module>_walkmesh.obj` séparé.
- `kotor2ja export-obj <module> --out <dir> [--kotor <dir>]` : écrit les fichiers, puis le journal `<dir>/<module>.journal.txt`, et affiche un résumé (pièces, maillages, triangles, textures manquantes, entrées par code).
  - Code de sortie 0 si au moins un triangle, 1 sinon, 2 si le module est inconnu.

- [ ] **Étape 1 : tests qui échouent**
  - `TwoDa_reads_synthetic` : un 2DA binaire construit en mémoire (2 colonnes, 2 lignes) donne `Get(1,"Walk") == "1"`.
  - `Missing_room_model_is_logged_and_skipped` : avec un résolveur sur un faux dossier contenant LYT, IFO et ARE, mais une pièce sans MDL, on obtient `ROOM_MODEL_MISSING` dans le journal, aucune exception, et une scène sans maillage de cette pièce.
  - `[SkippableFact] Real_tar_m02aa_scene` :
    - autant de pièces distinctes dans `Meshes` que dans le LYT, moins celles journalisées `ROOM_MODEL_MISSING` ;
    - au moins 90 % des maillages ont un `Lightmap` non nul ;
    - toutes les positions finies.
  - `[SkippableFact] Walkmesh_aligns_with_room` : pour `m02aa_01a`, le centre de la boîte du walkmesh retenu (après la décision ci-dessus) est à moins de 2 m (en XY) du centre de la boîte des maillages de la pièce.
  - `Obj_export_writes_files` : une scène synthétique de 1 triangle produit `.obj`, `.mtl` et `textures/x.png`. L'OBJ contient `f 1/1 2/2 3/3`.
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter.**
- [ ] **Étape 4 : `dotnet test Kotor2Ja.sln`** tout vert, puis `dotnet run -c Release --project src/Kotor2Ja.Cli -- export-obj tar_m02aa --out out/tar_m02aa`. Copier le résumé dans le rapport.
- [ ] **Étape 5 : rapport** `reports/step-06-impl.md`.
- [ ] **Étape 6 (orchestrateur, avec l'utilisateur) : contrôle visuel.** L'utilisateur ouvre `out/tar_m02aa/tar_m02aa.obj` dans Blender (axe Z vers le haut). Il vérifie trois choses : les pièces sont jointives (pas de trous entre elles), les textures sont à l'endroit, et rien de visible ne manque par rapport au souvenir du jeu. On ne passe à l'étape 08 qu'après son accord. L'étape 07 peut avancer en parallèle.
