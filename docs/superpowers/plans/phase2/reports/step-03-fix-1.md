# Etape 03 - Correction 1

## Changements

1. **Ciel conserve (Important)** : `PreviewBatch` recoit `bool IsSky = false` (dernier parametre optionnel). `PreviewBuilder.FromBsp` ne saute plus la surface `JaDefaults.SkyShader` : elle devient un lot `IsSky = true` (cle diffuse `missing`, sans lightmap). `TriangleCount` = total du BSP. Le test reel et `PreviewBuilderTests` comparent explicitement les triangles hors ciel a la vue Importer, et verifient BSP total = Importer + ciel (`SkyBox.IndexCount / 3` = 12).
2. **Etat interne de `KotorWorkflowSteps`** : `Preview` et `Import` ne remettent plus l'etat a zero avant de travailler ; tout est construit dans des locaux et l'etat n'est commite qu'apres succes (Preview : scene/entites, puis invalidation de imported/exported/importModel ; Import : imported, exported = null, importModel). `Export` n'efface plus `_exported` en amont. Comportement retenu : un echec laisse l'etat interne intact, coherent avec le controleur (`Stage` inchange). Test : `Failed_repreview_keeps_previous_state_usable` (reel, SkippableFact) - apres un `Preview` en echec, `Export` reussit.
3. **Ligne `PK3 :`** : passe par `p.Report(...)` (donc filtree par `IsCurrent(gen)`). Tests : `Stale_pk3_result_does_not_log`, `Pk3_success_is_logged`.
4. **Doublons de shaders** : `IndexShaders` (TryAdd, premier garde) remplace les deux `ToDictionary`. Test : `Duplicate_shader_names_do_not_throw` (FromImport et FromBsp).
5. **Gardes de longueur dans `FromBsp`** : Normals (repli `UnitZ`), Uv0 (zero), Uv1 (zero) comme dans `FromImport`. Test : `FromBsp_short_vertex_arrays_fall_back`.

## Tests couvrants

`PreviewBuilderTests.FromBsp_one_batch_per_surface_sky_included`, `Duplicate_shader_names_do_not_throw`, `FromBsp_short_vertex_arrays_fall_back` ; `WorkflowControllerTests.Stale_pk3_result_does_not_log`, `Pk3_success_is_logged`, `Failed_repreview_keeps_previous_state_usable`, `Real_tar_m02aa_full_workflow` (adapte : hors ciel + 12 = total).

## Commande

`dotnet test Kotor2Ja.sln` :
- Kotor2Ja.Cli.Tests 5/5, Kotor.Compiler.Tests 68/68, Kotor.Core.Tests 108/108, Kotor.Pipeline.Tests 20/20 (0 ignore, tests reels executes). Aucun avertissement, aucune ecriture dans le dossier du jeu (dossiers temporaires).
