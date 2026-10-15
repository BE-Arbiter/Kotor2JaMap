# Assemblage de modules en une carte JA : plan d'implémentation

> **Pour les agents :** SOUS-COMPÉTENCE REQUISE : utiliser superpowers:subagent-driven-development (recommandé) ou superpowers:executing-plans pour exécuter ce plan tâche par tâche. Les étapes utilisent la syntaxe à cases (`- [ ]`).

**Objectif :** sélectionner plusieurs modules KOTOR ou KOTOR 2 et obtenir une seule carte JA (un .bsp, un pk3), les modules étant liés automatiquement par leurs transitions ou manuellement dans un éditeur visuel, avec jonction comblée et porte conservée.

**Architecture :** on assemble plusieurs `ModuleScene` en **une seule** `ModuleScene` (transformation rigide par module + couloirs de raccord), avant `ModuleExporter.BuildPlan`. La scène assemblée garde la liste de ses parties (`Parts`) pour que `PipelineSteps.ConvertObjects` convertisse portes et objets module par module, avec les entités transformées. Le reste du pipeline (compilateur, ciel, pk3) est inchangé. L'assemblage vit dans `Kotor.Pipeline/Assembly` (il a besoin des résolveurs de ressources et des modèles de porte) ; les maths pures vivent dans `Kotor.Core/Scene`.

**Stack :** .NET 10, C#, xUnit (projets `tests/Kotor.*.Tests`), ImGui + MonoGame pour l'application (`src/Kotor2Ja.App`), System.CommandLine pour la CLI.

**Spec :** `docs/superpowers/specs/2026-10-15-assemblage-de-modules-design.md` (à lire en entier, surtout les sections 5 et 7).

## Contraintes globales

- Mètres KOTOR dans toute l'assemblage ; la mise à l'échelle JA reste dans `ModuleExporter.BuildPlan` (`opt.Scale`).
- Repère : yaw en degrés autour de Z, anti-horaire, 0 = face à +Y (`ModuleEntity.Yaw`). La direction d'une porte de yaw `y` est `(-sin y, cos y)`.
- Une seule `info_player_start` : celle du module de référence, placé à l'identité.
- Sélection multiple : un seul jeu (KOTOR ou KOTOR 2) ; pas de mélange.
- Écriture dans `GameData\base` : `kotor_<carte>.pk3` et `kotor2_<carte>.pk3` uniquement.
- Textes visibles par l'utilisateur (interface, journal, CLI, messages d'exception) en **anglais** ; commentaires de code, specs, plans en français.
- Un seul module sélectionné = comportement actuel strictement inchangé (tous les tests existants restent verts).
- Chaque code de journal est unique et préfixé `LINK_` ou `ASSEMBLY_` (liste dans les tâches).
- Limites JA : `ModuleExporter.MaxShaders` = 1024, `BspLimits`, budget de triangles ; vérifiées sur la carte entière, la réduction de collision du compilateur est conservée, un dépassement irréductible bloque l'export.

## Priorités de relecture

1. Deux modules dont les cadres de porte se superposent exactement (écart nul) : pas de couloir, pas de maille dégénérée, une seule porte.
2. Transition dont la cible `LinkedTo` est un waypoint, un tag inexistant, ou dans un module non sélectionné : pas d'exception, avertissement `LINK_*`, porte restée verrouillée.
3. Cycle de transitions A-B, B-C, C-A : pas de boucle infinie, arête en surplus ignorée (`LINK_EXTRA_EDGE`).
4. Sélection de modules sans aucune transition : le placement automatique n'invente rien, il demande la liaison manuelle (CLI : échec explicite nommant les modules).
5. Même texture ou même nom de shader dans deux modules : dédupliqués, `MaxShaders` respecté ; le chevauchement de deux modules est signalé, pas corrigé.

---

## Plan des fichiers

