# Jedi Knight : Dark Forces II → Jedi Academy — plan d'implémentation

> **Pour les agents :** sous-skill requis : superpowers:subagent-driven-development (recommandé) ou superpowers:executing-plans.
> Les étapes utilisent des cases (`- [ ]`). Un agent neuf par tâche, jamais réutilisé ; rapport dans
> `docs/superpowers/plans/jk/reports/T<n>.md` ; **l'orchestrateur commite** ; **aucun agent ne lance le jeu** (ni JK ni SWGL).

**But :** ajouter Jedi Knight (34 niveaux) comme troisième source de l'application et de la CLI, jusqu'au `jk_<niveau>.pk3`.

**Architecture :** `src/Jk.Core` lit les formats JK (GOB, JKL, CMP/MAT, 3DO, KEY, COG, UNI). `src/Jk.Pipeline` construit une
`ModuleScene` (géométrie, textures) et un `JkObjects` (entités, MD3, sous-modèles BSP), puis s'insère dans le pipeline existant
via `KotorWorkflowSteps.UseScene` comme SWTOR. Le compilateur BSP, `ModuleExporter.BuildPlan`, `Md3Writer` et `Pk3Builder` sont réutilisés.

**Pile :** C# / .NET 10, xUnit 2.9 + `Xunit.SkippableFact` (tests sur le jeu installé sautés sans JK).

**Spec :** `docs/superpowers/specs/2026-10-19-jedi-knight-design.md` (à lire en entier avant chaque tâche).

## Contraintes globales

