# Phase 2 — Application graphique : contexte partagé

Chaque agent lit **uniquement** ce fichier et **son** `step-NN.md`.

## Chemins

- Dépôt : `C:\Users\lambe\source\repos\SWGLKotor-Map`, branche `phase2-app`. La phase 1 (lecture KOTOR, scène, compilateur, export) est déjà faite.
- KOTOR (lecture seule) : `C:\Program Files (x86)\Steam\steamapps\common\swkotor\`.
- Jedi Academy SWGL : `C:\Program Files (x86)\Steam\steamapps\common\Jedi Academy - SWGL_DEV\GameData\`. On n'y écrit que `base\kotor_<carte>.pk3`. **Le jeu peut être lancé et verrouiller ce pk3 : les tests écrivent dans un dossier temporaire.**
- Projet TFU (source de copie, lecture seule) : `C:\Users\lambe\source\repos\SWGLFU-Map-importer\` (`src/Tfu2Ja.App`, `src/Tfu.Pipeline`).

## Code existant utile (lire les signatures réelles avant usage)

- `Kotor.Core` :
  - `Archives.ResourceResolver` ;
  - `Modules.ModuleCatalog` (`ListModuleNames`, `Load`) ;
  - `Tlk.TlkFile`, `Gff.GffReader`/`GffStruct` ;
  - `Scene.ModuleSceneBuilder.Build` → `ModuleScene`/`SceneMesh`/`SceneWalkmesh` (mètres KOTOR, UV en convention GL, `Textures` : nom → `KotorTexture?`) ;
  - `Mdl.MdlReader`/`KotorModel.Walk` ;
  - `Export.ExportOptions`, `ConfigLoader`, `ShaderPlanner`, `Pk3Builder`, `TgaWriter` ;
  - `Diagnostics.ImportLog`.
- `Kotor.Compiler` :
  - `Compiler.Compile(CompileInput, bspPath, Limits, ImportLog)` ;
  - `CompileInput`/`SurfaceInput` (unités JA, UV en convention Q3) ;
  - `Bsp.Limits.Swgl`/`Vanilla`, `Bsp.BspWriter` (format RBSP, 18 lumps) ;
  - `Export.ModuleExporter` (`BuildPlan` → `ExportPlan` ; `Export(...)`).
- CLI `Kotor2Ja.Cli` : `list-modules`, `export-obj`, `export-ja`.

## Conventions

- Échelle par défaut 40. La vue utilise le repère JA (Z vers le haut, unités Quake). À l'étape Prévisualiser, les mètres KOTOR sont multipliés par l'échelle.
- Journal : `ImportLog.Warn(code, message)`, codes en MAJUSCULES, rien n'est ignoré en silence.
- Tests xUnit. Les tests d'intégration sur le jeu installé sont des `[SkippableFact]` (`TestEnv.KotorDir`).

## Protocole

- Un agent ne lance jamais de sous-agent et ne relance jamais un autre agent.
- Rapport : `docs/superpowers/plans/phase2/reports/step-NN-<impl|review|fix-R|rereview-R>.md`, en français. L'agent renvoie un statut court.
- **Les agents ne commitent pas.** L'orchestrateur commite.
- Commande de test globale : `dotnet test Kotor2Ja.sln`, verte et sans avertissement à la fin de chaque étape.
- Si l'étape ne correspond pas au code réel, l'agent s'arrête et le décrit (`BLOQUE`).
