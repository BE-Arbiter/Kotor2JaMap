# Étape 01 — Bibliothèque `Kotor.Pipeline` : étapes séparées et relecture du BSP

**But :** les quatre étapes (Prévisualiser, Importer, Exporter, Créer PK3) deviennent des appels séparés, utilisés à la fois par la CLI et, plus tard, par l'application. Le `.bsp` compilé peut être relu.

**Fichiers :**
- Créer : `src/Kotor.Pipeline/Kotor.Pipeline.csproj` (références : `Kotor.Core`, `Kotor.Compiler`), `src/Kotor.Pipeline/PipelineSteps.cs`, `src/Kotor.Pipeline/StepResults.cs`
- Déplacer : `src/Kotor.Compiler/Export/ModuleExporter.cs` → `src/Kotor.Pipeline/ModuleExporter.cs`, namespace `Kotor.Pipeline`. `Export(...)` devient l'enchaînement des étapes.
- Créer : `src/Kotor.Compiler/Bsp/BspReader.cs`
- Modifier : `src/Kotor2Ja.Cli` (référence `Kotor.Pipeline`), `Kotor2Ja.sln`, `tests/*` (références et namespaces)
- Tester : `tests/Kotor.Pipeline.Tests/` (nouveau projet) `PipelineStepsTests.cs`, `tests/Kotor.Compiler.Tests/BspReaderTests.cs`

**Interfaces :**
- Produit :
```csharp
namespace Kotor.Pipeline;
public sealed record ImportStepResult(ModuleScene Scene, ExportPlan Plan, string MapName);
public sealed record ExportStepResult(string BspPath, BspFile Bsp, string StagingDir);
public sealed record Pk3StepResult(string Pk3Path);
public static class PipelineSteps
{
    public static ModuleScene Preview(string module, ExportOptions opt, ImportLog log);   // résolveur + TLK + ModuleSceneBuilder.Build
    public static ImportStepResult Import(ModuleScene scene, string? mapName, ExportOptions opt, ImportLog log); // = BuildPlan, rien n'est écrit
    public static ExportStepResult Export(ImportStepResult imported, ExportOptions opt, ImportLog log);         // TGA + shader dans le staging, compile le .bsp, le relit
    public static Pk3StepResult CreatePk3(ExportStepResult exported, ImportStepResult imported, ExportOptions opt, ImportLog log); // pk3 dans JaBaseDir
}
// ModuleExporter.Export(module, mapName, opt, log) et Export(scene, ...) gardent leur signature et leur ExportResult :
// ils appellent Preview/Import/Export/CreatePk3 à la suite.

namespace Kotor.Compiler.Bsp;
public sealed record BspSurface(string Shader, int SurfaceType, Vector3[] Positions, Vector3[] Normals, Vector2[] Uv0, Vector2[] Uv1,
    byte[] Colors /* RGBA, 4 octets par sommet, couleur [0] */, int[] Indices);
public sealed record BspBrushInfo(int BrushCount, int BrushSideCount, int PlaneCount);
public sealed record BspFile(IReadOnlyList<string> Shaders, IReadOnlyList<BspSurface> Surfaces, string Entities, BspBrushInfo Collision);
public static class BspReader { public static BspFile Read(byte[] data); }  // "RBSP" version 1, 18 lumps (comme BspWriter)
```
- En cas d'échec d'une étape (limite, E/S, pk3 verrouillé), on lève une `PipelineStepException(string code, string message)` (à créer dans `Kotor.Pipeline`). Le code est déjà écrit au journal. `ModuleExporter.Export` la convertit en `ExportResult` en échec, comme aujourd'hui.

**Ce qui est fixé :**
- Le contenu et l'emplacement des fichiers produits ne changent pas par rapport à la phase 1 (staging `<WorkDir>\<carte>\pk3\`, `shaders/kotor_<carte>.shader`, `textures/kotor/…`, pk3 `<JaBaseDir>\kotor_<carte>.pk3`, journal).
- `BspReader` lit tout ce que `BspWriter` écrit pour les lumps entités, shaders, sommets, index, surfaces, brushes, côtés et plans. Un en-tête inconnu, une version différente de 1, un lump hors fichier ou une surface dont les index sortent de ses sommets lèvent une `InvalidDataException` explicite.
- La sortie de compilation est **déterministe** : deux exports successifs du même module avec les mêmes options donnent des `.bsp` identiques octet pour octet. Si ce n'est pas le cas aujourd'hui (ordre de dictionnaire, horodatage…), il faut le corriger.

- [ ] **Étape 1 : tests qui échouent**
  - `BspReader_roundtrips_compiled_map` : compiler en mémoire un petit `CompileInput` (2 surfaces, 2 shaders, un sol), puis relire. On retrouve les noms de shaders, le nombre de surfaces, les positions, les UV0/UV1 et les index des surfaces. Le lump d'entités contient `info_player_start`.
  - `BspReader_rejects_bad_header` et `BspReader_rejects_out_of_range_lump`.
  - `Export_is_deterministic` : deux compilations du même `CompileInput` donnent les mêmes octets.
  - `Steps_chain_equals_ModuleExporter` (`[SkippableFact]`, KOTOR réel, `JaBaseDir` et `WorkDir` temporaires) : `ModuleExporter.Export("tar_m02aa", …)` et l'enchaînement manuel `Preview → Import → Export → CreatePk3` donnent deux pk3 avec la même liste d'entrées et le même `.bsp` octet pour octet.
  - `CreatePk3_locked_target_fails_cleanly` : le pk3 cible est ouvert en écriture exclusive. On obtient une `PipelineStepException` de code `EXPORT_IO_ERROR`, et le fichier existant n'est pas modifié.
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter** (déplacement, découpage, `BspReader`, CLI).
- [ ] **Étape 4 : `dotnet test Kotor2Ja.sln`** tout vert, sans avertissement. `kotor2ja export-ja tar_m02aa --config <config temporaire vers un JaBaseDir temporaire>` fonctionne encore.
- [ ] **Étape 5 : rapport** `reports/step-01-impl.md`.
