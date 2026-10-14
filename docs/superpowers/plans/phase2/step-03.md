# Étape 03 — Modèles de vue et automate des quatre étapes

**But :** tout ce que l'application affiche et enchaîne, sans GPU, et donc testable : les trois modèles de vue, et l'automate qui pilote le bouton « Prévisualiser » et le bouton variable.

**Fichiers :**
- Créer : `src/Kotor.Pipeline/Preview/{PreviewModel,PreviewBuilder}.cs`, `src/Kotor.Pipeline/Workflow/{IWorkflowSteps,KotorWorkflowSteps,WorkflowController}.cs`
- S'inspirer de (copie autorisée, puis adaptation) : `C:\Users\lambe\source\repos\SWGLFU-Map-importer\src\Tfu.Pipeline\Workflow\WorkflowController.cs` et `Preview\*`
- Tester : `tests/Kotor.Pipeline.Tests/{PreviewBuilderTests,WorkflowControllerTests}.cs`

**Interfaces :**
- Consomme : `PipelineSteps`, `ImportStepResult`, `ExportStepResult`, `Pk3StepResult`, `PipelineStepException` (01) ; `BspFile` (01) ; `ModuleEntity`, `EntityKind`, `ModuleEntitiesReader` (02) ; `ModuleScene`, `KotorTexture`, `ShaderKind`/`PlannedShader`, `ExportOptions`, `ModuleCatalog`.
- Produit :
```csharp
namespace Kotor.Pipeline.Preview;
public readonly record struct PreviewVertex(Vector3 Position, Vector3 Normal, Vector2 Uv0, Vector2 Uv1); // unités JA ; UV « haut-gauche » (t = 0 en haut)
public enum PreviewBlend { Opaque, Cutout, Additive, Alpha }
public sealed record PreviewTexture(int Width, int Height, byte[] Rgba);              // ligne 0 = haut
public sealed record PreviewBatch(string Name, string DiffuseKey, string? LightmapKey, PreviewBlend Blend, bool DoubleSided,
    PreviewVertex[] Vertices, int[] Indices);
public sealed record PreviewMarker(EntityKind Kind, string Label, Vector3 Position, float Yaw, Vector3[]? Polygon);
public sealed record PreviewModel(IReadOnlyList<PreviewBatch> Batches, IReadOnlyDictionary<string, PreviewTexture> Textures,
    Vector3[] CollisionTriangles, IReadOnlyList<PreviewMarker> Markers, Vector3? Spawn, float SpawnYaw, Vector3 BoundsMin, Vector3 BoundsMax)
{ public const string MissingTextureKey = "missing"; public long TriangleCount { get; } }
public static class PreviewBuilder
{
    public static PreviewModel FromScene(ModuleScene scene, float scale);                                    // Prévisualiser
    public static PreviewModel FromImport(ImportStepResult imported, IReadOnlyList<ModuleEntity> entities, float scale); // Importer
    public static PreviewModel FromBsp(BspFile bsp, IReadOnlyDictionary<string, PreviewTexture> textures);   // Exporter
}

namespace Kotor.Pipeline.Workflow;
public sealed record ModuleListItem(string Module, string AreaName);
public enum WorkflowStage { None, Previewed, Imported, Exported, Packed }
public interface IWorkflowSteps
{
    IReadOnlyList<ModuleListItem> Modules();
    PreviewModel Preview(string module, IProgress<string> log);
    PreviewModel Import(IProgress<string> log);    // à partir de l'état gardé par l'implémentation
    PreviewModel Export(IProgress<string> log);
    string CreatePk3(IProgress<string> log);       // chemin du pk3
}
public sealed class KotorWorkflowSteps : IWorkflowSteps { public KotorWorkflowSteps(ExportOptions options); }
public sealed class WorkflowController
{
    public WorkflowController(IWorkflowSteps steps);
    public IReadOnlyList<ModuleListItem> Modules();
    public string? SelectedModule { get; }
    public WorkflowStage Stage { get; }
    public bool IsBusy { get; }
    public bool SelectModule(string module);       // false si occupé ; sinon remet Stage à None et Preview à null
    public bool CanPreview { get; }                // module choisi et pas occupé
    public string? NextLabel { get; }              // "Importer" | "Exporter" | "Créer PK3" | null (avant Prévisualiser ou après le pk3)
    public bool CanNext { get; }
    public Task PreviewAsync();
    public Task NextAsync();                       // exécute l'étape annoncée par NextLabel
    public PreviewModel? Preview { get; }
    public string? LastError { get; }
    public IReadOnlyList<string> DrainLog();
}
```
**Ce qui est fixé :**
- **UV de la vue** : convention « haut-gauche » (t = 0 en haut), comme les images (ligne 0 = haut).
  - `FromScene` inverse `v` (UV KOTOR en convention GL).
  - `FromImport` et `FromBsp` reprennent les UV du `CompileInput` et du BSP, qui sont déjà en convention Q3 (t = 0 en haut).