| Fichier | Rôle |
|---|---|
| `src/Kotor.Core/Scene/RigidTransform.cs` (créer) | rotation Z + translation, application à points, directions, yaw |
| `src/Kotor.Core/Scene/ModuleScene.cs` (modifier) | `Transformed`, `Parts` |
| `src/Kotor.Core/Entities/ModuleEntity.cs`, `ModuleEntitiesReader.cs` (modifier) | champ `LinkedTo` |
| `src/Kotor.Pipeline/Assembly/AssemblyPart.cs` (créer) | module placé : scène, transformation, entités |
| `src/Kotor.Pipeline/Assembly/TransitionLinks.cs` (créer) | découverte des transitions entre modules sélectionnés |
| `src/Kotor.Pipeline/Assembly/AutoPlacer.cs` (créer) | placement automatique par parcours en largeur |
| `src/Kotor.Pipeline/Assembly/DoorFrame.cs`, `JunctionBuilder.cs` (créer) | cadre de porte, couloir de raccord |
| `src/Kotor.Pipeline/Assembly/ModuleAssembler.cs` (créer) | fusion en une scène, filtre des portes et des points d'apparition |
| `src/Kotor.Pipeline/Assembly/OverlapChecker.cs` (créer) | détection des chevauchements |
| `src/Kotor.Pipeline/PipelineSteps.cs` (modifier) | `ConvertObjects` sur `scene.Parts` |
| `src/Kotor.Pipeline/Workflow/*` (modifier) | sélection multiple, état de liaison, aperçu assemblé |
| `src/Kotor2Ja.App/Ui/MainScreen.cs`, `Ui/PlacementEditor.cs` (modifier, créer) | sélection multiple, éditeur visuel |
| `src/Kotor2Ja.Cli/Commands/ExportJaCommand.cs` (modifier) | option `--modules` |

---

### Task 1 : Fondations (transformation, champ LinkedTo, scène transformée)

**Files:**
- Create: `src/Kotor.Core/Scene/RigidTransform.cs`
- Modify: `src/Kotor.Core/Scene/ModuleScene.cs`, `src/Kotor.Core/Entities/ModuleEntity.cs`, `src/Kotor.Core/Entities/ModuleEntitiesReader.cs`
- Test: `tests/Kotor.Core.Tests/Scene/RigidTransformTests.cs`, `tests/Kotor.Core.Tests/Entities/ModuleEntitiesReaderTests.cs` (ajouter)

**Interfaces:**
- Produces:
  - `public readonly record struct RigidTransform(float YawDegrees, Vector3 Translation)` avec `static RigidTransform Identity`, `Vector3 Apply(Vector3 p)` (rotation Z puis translation), `Vector3 ApplyDirection(Vector3 d)` (rotation seule), `float ApplyYaw(float yawDegrees)` (somme, normalisée dans ]-180, 180]), `RigidTransform Then(RigidTransform next)` et `static RigidTransform AligningDoors(Vector3 posA, float yawA, Vector3 posB, float yawB)` : transformation à appliquer au module de B pour que sa porte coïncide avec celle de A, dos à dos (`θ = yawA + 180 - yawB`, translation = `posA - R(θ)·posB`).
  - `ModuleEntity.LinkedTo` : `string LinkedTo = ""` ajouté en dernier paramètre du record, lu dans le GIT des portes (champ `LinkedTo`) et des déclencheurs.
  - `ModuleScene.Transformed(RigidTransform t) -> ModuleScene` : positions, normales (rotation seule), lumières et `Info.Entry` (position et direction) transformés ; textures partagées ; toutes les propriétés (`Lights`, `AmbientColor`, `SunDiffuseColor`, `AreaLightingKnown`) conservées.
  - `ModuleScene.Parts` : `IReadOnlyList<AssemblyPartRef>? Parts { get; init; } = null` où `public sealed record AssemblyPartRef(string Module, RigidTransform Transform)` est défini dans `ModuleScene.cs`. `null` = scène d'un seul module.

