# Etape 01 - Rapport d'implementation

Statut : TERMINE. `dotnet test Kotor2Ja.sln` : 172 tests verts (Core 99, Compiler 65, Pipeline 3, Cli 5), 0 avertissement, aucun test ignore (KOTOR present). Rien n'est commite.

## Ce qui a ete construit

- Nouveau projet `src/Kotor.Pipeline` (references Core + Compiler), ajoute a `Kotor2Ja.sln` (dossier `src`).
- `PipelineSteps` : `Preview`, `Import`, `Export`, `CreatePk3`, avec les signatures du plan.
  - `Preview` : resolveur + TLK + `ModuleSceneBuilder.Build`. Echec E/S : journal `MODULE_UNREADABLE`, `PipelineStepException("MODULE_UNREADABLE", "Module illisible : ...")`.
  - `Import` = `BuildPlan`, n'ecrit rien (test : `WorkDir` n'existe pas apres). Options invalides : `OPTIONS_INVALID`.
  - `Export` : limite de shaders (`SHADER_LIMIT`), staging, compilation (echec : `BSP_ERROR`, deja journalise par le compilateur), shader, TGA, puis relecture du `.bsp` par `BspReader` (echec : `BSP_READBACK_FAILED`). Toute E/S en echec : `EXPORT_IO_ERROR`.
  - `CreatePk3` : `Pk3Builder.Build` vers `<JaBaseDir>\kotor_<carte>.pk3` ; E/S en echec : `EXPORT_IO_ERROR`.
- `StepResults.cs` : `ImportStepResult`, `ExportStepResult`, `Pk3StepResult`, `PipelineStepException(Code)`.
- `ModuleExporter` deplace par `git mv` vers `src/Kotor.Pipeline/ModuleExporter.cs`, namespace `Kotor.Pipeline` (`ExportResult`, `ExportPlan`, `ExportTexture`, `BuildPlan` inchanges). Les deux `Export(...)` gardent leur signature et leur `ExportResult` : ils enchainent les etapes et convertissent `PipelineStepException` en resultat en echec (meme message qu'avant : message de l'exception E/S, "Module illisible : ..." etc.). Journal ecrit comme avant. `Checker`/`Brighten` passes en `internal` pour `PipelineSteps`.
- `src/Kotor.Compiler/Bsp/BspReader.cs` : `BspSurface`, `BspBrushInfo`, `BspFile`, `BspReader.Read(byte[])` (entites, shaders, sommets, index, surfaces, nombres de brushes/cotes/plans). `InvalidDataException` explicite pour : fichier trop court, en-tete different de "RBSP", version differente de 1, lump hors fichier, longueur de lump non multiple de la taille d'enregistrement, shader de surface inexistant, plage de sommets/index hors lump, index hors des sommets de la surface.
- Mise a jour des references : CLI (`using Kotor.Pipeline` + reference projet), `Kotor.Core.Tests` et `Kotor2Ja.Cli.Tests` (reference projet + using). `ModuleExporterTests` reste dans `Kotor.Core.Tests` (juste namespace d'import change).

## Preuve TDD

- RED : tests ecrits avant le code ; `dotnet build` : erreurs CS0103/CS0246 (`BspReader`, `BspFile`, `BspSurface`, `PipelineSteps`, `ImportStepResult`, `ExportStepResult`, `Pk3StepResult`, `PipelineStepException` introuvables).
- GREEN apres implementation. Une premiere execution a eu 1 echec : mon test `BspReader_roundtrips_compiled_map` comparait positions/index dans l'ordre alors que le compilateur reordonne les sommets d'une surface (`RenderSurfaces`). Test corrige pour comparer des ensembles (position, UV0, UV1) et des ensembles de triangles (comportement du compilateur, pas un bug du lecteur).
- Tests ajoutes : `tests/Kotor.Compiler.Tests/BspReaderTests.cs` (`BspReader_roundtrips_compiled_map`, `BspReader_rejects_bad_header`, `BspReader_rejects_out_of_range_lump`, `BspReader_rejects_surface_indices_outside_vertices`, `Export_is_deterministic`) ; `tests/Kotor.Pipeline.Tests/PipelineStepsTests.cs` (`Steps_produce_pk3_with_readable_bsp`, `CreatePk3_locked_target_fails_cleanly`, `Steps_chain_equals_ModuleExporter` [SkippableFact, KOTOR reel, dossiers temporaires]) ; `tests/Kotor.Pipeline.Tests/TestEnv.cs` (copie de celui de Core.Tests).
- Test de pk3 verrouille : le pk3 existant (5 octets) est ouvert en `FileShare.None` ; `CreatePk3` leve `PipelineStepException` code `EXPORT_IO_ERROR`, le journal contient le code, le contenu du fichier est inchange.
- Verification manuelle : `kotor2ja export-ja tar_m02aa --config <config temporaire>` (JaBaseDir/WorkDir temporaires, supprimes ensuite) produit le pk3 et le resume du journal. Rien n'a ete ecrit dans GameData.

## Fichiers modifies / deplaces

Deplace : `src/Kotor.Compiler/Export/ModuleExporter.cs` -> `src/Kotor.Pipeline/ModuleExporter.cs` (le dossier `Export` du Compiler a disparu).
Crees : `src/Kotor.Pipeline/{Kotor.Pipeline.csproj,PipelineSteps.cs,StepResults.cs}`, `src/Kotor.Compiler/Bsp/BspReader.cs`, `tests/Kotor.Pipeline.Tests/{Kotor.Pipeline.Tests.csproj,PipelineStepsTests.cs,TestEnv.cs}`, `tests/Kotor.Compiler.Tests/BspReaderTests.cs`.
Modifies : `Kotor2Ja.sln`, `src/Kotor2Ja.Cli/{Kotor2Ja.Cli.csproj,Commands/ExportJaCommand.cs}`, `tests/Kotor.Core.Tests/{Kotor.Core.Tests.csproj,Export/ModuleExporterTests.cs}`, `tests/Kotor2Ja.Cli.Tests/{Kotor2Ja.Cli.Tests.csproj,ExportJaCommandTests.cs}`.

## Determinisme

Aucune correction necessaire : deux compilations du meme `CompileInput` donnent des `.bsp` identiques (test), et `Steps_chain_equals_ModuleExporter` montre que deux exports reels complets de `tar_m02aa` (ModuleExporter vs enchainement manuel) donnent le meme `.bsp` octet pour octet et la meme liste d'entrees de pk3. Les dictionnaires utilises conservent l'ordre d'insertion ; pas d'horodatage dans le `.bsp`. Remarque : les pk3 eux-memes ne sont pas identiques octet pour octet (le zip embarque la date de modification des fichiers du staging) ; le plan ne l'exige pas (liste d'entrees + `.bsp`).

## Remarques / points d'attention

- Le compilateur reordonne les sommets d'une surface par rapport a l'entree ; l'application doit comparer par ensembles si elle compare entree et relecture.
- `BspReader` ne relit que la couleur de style 0 et les UV de lightmap de style 0 (comme le plan). UV1 est (0,0) pour les surfaces sans lightmap.
- `Import` leve `PipelineStepException("OPTIONS_INVALID")` si les options sont invalides ; `ModuleExporter.Export` valide avant, donc son comportement n'a pas change.
- `Export` (etape) supprime puis recree le staging `<WorkDir>\<carte>\pk3\` comme en phase 1.
