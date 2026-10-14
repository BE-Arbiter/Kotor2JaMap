# Etape 03 - Rapport d'implementation

## Construit
- `Kotor.Pipeline/Preview/PreviewModel.cs`, `PreviewBuilder.cs` : `FromScene` (inverse v, echelle), `FromImport` (plan deja a l'echelle, UV Q3, reperes * echelle, lightmaps eclaircies, spawn lu dans `info_player_start`), `FromBsp` (un lot par surface, lightmap retrouvee par le nom du shader, boite de ciel ignoree). Texture manquante -> cle `missing`, damier 8x8.
- `Kotor.Pipeline/Workflow/IWorkflowSteps.cs`, `KotorWorkflowSteps.cs`, `WorkflowController.cs` : automate a jeton de generation, `Task.Run`, erreurs (`PipelineStepException` ou autre) -> `Stage` inchange, `LastError`, ligne `ERREUR : code : message` au journal.
- `KotorWorkflowSteps` lit `<AreaResRef>.lyt` via le `ResourceResolver` pour `ModuleEntitiesReader.Read`.

## Ecarts / ajouts par rapport a la specification
- `FromImport` a un parametre optionnel `lightmapBrightness = 1f` (la signature fixee n'avait pas de moyen de recevoir `LightmapBrightness`).
- `FromBsp` a un parametre optionnel `IEnumerable<PlannedShader>? shaders` (le BSP ne dit pas le melange/double face) ; sans lui : Opaque, simple face.
- `FromBsp` ignore la boite de ciel (12 triangles ajoutes par le compilateur) pour que Importer = Exporter.
- `KotorWorkflowSteps.Export` reporte reperes et triangles de collision de la vue Importer dans la vue Exporter (le BSP ne les fournit pas ; `CollisionTriangles` de `FromBsp` est vide).
- `WorkflowController.Reset()` public (invalide la generation) : sert au test `Stale_result_is_ignored`.
- Collision `FromScene` = tous les triangles des walkmeshes ; `FromImport` = walkable + `walkmesh_solid`.
- `Modules()` charge chaque module pour obtenir le nom de zone (repli sur le nom du module en cas d'echec).

## TDD
- RED : avant implementation, `dotnet test tests/Kotor.Pipeline.Tests` -> 13 erreurs CS (types `Preview`/`Workflow` inexistants).
- GREEN : Kotor.Pipeline.Tests 15/15 (dont les 10 nouveaux, le SkippableFact reel execute, non ignore). `dotnet test Kotor2Ja.sln` : Cli 5, Compiler 68, Pipeline 15, Core 108, tous verts, aucun avertissement.

## Chiffres du test reel tar_m02aa (JaBaseDir/WorkDir temporaires)
- Triangles : vue Previsualiser 41216, vue Importer 41216, vue Exporter 41216 (aucune perte, ciel exclu).
- Reperes d'entites (Importer) : 180.
- Le pk3 `kotor_tar_m02aa.pk3` est cree dans le JaBaseDir temporaire.

## Fichiers
src/Kotor.Pipeline/Preview/{PreviewModel,PreviewBuilder}.cs ; src/Kotor.Pipeline/Workflow/{IWorkflowSteps,KotorWorkflowSteps,WorkflowController}.cs ; tests/Kotor.Pipeline.Tests/{PreviewBuilderTests,WorkflowControllerTests}.cs.

## Concerns
- Cle de texture commune diffuse/lightmap : un diffuse et une lightmap de meme nom se confondraient (improbable).
- Nom de shader tronque (hash) : sans la liste `shaders`, la lightmap ne serait pas retrouvee ; `KotorWorkflowSteps` la passe.
- Rien n'est commite.