- [ ] **Step 1 : Écrire les tests.** `RigidTransformTests` : `Apply` de (1,0,0) avec yaw 90 donne (0,1,0) (tolérance 1e-5) ; `ApplyYaw(170)` pour yaw 20 donne -170 ; `AligningDoors(posA=(10,0,0), yawA=0, posB=(0,5,0), yawB=0)` : le point B transformé vaut posA et `ApplyYaw(0)` vaut 180 ; `Then` compose dans l'ordre ; `Transformed` sur une scène synthétique d'un triangle déplace ses sommets, tourne ses normales sans les translater et conserve `AmbientColor`. Test du lecteur : un GIT synthétique (voir les helpers `GffBuilder` déjà utilisés dans `ModuleEntitiesReaderTests`) avec une porte `LinkedTo = "wp_x"` donne `LinkedTo == "wp_x"`.
- [ ] **Step 2 : Lancer, vérifier l'échec.** `dotnet test tests/Kotor.Core.Tests --filter "RigidTransform|LinkedTo"` : échec de compilation ou assertions.
- [ ] **Step 3 : Implémenter** `RigidTransform`, `ModuleScene.Transformed`/`Parts`/`AssemblyPartRef`, et la lecture de `LinkedTo` (même style que `LinkedToModule` ligne 98 du lecteur).
- [ ] **Step 4 : Lancer toute la suite** `dotnet test Kotor2Ja.sln` : tout vert.
- [ ] **Step 5 : Commit** `feat: RigidTransform, LinkedTo et scene transformee (assemblage)`.

---

### Task 2 : Découverte des transitions et placement automatique

**Files:**
- Create: `src/Kotor.Pipeline/Assembly/AssemblyPart.cs`, `TransitionLinks.cs`, `AutoPlacer.cs`
- Test: `tests/Kotor.Pipeline.Tests/Assembly/AutoPlacerTests.cs`

**Interfaces:**
- Consumes: `ModuleEntity` (`Kind`, `Name` = tag, `Position`, `Yaw`, `LinkedToModule`, `LinkedTo`), `RigidTransform.AligningDoors`.
- Produces:
  - `public sealed record AssemblyPart(ModuleScene Scene, IReadOnlyList<ModuleEntity> Entities)` : `Entities` = portes, waypoints et déclencheurs du GIT du module, repère du module.
  - `public sealed record DoorLink(int FromPart, ModuleEntity FromDoor, int ToPart, ModuleEntity ToTarget)` : `ToTarget` est la porte ou le waypoint de tag `FromDoor.LinkedTo` dans la partie `ToPart`.
  - `public static IReadOnlyList<DoorLink> TransitionLinks.Find(IReadOnlyList<AssemblyPart> parts, ImportLog log)` : une entrée par paire de parties (la première trouvée) ; transitions vers un module non sélectionné ignorées sans bruit ; cible introuvable : `LINK_TARGET_NOT_FOUND` ; arêtes en surplus (paire déjà reliée) : `LINK_EXTRA_EDGE`.
  - `public sealed record Placement(IReadOnlyList<RigidTransform> Transforms, IReadOnlyList<DoorLink> Used, IReadOnlyList<IReadOnlyList<int>> UnlinkedGroups)`.
  - `public static Placement AutoPlacer.Place(IReadOnlyList<AssemblyPart> parts, int referencePart, IReadOnlyList<DoorLink> links, ImportLog log)` : référence à l'identité, parcours en largeur ; la cible waypoint est remplacée par la porte de sa partie la plus proche (`LINK_NO_TARGET_DOOR` et l'avertissement si aucune porte, la position du waypoint sert alors d'ancre) ; `UnlinkedGroups` : une liste d'indices par composante non atteinte (vide si tout est lié). Les parties non atteintes gardent `Identity` dans `Transforms`.

- [ ] **Step 1 : Écrire les tests** (parties synthétiques, sans jeu installé) : deux parties dont la porte A (module `m_a`, tag `d1`, `LinkedToModule = "m_b"`, `LinkedTo = "d2"`, position (10,0,0), yaw 0) et la porte B (tag `d2`, position (0,5,0), yaw 0) ; `Place` donne pour B la transformation de `AligningDoors`, `UnlinkedGroups` vide ; trois parties en triangle A-B, B-C, C-A : une seule arête en surplus journalisée, pas de boucle ; deux parties sans transition : `UnlinkedGroups` contient la partie B ; cible waypoint avec une porte voisine de la partie B : la porte sert d'ancre ; tag introuvable : `LINK_TARGET_NOT_FOUND` et groupe non lié.
- [ ] **Step 2 : Lancer, vérifier l'échec.** `dotnet test tests/Kotor.Pipeline.Tests --filter AutoPlacer`.
- [ ] **Step 3 : Implémenter** `TransitionLinks.Find` et `AutoPlacer.Place`.
- [ ] **Step 4 : Lancer** `dotnet test tests/Kotor.Pipeline.Tests` : vert.
- [ ] **Step 5 : Commit** `feat: decouverte des transitions et placement automatique`.

