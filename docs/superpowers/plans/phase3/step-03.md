# Étape 03 — Objets placés → MD3

**But :** chaque objet placé (`Placeable List` du GIT) apparaît dans la carte JA. C'est un MD3 posé par `misc_model_static`, avec sa collision PWK.

**Fichiers :**
- Créer :
  - `src/Kotor.Core/Export/Md3Writer.cs`, en reprenant celui du projet TFU s'il existe (le chercher dans `C:\Users\lambe\source\repos\SWGLFU-Map-importer\`) ;
  - `src/Kotor.Core/Models/ModelMeshBuilder.cs` : MDL → maillages par texture dans le repère du modèle ;
  - `src/Kotor.Pipeline/Entities/PlaceableConverter.cs`, `src/Kotor.Pipeline/Entities/ConvertedEntities.cs`.
- Modifier :
  - `ModuleEntitiesReader` / `ModuleEntity` : ajouter le chemin vers le modèle KOTOR résolu (`string? Model`) pour les objets et les portes ;
  - `ModuleExporter` / `PipelineSteps` (`ImportStepResult` porte les entités converties ; l'étape Exporter écrit les MD3, les TGA et le shader de modèles, et ajoute les entités et la collision) ;
  - `CompileInput` : rien à changer pour la collision (`Solid`).
- Tester : `tests/Kotor.Core.Tests/Export/Md3WriterTests.cs`, `tests/Kotor.Pipeline.Tests/PlaceableConverterTests.cs`

**Interfaces produites :**
```csharp
namespace Kotor.Core.Models;
public sealed record ModelPart(string Texture, Vector3[] Positions, Vector3[] Normals, Vector2[] Uv /* convention GL */, int[] Indices);
public static class ModelMeshBuilder
{
    // Parcourt KotorModel.Walk(), applique les transformations, garde les maillages rendus et regroupe-les par texture (minuscules).
    public static IReadOnlyList<ModelPart> Build(KotorModel model, ImportLog log, string where);
}
namespace Kotor.Core.Export;
public static class Md3Writer
{
    // Une image, une surface par ModelPart, shader = shaderPrefix + texture. Les positions sont en unités JA.
    // Lève ArgumentException si une coordonnée dépasse ±511.98 (l'appelant réduit et compense par modelscale).
    public static byte[] Write(string name, IReadOnlyList<ModelPart> partsJaUnits, string shaderPrefix);
}
namespace Kotor.Pipeline.Entities;
public sealed record Md3Asset(string Path /* models/kotor/<modele>.md3 */, byte[] Data, IReadOnlyList<string> Textures);
public sealed record ConvertedEntities(IReadOnlyList<IReadOnlyDictionary<string, string>> Entities, IReadOnlyList<Md3Asset> Models,
    IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> ExtraSolid /* unités JA */);
public static class PlaceableConverter
{
    public static ConvertedEntities Convert(ResourceResolver res, KotorGame game, IReadOnlyList<ModuleEntity> entities, float scale, ImportLog log);
}
```

**Ce qui est fixé :**
- Modèle : UTP du template (`TemplateResRef` de l'entrée GIT), champ `Appearance`, puis `placeables.2da` colonne `modelname`. Les étapes manquantes (UTP introuvable, apparence hors table, `modelname` vide ou `****`, MDL introuvable ou illisible) sont journalisées avec `PLACEABLE_SKIPPED` et la raison, en anglais.
- Un seul MD3 par modèle, même pour plusieurs instances. Les textures du modèle sont ajoutées comme les autres (TGA dans `textures/<dossier>/`, via `TextureLookup`), et les shaders MD3 vont dans le même fichier de shaders de la carte : `models/<dossier>/<texture>` avec `{ map textures/<dossier>/<texture>.tga ; rgbGen lightingDiffuse }`. Une texture avec un vrai alpha reçoit `alphaFunc GE128` et `cull none`.
- Échelle : les positions sont multipliées par `scale`. Si `max(|coordonnée|) > 511`, les sommets sont divisés par `k = ceil(max / 511)` et l'entité reçoit `"modelscale" "k"`.
- Entité : `{"classname":"misc_model_static", "model":"models/kotor/<m>.md3", "origin":"x y z", "angles":"0 <yaw + 90> 0"}`. **Le +90 est à vérifier** : comparer l'orientation d'un objet asymétrique dans la vue Prévisualiser (modèle de pièce) et la formule, puis consigner la preuve. Le yaw est normalisé dans [0, 360).
- Vérifier dans `OpenJK-SWGL/code/cgame/cg_main.cpp` (`CG_CreateMiscEntFromGent`) les clés réellement lues (`angles` ou `angle`, `modelscale`) et citer les lignes.
- Collision : PWK du modèle (`<modelname>.pwk`, lue avec `WokReader` ou équivalent), transformée par la position et le yaw de l'objet, puis mise à l'échelle et ajoutée à `ExtraSolid`. Elle rejoint ensuite la collision solide de `CompileInput`. Sans PWK : `PLACEABLE_NO_PWK`.
- Bilan au journal : `PLACEABLES` (convertis, sautés, MD3 uniques).

- [ ] **Étape 1 : tests qui échouent**
  - `Md3_header_and_counts` : 2 parts. On vérifie l'en-tête `IDP3`, la version 15, 1 image, 2 surfaces, les nombres de sommets et de triangles, le nom du shader de chaque surface, et une position relue = valeur × 64 arrondie.
  - `Md3_rejects_out_of_range` : une coordonnée de 600 lève `ArgumentException`.
  - `Large_model_gets_modelscale` : un modèle synthétique de 30 m à l'échelle 40 (1200 unités) donne `modelscale 3`. Les sommets écrits restent dans ±511.
  - `Shared_model_written_once` : 3 objets avec le même modèle donnent 1 `Md3Asset` et 3 entités.
  - `Missing_template_is_logged` : `PLACEABLE_SKIPPED`, aucune exception.
  - `[SkippableFact] Real_tar_m02aa_placeables` : au moins 10 entités `misc_model_static` ; chaque `model` existe dans `Models` ; chaque origine est dans la boîte de la scène élargie de 200 unités. Écrire la liste dans `out/placeables-tar_m02aa.txt`.
  - `[SkippableFact] Real_tar_m02aa_pk3_contains_md3` : un export vers un dossier temporaire donne un pk3 qui contient les `models/kotor/*.md3`, toutes les TGA référencées par leurs shaders, et les entités dans le lump du BSP.
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter.**
- [ ] **Étape 4 : `dotnet build` / `dotnet test` verts.**
- [ ] **Étape 5 : rapport** `reports/step-03-impl.md` : origine de l'écrivain MD3 (copié ou nouveau), clés vérifiées dans cgame, preuve de l'orientation, bilan pour tar_m02aa.
