# Étape 01 — Rendu : vert, découpe abusive et lumière incohérente

**But :** supprimer le vert des lightmaps, ne plus découper les textures dont l'alpha est un masque de reflet, et éclairer les surfaces sans lightmap avec les lumières KOTOR. La spec §1 décrit le diagnostic.

**Fichiers :**
- Créer : `src/Kotor.Core/Textures/LightmapRepair.cs`, `src/Kotor.Core/Scene/SceneLight.cs`
- Modifier :
  - `src/Kotor.Core/Textures/TpcDecoder.cs` et `TextureLookup.cs` (vrai `HasAlpha`, réparation des lightmaps) ;
  - `src/Kotor.Core/Export/ShaderPlanner.cs` (alpha de reflet, `nopicmip`/`nomipmaps`) ;
  - `src/Kotor.Core/Scene/ModuleSceneBuilder.cs` et `ModuleScene` (lumières des pièces, ambiante ARE) ;
  - `src/Kotor.Compiler/Bsp/VertexLighting.cs` et `CompileInput.cs` (lumières ponctuelles) ;
  - `src/Kotor.Pipeline/ModuleExporter.cs` (transmission des lumières à l'échelle) ;
  - `src/Kotor.Pipeline/Preview/PreviewBuilder.cs`, si nécessaire, pour que la vue reste fidèle.
- Tester : `tests/Kotor.Core.Tests/Textures/LightmapRepairTests.cs`, `tests/Kotor.Core.Tests/Export/ShaderPlannerTests.cs` (ajouts), `tests/Kotor.Compiler.Tests/VertexLightingTests.cs` (ajouts)

**Interfaces produites :**
```csharp
namespace Kotor.Core.Textures;
public static class LightmapRepair
{
    /// Remplace les texels alpha != 255 par le texel valide le plus proche (dilatation 8-voisins, itérée) ; alpha = 255 partout.
    /// Aucun texel valide : tout à (128,128,128,255). Renvoie le nombre de texels réparés et si l'image n'avait aucun texel valide.
    public static (int Repaired, bool NoValidTexel) Repair(byte[] rgba, int width, int height);
}
namespace Kotor.Core.Scene;
public sealed record SceneLight(string Name, Vector3 Position /* mètres, repère du module */, Vector3 Color /* 0..1 */, float Radius /* mètres */, float Multiplier);
// ModuleScene gagne : IReadOnlyList<SceneLight> Lights, Vector3 AmbientColor (0..1)
namespace Kotor.Compiler.Bsp;
public sealed record PointLight(Vector3 Position /* unités JA */, Vector3 Color, float Radius /* unités JA */, float Multiplier);
// VertexLighting gagne un mode « KOTOR » : ambiante + somme des PointLight (atténuation linéaire 1 - d/rayon, n·l pour les faces simples, |n·l| en double face).
// Evaluate prend maintenant la position du sommet. CompileInput porte les lumières (liste vide = comportement actuel, soleil DAG_1).
```

**Ce qui est fixé :**
- Lightmaps : la réparation se fait dans `TextureLookup.Find` pour `TextureKind.Lightmap`. `ModuleSceneBuilder` journalise `LIGHTMAP_REPAIRED` (une ligne de bilan par module : nombre de lightmaps et de texels) et `LIGHTMAP_NO_VALID_TEXEL` (une ligne par lightmap). Les messages sont en anglais.
- Shaders `Lightmapped` et `LightmappedCutout` : ajouter `nopicmip` et `nomipmaps` au niveau du shader. **Vérifier dans `C:\Users\lambe\source\repos\OpenJK-SWGL\code\rd-vulkan` (ou le renderer réellement chargé, `rdsp-vulkan`) que ces mots-clés sont acceptés au niveau du shader**, et citer la ligne dans le rapport.
- `ShaderPlanner.Plan` : si `mesh.Txi.EnvMapTexture != null`, l'alpha de la texture est ignoré (ni `Blend` ni `Cutout`, sauf `blending punchthrough` explicite ou TXI `decal 1`). `TpcDecoder` met `HasAlpha` à vrai seulement si un pixel a alpha < 255 (DXT1, DXT5 et non compressé).
- Éclairage :
  - lumières = nœuds `Light` des modèles de pièce (position monde = transformation du nœud + position de la pièce, comme dans `ModuleEntitiesReader`) ;
  - couleur, rayon et multiplicateur sont lus dans le nœud lumière du MDL. **Les décalages du nœud lumière sont à vérifier** (xoreos `model_kotor.cpp`, kotorblender) et à citer dans le rapport. Si la lecture est impossible : rayon 5 m, couleur blanche, multiplicateur 1, avec le code `LIGHT_DEFAULTS` ;
  - ambiante = ARE `DynAmbientColor`, sinon `SunAmbientColor` (entier BGR 0x00BBGGRR, à vérifier), sinon (0.2, 0.2, 0.2).
- **La zone blanche de Taris** (tar_m02aa, depuis l'apparition `3678 5309 25` en regardant vers -45°, on voit à gauche un mur blanc rosé) : identifier le shader et la cause par sondage des données (pas en lançant le jeu), corriger la cause, puis consigner la preuve dans le rapport. Si elle vient du faux soleil, le nouvel éclairage la règle.
- La vue Prévisualiser/Importer/Exporter doit montrer les lightmaps réparées (même `TextureLookup`).

- [ ] **Étape 1 : tests qui échouent**
  - `Repair_fills_invalid_from_nearest` : image 4×1 avec alpha [255, 0, 0, 255] et couleurs A, x, y, B. Résultat : [A, A, B, B], alpha 255 partout, `Repaired == 2`.
  - `Repair_all_invalid_gives_gray` : 4×4, alpha 1 partout. Résultat : (128,128,128,255) partout, `NoValidTexel == true`.
  - `Envmap_alpha_is_not_cutout` : maillage avec lightmap, texture `HasAlpha`, TXI `envmaptexture cm_x`. Résultat : `ShaderKind.Lightmapped`.
  - `Punchthrough_still_cutout` : comme ci-dessus, mais avec `blending punchthrough`. Résultat : `LightmappedCutout`.
  - `Lightmapped_shader_has_nopicmip` : le texte écrit contient `nopicmip` et `nomipmaps`.
  - `Dxt5_opaque_has_no_alpha` : un DXT5 d'un bloc d'alpha 255 donne `HasAlpha == false`.
  - `Point_light_attenuates` : une lumière blanche de rayon 100 en (0,0,0). Un sommet à 50 unités, normale tournée vers la lumière, reçoit la moitié de la lumière par rapport à 0 unité, et rien au-delà de 100 (hors ambiante).
  - `[SkippableFact] Real_ebo_m41aa_no_invalid_lightmap_texel` : après `ModuleSceneBuilder.Build`, toutes les lightmaps de la scène ont alpha 255 partout.
  - `[SkippableFact] Real_tar_m02aa_lights_read` : 71 lumières, rayons > 0, couleurs dans [0,1].
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter.**
- [ ] **Étape 4 : `dotnet build` et `dotnet test`** sur `Kotor2Ja.sln`. Exporter ensuite `tar_m02aa` et `ebo_m41aa` avec la CLI vers un `JaBaseDir` **temporaire** (config temporaire). Dans le rapport :
  - nombre de shaders par type, avant et après ;
  - vérification qu'aucune TGA de `textures/kotor/lm/` du staging n'a d'alpha ≠ 255.
- [ ] **Étape 5 : rapport** `reports/step-01-impl.md` : cause de la zone blanche avec la preuve, décalages du nœud lumière, mots-clés vérifiés dans le renderer.