---

### Task 3 : Cadre de porte et couloir de raccord

**Files:**
- Create: `src/Kotor.Pipeline/Assembly/DoorFrame.cs`, `JunctionBuilder.cs`
- Modify: `src/Kotor.Pipeline/Entities/DoorConverter.cs` (exposer la taille du modèle de porte), `PlaceableConverter.cs` si besoin d'un accès interne
- Test: `tests/Kotor.Pipeline.Tests/Assembly/JunctionBuilderTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record DoorFrame(Vector3 Center, float Yaw, float Width, float Height)` : centre du cadre (milieu du bas de la porte, à la hauteur du sol), mètres, repère du module ; `Width` selon l'axe de la porte, `Height` selon Z.
  - `public static DoorFrame? DoorFrames.Of(ResourceResolver res, KotorGame game, ModuleEntity door, ImportLog log)` : taille tirée de la boîte du modèle de porte (`PlaceableConverter.BuildModel`, même source que `DoorConverter`) ; `null` + `LINK_DOOR_MODEL_MISSING` si le modèle est introuvable (largeur 2 m, hauteur 3 m par défaut dans `DefaultFrame`).
  - `public static (SceneMesh? Mesh, SceneWalkmesh? Walk) JunctionBuilder.Build(DoorFrame a, DoorFrame b, string wallTexture)` : `a` et `b` en repère assemblé, face à face. Si la distance entre les centres est < 0,01 m : `(null, null)`. Sinon un prisme entre les deux cadres : sol (marchable), deux murs, plafond ; largeur et hauteur = minimum des deux cadres ; normales vers l'intérieur ; une seule texture (`wallTexture`, un nom existant de la scène), `Lightmap = null`. Les triangles du sol vont dans `SceneWalkmesh.Walkable` (true), les murs et le plafond dans un `SceneWalkmesh` solide (Walkable false) afin de rester dans la collision.

- [ ] **Step 1 : Écrire les tests.** Écart nul : `(null, null)`. Cadres espacés de 4 m le long de +X, largeur 2, hauteur 3 : le maillage contient exactement le prisme (étendue X de 4, Y de 2, Z de 3 dans les bornes), toutes les normales de murs orientées vers l'axe central, le sol marchable couvre 8 m² (somme des aires des triangles marchables), le plafond est à z = Z du sol + 3. Cadres de largeurs 2 et 3 : largeur retenue 2.
- [ ] **Step 2 : Lancer, vérifier l'échec.** `dotnet test tests/Kotor.Pipeline.Tests --filter Junction`.
- [ ] **Step 3 : Implémenter** `DoorFrame`, `DoorFrames.Of` et `JunctionBuilder.Build`.
- [ ] **Step 4 : Lancer** la suite des tests du pipeline : vert.
- [ ] **Step 5 : Commit** `feat: cadre de porte et couloir de raccord`.

---

### Task 4 : Assembleur de scènes, filtre d'entités, chevauchements

**Files:**
- Create: `src/Kotor.Pipeline/Assembly/ModuleAssembler.cs`, `OverlapChecker.cs`
- Modify: `src/Kotor.Pipeline/PipelineSteps.cs` (`ConvertObjects` : boucle sur `scene.Parts`)
- Test: `tests/Kotor.Pipeline.Tests/Assembly/ModuleAssemblerTests.cs`, `tests/Kotor.Pipeline.Tests/Assembly/OverlapCheckerTests.cs`, `tests/Kotor.Pipeline.Tests/PipelineStepsTests.cs` (ajouter)