- Dossier JK par défaut : `C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight` ; détection Steam, dossier `Star Wars Jedi Knight`.
- `KotorGame.JediKnight` (valeur ajoutée en fin d'énumération), préfixe `jk`, nom affiché `Jedi Knight`. Sortie `jk_<niveau>.pk3`,
  `<niveau>` = nom du `.jkl` sans extension, en minuscules. Assets sous `textures/jk/`, `models/jk/`, `sound/jk/`.
- Ordre de résolution des ressources : GOB de l'épisode du niveau, puis `Resource\Res2.gob`, puis `Resource\Res1hi.gob` ; insensible à la casse.
- 34 niveaux : 25 solo (`JK1.GOB`), 6 multi (`JK1MP.GOB`), 3 CTF (`JK1CTF.GOB`).
- Pas d'acteurs (`JK_ACTOR_SKIPPED`, info avec le nombre). Rien de visible n'est sauté sans code `JK_*` dans le journal.
- Texte visible (UI, journal, CLI, README, exceptions) en anglais ; commentaires, docs, rapports en français.
- Le jeu JK n'est jamais modifié ; seul `Create PK3` écrit dans `GameData\base` ; les tests n'y écrivent jamais.
- Code original ; les specs publiques des formats JK peuvent servir de référence, sources citées dans `docs/superpowers/specs/jk-formats.md`.
- Pas de régression KOTOR / SWTOR : `dotnet test Kotor2Ja.sln` vert à la fin de chaque tâche.

## Points de vigilance de la revue

Cas que la spec implique et qu'un utilisateur rencontrera ; chaque ligne a son test dans la tâche indiquée.

1. **Un thing mobile dont le COG ne bouge rien de reconnaissable** : il ne doit jamais disparaître ; position « passage ouvert » +
   `JK_COG_UNSUPPORTED` (test T9 `UnknownCog_KeepsThingVisible_OpenPose`).
2. **Matériau introuvable ou palette `.cmp` absente** : la surface reste avec la texture de remplacement et `TEXTURE_MISSING`
   (test T3 `MissingCmp_FallsBackToDefaultPalette`, T4 `MissingMaterial_SurfaceKeptWithPlaceholder`).
3. **Porte qui ne s'ouvre qu'à la mort d'un acteur absent** : déclenchée à l'approche (`JK_COG_ACTOR_TRIGGER`) pour que le joueur passe
   (test T9 `ActorDeathCog_BecomesProximityTrigger`).
4. **Niveau qui dépasse `Limits`** (gros niveaux comme `21bjerec`) : fusion de secteurs pour la visibilité, jamais de géométrie supprimée
   (test T4 `VisibilityClusters_MergedUnderLimit_NoSurfaceDropped`).
5. **Gabarit qui hérite d'un gabarit absent ou circulaire** : le thing garde ce qui est résolu, `JK_TEMPLATE_UNRESOLVED`, pas d'exception
   (test T2 `Template_CircularOrMissingParent_DoesNotThrow`).

---

### Tâche 0 : branche

- [ ] **Étape 1 :** l'orchestrateur vérifie `git status` sur `feature/swtor-maps`. S'il reste des changements non commités, il **demande à
  l'utilisateur** s'il peut les commiter (ils peuvent venir d'une autre session) ; il ne fait rien sans son accord.
- [ ] **Étape 2 :** `git switch -c feature/jk-maps` depuis `feature/swtor-maps` propre ; `dotnet build Kotor2Ja.sln` et `dotnet test Kotor2Ja.sln` verts.

### Tâche 1 : projet `Jk.Core`, archives GOB, jeu et détection

**Fichiers :**
- Créer : `src/Jk.Core/Jk.Core.csproj`, `src/Jk.Core/Gob/GobArchive.cs`, `src/Jk.Core/Gob/JkFileSystem.cs`, `src/Jk.Core/JkInstall.cs`
- Créer : `tests/Jk.Core.Tests/Jk.Core.Tests.csproj`, `tests/Jk.Core.Tests/GobArchiveTests.cs`, `tests/Jk.Core.Tests/JkFileSystemTests.cs`
- Modifier : `Kotor2Ja.sln`, `src/Kotor.Core/KotorGame.cs`, `src/Kotor.Core/Export/ExportOptions.cs`, `src/Kotor.Core/Export/GameLocator.cs`,
  `src/Kotor.Core/Export/ConfigLoader.cs`, `src/Kotor2Ja.Cli/kotor2ja.config.example.json`

**Interfaces produites :**
- `enum KotorGame { Kotor1, Kotor2, Swtor, JediKnight }` ; `KotorGames.MapPrefix(JediKnight) == "jk"`, `DisplayName == "Jedi Knight"`.
- `ExportOptions` gagne `string? JediKnightDir = null` (clé de config `jediKnightDir`, variable d'environnement `JK_DIR`) ; `GamePaths` gagne `JediKnightDir`.
- `sealed class GobArchive : IDisposable` : `static GobArchive Open(string path)`, `IReadOnlyList<GobEntry> Entries`, `byte[] Read(GobEntry e)`.
  `record GobEntry(string Path, long Offset, int Size)` (chemin avec `\` tel quel).
- `sealed class JkFileSystem : IDisposable` : `static JkFileSystem ForEpisode(string jkDir, string episodeGob)`,
  `byte[]? TryRead(string path)` (ex. `"mat\\dflt.mat"`, insensible à la casse, ordre épisode → `Res2.gob` → `Res1hi.gob`),
  `bool Exists(string path)`, `IEnumerable<string> List(string folder, string extension)`.
- `static class JkInstall` : `IReadOnlyList<JkLevelRef> ListLevels(string jkDir)` ; `record JkLevelRef(string Name, string EpisodeGob, JkLevelKind Kind)` ;
  `enum JkLevelKind { SinglePlayer, Multiplayer, Ctf }`.

- [ ] **Étape 1 : tests qui échouent**
  - `GobArchive_ReadsSyntheticIndex` : un GOB construit en mémoire (`"GOB "`, version `0x14`, offset d'index à l'octet 8, `u32` nombre, entrées
    `u32 offset, u32 taille, char[128] nom`) avec `jkl\a.jkl` (« abc ») → `Entries.Count == 1`, `Read` renvoie « abc ».
  - `GobArchive_BadMagic_Throws` : `InvalidDataException`.
  - `JkFileSystem_EpisodeOverridesResource` : même chemin dans l'épisode et dans `Res2` → contenu de l'épisode.
  - `JkFileSystem_CaseInsensitive` : `TryRead("MAT\\DFLT.MAT")` trouve `mat\dflt.mat`.
  - `[SkippableFact] ListLevels_RealInstall_34Levels` : 25 `SinglePlayer`, 6 `Multiplayer`, 3 `Ctf` ; contient `01narshadda`, `m10`, `c1`.
  - `GameLocator_DetectsJediKnightFolder` : bibliothèque Steam factice contenant `steamapps\common\Star Wars Jedi Knight\Episode\JK1.GOB`.
- [ ] **Étape 2 :** `dotnet test tests/Jk.Core.Tests` → échec (types absents).
- [ ] **Étape 3 :** implémenter (lecture GOB par flux, pas de chargement complet en mémoire ; index en dictionnaire insensible à la casse).
- [ ] **Étape 4 :** `dotnet test Kotor2Ja.sln` → vert.
- [ ] **Étape 5 :** commit `feat(jk): archives GOB, detection du jeu et liste des 34 niveaux`.

### Tâche 2 : lecture des niveaux `.jkl` et des gabarits, document des formats

**Fichiers :**
- Créer : `src/Jk.Core/Jkl/JklReader.cs`, `src/Jk.Core/Jkl/JklLevel.cs` (modèle), `src/Jk.Core/Jkl/JkTemplates.cs`, `src/Jk.Core/Text/JkTokenizer.cs`
- Créer : `docs/superpowers/specs/jk-formats.md`
- Créer : `src/Kotor2Ja.Cli/Commands/JkDumpLevelCommand.cs` (enregistré dans `Program.cs`)
- Test : `tests/Jk.Core.Tests/JklReaderTests.cs`, `tests/Jk.Core.Tests/JkTemplatesTests.cs`

**Interfaces produites (modèle, unités JK, Z haut) :**
- `JklLevel(JklHeader Header, IReadOnlyList<string> Materials, IReadOnlyList<string> Colormaps, Vector3[] Vertices, Vector2[] TexVertices,
  IReadOnlyList<JklAdjoin> Adjoins, IReadOnlyList<JklSurface> Surfaces, IReadOnlyList<JklSector> Sectors, IReadOnlyList<JklThing> Things,
  IReadOnlyList<JklCogInstance> Cogs, IReadOnlyDictionary<string, JklTemplate> Templates, IReadOnlyList<string> Models, IReadOnlyList<string> Sounds)`.
- `JklSurface(int Material /* -1 = aucun */, uint SurfaceFlags, uint FaceFlags, int Geo, int Light, int Tex, int Adjoin /* -1 */,
  float ExtraLight, int[] VertexIndices, int[] TexIndices, float[] Intensities, Vector3 Normal)`.
- `JklSector(int Index, uint Flags, float AmbientLight, float ExtraLight, int Colormap, Vector3 Tint, string? Sound, float SoundVolume,
  int FirstSurface, int SurfaceCount, Vector3 Thrust)`.
- `JklAdjoin(uint Flags, int Mirror)`.
- `JklThing(int Index, string Template, string Name, Vector3 Position, Vector3 PitchYawRoll, int Sector, IReadOnlyDictionary<string,string> Params)`
  (`Params` = paires `cle=valeur` de la ligne, dont `frame=(...)`, `thingflags`, `model3d`).
- `JklCogInstance(int Index, string CogFile, IReadOnlyList<string> Values)`.
- `JklTemplate(string Name, string Parent, IReadOnlyDictionary<string,string> Params)` ; `JkTemplates.Resolve(string name) → IReadOnlyDictionary<string,string>`
  (fusion des parents jusqu'à `none`, l'enfant gagne ; `static.jkl` + niveau) ; `JkTemplates.Unresolved` (noms à journaliser `JK_TEMPLATE_UNRESOLVED`).
- `JklReader.Read(string text) → JklLevel` ; `JklReader.ReadFrames(string value) → IReadOnlyList<(Vector3 Pos, Vector3 Pyr)>`.

- [ ] **Étape 1 : tests qui échouent**
  - `Read_MinimalLevel` : un `.jkl` texte minimal (en ressource de test) avec 1 matériau, 4 sommets, 1 surface de 4 sommets, 1 secteur,
    1 thing `walkplayer` → valeurs lues exactes (index, drapeaux hexadécimaux, intensités, `Position`).
  - `Read_IgnoresCommentsAndBlankLines` : lignes `#` ignorées.
  - `ReadFrames_ParsesPositionsAndAngles` : `frame=(1.0/2.0/3.0:0.0/90.0/0.0)` → `(1,2,3)`, `(0,90,0)`.
  - `Template_ChildOverridesParent` ; `Template_CircularOrMissingParent_DoesNotThrow` (cycle a→b→a et parent absent → `Unresolved` non vide, pas d'exception).
  - `[SkippableFact] Read_All34Levels` : chaque niveau se lit, `Surfaces.Count > 0`, `Sectors.Count > 0`, au moins un thing de départ joueur.
- [ ] **Étape 2 :** lancer, constater l'échec.
- [ ] **Étape 3 :** implémenter le lecteur de sections (`SECTION: HEADER`, `SOUNDS`, `MATERIALS`, `GEORESOURCE`, `SECTORS`, `AICLASS`, `MODELS`,
  `SPRITES`, `KEYFRAMES`, `ANIMCLASS`, `SOUNDCLASS`, `COGSCRIPTS`, `COGS`, `TEMPLATES`, `THINGS`, `PVS`, sections inconnues sautées avec info).
  Écrire `jk-formats.md` (sections, champs, drapeaux de surface / face / secteur / adjoin / thing utilisés par la suite, sources consultées).
- [ ] **Étape 4 :** `jk-dump-level <niveau>` affiche les comptes (sommets, surfaces, secteurs, things par type résolu, COG par fichier). Tests verts.
- [ ] **Étape 5 :** commit `feat(jk): lecteur JKL, gabarits et commande jk-dump-level`.

### Tâche 3 : palettes `.cmp` et textures `.mat`

**Fichiers :** créer `src/Jk.Core/Textures/CmpPalette.cs`, `src/Jk.Core/Textures/MatReader.cs` ; test `tests/Jk.Core.Tests/MatReaderTests.cs`.

**Interfaces produites :**
- `CmpPalette.Read(byte[] cmp) → CmpPalette` (`Rgb(int index)`, plus les tables de lumière si utiles pour l'éclairage) ; `CmpPalette.Default` (gris).
- `MatReader.Read(byte[] mat, CmpPalette pal) → JkMaterial` ;
  `record JkMaterial(string Name, IReadOnlyList<KotorTexture> Cels, bool IsSolidColor, bool HasTransparency)`.
  Cellule 0 = texture de départ. Index 0 transparent quand le matériau le déclare. Formats 8 bits indexés et 16 bits (RGB565 / RGBA) gérés.

- [ ] **Étape 1 :** tests `Mat8Bit_DecodesWithPalette` (MAT synthétique 2×2), `MatColorOnly_ProducesSolidTexture`, `MatMultiCel_KeepsAllCels`,
  `MissingCmp_FallsBackToDefaultPalette`, `[SkippableFact] AllMatsInRes2_Decode` (aucune exception sur les ~1 980 `.mat`, palette du premier niveau).
- [ ] **Étape 2 :** échec constaté. **Étape 3 :** implémenter. **Étape 4 :** vert.
- [ ] **Étape 5 :** commit `feat(jk): palettes CMP et textures MAT`.

### Tâche 4 : scène JK (géométrie, collision, éclairage, visibilité, échelle)

**Fichiers :**
- Créer : `src/Jk.Pipeline/Jk.Pipeline.csproj`, `src/Jk.Pipeline/JkSceneBuilder.cs`, `src/Jk.Pipeline/JkWorldScale.cs`, `src/Jk.Pipeline/JkVisibility.cs`
- Test : `tests/Jk.Pipeline.Tests/JkSceneBuilderTests.cs`

**Interfaces :**
- Consomme : T1 `JkFileSystem`, T2 `JklLevel`, T3 `MatReader`.
- Produit : `JkSceneBuilder.Build(JkFileSystem fs, JkLevelRef level, ImportLog log) → JkPreparedScene` ;
  `record JkPreparedScene(ModuleScene Scene, JklLevel Level, JkTemplates Templates, IReadOnlyDictionary<string, JkMaterial> Materials)`.
- `JkWorldScale.MetresPerJkUnit` : constante unique (la scène est en « mètres » comme KOTOR ; `ExportOptions.Scale` reste celui du pipeline).
  Valeur à caler : hauteur d'yeux / taille du joueur JK = taille du joueur JA (64 u), vérifiée sur les marches (≤ 18 u JA) de `01narshadda`.
  La justification chiffrée va dans le rapport.

**Règles :**
- `SceneMesh.Room` = `"s<index secteur>"` ; un maillage par (secteur, matériau). Polygones triangulés en éventail. UV = texverts / taille de la cellule 0.
- Surface sans matériau ou `Geo == 0` → pas de rendu. Adjoin avec matériau → rendu double face (`Alpha` selon le matériau / face translucide).
- Collision : surfaces solides sauf adjoin franchissable (drapeau « move » de l'adjoin) → `SceneWalkmesh` solide. `RenderMeshesAreSolid = false`.
- Éclairage : couleurs de sommet ou ambiance tirées des intensités JK + `ExtraLight` + lumière du secteur (choix du moyen au builder, consigné
  dans le rapport) ; pas de soleil.
- Visibilité : `RoomVisibility` depuis les adjoins (secteurs voisins jusqu'à une profondeur fixée par le builder), groupes de secteurs fusionnés
  quand le nombre de clusters dépasse `Limits`.
- Entrée (`ModuleInfo.Entry`) = premier thing de départ joueur.

- [ ] **Étape 1 :** tests sur un niveau synthétique de 2 secteurs reliés par un adjoin : `TwoSectors_TwoRooms_VisibleFromEachOther`,
  `PassableAdjoin_NotSolid`, `AdjoinWithMaterial_RenderedDoubleSided`, `MissingMaterial_SurfaceKeptWithPlaceholder`,
  `VisibilityClusters_MergedUnderLimit_NoSurfaceDropped` (300 secteurs en chaîne, limite basse injectée → nombre de surfaces inchangé),
  `[SkippableFact] Narshadda01_BuildsAndExportsBsp` (via `ModuleExporter.BuildPlan` + compilateur dans un dossier temporaire du work dir, pas JA).
- [ ] **Étape 2 :** échec constaté. **Étape 3 :** implémenter ; `export-ja --game jk 01narshadda` produit un pk3 (géométrie seule) dans un dossier de sortie de test.
- [ ] **Étape 4 :** vert ; captures de la vue d'aperçu (pas du jeu) jointes au rapport.
- [ ] **Étape 5 :** commit `feat(jk): scene JK (geometrie, collision, visibilite, echelle)`.

### Tâche 5 : matériaux spéciaux (ciel, transparence, animations, défilement, eau, lave, dégâts)

**Fichiers :** modifier `src/Kotor.Core/Export/ShaderPlanner.cs` et `ShaderWriter.cs` (cas `KotorGame.JediKnight`), `src/Jk.Pipeline/JkSceneBuilder.cs` ;
créer `src/Jk.Pipeline/JkSurfaceEffects.cs` ; test `tests/Jk.Pipeline.Tests/JkSurfaceEffectsTests.cs`, `tests/Kotor.Core.Tests/Export/ShaderPlannerJkTests.cs`.

**Interfaces :**
- `SceneMesh` gagne `ShaderHints Hints { get; init; } = ShaderHints.None` ; `record ShaderHints(IReadOnlyList<string>? AnimFrames, float AnimFps,
  Vector2? Scroll, bool Sky, bool Water, bool Lava, bool FullBright)` (`None` = tout faux / null : sortie KOTOR et SWTOR inchangée).
- `ModuleScene.SkyShader` rempli pour JK : shader `textures/jk/sky_<niveau>` construit à partir du matériau de ciel et du `HEADER`.
- `JkSurfaceEffects.Volumes(JklLevel) → IReadOnlyList<JkVolume>` ; `record JkVolume(JkVolumeKind Kind, int Sector, Vector3 Min, Vector3 Max)`,
  `enum JkVolumeKind { Water, Hurt, Kill }` : eau → brush `surfaceparm water` + brouillard couleur du secteur ; `Hurt`/`Kill` → `trigger_hurt`
  (les entités sont produites en T7 via `JkObjects`).

- [ ] **Étape 1 :** tests `SkySurface_UsesMapSkyShader`, `MultiCelAnimated_WritesAnimMap`, `ScrollingSurface_WritesTcModScroll`,
  `UnderwaterSector_ProducesWaterVolume`, `LavaSurface_FullBrightAndHurt`, `NoHints_ShaderUnchangedForKotor` (snapshot d'un shader KOTOR existant).
- [ ] **Étape 2–4 :** échec, implémentation, vert. Les textures défilantes viennent des COG `SlideSurface`/`SlideWall` (lecture minimale : surface + vecteur ;
  le reste des COG est en T8).
- [ ] **Étape 5 :** commit `feat(jk): ciel, transparence, animations de textures, eau et lave`.

### Tâche 6 : modèles `.3do` et animations `.key` → MD3

**Fichiers :** créer `src/Jk.Core/Models/ThreeDoReader.cs`, `src/Jk.Core/Models/KeyReader.cs`, `src/Jk.Pipeline/JkModelConverter.cs` ;
modifier `src/Kotor.Core/Export/Md3Writer.cs` (images multiples) ; tests `tests/Jk.Core.Tests/ThreeDoReaderTests.cs`,
`tests/Jk.Pipeline.Tests/JkModelConverterTests.cs`, `tests/Kotor.Core.Tests/Export/Md3WriterFramesTests.cs`.

**Interfaces produites :**
- `ThreeDoReader.Read(string text) → ThreeDoModel` (géosets, maillages, nœuds hiérarchiques avec pivot/position/rotation) ; on prend le géoset 0 (le plus détaillé).
- `KeyReader.Read(string text) → KeyAnimation` (images par nœud, fps, nombre d'images).
- `Md3Writer.WriteFrames(string name, IReadOnlyList<IReadOnlyList<ModelPart>> framesJaUnits, string shaderPrefix) → byte[]` (même topologie par image ;
  `Write` existant = une image, inchangé).
- `JkModelConverter.ToMd3(JkFileSystem fs, string model3do, string? key, float scale, IReadOnlyDictionary<string,JkMaterial> mats, ImportLog log) → JkMd3?` ;
  `record JkMd3(Md3Asset Asset, Vector3 Min, Vector3 Max, int Frames, float Fps, bool PureRotation, Vector3 RotationAxis, float DegreesPerSecond)`.
  Chemin `models/jk/<modele>.md3`, shader `models/jk/<texture>`, découpage ±512 comme `PlaceableConverter`.

- [ ] **Étape 1 :** tests `ThreeDo_HierarchyApplied` (2 nœuds, enfant décalé), `Key_TwoFrames_Interpolated`, `Md3_WriteFrames_HeaderFrameCount`,
  `Converter_PureSpin_DetectedAsRotation`, `[SkippableFact] All3doInRes2_Read` (aucune exception sur les 477 modèles).
- [ ] **Étape 2–4 :** échec, implémentation, vert.
- [ ] **Étape 5 :** commit `feat(jk): modeles 3DO et animations KEY vers MD3`.

### Tâche 7 : objets placés, départs, objets à ramasser, sons, volumes

**Fichiers :** créer `src/Jk.Pipeline/JkObjects.cs`, `src/Jk.Pipeline/JkThingConverter.cs`, `src/Jk.Pipeline/JkPickups.cs` ;
modifier `src/Kotor.Pipeline/PipelineSteps.cs` et `src/Kotor.Pipeline/Workflow/KotorWorkflowSteps.cs` ; test `tests/Jk.Pipeline.Tests/JkThingConverterTests.cs`.

**Interfaces :**
- Hook générique : `KotorWorkflowSteps.UseScene(ModuleScene scene, ExportOptions opt, string? mapName, SceneObjects? objects = null)` et
  `PipelineSteps.Import(..., SceneObjects? objects = null)` ; `delegate (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) SceneObjects(ModuleScene scene, ExportOptions opt, ImportLog log)`
  dans `Kotor.Pipeline`. Quand `objects` est fourni, il remplace `ConvertObjects` (KOTOR et SWTOR inchangés quand il est null).
- `JkObjects.Convert(JkPreparedScene prepared, JkFileSystem fs, ExportOptions opt, ImportLog log) → (ConvertedEntities, IReadOnlyList<SubModelInput>)` :
  fusionne `JkThingConverter`, `JkPickups`, volumes (T5) et mobiles (T9, branché à ce moment-là).
- `JkPickups.Map(string resolvedTemplateName) → string?` (classe JA ou null) — table de la spec §5.6, complétée par les gabarits réellement vus
  (liste produite par `jk-dump-level`, jointe au rapport).

**Règles (spec §5.5) :** acteurs ignorés + `JK_ACTOR_SKIPPED` (nombre) ; départ solo `info_player_start` ; multi : premier départ en `info_player_start`
puis `info_player_deathmatch` ; CTF : `team_CTF_redplayer`/`team_CTF_blueplayer` + drapeaux `team_CTF_redflag`/`blueflag` ; décor `.3do` →
`misc_model_static` (yaw converti, vérifié sur l'aperçu) ; décor animé (T6 `Frames > 1`) → entité JA qui boucle l'animation, `PureRotation` → `func_rotating`
avec `model2` (le builder confirme quelle entité SWGL/JA SP joue un MD3 en boucle et l'écrit dans le rapport) ; sprites → shader `autosprite` ;
lumières de things → `light` ; générateurs de particules (`.par`, `.spr` émis) → `fx_runner` avec l'effet JA le plus proche, sinon sprite fixe + `JK_PARTICLE_APPROX` ; sons de secteurs → `target_speaker` (wav de `Res1hi.gob` copiés sous `sound/jk/`) ; explosifs → décor + `JK_EXPLOSIVE_STATIC` ;
objet sans équivalent → décor + `JK_ITEM_NO_EQUIVALENT` ; things créés par COG en jeu → `JK_RUNTIME_THING`.

- [ ] **Étape 1 :** tests `Actor_Skipped_Counted`, `Multiplayer_FirstStartIsPlayerStart`, `Ctf_TeamSpawnsAndFlags`, `BryarPickup_MapsToWeaponBryarPistol`,
  `UnknownItem_PlacedAsDecor`, `SpinningFan_FuncRotatingWithModel2`, `SectorSound_TargetSpeaker`, `NullObjects_KotorImportUnchanged`.
- [ ] **Étape 2–4 :** échec, implémentation, vert.
- [ ] **Étape 5 :** commit `feat(jk): objets places, departs, objets a ramasser et sons`.

### Tâche 8 : COG — lecture et classement des comportements

**Fichiers :** créer `src/Jk.Core/Cog/CogReader.cs`, `src/Jk.Core/Cog/CogScript.cs`, `src/Jk.Pipeline/Movers/CogClassifier.cs`,
`src/Jk.Pipeline/Movers/MoverBehavior.cs` ; étendre `jk-dump-level` (liste des COG avec comportement) ; créer `docs/superpowers/plans/jk/cog-catalogue.md` ;
test `tests/Jk.Pipeline.Tests/CogClassifierTests.cs`.

**Interfaces :**
- `CogReader.Read(string text) → CogScript` : symboles (type, nom, valeur par défaut, `local`, `desc`, lien `mask`) et code découpé en messages
  (`activated`, `touched`, `entered`, `killed`, `startup`, `arrived`, `timer`, `pulse`, `crossed`…) avec les appels de verbes et leurs arguments.
- Liaison instance : `CogBinding.Bind(CogScript s, JklCogInstance inst, JklLevel lvl) → BoundCog` (symboles non `local` remplis dans l'ordre par `Values`).
- `CogClassifier.Classify(BoundCog cog, JklLevel lvl) → IReadOnlyList<MoverBehavior>` ;
  `abstract record MoverBehavior(int Thing)` avec `TouchDoor`, `TwoStopLift`, `SwitchMove(int SwitchSurfaceOrThing, int[] Frames)`,
  `MultiStepPath(int[] Frames, float Wait, bool Loop)`, `ActorTriggered(int Sector)`, `SurfaceCelSwitch(int Surface)`, `Approximate(string Reason)`,
  `Unsupported(string Reason)`, avec vitesse, délai, sons quand le COG les donne.
- Catalogue : `cog-catalogue.md` liste chaque fichier COG utilisé par les 34 niveaux, son nombre d'instances et le comportement retenu.

- [ ] **Étape 1 :** tests sur COG texte synthétiques : `DoorCog_TouchDoor` (`MoveToFrame` dans `touched`/`activated` puis retour), `ElevatorSwitch_SwitchMove`,
  `KilledMessage_ActorTriggered`, `ThreeFrames_MultiStepPath`, `UnknownVerbs_Approximate`, `[SkippableFact] All34Levels_EveryMoverClassified`
  (chaque thing avec `frame=` ou mû par un COG reçoit au moins un comportement ; compte `Unsupported` écrit dans le rapport).
- [ ] **Étape 2–4 :** échec, implémentation, vert. Le builder commence par produire le catalogue (fichiers COG distincts réellement utilisés), puis
  écrit les règles du classifieur à partir des COG stock les plus fréquents.
- [ ] **Étape 5 :** commit `feat(jk): lecture des COG et catalogue des comportements`.

### Tâche 9 : éléments mobiles → entités JA

**Fichiers :** créer `src/Jk.Pipeline/Movers/MoverConverter.cs` ; modifier `src/Jk.Pipeline/JkObjects.cs` ; test `tests/Jk.Pipeline.Tests/MoverConverterTests.cs`.

**Interfaces :**
- Consomme : T6 `JkMd3`, T8 `MoverBehavior`, sous-modèles `SubModelInput` (boîte orientée du modèle, relative à l'origine, comme `DoorConverter`).
- `MoverConverter.Convert(IReadOnlyList<MoverBehavior> behaviors, JkPreparedScene p, IReadOnlyDictionary<int, JkMd3> models, float scale, int firstSubModel, ImportLog log)
  → (ConvertedEntities, IReadOnlyList<SubModelInput>)`.

**Correspondances :** `TouchDoor` → `func_door` (direction et distance = frame 0 → frame 1, `speed` / `wait` du COG) ; `TwoStopLift` → `func_plat` ou `func_door`
selon le déclenchement ; `SwitchMove` → `func_door` / `func_train` ciblé par `func_button` (thing interrupteur) ou `trigger_multiple` (surface interrupteur) ;
`MultiStepPath` → `func_train` + `path_corner` par frame ; `ActorTriggered` → même mouvement déclenché par `trigger_multiple` sur le secteur + `JK_COG_ACTOR_TRIGGER` ;
`SurfaceCelSwitch` → `target_*` / shader équivalent ; portes à clé → ouvertes sans clé ; `Approximate` → `func_train` entre ses frames, déclenché à l'approche,
`JK_COG_APPROX` ; `Unsupported` → décor figé à la frame qui laisse le passage ouvert + `JK_COG_UNSUPPORTED`. Les rotations de frames (pitch/yaw/roll) passent
en `func_rotating` ou `func_door_rotating` quand le mouvement n'est qu'une rotation.

- [ ] **Étape 1 :** tests `TouchDoor_FuncDoorWithModel2AndSubModel`, `SwitchLift_ButtonTargetsMover`, `ThreeStepPath_TrainWithPathCorners`,
  `ActorDeathCog_BecomesProximityTrigger`, `UnknownCog_KeepsThingVisible_OpenPose`, `RotatingFrames_FuncDoorRotating`.
- [ ] **Étape 2–4 :** échec, implémentation, vert ; `JkObjects` numérote les sous-modèles à la suite.
- [ ] **Étape 5 :** commit `feat(jk): portes, ascenseurs et mecanismes en entites JA`.

### Tâche 10 : workflow, application et CLI

**Fichiers :** créer `src/Jk.Pipeline/JkWorkflowSteps.cs`, `src/Jk.Core/Text/UniStrings.cs`, `src/Jk.Core/Text/EpisodeReader.cs` ; modifier
`src/Swtor.Pipeline/GameWorkflowSteps.cs` (troisième implémentation), `src/Kotor2Ja.App/AppGame.cs`, `src/Kotor2Ja.App/Ui/MainScreen.cs` (nœud et Settings),
`src/Kotor2Ja.Cli/Program.cs`, `src/Kotor2Ja.Cli/Commands/GameOption.cs`, `ListModulesCommand.cs`, `ExportJaCommand.cs`, `README.md`, `scripts/publish.ps1` si besoin ;
tests `tests/Jk.Pipeline.Tests/JkWorkflowStepsTests.cs`, `tests/Kotor2Ja.Cli.Tests/` (option `--game jk`).

**Interfaces :**
- `JkWorkflowSteps(ExportOptions options) : IWorkflowSteps` ; `Modules()` renvoie les 34 niveaux (`ModuleListItem(KotorGame.JediKnight, <niveau>, <nom lisible>)`) ;
  `Preview` construit la scène (T4) et un aperçu avec objets et mobiles (vrais modèles) ; `Import` passe par `UseScene(..., objects: JkObjects.Convert)`.
- `GameWorkflowSteps(KotorWorkflowSteps kotor, SwtorWorkflowSteps swtor, JkWorkflowSteps jk)` ; `Modules()` concatène KOTOR et JK.
- Arbre : nœud `Jedi Knight` avec `Single player`, `Multiplayer`, `CTF` (groupe déduit de `JkLevelRef.Kind`) ; noms lisibles via `episode.jk` + `.uni`,
  sinon nom du fichier. Panneau `Settings` : champ `Jedi Knight` (rouge si invalide), `Detect` le remplit.
- CLI : `--game jk` (`kotor|kotor2|swtor|jk`) pour `list-modules` et `export-ja`.

- [ ] **Étape 1 :** tests `Modules_34Levels_GroupedByKind` (skippable), `GameOption_ParsesJk`, `Composer_JkPreviewRoutesToJk`, `NotInstalled_NodeGreyed`.
- [ ] **Étape 2–4 :** échec, implémentation, vert ; captures de l'application (option `--game jk --module 01narshadda --run-to export --screenshot`, existante, à étendre à `jk`)
  jointes au rapport. README : section Jedi Knight (dossier par défaut, préfixe `jk_`, ce qui est converti, pas d'ennemis).
- [ ] **Étape 5 :** commit `feat(jk): Jedi Knight dans l'application et la CLI`.

### Tâche 11 : passe complète sur les 34 niveaux

**Fichiers :** créer `tests/Jk.Pipeline.Tests/AllLevelsExportTests.cs` (catégorie `Slow`, skippable) ; rapport `docs/superpowers/plans/jk/reports/T11.md`.

- [ ] **Étape 1 :** test `[SkippableFact] AllLevels_ExportToStaging` : pour chaque niveau, Import + Export dans un dossier temporaire du work dir (pas `base`) ;
  aucune erreur ; collecte du bilan des codes `JK_*`, `TEXTURE_MISSING`, `LIMIT_*`.
- [ ] **Étape 2 :** lancer ; tableau par niveau dans le rapport. Objectif : `JK_COG_UNSUPPORTED = 0`, aucun matériau manquant. Chaque écart restant est
  corrigé dans la tâche concernée (nouvel agent) ou listé avec sa raison.
- [ ] **Étape 3 :** l'orchestrateur crée `jk_01narshadda.pk3`, un niveau à ascenseurs et `jk_m10.pk3` avec `Create PK3` et **demande à l'utilisateur de tester en jeu**
  (échelle, portes, ascenseurs, complétude). L'échelle est ajustée d'après son retour.
- [ ] **Étape 4 :** commit `test(jk): export des 34 niveaux et bilan`.

### Tâche 12 : revue de code de la branche

- [ ] **Étape 1 :** revue complète de `feature/jk-maps` contre la spec (superpowers:requesting-code-review, modèle le plus capable), rapport `T12.md`.
- [ ] **Étape 2 :** corrections par agents neufs, tests verts, commit. Fusion locale seulement après accord de l'utilisateur.
