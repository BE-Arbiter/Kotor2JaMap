# Phase 3 — Relecture finale de la branche `phase3-contenu` (0699f27..26b80fd)

Méthode : lecture de la spec, du plan (Global Constraints, Review Focus), des briefs `step-0*.md`, des rapports, du registre `progress.md`, du code de la branche (HEAD 26b80fd), et des sources du moteur `OpenJK-SWGL/code` (qcommon, rd-vulkan, game, cgame). Vérifications exécutées :

- `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur.
- `dotnet test Kotor2Ja.sln` : Compiler 80, Cli 20, Core 164, Pipeline 74, tous verts, **aucun test sauté** (les tests sur jeux installés ont tourné).
- Deux exports CLI de `tar_m02aa` dans deux dossiers temporaires : comparaison octet à octet du BSP et du staging.
- Un export CLI de `001ebo` (KOTOR 2) dans un dossier temporaire, puis une sonde jetable (projet hors dépôt, dans le dossier temporaire de la session) sur 5 modules KOTOR 2 : portes verrouillées ou liées, lumières `ambient_only`, couleurs de sommet saturées.
- `export-ja` lancé avec un fichier de configuration JSON tronqué.

Aucun code n'a été modifié et rien n'a été commité.

**Incident à signaler.** Mon premier essai de déterminisme a utilisé un fichier de configuration aux chemins vides. Une chaîne vide vaut « valeur par défaut » (`ConfigLoader.Load`, `Str`). La CLI a donc écrit `kotor_tar_m02aa.pk3` dans le vrai `GameData\base` de SWGL_DEV (12:53), et un dossier de travail dans `%LOCALAPPDATA%\Kotor2Ja\work\tar_m02aa`. Le pk3 écrasé est la sortie exacte de HEAD : son staging est identique, octet pour octet, aux deux exports temporaires. La version précédente de ce fichier est perdue. La suppression de `%LOCALAPPDATA%\Kotor2Ja` a été refusée par les permissions : ce dossier est resté en place, à supprimer à la main si besoin.

## Verdict global

**Prêt à fusionner après les essais en jeu de l'étape 08**, avec un mineur reporté à corriger avant la fusion (T02 : plantage de `export-ja` sur une configuration illisible).

- Aucun constat Critical ni Important vérifié.
- Les cinq points du Review Focus sont couverts par des tests. Pour le point 5, la couverture des portes est partielle (voir plus bas).
- Le BSP et le contenu du pk3 sont déterministes. Le fichier pk3 lui-même ne l'est pas, à cause des horodatages du zip (mineur connu).

## 1. Cohérence entre étapes

### KOTOR 2 de bout en bout (objets et portes)
- Le préfixe vient d'une seule source, `KotorGames.MapPrefix` / `AssetFolder` (`KotorGame.cs`). Il est utilisé par :
  - `ShaderNames.PrefixFor` / `LightmapPrefixFor` ;
  - `Md3ShaderWriter.ModelPrefix`, donc `models/kotor2/` ;
  - `ShaderPlanner.Plan/Write` ;
  - le nom du fichier de shaders et le nom du pk3 (`PipelineSteps.cs`, Export et CreatePk3).
- `PlaceableConverter` et `DoorConverter` reçoivent `game` et prennent tous deux le préfixe de `Md3ShaderWriter.ModelPrefix(game)`.
- Les textures des MD3 sont ajoutées sous `ShaderNames.PrefixFor(game)` (`PipelineSteps.ApplyObjects`).
- Vérifié sur données réelles (export de `001ebo`) :
  - 11 MD3 sous `models/kotor2/` (dont la porte `dor_eh1.md3`) ;
  - `PLACEABLES 16 converted, 27 skipped` et `DOORS 11 door(s) converted (11 locked)` ;
  - le test `Real_k2_preview_and_export` vérifie qu'aucune entrée `textures/kotor/` n'apparaît.
- MDL de KOTOR 2 : la détection par pointeur de fonction et le décalage de +8 octets (`MdlReader.cs`, `K2MeshExtra`) ne s'appliquent qu'aux deux décalages lus après les champs « dirt ». Cela concorde avec xoreos. Les maillages de portes et d'objets de 001ebo se lisent.
- Verrouillage des portes sur données réelles (sonde) : 001ebo 11/11 verrouillées par l'UTD, 003ebo 0/2, 101per 8 verrouillées et 3 liées, 201tel 9 et 1, 301nar 0/17. Les valeurs sont variées et plausibles, ce qui lève en partie la réserve « Locked/LinkedToModule testés seulement sur données synthétiques » (T04).
- Restes mineurs déjà reportés : `CompilerNames.CollisionShader` = `textures/kotor/collision` et `ShaderNames.FogName` sans préfixe de jeu. Aucune collision de fichier n'en résulte : le shader de collision n'a pas de fichier, et le brouillard n'est pas utilisé par les modules KOTOR. Aucun module ne porte le même nom dans les deux jeux (117 noms KOTOR et 82 noms KOTOR 2, intersection vide), donc `maps/<carte>.bsp` et `WorkDir/<carte>` ne se heurtent pas en pratique.

### Lumières et éclairage
- Une seule chaîne : nœuds lumière (`MdlReader`) → `SceneLight` → filtre fini/positif (`ModuleExporter.cs`, BuildPlan) → `PointLight` en unités JA → `Compiler.EffectiveLighting` → `VertexLighting.Evaluate(position, normale)` (`BspWriter`).
- Les surfaces à lightmap gardent des couleurs de sommet blanches. Le décalage de surbrillance vaut 0 partout (Ruling), ce qui concorde avec `r_mapOverBrightBits` 0 (rd-vulkan `tr_init.cpp:989`).
- Mesures sur les surfaces sans lightmap :
  - `tar_m02aa` : 599 sommets blancs (255,255,255) sur 2931 ;
  - `001ebo` : 100 sur 12410.
  Une couleur de sommet à 255 rend la texture telle quelle, sans surbrillance : rien d'aberrant.
- Les MD3 utilisent `rgbGen lightingDiffuse` sans grille de lumière (hors périmètre de la spec, mineur T05 « objets un peu clairs ») : à juger en jeu.

### MD3 partagés entre objets et portes
- Même générateur pour les deux : `PlaceableConverter.BuildModel`, appelé avec `loadPwk: false` pour les portes. Le chemin `models/<dossier>/<modele>.md3` est le même, et les octets sont identiques pour un même modèle et une même échelle.
- `ConvertedEntities.Merge` déduplique par chemin, par texture et par bloc de shader.
- Une porte dont le modèle a été découpé ou réduit (`modelscale`) est sautée et journalisée (`DOOR_SKIPPED`). C'est cohérent : le moteur n'applique pas `modelscale` à `func_door` (`g_misc.cpp:401-417` seulement).

### Vues de l'application face aux fichiers produits
- La vue Importer lit les octets mêmes des MD3 écrits plus tard (`Md3Reader.Read(asset.Data)`) et pose les instances d'après les entités produites (`InstancesOf`).
- La vue Exporter relit le BSP, le dossier de staging (MD3, TGA) et le fichier de shaders (`alphaFunc`).
- Ce que les vues reproduisent du moteur :
  - le placement (échelle, puis yaw, puis translation, comme `CG_DrawMiscEnts` avec `ScaleModelAxis`) ;
  - la porte posée à `origin` avec `modelAngles` (`cg_ents.cpp:1354-1358`) ;
  - `modelscale` ignoré pour les portes ;
  - l'enroulement inversé (`Md3Reader`).
- La CLI et l'application passent par les mêmes `PipelineSteps.Import/Export/CreatePk3` (`ModuleExporter.Export`, `KotorWorkflowSteps`).

### Mode automatisé
- `AutomationParser` : arguments validés, messages en anglais, code 2 sur erreur d'analyse.
- `AppGame.StepAutomation` :
  - attend la liste des modules, enchaîne les étapes (`_busy` est positionné de façon synchrone, donc pas de double lancement) ;
  - échoue proprement : code 1 avec `--exit` ;
  - fait la capture après 3 images.
- Le portable a été essayé par l'étape 07 (code 0, capture non noire).

### Build portable
- `scripts/publish.ps1` :
  - `$ErrorActionPreference='Stop'` et contrôle de `$LASTEXITCODE` après chaque publication ;
  - retire la configuration de développement, vérifie la présence des deux exécutables, puis crée le zip.
- `build-latest.yml` :
  - `--target ${{ github.sha }}`, `concurrency` ;
  - les tests sur jeux se sautent sur le runner (`TestEnv` : variable d'environnement ou chemin Steam par défaut).
- La configuration d'exemple a des chaînes vides, qui valent « détection ».

## 2. Review Focus : couverture par des tests

| # | Point | Verdict | Tests |
|---|---|---|---|
| 1 | Lightmap entièrement invalide : gris et journal | **Couvert** | `LightmapRepairTests.Repair_all_invalid_gives_gray` (4×4 de bruit vert, alpha 1, devient (128,128,128,255)) ; `ModuleSceneBuilderTests.Lightmap_without_valid_texel_is_logged_and_has_no_alpha` (code `LIGHTMAP_NO_VALID_TEXEL`). |
| 2 | `.mod` à la place des `.rim` ; dossier de jeu absent | **Couvert** (au niveau du workflow) | `Kotor2Tests.Resolver_prefers_mod_over_rim` (résolution et catalogue), `ModuleCatalogTests.ListModuleNames_excludes_s_rims_and_lists_mods`, `WorkflowControllerTests.Missing_game_folder_is_reported_not_installed` (aucune exception, `GAME_NOT_FOUND`), `GameLocatorTests.Detect_missing_games_null`, `GameOptionTests.Missing_install_message_is_english_and_names_the_game`. `AppGame` lui-même n'a pas de test unitaire (non testable) ; l'essai portable sans configuration de l'étape 07 tient lieu de preuve. |
| 3 | Modèle au-delà de ±512 unités : `modelscale`, sans débordement int16 | **Couvert** | `PlaceableConverterTests.Large_model_gets_modelscale` (`modelscale` 3, chaque sommet dans ±511·64, valeur 25600 vérifiée). |
| 4 | Porte vers un autre module : verrouillée | **Couvert** (entité) | `DoorConverterTests.Transition_door_locked` (`spawnflags` 16), `Locked_utd_door_is_locked_and_summary_is_logged`. « Ne s'ouvre pas » repose sur `MOVER_LOCKED` (`g_local.h:521`, `SP_func_door` `g_mover.cpp:1446-1540` : déclencheur créé mais porte verrouillée). À voir en jeu. |
| 5 | Deux objets ou deux portes, un modèle : un seul MD3 | **Objets : couvert ; portes : partiel** | `PlaceableConverterTests.Shared_model_written_once` (3 instances, 1 MD3). Pour les portes, `DoorConverterTests.Merge_keeps_one_copy_of_shared_assets` déduplique deux résultats issus de deux appels distincts. Aucun test n'affirme « 2 portes d'un même modèle dans un seul `Convert` donnent 1 MD3 » (`Submodel_numbers_follow_first_index...`, `DoorConverterTests.cs:119-127`, a deux portes du même modèle mais ne vérifie pas `r.Models`). Le code est le même dictionnaire que pour les objets : risque faible, voir Minor m6. |

## 3. Constats

### Critical
Aucun.

### Important
Aucun.

### Minor

- **m1 (T02 reporté, à corriger avant fusion) : `export-ja` plante sur une configuration illisible.**
  - Lieu : `src/Kotor2Ja.Cli/Commands/ExportJaCommand.cs:28`, `ConfigLoader.Load(cfgPath)` sans try/catch. Les commandes `list-modules` et `export-obj` passent, elles, par `GameOption.ResolveDir`, qui se replie sur les défauts.
  - Scénario vérifié : avec `{ "workDir": "x", ` (JSON tronqué, par exemple une saisie inachevée dans la configuration du zip portable), la commande affiche `Unhandled exception: System.Text.Json.JsonReaderException...` suivi de la pile d'appels.
  - Pourquoi avant la fusion : la configuration est maintenant éditée à la main par les utilisateurs du zip. La correction est courte : un message en anglais et le code 1, comme l'application (`AppGame.cs:69-73`).
- **m2 (T03 reporté) : le pk3 n'est pas reproductible octet à octet.**
  - Lieu : `src/Kotor.Core/Export/Pk3Builder.cs:40`. `CreateEntryFromFile` copie la date de modification du fichier, et le staging est recréé à chaque export.
  - Vérifié : deux exports de `tar_m02aa` donnent un BSP identique (SHA-256 `61544d2e…`) et un staging identique (`diff -r`), mais des pk3 différents (`8c3a4a…` contre `d5de95…`).
  - La contrainte « la compilation reste déterministe » est tenue. Pour un pk3 reproductible, fixer `entry.LastWriteTime`. Non bloquant.
- **m3 : le chemin d'un MD3 ne dépend ni de la carte ni de l'échelle.**
  - Lieu : `src/Kotor.Pipeline/Entities/PlaceableConverter.cs:152`.
  - Scénario : on exporte une carte avec `--scale 32` (comme les variantes `_s32` déjà présentes dans `base`) et une autre à 40. Les deux pk3 contiennent `models/kotor/plc_footlker.md3` avec des sommets d'échelles différentes. Tous les pk3 de `base` sont chargés ensemble : celui qui gagne dans l'ordre de recherche impose sa géométrie à l'autre carte. Les objets ont alors la mauvaise taille et ne correspondent plus à leur collision PWK, qui est cuite à la bonne échelle dans le BSP.
  - Avec l'échelle par défaut partout, les octets sont identiques et il n'y a aucun effet. Non bloquant ; à documenter, ou mettre l'échelle dans le chemin si elle diffère de 40.
- **m4 (T01 Minor 4 reporté) : `nopicmip` / `nomipmaps` s'appliquent aussi à la texture diffuse.**
  - Lieu : `src/Kotor.Core/Export/ShaderPlanner.cs:46`. Ce sont des drapeaux du shader entier : `tr_shader.cpp:1287-1291` et `:4803-4804` s'appliquent à toutes les étapes.
  - Scénario : les murs et sols à lightmap n'ont plus de mipmaps, d'où un scintillement ou du moiré au loin ; `r_picmip` est ignoré.
  - À juger en jeu. Si c'est visible, la dilatation suffit peut-être sans `nomipmaps` : à mesurer.
- **m5 : une lumière `ambient_only` agit sur toute la carte, sans rayon.**
  - Lieu : `src/Kotor.Compiler/Bsp/VertexLighting.cs:61`.
  - Scénario : un module avec plusieurs lumières `ambient_only` les additionne partout, et les surfaces sans lightmap saturent.
  - Conforme au Ruling de l'étape 01, mais sans effet mesuré : la sonde trouve 0 lumière `ambient_only` sur 001ebo, 003ebo, 101per, 201tel, 301nar et tar_m02aa. Non bloquant ; à surveiller sur un module où le cas existe.
- **m6 : test manquant (Review Focus 5, portes).** Il manque un test « deux portes d'un même modèle dans un seul `DoorConverter.Convert` donnent un seul MD3 », et un test « un objet et une porte d'un même modèle dans un même export donnent un seul fichier dans le pk3 » (voir la section 2).
- **m7 : fichier temporaire dans `GameData\base`.**
  - Lieu : `src/Kotor.Core/Export/Pk3Builder.cs:19`, qui écrit `kotor_<carte>.pk3.tmp`, renommé ou supprimé dans le `finally`.
  - C'est une écriture transitoire hors de la liste « `kotor_<carte>.pk3` / `kotor2_<carte>.pk3` ». Le comportement existait avant la phase 3 et le moteur ne charge que `*.pk3`. Sans conséquence ; à mentionner dans la règle si l'on veut être exact.
- **m8 : des tests écrivent dans le dépôt.**
  - Lieux : `tests/Kotor.Pipeline.Tests/DoorConverterTests.cs:186-193` et `PlaceableConverterTests.cs:250-257`, qui écrivent dans `<dépôt>/out/`.
  - Le dossier `out/` est ignoré par git et la convention existait déjà en phase 2 (le plan de l'étape 03 le prévoit), mais cela s'écarte de « les tests écrivent dans des dossiers temporaires ».

## 4. Tri des mineurs reportés du registre

| Étape | Constat reporté | Décision |
|---|---|---|
| 01 | Surbrillance 0 qui sature les défauts du compilateur quand l'ARE est illisible | Garder reporté (cas rare, surfaces sans lightmap seulement). |
| 01 | Réserves périmées du rapport ; `ambient_only` non vérifié sur une valeur réelle | Garder reporté (voir m5 : aucun cas réel trouvé). |
| 01 | Minors 2, 3, 6, 7, 8 de la relecture 01 | Garder reportés. |
| 01 | Minor 4 (`nomipmaps` sur la diffuse) | **À juger en jeu** (m4) ; à corriger seulement si le scintillement se voit. |
| 02 | `export-ja` plante si la configuration est illisible | **À corriger avant fusion** (m1, vérifié). |
| 02 | `IsInstalled` à chaque image ; BSP non préfixé par jeu ; `FogName` / `CollisionShader` | Garder reportés (aucun nom de module commun aux deux jeux ; shader de collision sans fichier). |
| 03 | `MaxSurfaces` = 64 non vérifié par le chargeur | Garder reporté. Confirmé : `R_LoadMD3` (rd-vulkan `tr_model.cpp:970-1210`) ne vérifie pas `numSurfaces`, seulement sommets < 1200 et triangles·3 < 7200, que `Md3Writer` respecte. |
| 03 | MD3 laissé pour un objet refusé par le plafond de 2000 ; double lecture du GIT ; normales sous échelle non uniforme | Garder reportés. |
| 03 | Horodatage du zip | Non bloquant (m2) ; à corriger si l'on veut un pk3 reproductible. |
| 03 | Enroulement à vérifier en jeu | Clos (corrigé et vérifié en jeu à l'étape 05). |
| 04 | Sous-modèle à 0 brush ; `MAX_SUBMODELS` classé « collision » ; `MAX_MODELS` | Garder reportés (la boîte a toujours une épaisseur minimale de 2 ; plus de 510 portes irréaliste). |
| 04 | `Locked` / `LinkedToModule` testés seulement en synthétique | Partiellement levé par la sonde (section 1) ; à confirmer en jeu. |
| 05 | Attente sans délai ; codes `PREVIEW_MODEL_*` sans test ; double journal ; objets clairs | Garder reportés ; objets clairs à juger en jeu. |
| 07 | Save synchrone ; N1 ; N2 ; double `<summary>` ; préférence SWGL ; VDF ancien ; `workDir` par défaut ; Save non atomique | Garder reportés. N1 n'arrive que si un utilisateur dépose une configuration non inscriptible à côté de l'exécutable (le zip n'en livre pas). Le cas « Save non atomique » est sans effet sur l'application, qui se replie sur les défauts ; côté CLI, il est couvert par la correction m1. |

## 5. Risques en jeu (étape 08)

1. **Une carte qui ne se charge pas à cause d'un MD3.**
   - `CG_CreateMiscEnts` (`cgame/cg_main.cpp:2233-2236`) lève `Com_Error(ERR_DROP, "misc_model_static failed to load model '%s'")` si un seul MD3 est refusé par `R_LoadMD3`.
   - Causes couvertes par le code : version 15, au moins une image, sommets ≤ 1199 et triangles ≤ 2399 par surface (`Md3Writer`), extension `.md3`, nom ≤ 63 caractères.
   - Plafond `MAX_MISC_ENTS` 2000, sinon `ERR_DROP` : respecté (`PlaceableConverter.MaxMiscModels`, une entité par fichier).
   - En cas d'échec, la console nomme le modèle fautif.
2. **Portes : premier essai réel des sous-modèles `*N` en jeu.**
   - Format vérifié sur le papier :
     - lump des modèles : monde, puis un modèle par porte, avec 0 surface et 1 brush ;
     - brushes hors des feuilles de l'arbre (`Real_tar_m02aa_doors` vérifie `LeafBrushes`) ;
     - 6 côtés axiaux en tête (`CM_BoundBrush`) ;
     - shader `textures/system/clip` marqué `CONTENTS_SOLID` dans le lump ;
     - brushes relatifs à `origin` (`SetBrushModel`, `pos1 = origin`).
   - À regarder en jeu :
     - une porte non verrouillée s'ouvre au toucher et monte de sa hauteur moins 8 ;
     - une porte de transition ou verrouillée reste fermée ;
     - la boîte bloque le joueur ;
     - le MD3 est orienté comme l'ouverture.
3. **Pk3 anciens dans `base` qui masquent la sortie.**
   - Présents : `kotor_tar_m02aa_s32.pk3`, `_s36`, `_s40` (14 oct.), ainsi que `kotor_danm16.pk3` et `kotor_ebo_m12aa.pk3` (07:06 et 07:22 le 15 oct., à vérifier : peut-être d'avant l'étape 01).
   - Ils partagent l'espace de noms `textures/kotor/` : textures, lightmaps du même module pour les variantes, et blocs de shader de même nom avec les anciennes définitions (sans `nopicmip`, alpha de reflet traité en découpe).
   - Les retirer ou les régénérer avant de juger le vert, l'alpha de reflet ou l'éclairage, sinon les essais ne sont pas fiables (le registre le signalait déjà pour tar_m02aa).
   - `kotor_tar_m02aa.pk3` est la sortie actuelle de HEAD (voir l'incident en tête de rapport).
4. **Éclairage.** Objets MD3 sans grille de lumière (m4/T05), surfaces sans lightmap saturées près des lumières à multiplicateur 2 (20 % des sommets sans lightmap de tar_m02aa sont à 255), zone blanche de Taris (Ruling de l'étape 01) : à comparer avec la vue Preview.
5. **Entités.**
   - `misc_model_static` : `model`, `origin`, `angles "0 yaw 0"`, `modelscale` (`g_misc.cpp:401-424`). L'entité est libérée après la création côté cgame, sans coût en `MAX_GENTITIES`.
   - `func_door` : `model *N`, `model2`, `origin`, `modelAngles`, `angle -1`, `lip 8`, `speed 200`, `wait 3`, `spawnflags 16` (`g_spawn.cpp:232-267`, `g_mover.cpp:1446-1540`). Les clés sont conformes.
   - La chaîne d'entités n'a pas de plafond au chargement (`cm_load.cpp:488`).

## 6. Déterminisme

- **Compilation : déterministe**, vérifié par deux exports réels de `tar_m02aa` qui donnent un BSP identique et un staging identique (shaders, MD3, TGA).
- Sources d'ordre examinées :
  - modèles et textures triés en ordinal (`OrderBy(Path)`, `SortedDictionary`) ;
  - surfaces MD3 triées par texture ;
  - entités dans l'ordre du GIT ;
  - `Merge` trié ;
  - aucun parallélisme dans le pipeline ;
  - nombres formatés en culture invariante (`Num`).
- **Pk3 : contenu déterministe, fichier non reproductible** (horodatages du zip, m2).

## 7. Écritures

- Code de production :
  - staging et journal sous `WorkDir/<carte>` ;
  - `kotor_<carte>.pk3` ou `kotor2_<carte>.pk3` dans `JaBaseDir`, avec le `.tmp` transitoire (m7) ;
  - configuration à côté de l'exécutable, sinon `%APPDATA%\Kotor2Ja` (ajouté par la correction de l'étape 07) ;
  - capture PNG au chemin donné par `--screenshot`.
- Les dossiers des jeux (KOTOR, KOTOR 2, détection Steam) sont lus seulement : aucun appel d'écriture vers `KotorDir` ou `Kotor2Dir`.
- Tests :
  - dossiers temporaires, avec `JaBaseDir` temporaire pour tous les exports réels (`ModuleExporterTests`, `Kotor2Tests`, `DoorConverterTests`, `PlaceableConverterTests`, `FinalReviewFixTests`) ;
  - exception : `out/` du dépôt (m8).
