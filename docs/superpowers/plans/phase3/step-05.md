# Étape 05 — PNJ retirés, modèles dans les vues, essais sans clic

**But :**
- les PNJ ne sont plus lus ni montrés ;
- la vue Importer montre les objets et les portes avec leur maillage ;
- la vue Exporter montre le BSP et les MD3 ;
- l'application peut être pilotée en ligne de commande pour des captures d'écran automatiques.

**Fichiers :**
- Modifier :
  - `ModuleEntitiesReader` et `EntityKind` (on garde l'enum, mais les créatures et les rencontres ne sont plus lues) ;
  - `src/Kotor.Pipeline/Preview/PreviewModel.cs` et `PreviewBuilder.cs` (instances de modèles) ;
  - `src/Kotor.Pipeline/Workflow/KotorWorkflowSteps.cs` ;
  - `src/Kotor2Ja.App/*` (rendu des instances, options de ligne de commande, capture).
- Créer : `src/Kotor2Ja.App/AutomationOptions.cs`
- Tester : ajouts à `tests/Kotor.Core.Tests/Entities/ModuleEntitiesReaderTests.cs` et `tests/Kotor.Pipeline.Tests/PreviewBuilderTests.cs`, puis `tests/Kotor.Pipeline.Tests/AutomationOptionsTests.cs`. Pour ce dernier, la logique d'analyse va dans `Kotor.Pipeline/Workflow/AutomationOptions.cs` (testable) ; l'application ne fait que l'appeler.

**Interfaces produites :**
```csharp
namespace Kotor.Pipeline.Preview;
public sealed record PreviewModelInstance(string ModelKey, Matrix4x4 Transform /* unités JA */, EntityKind Kind, string Label);
// PreviewModel gagne : IReadOnlyDictionary<string, IReadOnlyList<PreviewBatch>> Models ; IReadOnlyList<PreviewModelInstance> Instances.
namespace Kotor.Pipeline.Workflow;
public sealed record AutomationOptions(KotorGame Game, string Module, string RunTo /* preview|import|export|pk3 */,
    Vector3? CameraPosition, float CameraYaw, float CameraPitch, string? ScreenshotPath, bool Exit);
public static class AutomationParser { public static AutomationOptions? Parse(string[] args, out string? error); }
```

**Ce qui est fixé :**
- PNJ : `Creature List` et `Encounter List` ne sont plus convertis en entités. Un code `NPC_SKIPPED` (anglais) donne leur nombre par type. Le bilan `ENTITIES` ne les compte plus.
- Vue Importer :
  - les objets et les portes convertis à l'étape 03/04 sont dessinés avec leurs maillages (textures comprises, sans lightmap), à la place des cubes ;
  - les autres types gardent leurs repères ;
  - les cases à cocher par type restent.
- Vue Exporter : le BSP relu, plus les MD3 du staging (relus par un `Md3Reader` minimal dans `Kotor.Pipeline`), posés selon les entités du lump (`origin`, `angles`/`modelAngles`, `modelscale`). Les portes sont dessinées fermées.
- Ligne de commande de l'application :
  `--game kotor|kotor2 --module <m> [--run-to preview|import|export|pk3] [--camera "x y z yaw pitch"] [--screenshot <png>] [--exit]`
  - Sans `--module`, rien ne change.
  - Avec `--module`, l'application sélectionne le module et enchaîne les étapes jusqu'à `--run-to` (par défaut `preview`).
  - Elle place ensuite la caméra (sinon la caméra par défaut), attend 3 images, et écrit la capture du back buffer en PNG (`PngWriter`) si `--screenshot` est donné.
  - Avec `--exit`, elle quitte ensuite. Avec `--exit`, une étape en échec fait quitter avec le code 1.
  - `pk3` écrit dans le `JaBaseDir` configuré, comme le bouton.
- Arguments invalides : message clair sur la sortie d'erreur, code 2, et aucune fenêtre ouverte.

- [ ] **Étape 1 : tests qui échouent**
  - `Creatures_not_read` : un GIT synthétique avec 1 créature, 1 rencontre et 1 objet donne 1 entité, plus `NPC_SKIPPED` au journal.
  - `Import_view_has_model_instances` : les entités converties synthétiques (1 objet) donnent 1 instance et 1 modèle dans `Models`.
  - `Automation_parse_full` / `Automation_parse_invalid_runto` / `Automation_none_without_module`.
  - `[SkippableFact] Real_tar_m02aa_export_view_has_md3` : la vue Exporter a au moins 10 instances et au moins 1 porte.
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter.**
- [ ] **Étape 4 : `dotnet build` / `dotnet test` verts.** Puis lancer réellement :
  `dotnet run -c Release --project src/Kotor2Ja.App -- --game kotor --module tar_m02aa --run-to export --screenshot <TEMP>\shot.png --exit`
  Vérifier que le PNG existe, n'est pas uniformément noir (variance > 0), et que le code de sortie est 0. Pas de `pk3` dans cet essai.
- [ ] **Étape 5 : rapport** `reports/step-05-impl.md`, avec la commande exacte et le résultat.