**Interfaces:**
- Consumes: `AssemblyPart`, `Placement`, `DoorLink`, `DoorFrames.Of`, `JunctionBuilder.Build`, `ModuleScene.Transformed`.
- Produces:
  - `public sealed record AssemblyResult(ModuleScene Scene, IReadOnlyDictionary<int, IReadOnlyList<ModuleEntity>> EntitiesByPart, IReadOnlyList<(int A, int B, string Detail)> Overlaps)` : `Scene.Parts` renseigné ; `EntitiesByPart[i]` = entités de la partie `i` **déjà transformées en repère assemblé**, filtrées : porte de destination d'une jonction retirée, porte source gardée avec `LinkedToModule = ""` (donc non verrouillée par `DoorConverter`), autres portes de transition inchangées (verrouillées).
  - `public static AssemblyResult ModuleAssembler.Assemble(IReadOnlyList<AssemblyPart> parts, int referencePart, IReadOnlyList<RigidTransform> transforms, IReadOnlyList<DoorLink> junctions, Func<AssemblyPart, ModuleEntity, DoorFrame?> frameOf, ImportLog log)` : scène fusionnée (maillages, walkmeshes, lumières, textures de toutes les parties transformées, plus un couloir par jonction), `Info` et ambiante/soleil/`AreaLightingKnown` de la partie de référence (`Info.Module` = module de référence, `Info.AreaName` = noms joints par « + »), `Parts` = `AssemblyPartRef` par partie. Codes : `ASSEMBLY_JUNCTION` (info, une ligne par jonction avec l'écart en mètres), `ASSEMBLY_OVERLAP` (une ligne par paire qui se chevauche).
  - `public static IReadOnlyList<(int A, int B, string Detail)> OverlapChecker.Check(IReadOnlyList<ModuleScene> transformedScenes, float shrinkMeters = 0.5f)` : boîtes englobantes des walkmeshes marchables, réduites de `shrinkMeters`, qui se recoupent en 3D ; la zone de la jonction n'est pas comptée à cause de la réduction.
  - `PipelineSteps.ConvertObjects` : si `scene.Parts` est non nul, pour chaque partie un `ResourceResolver.ForModule(opt.KotorDir, part.Module, opt.Game)` distinct, lecture des objets avec `ModuleEntitiesReader.ReadObjects`, application de `Transform` (position, yaw, polygone) puis du filtre ci-dessus, conversion, `Merge` ; `firstSubModelIndex` continue d'une partie à l'autre. Pour le cas à un module, le code actuel est inchangé.

- [ ] **Step 1 : Écrire les tests.** Assembleur : deux scènes synthétiques (un triangle chacune) + une jonction : la scène fusionnée contient les deux triangles transformés + le couloir, `Info` de la référence, `Parts.Count == 2` ; la porte de destination est absente des entités, la porte source a `LinkedToModule == ""` ; une porte de transition non utilisée garde `LinkedToModule` ; écart nul : pas de couloir ; `AreaName` = « A + B ». Chevauchement : deux walkmeshes carrés superposés signalés, deux carrés adjacents (se touchant sur 0,3 m) non signalés. `PipelineStepsTests` : `Import` d'une scène assemblée synthétique avec `KotorDir` inexistant journalise `PLACEABLES_NO_GAME_DIR` et ne lève pas.
- [ ] **Step 2 : Lancer, vérifier l'échec.**
- [ ] **Step 3 : Implémenter** l'assembleur, le vérificateur et la boucle de `ConvertObjects`. La sélection du texture du couloir : la texture du premier maillage de la partie source dont le nom est dans `Scene.Textures` non nul.
- [ ] **Step 4 : Lancer toute la suite** : vert (les tests existants prouvent l'absence de régression à un module).
- [ ] **Step 5 : Commit** `feat: assembleur de scenes, filtre de portes, chevauchements`.

---

### Task 5 : Flux de travail multi-modules (sans interface)

**Files:**
- Modify: `src/Kotor.Pipeline/Workflow/IWorkflowSteps.cs`, `KotorWorkflowSteps.cs`, `WorkflowController.cs`
- Create: `src/Kotor.Pipeline/Assembly/AssemblySession.cs`
- Test: `tests/Kotor.Pipeline.Tests/WorkflowControllerTests.cs`, `tests/Kotor.Pipeline.Tests/Assembly/AssemblySessionTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class AssemblySession` : `IReadOnlyList<string> Modules`, `int ReferenceIndex { get; set; }`, `IReadOnlyList<RigidTransform> Transforms { get; }`, `IReadOnlyList<IReadOnlyList<int>> UnlinkedGroups { get; }` (groupes sans liaison après le placement automatique), `IReadOnlyList<DoorLink> AutoLinks`, `void SetTransform(int part, RigidTransform t)` (placement manuel), `void SetManualJunction(int partA, ModuleEntity doorA, int partB, ModuleEntity doorB)` (porte de jonction choisie de chaque côté ; `Transforms[partB]` recalculé avec `AligningDoors`), `bool IsComplete` (aucun groupe non lié, ou tous liés manuellement), `AssemblyResult Build(ImportLog log)`.
  - `IWorkflowSteps` gagne `PreviewModel Preview(KotorGame game, IReadOnlyList<string> modules, IProgress<string> log)` (si `modules.Count == 1` : même résultat que l'ancienne surcharge) et `AssemblySession? Assembly { get; }` (non nul quand la sélection a plus d'un module) ; `PreviewModel ApplyAssembly(IProgress<string> log)` reconstruit l'aperçu après l'édition.
  - `WorkflowController` : `SelectModules(KotorGame game, IReadOnlyList<string> modules)` (refuse pendant une étape ; `SelectModule` devient un cas particulier), `IReadOnlyList<string> SelectedModules`, `bool NeedsLinking` (vrai tant que `Assembly` n'est pas complète : `CanNext` reste faux), `Task ApplyAssemblyAsync()`.
  - Si les modules sélectionnés sont de jeux différents : `PipelineStepException("ASSEMBLY_MIXED_GAMES", "...")`.

- [ ] **Step 1 : Écrire les tests** avec le faux `IWorkflowSteps` déjà présent dans `WorkflowControllerTests` : `SelectModules` de deux modules retient la liste et remet l'étape à `None` ; `NeedsLinking` vrai puis faux après `ApplyAssemblyAsync` quand le faux marque la session complète ; `Next` refusé pendant `NeedsLinking`. `AssemblySessionTests` avec parties synthétiques : liaison auto complète ; deux groupes non liés puis `SetManualJunction` rend la session complète ; `Build` appelle l'assembleur.
- [ ] **Step 2 : Lancer, vérifier l'échec.**
- [ ] **Step 3 : Implémenter** (état gardé dans `KotorWorkflowSteps` ; `Preview` multi-modules charge chaque module avec `PipelineSteps.Preview`, lit ses entités, appelle `TransitionLinks.Find` puis `AutoPlacer.Place` ; aperçu = `PreviewBuilder.FromScene(assembled, opt.Scale)` + marqueurs transformés).
- [ ] **Step 4 : Lancer toute la suite** : vert.
- [ ] **Step 5 : Commit** `feat: flux de travail multi-modules`.

---

### Task 6 : Interface — sélection multiple et éditeur de placement

**Files:**
- Modify: `src/Kotor2Ja.App/Ui/MainScreen.cs` (arbre : Ctrl+clic ou cases pour sélection multiple, bouton « Link modules » quand `NeedsLinking`, choix du module de référence)
- Create: `src/Kotor2Ja.App/Ui/PlacementEditor.cs`
- Test: `tests/Kotor.Pipeline.Tests/Assembly/PlacementEditorModelTests.cs` (la logique sans ImGui)

**Interfaces:**
- Produces: `PlacementEditorModel` (dans `Kotor.Pipeline/Assembly`, sans dépendance ImGui, testé) : `Nudge(int part, Vector3 deltaMeters)`, `Rotate(int part, float deltaDegrees)`, `SetNumeric(int part, Vector3 position, float yawDegrees)`, `SnapDoors(int partA, ModuleEntity doorA, int partB, ModuleEntity doorB)`, `Overlaps` (recalculé via `OverlapChecker` à chaque modification). `PlacementEditor` : fenêtre ImGui plein écran avec vue de dessus (orthographique, drag souris pour déplacer le module sélectionné, molette = zoom, clic droit = rotation), vue libre (caméra `FreeCamera` existante), champs numériques X/Y/Z/yaw, listes de portes de chaque côté, liste des chevauchements en rouge, boutons « Apply » et « Cancel ». Tous les textes en anglais.

- [ ] **Step 1 : Écrire les tests du modèle** : `Nudge` change la translation du module ; `Rotate(part, 90)` tourne autour du centre du module (le centre ne bouge pas) ; `SnapDoors` donne le même résultat que `AligningDoors` ; `Overlaps` apparaît quand deux modules sont déplacés l'un sur l'autre et disparaît quand on les écarte.
- [ ] **Step 2 : Lancer, vérifier l'échec.**
- [ ] **Step 3 : Implémenter** le modèle, puis l'écran ImGui (pas de test automatisé de l'écran).
- [ ] **Step 4 : Lancer la suite**, puis `dotnet build Kotor2Ja.sln -c Release` : vert ; lancer l'application (`dotnet run --project src/Kotor2Ja.App`) sans jeu ne plante pas.
- [ ] **Step 5 : Commit** `feat: selection multiple et editeur de placement`.

---

### Task 7 : CLI, essai visuel, documentation

**Files:**
- Modify: `src/Kotor2Ja.Cli/Commands/ExportJaCommand.cs`, `README.md`, `src/Kotor2Ja.App/AppGame.cs` / `Workflow/AutomationOptions.cs` (option `--module a,b` pour les essais visuels)
- Test: `tests/Kotor2Ja.Cli.Tests/` (ajouter), `tests/Kotor.Pipeline.Tests/AutomationOptionsTests.cs` (ajouter)

**Interfaces:**
- CLI : `export-ja <module>` accepte plusieurs modules séparés par des virgules (`end_m01aa,end_m01ab`) ; liaison automatique seulement ; si un groupe reste non lié : code de sortie 1 et message anglais `Modules not linked: a, b. Use the application to place them manually.` ; `--reference <module>` choisit la référence (défaut : le premier) ; `--name` par défaut `<référence>_<n>` (ex. `end_m01aa_2`).
- Automatisation de l'application : `--module a,b` fonctionne comme la sélection multiple ; `--run-to` passe par la liaison automatique, échoue avec le même message sinon.

- [ ] **Step 1 : Écrire les tests** : analyse de `--module a,b` en liste ; `export-ja a,b` sans jeu installé échoue proprement (message de jeu introuvable, pas de pile d'appels) ; modules de jeux différents (via `--game` unique) : sans objet ici, mais `ASSEMBLY_MIXED_GAMES` est testé au niveau de la session.
- [ ] **Step 2 : Lancer, vérifier l'échec.**
- [ ] **Step 3 : Implémenter** CLI, options d'automatisation et section « Assembling modules » du README (EN).
- [ ] **Step 4 : Essai visuel en jeu** (voir la procédure des phases précédentes, mémoire du projet) : exporter `end_m01aa,end_m01ab` (KOTOR 1) vers `kotor_end_m01aa_2.pk3` ; supprimer d'abord les anciens `kotor_*.pk3` de test dans `base` (voir `kotor-test-pk3-shadowing`) ; lancer `SWGL_SP.x86_64.exe` fenêtré avec `+devmap kotor_end_m01aa_2` ; capturer le couloir de raccord, la porte (elle s'ouvre), et un point de chaque module. Critères : aucun trou à la jonction, porte ouvrable, ciel présent, pas de pièces manquantes par rapport à l'export séparé. Joindre les captures dans `docs/superpowers/plans/assemblage/`.
- [ ] **Step 5 : Lancer toute la suite**, puis commit `feat: export CLI multi-modules, documentation et essais visuels`.

---

## Auto-relecture

- **Couverture de la spec :** sélection multiple (T5, T6), liaison auto (T2), éditeur manuel complet (T6), jonction et porte conservée (T3, T4), ciel/ambiante/spawn de la référence (T4 : `Info` et éclairage de la référence ; ciel déjà global au compilateur), un seul pk3 (pipeline existant), chaînes et cycles (T2), chevauchements signalés (T4, T6), limites JA (contraintes globales ; le compilateur bloque déjà le dépassement irréductible, vérifié à l'essai de T7), CLI (T7), pas de mélange de jeux (T5), codes de journal (T2 à T5).
- **Cohérence des types :** `AssemblyPart`, `DoorLink`, `Placement`, `AssemblyResult`, `AssemblyPartRef`, `RigidTransform` sont définis aux tâches 1 et 2 avant leur usage ; `DoorFrames.Of` (T3) est consommé par un délégué `frameOf` en T4 pour rester testable sans jeu.
- **Point d'attention :** la couverture du « ciel » est celle du compilateur (boîte autour de toute la géométrie, `JaDefaults.SkyShader`) : aucun traitement par module.
