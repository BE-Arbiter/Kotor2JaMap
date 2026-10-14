# Étape 05 — Lecteur de modèles MDL/MDX

**But :** lire un modèle binaire KOTOR 1 PC : la hiérarchie des nœuds, la transformation de chaque nœud et, pour les maillages, les sommets, les normales, les deux jeux d'UV (diffuse et lightmap), les triangles et les noms de textures.

**Fichiers :**
- Créer : `src/Kotor.Core/Mdl/{KotorModel,KotorNode,KotorMesh,NodeKind,MdlReader}.cs`
- Tester : `tests/Kotor.Core.Tests/Mdl/MdlReaderTests.cs`

**Interfaces :**
- Consomme : `ResourceResolver` (02), uniquement dans les tests d'intégration.
- Produit :
```csharp
[Flags] public enum NodeKind : ushort { Header = 0x001, Light = 0x002, Emitter = 0x004, Camera = 0x008, Reference = 0x010,
    Mesh = 0x020, Skin = 0x040, Anim = 0x080, Dangly = 0x100, Aabb = 0x200, Saber = 0x800 }
public sealed record KotorMesh(string Texture0, string? Texture1, bool Render, bool HasLightmap, int TransparencyHint,
    Vector3[] Positions, Vector3[]? Normals, Vector2[]? Uv0, Vector2[]? Uv1, int[] Triangles);   // Texture* en minuscules, null si "NULL" ou vide
public sealed class KotorNode
{
    public string Name { get; }
    public NodeKind Kind { get; }
    public Vector3 Position { get; }          // relatif au parent (1re image clé du contrôleur position, sinon en-tête)
    public Quaternion Orientation { get; }    // idem, contrôleur orientation (20), compressé ou non
    public KotorMesh? Mesh { get; }           // non nul si Kind contient Mesh (y compris Skin, Dangly, Aabb)
    public IReadOnlyList<KotorNode> Children { get; }
}
public sealed record KotorModel(string Name, string? SuperModel, KotorNode Root, Vector3 BBoxMin, Vector3 BBoxMax)
{
    public IEnumerable<(KotorNode Node, Matrix4x4 ModelTransform)> Walk(); // parcours en profondeur, transform cumulée racine -> nœud
}
public static class MdlReader { public static KotorModel Read(byte[] mdl, byte[] mdx); }
```
**Ce qui est fixé :**
- Format KOTOR 1 PC uniquement. Les modèles KOTOR 2 (pointeurs de fonctions différents dans l'en-tête de géométrie) lèvent une `NotSupportedException` avec un message clair.
- Fichier : en-tête de 12 octets (`0`, `mdlDataSize`, `mdxDataSize`). **Tous les offsets internes sont relatifs à l'octet 12.**
- Positions : dans le MDL. UV, normales et UV de lightmap : dans le MDX, selon la taille de sommet MDX et les offsets de l'en-tête trimesh (`-1` = absent). Les tailles exactes viennent des références du `context.md`. On ne devine pas.
- Contrôleurs : on ne lit que la **première image clé** de position (type 8) et d'orientation (type 20).
  - Orientation : 4 colonnes x,y,z,w, ou 2 colonnes = quaternion compressé sur 32 bits (xoreos `model_kotor.cpp` documente le décodage).
  - Les autres contrôleurs (couleur, échelle, alpha…) sont ignorés : c'est le figé de la spec §6.
- Les nœuds `Skin` et `Dangly` sont lus comme des maillages ordinaires (géométrie au repos).
- `Aabb` : maillage lu, avec `Render` tel quel. L'étape 06 décide de son usage.
- Les données inattendues (offset hors du fichier, nombre négatif) lèvent une `InvalidDataException` qui nomme le modèle et le nœud. Pas de lecture hors limites.

- [ ] **Étape 1 : tests qui échouent** (intégration, en `[SkippableFact]`, car fabriquer un MDL synthétique complet coûte plus cher que ce qu'il apporte) :
  - `Real_room_model_reads` : `m02aa_01a` donne :
    - au moins un nœud `Mesh` avec `Render` et `Texture1` commençant par `"m02aa_01a_lm"` ;
    - pour chaque maillage, `Triangles.Length % 3 == 0`, tous les indices dans `[0, Positions.Length)`, et `Uv1.Length == Positions.Length` si `HasLightmap` ;
    - toutes les positions finies.
  - `Real_room_bbox_is_plausible` : après `Walk()`, la boîte des sommets transformés de `m02aa_01a` fait entre 1 et 500 mètres sur chaque axe horizontal.
  - `Real_all_tar_m02aa_rooms_read` : chaque pièce de `m02aa.lyt` se lit sans exception. Le test écrit pour chaque pièce le nombre de nœuds par `NodeKind` et le nombre de triangles dans `out/mdl-stats.txt`.
  - `Real_compressed_orientation_model` : un modèle de personnage (`p_bastilabb` ou un autre modèle humain) se lit. Ses quaternions sont normalisés (norme dans 1 ± 1e-3). La hauteur de sa boîte englobante (Z) est écrite dans `out/mdl-stats.txt` : **c'est la mesure qui justifie l'échelle par défaut**.
  - Test unitaire `Compressed_quaternion_decodes_identity` : la valeur compressée de l'identité (calculée d'après la formule xoreos) redonne (0,0,0,1).
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter `MdlReader`.**
- [ ] **Étape 4 : `dotnet test Kotor2Ja.sln`** tout vert. Recopier `out/mdl-stats.txt` dans le rapport, avec en particulier la hauteur du modèle humain.
- [ ] **Étape 5 : rapport** `reports/step-05-impl.md`. Il liste les tailles et offsets retenus et la référence de chacun.
