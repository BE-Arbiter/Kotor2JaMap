# Étape 01 : rapport d'implémentation

**Statut : DONE_WITH_CONCERNS.** La solution compile (0 erreur, 0 avertissement) et `dotnet test Kotor2Ja.sln` est vert (exit 0). Rien n'est commité.

## Ce qui a été fait

- Sous-étape 1 (`git init`) sautée comme demandé : dépôt, branche `phase1-module-ja` et `.gitignore` existants (`bin/`, `obj/`, `out/`, `.superpowers/` déjà présents, `.gitignore` non modifié).
- Solution `Kotor2Ja.sln` (format `.sln`, comme le projet TFU) et six projets :
  - `src/Kotor.Core/Kotor.Core.csproj` (net10.0, `Nullable`, `ImplicitUsings`, sans paquet) ;
  - `src/Kotor.Compiler/Kotor.Compiler.csproj` (référence `Kotor.Core`) ;
  - `src/Kotor2Ja.Cli/Kotor2Ja.Cli.csproj` (Exe, référence `Kotor.Core` et `Kotor.Compiler`) avec `Program.cs` sans commande (affiche un message et renvoie 0) ;
  - `tests/Kotor.Core.Tests/`, `tests/Kotor.Compiler.Tests/` et `tests/Kotor2Ja.Cli.Tests/` (xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, coverlet, Xunit.SkippableFact pour les deux premiers, repris des `.csproj` TFU).
