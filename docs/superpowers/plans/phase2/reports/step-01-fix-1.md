# Étape 01 — Correctifs de revue (fix 1)

## Changements

1. **Relecture BSP (Important)**
   - `src/Kotor.Compiler/Bsp/BspReader.cs` : une surface sans sommet, sans index ou avec `numIndexes % 3 != 0` lève `InvalidDataException` (`Surface {s} : aucun sommet / aucun index / N index, non multiple de 3`).
   - `src/Kotor.Pipeline/PipelineSteps.cs` : nouvelle méthode `internal static ReadBack(byte[], ExportPlan, ImportLog)` (seam de test, `InternalsVisibleTo Kotor.Pipeline.Tests` ajouté au csproj). `Export` l'appelle. Elle échoue en `BSP_READBACK_FAILED` (journalisé via `Fail`) si le BSP est illisible, a zéro surface, ou si le total d'index relus diffère du plan.
   - **Écart sur l'égalité stricte** : sur tar_m02aa (et sur la scène de test) le total relu vaut le total du plan + 36 index : le compilateur ajoute toujours la boîte de ciel (6 faces x 2 triangles) et ne retire rien. Le contrôle accepte donc `plan` ou `plan + SkyBox.IndexCount` (constante publique ajoutée dans `SkyBox.cs` = 36). Tar_m02aa : 123648 prévus, 123684 relus (+36).
2. `TestEnv.JaBaseDir` supprimé de `tests/Kotor.Pipeline.Tests/TestEnv.cs` (non utilisé ; les tests utilisent `opt.JaBaseDir` sur dossier temporaire).
3. Assertion de shader renforcée : `s.Shader == "textures/kotor/t"`.
4. Au passage : blancs de mise en forme de `BspReaderTests.cs` (mineur 4 de la revue).

## Tests couvrants

- `BspReaderTests` : `BspReader_rejects_surface_without_indexes`, `..._index_count_not_multiple_of_three`, `..._surface_without_vertices`.
- `PipelineStepsTests` : `ReadBack_of_real_compile_matches_plan` (plan + ciel), `ReadBack_fails_when_bsp_is_empty_or_inconsistent_with_plan` (lump surfaces vide, surface sans index, total d'index incohérent ; code `BSP_READBACK_FAILED` et entrée de journal).
- Tests écrits avant l'implémentation ; la première exécution a révélé les +36 index de ciel, d'où l'ajustement ci-dessus.

## Commandes

`dotnet test Kotor2Ja.sln` : aucun avertissement ni erreur.
- Kotor2Ja.Cli.Tests : 5 réussis
- Kotor.Pipeline.Tests : 5 réussis (dont `Steps_chain_equals_ModuleExporter` sur tar_m02aa, non ignoré)
- Kotor.Compiler.Tests : 68 réussis
- Kotor.Core.Tests : 99 réussis

Rien écrit dans GameData (dossiers temporaires uniquement). Rien commité.
