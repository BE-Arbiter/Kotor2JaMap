# Étape 01 — Solution, dépôt git et copie depuis le projet TFU

**But :** une solution `Kotor2Ja` qui compile, avec les tests verts. Elle contient le compilateur BSP et les écritures JA repris du projet TFU, et rien de propre à TFU.

**Fichiers :**
- Créer : `Kotor2Ja.sln`, `.gitignore` (`bin/`, `obj/`, `out/`), `README.md` (court, en français), `THIRD-PARTY-NOTICES.md`.
- Créer : `src/Kotor.Core/Kotor.Core.csproj`, `src/Kotor.Compiler/Kotor.Compiler.csproj`, `src/Kotor2Ja.Cli/Kotor2Ja.Cli.csproj` (avec un `Program.cs` sans commande), et les trois projets de tests.
- Copier depuis `C:\Users\lambe\source\repos\SWGLFU-Map-importer\` en renommant les namespaces `Tfu.*` en `Kotor.*` :

| Source TFU | Destination |
|---|---|
| `src/Tfu.Compiler/**` (sauf `bin`, `obj`) | `src/Kotor.Compiler/` |
| `src/Tfu.Core/Geometry/Mesh.cs` | `src/Kotor.Core/Geometry/Mesh.cs` |
| `src/Tfu.Core/Diagnostics/ImportLog.cs` | `src/Kotor.Core/Diagnostics/` |
| `src/Tfu.Core/Export/{TgaWriter,TgaReader,PngWriter,ShaderWriter,ShaderNames,MaterialAlpha,Pk3Builder}.cs` | `src/Kotor.Core/Export/` |
| `tests/Tfu.Compiler.Tests/**` | `tests/Kotor.Compiler.Tests/` |
| tests TFU de ces fichiers (`TgaWriterTests`, `TgaReaderTests`, `ShaderWriterTests`, `MaterialAlphaTests`, `Pk3BuilderTests`), `Support/PngTestReader.cs` | `tests/Kotor.Core.Tests/` |

- **Ne pas copier** : `Archives`, `Gto`, `Layout`, `Levels`, `Scene`, `LevelExporter`, `ObjExporter`, `MapWriter`, `TextureConverter` (dépend de Pfim/DDS), `JaConfigLoader`, `Tfu.Pipeline`, `Tfu2Ja.App`, `tools`.
- Les dépendances du compilateur vers des fichiers non copiés (`JaExportOptions.DefaultSkyShader`, `MaterialProperty`, `Tfu.Core.Scene`, etc.) sont remplacées par le minimum nécessaire dans `Kotor.Core` :
  - une constante `Kotor.Core.Export.JaDefaults.SkyShader = "textures/skies/hevil"` ;
  - si `MaterialAlpha.From(props)` dépend de `MaterialProperty`, on retire cette surcharge et on garde le record et ses constantes.
- Les tests copiés qui testent du code non copié sont supprimés. La liste va dans le rapport.

**Interfaces :**
- Produit :
  - les namespaces `Kotor.Compiler`, `Kotor.Compiler.Bsp`, `Kotor.Core.Geometry` (`SubMesh`, `ModelMesh`), `Kotor.Core.Diagnostics` (`ImportLog`), `Kotor.Core.Export` (`TgaWriter`, `TgaReader`, `PngWriter`, `ShaderWriter`, `ShaderNames`, `MaterialAlpha`, `Pk3Builder`, `JaDefaults`) ;
  - `tests/Kotor.Core.Tests/TestEnv.cs` avec `public static string? KotorDir` (valeur de `KOTOR_PATH`, sinon le chemin Steam par défaut s'il existe, sinon null) et `public static string JaBaseDir`.

- [ ] **Étape 1 : `git init`** dans `C:\Users\lambe\source\repos\SWGLKotor-Map`, branche `master`. Ajouter le `.gitignore`.
- [ ] **Étape 2 : créer la solution et les projets** (net10.0, `Nullable` et `ImplicitUsings` activés, `TreatWarningsAsErrors` comme dans le projet TFU s'il l'utilise). Paquets de test : ceux des `.csproj` de test du projet TFU (xUnit, `Xunit.SkippableFact`, etc.).
- [ ] **Étape 3 : copier les fichiers** du tableau et renommer les namespaces.
- [ ] **Étape 4 : compiler** avec `dotnet build Kotor2Ja.sln`. Attendu : 0 erreur.
- [ ] **Étape 5 : tester** avec `dotnet test Kotor2Ja.sln`. Attendu : tous les tests copiés passent. Indiquer le nombre dans le rapport.
- [ ] **Étape 6 : `THIRD-PARTY-NOTICES.md`**. Il mentionne l'origine du compilateur (projet TFU, GPL-2.0, démarche q3map2) et précise qu'aucune bibliothèque d'images n'est utilisée.
- [ ] **Étape 7 : rapport** `reports/step-01-impl.md` : fichiers copiés, fichiers adaptés, tests supprimés et pourquoi.