- **Textures** : la clé est le nom de la texture ou de la lightmap. Une texture absente ou nulle pointe vers `MissingTextureKey`, un damier magenta 8×8. La luminosité des lightmaps suit `ExportOptions.LightmapBrightness` à l'étape Importer et à l'étape Exporter (comme dans la carte), et vaut 1 à l'étape Prévisualiser.
- **Échelle** : `FromScene` multiplie les positions par `scale`. `FromImport` reprend le plan, déjà à l'échelle. Les repères d'entités sont multipliés par `scale`.
- **Blend** : il découle du `ShaderKind` du plan (`Lightmapped` → Opaque, `LightmappedCutout` → Cutout, `Additive` → Additive, `Blend` → Alpha, `Unlit` → Opaque sans lightmap). Pour `FromScene`, on applique la même règle via `ShaderPlanner.Plan`.
- **FromBsp** : un lot par surface du BSP. La lightmap est retrouvée par le nom du shader (`textures/kotor/<tex>__<lm>`), et les textures par le staging de l'étape Exporter (TGA relues avec `TgaReader`).
- **Automate** :
  - Les étapes tournent hors du fil appelant (`Task.Run`).
  - Pendant le travail, `IsBusy` est vrai, `SelectModule` renvoie false, et `CanPreview`/`CanNext` sont faux.
  - Une `PipelineStepException`, ou toute autre exception, laisse `Stage` inchangé, renseigne `LastError` et écrit une ligne d'erreur dans le journal.
  - Un résultat qui arrive après un changement de module (jeton de génération) est ignoré.
  - Après `CreatePk3`, `Stage = Packed`, `NextLabel = null`, et la vue ne change pas.

- [ ] **Étape 1 : tests qui échouent** (avec un faux `IWorkflowSteps`, sauf mention contraire)
  - `Labels_follow_stages` : null, puis Prévisualiser donne « Importer », puis « Exporter », puis « Créer PK3 », puis null. Le `Preview` après Créer PK3 est le même objet qu'après Exporter.
  - `Busy_blocks_selection_and_buttons` : une étape bloquée sur un `TaskCompletionSource` rend `SelectModule` false et `CanNext` faux.
  - `Failure_keeps_stage_and_logs` : l'étape Exporter lève `PipelineStepException("EXPORT_IO_ERROR", …)`. `Stage` reste `Imported`, `NextLabel` reste « Exporter », et le journal contient le code.
  - `Stale_result_is_ignored` : on change de module pendant qu'une étape tourne (via une sélection autorisée après la fin d'une autre tâche simulée, ou via un appel interne). Le résultat tardif n'écrase pas `Preview`.
  - `Select_resets` : après Importer, sélectionner un autre module donne `Stage None`, `Preview null`, `NextLabel null`.
  - `FromScene_flips_v_and_scales` : sur une scène synthétique, l'UV (0,0) devient (0,1), et le sommet (1,2,3) à l'échelle 40 devient (40,80,120).
  - `Missing_texture_maps_to_checker` : un maillage dont la texture est nulle a `DiffuseKey == "missing"`, et la texture `missing` existe en 8×8.
  - `FromImport_contains_markers_and_spawn` : 2 entités donnent 2 repères à l'échelle, et `Spawn` n'est pas null.
  - `FromBsp_one_batch_per_surface`.
  - `[SkippableFact] Real_tar_m02aa_full_workflow` (`KotorWorkflowSteps`, `JaBaseDir` et `WorkDir` temporaires) : les quatre étapes réussissent. Le nombre de triangles de la vue Importer est égal à celui de la vue Exporter (aucune perte), et le pk3 existe.
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter.**
- [ ] **Étape 4 : `dotnet test Kotor2Ja.sln`** tout vert, sans avertissement.
- [ ] **Étape 5 : rapport** `reports/step-03-impl.md`.
