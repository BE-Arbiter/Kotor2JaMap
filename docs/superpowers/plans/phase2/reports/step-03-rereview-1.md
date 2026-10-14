# Etape 03 - Re-relecture de la correction 1

Base 82c8a14, tete 7d43518. Relecture du diff uniquement (aucune relance de la suite).

## Verdict par constat

1. **Ciel perdu dans FromBsp : ADDRESSED.**
   `PreviewBuilder.cs` FromBsp : la surface `JaDefaults.SkyShader` n'est plus sautee (`bool isSky`), elle devient un lot `PreviewBatch(..., isSky)` avec diffuse `missing` et sans lightmap. `PreviewModel.cs` : `bool IsSky = false` en dernier parametre optionnel (compatible avec les appels existants). Tests : `PreviewBuilderTests.FromBsp_one_batch_per_surface_sky_included` (lots = surfaces, hors ciel == triangles Importer, total BSP = Importer + ciel, ciel = `SkyBox.IndexCount / 3`) et `WorkflowControllerTests.Real_tar_m02aa_full_workflow` adapte dans le meme sens.
2. **Re-previsualisation en echec laissant l'etat mort : ADDRESSED.**
   `KotorWorkflowSteps.cs` Preview/Import : plus de remise a zero en amont, tout est construit dans des locaux puis commite apres succes (Preview invalide imported/exported/importModel seulement apres succes ; Import met `_exported = null` apres succes) ; `Export` n'efface plus `_exported` en amont. Test : `Failed_repreview_keeps_previous_state_usable` (reel, SkippableFact) : apres un `Preview` en echec, `Export` reussit.
3. **Ligne `PK3 :` sans filtre de generation : ADDRESSED.**
   `WorkflowController.cs` (bloc Exported) : `_log.Enqueue` remplace par `p.Report(...)`, donc filtre par `IsCurrent(gen)`. Tests : `Stale_pk3_result_does_not_log`, `Pk3_success_is_logged`.
4. **ToDictionary et doublons de shaders : ADDRESSED.**
   `PreviewBuilder.cs` : `IndexShaders` (TryAdd, premier garde) utilise dans FromImport et FromBsp. Test : `Duplicate_shader_names_do_not_throw` (les deux chemins).
5. **Gardes de longueur dans FromBsp : ADDRESSED.**
   `PreviewBuilder.cs` FromBsp : Normals (repli `UnitZ`), Uv0 (zero), Uv1 (zero, aussi si pas de lightmap). Test : `FromBsp_short_vertex_arrays_fall_back`.

## Nouvelle casse

Aucune.

## Observations hors perimetre

- `Failed_repreview_keeps_previous_state_usable` exerce directement `KotorWorkflowSteps` (nouvelle instance) ; la premiere moitie du test (controleur, `SelectModule("zzz_inexistant")` qui remet a zero) n'apporte rien a la verification et pourrait etre retiree. Le scenario controleur (bouton de variante apres echec) n'est pas teste de bout en bout, mais le `Stage` du controleur est inchange par construction.
- Les lots `IsSky` sont desormais presents dans la vue BSP : l'application doit les filtrer ou les dessiner selon son choix (etape ulterieure), sinon la boite de ciel s'affichera avec le damier `missing`.
- Le rapport de correction annonce 20/20 et aucun avertissement ; non reverifie ici (consigne).

## Verdict

APPROUVE : les 5 constats sont traites, aucune casse introduite.
