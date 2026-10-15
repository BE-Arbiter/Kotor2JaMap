# Phase 3 — Contexte partagé

Chaque agent lit **uniquement** ce fichier, **son** `step-NN.md` et la spec `docs/superpowers/specs/2026-10-15-phase3-contenu-design.md`.

## Chemins

- Dépôt : `C:\Users\lambe\source\repos\SWGLKotor-Map`, branche `phase3-contenu`. Les phases 1 et 2 sont faites : lecture KOTOR, scène, compilateur BSP maison, export pk3, `Kotor.Pipeline` (étapes, `PreviewBuilder`, `WorkflowController`) et application `Kotor2Ja.App`.
- KOTOR (lecture seule) : `C:\Program Files (x86)\Steam\steamapps\common\swkotor\`
- KOTOR 2 (lecture seule) : `C:\Program Files (x86)\Steam\steamapps\common\Knights of the Old Republic II\`
- Jedi Academy SWGL : `C:\Program Files (x86)\Steam\steamapps\common\Jedi Academy - SWGL_DEV\GameData\`. On n'y écrit que `base\kotor_<carte>.pk3` et `base\kotor2_<carte>.pk3`, et **jamais depuis un test**.
- Source du moteur SWGL, à lire comme référence pour les entités et les shaders : `C:\Users\lambe\source\repos\OpenJK-SWGL\` (`code/game`, `code/cgame`, `code/rd-*`). À ne pas modifier.
- Projet TFU, source de copie en lecture seule : `C:\Users\lambe\source\repos\SWGLFU-Map-importer\`. Il a peut-être déjà un écrivain MD3 : à chercher avant d'en écrire un.
- Les références de format (xoreos, PyKotor, kotorblender) peuvent être consultées sur le web (WebFetch/WebSearch) ; citer l'URL dans le rapport.

## Code existant (lire les signatures réelles avant usage)

- `Kotor.Core` :
  - `Archives.ResourceResolver` (`ForModule`, `Find`, `FindSource`) et `Modules.ModuleCatalog` ;
  - `Gff.GffReader`, `TwoDa.TwoDaFile`, `Tlk.TlkFile` ;
  - `Mdl.MdlReader`, `KotorModel.Walk()`, `KotorMesh`, `Walkmesh.WokReader` ;
  - `Textures.TpcDecoder`, `TextureLookup`, `TxiInfo`, `KotorTexture` ;
  - `Scene.ModuleSceneBuilder` ;
  - `Entities.ModuleEntitiesReader`, `ModuleEntity`, `EntityKind` ;
  - `Export.ShaderPlanner`, `ShaderWriter`, `TgaWriter`, `TgaReader`, `Pk3Builder`, `ExportOptions`, `ConfigLoader`, `MapNames` ;
  - `Diagnostics.ImportLog`.
- `Kotor.Compiler` : `Compiler.Compile`, `CompileInput`, `SurfaceInput`, `Bsp/*` (`BspWriter`, `BspReader`, `VertexLighting`, `BrushBuilder`, `CollisionMerger`, `Limits`).
- `Kotor.Pipeline` : `PipelineSteps`, `ModuleExporter` (`BuildPlan`, `FlipV`), `Preview/*`, `Workflow/*`.
- `Kotor2Ja.Cli`, `Kotor2Ja.App` (`AppGame`, `Ui/MainScreen`, `Rendering/PreviewRenderer`, `FreeCamera`).
- **Outil de diagnostic** : un petit projet console jetable peut référencer `src/Kotor.Core/Kotor.Core.csproj` pour sonder de vrais fichiers. Il se place hors du dépôt, dans `%TEMP%`.

## Conventions

- Échelle par défaut 40. Repère JA : Z vers le haut, unités Quake. KOTOR : mètres, Z vers le haut.
- UV : les images sont « ligne 0 = haut ». Les UV KOTOR sont en convention GL ; le BSP et le MD3 attendent t = 1 − v.
- Journal : `ImportLog.Warn(code, message)`. Codes en MAJUSCULES, messages **en anglais** pour tout nouveau code.
- Tests xUnit. Les tests sur jeu installé sont des `[SkippableFact]` (voir `TestEnv`). Pour KOTOR 2, ajouter `TestEnv.Kotor2Dir`.

## Protocole

- Un agent ne lance jamais de sous-agent.
- Rapport : `docs/superpowers/plans/phase3/reports/step-NN-<impl|review|fix-R|rereview-R>.md`, en français. L'agent renvoie un statut court : `FAIT`, `FAIT_AVEC_RESERVES` ou `BLOQUE`, avec une ligne.
- **Les agents ne commitent pas.** L'orchestrateur commite.
- Commande globale : `dotnet build Kotor2Ja.sln` (0 avertissement) et `dotnet test Kotor2Ja.sln` (vert).
- Ne pas lancer le jeu. Ne pas cliquer dans l'application. L'orchestrateur fait les essais visuels.
- Si l'étape ne correspond pas au code réel, l'agent s'arrête et décrit l'écart (`BLOQUE`).