- `TreatWarningsAsErrors` non activé : le projet TFU ne l'utilise pas.
- Namespaces `Tfu.*` renommés en `Kotor.*` dans tous les fichiers copiés (`sed` sur `\bTfu\.`, octets préservés).
- `README.md` (court, en français) et `THIRD-PARTY-NOTICES.md` (origine GPL-2.0 du compilateur, démarche q3map2, aucune bibliothèque d'images : pas de Pfim).

## Fichiers copiés depuis le projet TFU (lecture seule, source non modifiée)

Compilateur (`src/Kotor.Compiler/`) :
- `Compiler.cs`
- `Bsp/BrushBuilder.cs`, `Bsp/BspTree.cs`, `Bsp/BspWriter.cs`, `Bsp/GlobalFog.cs`, `Bsp/LimitCheck.cs`, `Bsp/Limits.cs`, `Bsp/RenderSurfaces.cs`, `Bsp/SkyBox.cs`, `Bsp/VertexLighting.cs`, `Bsp/WalkableCollision.cs`
- `Kotor.Compiler.csproj` (depuis `Tfu.Compiler.csproj`)

Cœur (`src/Kotor.Core/`) :
- `Geometry/Mesh.cs`
- `Diagnostics/ImportLog.cs`
- `Export/TgaWriter.cs`, `Export/TgaReader.cs`, `Export/PngWriter.cs`, `Export/ShaderWriter.cs`, `Export/ShaderNames.cs`, `Export/MaterialAlpha.cs`, `Export/Pk3Builder.cs`

Tests compilateur (`tests/Kotor.Compiler.Tests/`) :
- `BspTreeTests.cs`, `BspWriterTests.cs`, `CompilerTests.cs`, `GlobalFogTests.cs`, `LimitCheckTests.cs`, `RenderSurfacesTests.cs`, `SkyBoxTests.cs`, `VertexLightingTests.cs`, `WalkableCollisionTests.cs`
- `Kotor.Compiler.Tests.csproj` (depuis `Tfu.Compiler.Tests.csproj`)

Tests cœur (`tests/Kotor.Core.Tests/`) :
- `TgaWriterTests.cs`, `TgaReaderTests.cs`, `ShaderWriterTests.cs`, `MaterialAlphaTests.cs`, `Pk3BuilderTests.cs`
- `Support/PngTestReader.cs`
- `Kotor.Core.Tests.csproj` (depuis `Tfu.Core.Tests.csproj`)

## Fichiers ajoutés ou adaptés hors liste (dépendances minimales)

Dépendances du compilateur vers du code non copié, traitées ainsi :

1. **`Geometry/MeshSimplifier.cs` copié (non prévu par l'étape).** `WalkableCollision.cs` l'appelle (`MeshSimplifier.Reduce`). Fichier autonome (seulement `System.Numerics`, `Mesh.cs`). Copié tel quel, namespace renommé.
2. **`Geometry/TriangleBudget.cs` copié (non prévu par l'étape).** Dépendance de `MeshSimplifier`. Fichier autonome. Sa doc cite `InstancePruner` (non copié) dans un `<see cref>`, sans effet sur la compilation.
3. **`Export/JaDefaults.cs` créé.** Remplace `JaExportOptions` (non copié). Contient `SkyShader = "textures/skies/hevil"` (demandé) et, pour `VertexLighting.Default`, `SunDir`, `Sun = 0.75`, `Ambient = 0.45`, `SunColor`, `AmbientColor`, avec les valeurs exactes de `JaExportOptions` TFU. Ces cinq constantes vont au-delà de la seule `SkyShader` prévue, car `VertexLighting` (copié) en dépend.
4. **`Compiler.cs`** : `DefaultSkyShader` pointe sur `Kotor.Core.Export.JaDefaults.SkyShader` au lieu de `JaExportOptions.DefaultSkyShader`.
5. **`Bsp/VertexLighting.cs`** : références `JaExportOptions.Default*` remplacées par `JaDefaults.*` (même valeurs, même logique).
6. **`Export/MaterialAlpha.cs`** : la surcharge `From(IReadOnlyList<MaterialProperty>)`, son helper `BlendName` et la table `BlendNames` sont retirés (dépendent de `MaterialProperty`, non copié). Le record, `AlphaKind` et `DefaultBlend` sont conservés, comme demandé.
7. **`tests/Kotor.Core.Tests/TestEnv.cs` créé** : `public static class TestEnv` avec `KotorDir` (`KOTOR_PATH`, sinon le chemin Steam par défaut s'il existe, sinon null) et `JaBaseDir` (`...\Jedi Academy - SWGL_DEV\GameData\base`).
8. **`src/Kotor2Ja.Cli/Program.cs` créé** : stub sans commande.

## Fichiers non copiés

- **`Bsp/SpawnPlacer.cs` et `tests/Kotor.Compiler.Tests/SpawnPlacerTests.cs` non copiés.** Ils dépendent de `Tfu.Core.Scene` (`LevelScene`, `SceneInstance`) et de `MapWriter` (`ToQuake`, `ComputeBounds`, `FallbackSpawnHeight`), tous deux exclus par l'étape. Aucun autre fichier copié ne référence `SpawnPlacer` (vérifié par recherche). À reprendre avec la scène, si nécessaire.
- Tests TFU non listés, non copiés : `MeshSimplifierTests.cs`, `InstancePrunerTests.cs`, `MaterialTable`, etc. (aucun de ces tests n'est dans la liste de l'étape).
- Fichiers exclus par l'étape (`Archives`, `Gto`, `Layout`, `Levels`, `Scene`, `LevelExporter`, `ObjExporter`, `MapWriter`, `TextureConverter`, `JaConfigLoader`, `Tfu.Pipeline`, `Tfu2Ja.App`, `tools`) : non copiés.

## Tests supprimés et raison

Tests copiés qui testent du code non copié, retirés :

| Fichier | Tests retirés | Raison |
|---|---|---|
| `Kotor.Core.Tests/MaterialAlphaTests.cs` | `No_alpha_property_is_opaque...`, `Alpha_flag_means_blend...` (3 cas `InlineData`), `Src_and_dest_blend_enums...`, `Alpha_test_means_cutout...`, `False_flags_are_opaque`, `Material_table_lists_every_exported_material...` | Appellent `MaterialAlpha.From` (retiré), `MaterialTable`, `LevelScene`, `SceneMaterial` (non copiés) |
| `Kotor.Core.Tests/MaterialAlphaTests.cs` | classe `OpaqueTgaTests` (2 tests) | Appelle `TextureConverter` et `DdsTestWriter` (non copiés) |
| `Kotor.Core.Tests/TgaReaderTests.cs` | `Reads_missing_texture_placeholder` | Appelle `TextureConverter.WriteMissingTga` (non copié) |
| `Kotor.Compiler.Tests/SpawnPlacerTests.cs` | fichier entier (2 tests) | Voir `SpawnPlacer` ci-dessus |

Restent dans `MaterialAlphaTests.cs` : `Blend_shader_has_blendFunc_and_no_alphaFunc_nor_depthWrite` et `Opaque_alpha_object_matches_legacy_opaque_shader` (qui n'utilisent que `ShaderWriter` et le record `MaterialAlpha`).

`PngTestReader.cs` est copié comme demandé mais n'est plus utilisé par aucun test copié (ses utilisateurs TFU, `PngWriterTests`, `ObjExporterTests` et `TextureConverterTests`, ne sont pas copiés).

## Commande de test et résultat

Commande : `dotnet test Kotor2Ja.sln` (dans `C:\Users\lambe\source\repos\SWGLKotor-Map`), après `dotnet build Kotor2Ja.sln` sans erreur.

- `Kotor.Core.Tests` : 20 réussis, 0 échec, 0 ignoré.
- `Kotor.Compiler.Tests` : 39 réussis, 0 échec, 0 ignoré.
- `Kotor2Ja.Cli.Tests` : aucun test (avertissement VSTest « Aucun test n'est disponible »), le processus renvoie 0.
- Résultat global : exit 0.

## Points d'attention

1. **Naming TFU conservé dans les sorties.** Les chaînes `textures/tfu2ja/`, le dossier `..\tfu2ja` par défaut de `Compiler.Compile`, les clés `_tfu_sky`, `_tfu_fog`, `_tfu_sun_dir`, `_tfu_sun`, `_tfu_ambient`, le préfixe `map textures/tfu2ja/` de `Pk3Builder` et le nom `tfu2ja` dans les commentaires sont repris tels quels, car les tests et le format attendent ces valeurs. Ils ne sont pas dans la liste des renommages de l'étape (qui ne touche que les namespaces). À trancher avant l'étape 08 (export JA) : soit on les garde, soit on les renomme partout avec les tests.
2. **Dépendances supplémentaires** (`MeshSimplifier.cs`, `TriangleBudget.cs`, cinq constantes dans `JaDefaults`) : écarts par rapport à la liste de l'étape, nécessaires à la compilation sans changer de comportement. À valider.
3. **`SpawnPlacer` non porté** : le compilateur ne l'appelle pas aujourd'hui, mais sa fonction (point d'apparition) sera à réintroduire avec la scène.
4. **`System.CommandLine` non ajouté** au projet CLI : le stub n'en a pas besoin ; à ajouter avec la première commande.
5. **`Kotor2Ja.Cli.Tests` vide** : le projet existe mais ne contient aucun test pour l'instant.
6. Le fichier `THIRD-PARTY-NOTICES.md` de TFU est en mojibake (double encodage UTF-8). Le nouveau fichier est écrit en UTF-8 propre.
