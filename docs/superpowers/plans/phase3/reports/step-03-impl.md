# Étape 03 — Objets placés → MD3 : rapport d'implémentation

Statut : FAIT_AVEC_RESERVES (essais visuels dans le jeu non faits, voir « Réserves »).

## Fichiers

Créés :
- `src/Kotor.Core/Export/Md3Writer.cs`, `Md3ShaderWriter.cs` (shader `models/<dossier>/<texture>`).
- `src/Kotor.Core/Models/ModelPart.cs`, `ModelMeshBuilder.cs`.
- `src/Kotor.Core/Entities/ObjectModelResolver.cs` (UTP → `placeables.2da`/`modelname` ; UTD → `genericdoors.2da`/`modelname`, sinon `doortypes.2da`/`model` ; la partie porte sert à l'étape 04).
- `src/Kotor.Pipeline/Entities/PlaceableConverter.cs`, `ConvertedEntities.cs`.
- Tests : `tests/Kotor.Core.Tests/Export/Md3WriterTests.cs`, `tests/Kotor.Pipeline.Tests/PlaceableConverterTests.cs`.

Modifiés :
- `ModuleEntity` : `string? Model = null` (resref du modèle, minuscules). `ModuleEntitiesReader` le remplit (résolution silencieuse) pour les objets et les portes, et gagne `ReadObjects(res, info, log)` (GIT seulement, objets et portes, sans bilan `ENTITIES` ni lecture des pièces).
- `ImportStepResult` : `ConvertedEntities? Converted = null`.
- `PipelineSteps.Import` lit les objets du GIT (si le dossier du jeu existe ; sinon résultat vide, ce qui garde les tests à scène synthétique inchangés), appelle `PlaceableConverter.Convert`, puis ajoute au plan les entités (après le worldspawn et les spawns), la collision (un `SubMesh` « placeable_solid » dans `Solid`) et les textures TGA des modèles. `PipelineSteps.Export` écrit les `models/<dossier>/*.md3` dans le staging et ajoute les shaders de modèles au même fichier `shaders/<prefixe>_<carte>.shader`.

## Origine de l'écrivain MD3

**Nouveau.** Le projet TFU (`SWGLFU-Map-importer`) ne contient aucun écrivain MD3 (recherche `md3` / `IDP3` dans les `.cs`, `.md`, `.py` : aucun résultat). Format écrit d'après `qfiles.h` de Quake 3 : en-tête 108 octets, une image de 56 octets, surfaces (en-tête 108, triangles, un shader de 68, ST, XYZ+normale 8 octets par sommet), sommets en shorts au 1/64, normale en lat/long (octet bas = acos(z), octet haut = atan2(y,x), comme `NormalToLatLong` de q3map2), t = 1 − v. Sortie déterministe (surfaces triées par nom de texture, ordre ordinal). Extensions : découpe d'une partie en plusieurs surfaces au-delà de 4096 sommets ou 8192 triangles ; refus (`ArgumentException`) au-delà de 32 surfaces, d'une coordonnée > ±511.98, ou d'un nom de shader > 63 caractères.

## Clés vérifiées dans OpenJK-SWGL

- `code/game/g_misc.cpp:401-426` (`SP_misc_model_static`) : lit `modelscale_vec` (l.408), `modelscale` (l.411-415, scalaire uniforme prioritaire), `zoffset` (l.417), et exige `model` (l.419).
- `code/game/g_spawn.cpp:265-266` : `angles` (vecteur, `s.angles`) **et** `angle` (`F_ANGLEHACK`) remplissent `s.angles`. On écrit `angles "0 yaw 0"`.
- `code/cgame/cg_main.cpp:2192-2218` (`CG_CreateMiscEntFromGent`) : copie `model` (doit finir par `.md3`, sinon `Com_Error`), `s.angles`, `scale`, `s.origin`. `MAX_MISC_ENTS` = 2000 (l.2176) : le convertisseur plafonne à 2000 entités (au-delà, `PLACEABLE_SKIPPED`). Le dessin applique `AnglesToAxis(MiscEnt->angles, refEnt.axis)` (l.2270) et `modelScale` (l.2271).

## Orientation : pas de +90

La spec prévoyait `angles "0 <yaw + 90> 0"` « à vérifier ». **La vérification conclut qu'il ne faut pas de +90** : le convertisseur écrit `angles "0 <yaw> 0"`, yaw normalisé dans [0, 360).

Preuve :
1. Mathématique : les sommets du MD3 sont ceux du MDL, non tournés (même repère local que KOTOR, droit, Z vers le haut). `AnglesToAxis` avec yaw `a` donne `forward = (cos a, sin a, 0)` et `left = (−sin a, cos a, 0)` ; un point local (x, y) devient `x·forward + y·left`, soit une rotation anti-horaire de `a` autour de Z, identique à la rotation `Bearing` du GIT (anti-horaire, déjà utilisée par la vue pour placer les objets). Le « +90 » (0 = +Y chez KOTOR, 0 = +X chez Quake) ne s'appliquerait que si l'on tournait la géométrie du modèle elle-même, ce que l'on ne fait pas.
2. Empirique (essai jetable hors dépôt, `%TEMP%\diag3`) : sur tous les modules KOTOR 1, 206 objets au sol dont l'emprise PWK est nettement asymétrique (rapport largeur/profondeur > 30 %). On transforme les sommets de l'emprise par chaque hypothèse et on compte ceux qui tombent hors du walkmesh praticable (projection XY) :

| Hypothèse | Points hors walkmesh |
|---|---|
| `yaw` (retenue) | 9,9 % (95 / 962) |
| `−yaw` | 14,0 % |
| `yaw + 90` | 27,0 % |
| `yaw − 90` | 25,8 % |

   Le yaw seul est nettement le meilleur ; les variantes ±90 font près de trois fois plus de pénétrations de mur.

La comparaison visuelle dans la vue Prévisualiser n'a pas été faite (règle : ne pas cliquer dans l'application) ; elle reste à faire par l'orchestrateur (voir réserves). Le test `Collision_is_placed_rotated_and_scaled` fixe la même formule pour la collision PWK.

## Autres décisions

- Échelle : positions × `scale` ; si `max(|coord|)·scale > 511`, `k = ceil(max/511)`, sommets divisés par `k` et `"modelscale" "k"` (entier) sur l'entité.
- Un MD3 par modèle (cache par resref), textures chargées une fois, un bloc shader par texture : `models/<dossier>/<texture>` avec `map textures/<dossier>/<texture>.tga` et `rgbGen lightingDiffuse`. Alpha réel (même règle que `ShaderPlanner` : pas de masque de reflet sauf `punchthrough`/`decal`) : `cull none` + `alphaFunc GE128`. Texture introuvable : damier `missing` (code `TEXTURE_MISSING`).
- Les MD3 de KOTOR 2 vont dans `models/kotor2/` (`KotorGames.AssetFolder`).
- Collision : PWK (lue par `WokReader`, format BWM identique, sommets locaux au modèle) tournée de `yaw`, translatée, mise à l'échelle, ajoutée à `ExtraSolid` puis à la collision solide. Sans PWK : `PLACEABLE_NO_PWK` (une fois par modèle).
- Codes journal (messages en anglais) : `PLACEABLE_SKIPPED` (template/UTP/apparence/modelname/MDL absents ou illisibles, modèle sans maillage rendu, MD3 non écrit, plafond de 2000), `PLACEABLE_NO_PWK`, `PLACEABLES` (bilan : convertis, sautés, MD3 uniques), `PLACEABLES_UNREADABLE`, plus `MESH_*`/`MDL_NO_UV`/`TEXTURE_*` repris du constructeur de maillages.

## Bilan tar_m02aa (KOTOR 1, échelle 40)

Liste complète dans `out/placeables-tar_m02aa.txt` (régénérée par le test) :
- 26 objets au GIT : **23 convertis, 3 sautés** (`plc_invis` : modèle sans maillage rendu), **9 MD3 uniques**, 83 triangles de collision PWK.
- Aucun `modelscale` nécessaire sur cette carte. Toutes les origines sont dans la boîte de scène élargie de 200 unités. L'export complet produit un pk3 avec les `models/kotor/*.md3`, toutes les TGA référencées par les shaders et les entités `misc_model_static` dans le lump du BSP.

## Tests

- `Md3WriterTests` (4) : en-tête / comptes / noms de shader / position × 64 / t = 1 − v ; refus de 600 ; déterminisme ; découpe à 4096 sommets.
- `PlaceableConverterTests` (11) : `Large_model_gets_modelscale`, `Shared_model_written_once`, `Missing_template_is_logged`, plus modèle sans `modelscale` et yaw normalisé, KOTOR 2, collision placée, `PLACEABLE_NO_PWK`, modèle illisible, et les trois tests sur jeu installé (`Real_tar_m02aa_placeables`, `Real_tar_m02aa_pk3_contains_md3`, `Real_kotor2_001ebo_placeables_use_kotor2_folders`).
- `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur. `dotnet test Kotor2Ja.sln` : vert (Cli 20, Compiler 76, Core 144, Pipeline 50 ; aucun test ignoré sur cette machine).

## Réserves

1. **Essais visuels non faits** : l'orientation (preuve statistique + mathématique, pas de capture), le sens d'enroulement des triangles du MD3 (conservé tel que KOTOR/BSP, CCW) et l'éclairage `lightingDiffuse` sont à confirmer dans le jeu.
2. Les placeables invisibles (`plc_invis`, sans maillage rendu mais avec PWK) sont sautés comme le veut la spec : leur collision invisible n'est pas ajoutée.
3. La vue Importer/Exporter de l'application ne montre pas encore les MD3 (étape 05).
4. `Import` relit le GIT via un nouveau résolveur (le workflow l'a déjà lu pour la vue) : légère duplication de lecture, sans effet sur le résultat.
5. Aucun commit (consigne).

## Correction 1

Statut : FAIT. Commandes finales : `dotnet build Kotor2Ja.sln` (0 avertissement, 0 erreur) et `dotnet test Kotor2Ja.sln` (vert : Compiler 76, Cli 20, Core 149, Pipeline 53 ; aucun test ignoré). Aucun commit, rien écrit dans le dossier Jedi Academy.

### Critical — limites par surface

- Valeurs du moteur SWGL : `SHADER_MAX_VERTEXES = 1200` et `SHADER_MAX_INDEXES = 6*1200` (`code/qcommon/qfiles.h:33-34`) ; `R_LoadMD3` refuse `numVerts >= SHADER_MAX_VERTEXES` et `numTriangles*3 >= SHADER_MAX_INDEXES` (`code/rd-vulkan/tr_model.cpp:1105-1118`). Soit au plus **1199 sommets** et **2399 triangles** par surface.
- Fait : `Md3Writer.MaxVerticesPerSurface = 1199`, `MaxTrianglesPerSurface = 2399` (constantes citées dans le code). La découpe d'une partie en plusieurs surfaces existait déjà, elle travaille maintenant avec ces bornes.
- Tests : `Md3WriterTests.Engine_limits_are_the_swgl_loader_limits`, `Surface_at_the_limit_stays_whole_and_one_over_is_split` (1197 sommets entiers ; 1200 sommets coupés en 1197 + 3), `Triangle_limit_splits_a_part_with_shared_vertices` (2399 triangles : une surface ; 2400 : deux), `Large_part_is_split_below_engine_limits` (remplace l'ancien test à 4096). `PlaceableConverterTests.Model_with_more_surfaces_than_the_engine_limit_is_split_into_files` relit aussi chaque surface (`< 1200` sommets, `*3 < 7200`).

### Important 1 — plus de surfaces que la limite MD3

- `MD3_MAX_SURFACES` vaut `32 + 32` (`code/qcommon/qfiles.h:105`), soit **64** (macro sans parenthèses). Le chargeur `R_LoadMD3` ne la vérifie pas (aucune autre occurrence dans le code, `tr_model.cpp:1087-1091` alloue simplement `numSurfaces`) ; c'est la borne déclarée, retenue : `Md3Writer.MaxSurfaces = 64` (c'était 32).
- Fait : nouveau `Md3Writer.WriteSplit` qui répartit les surfaces (ordre des textures) en fichiers de 64 surfaces au plus ; `Write` garde l'erreur au-delà de 64. `PlaceableConverter` écrit `models/<dossier>/<modele>_p0.md3`, `_p1`… (nom inchangé, sans suffixe, si un seul fichier ; l'en-tête MD3 porte le même nom). Une instance émet une entité `misc_model_static` par fichier (même origine, mêmes angles, même `modelscale`) ; la collision PWK n'est ajoutée qu'une fois par instance ; un fichier par modèle et par morceau, partagé entre instances. Le plafond de 2000 entités du moteur compte maintenant les entités (donc les fichiers).
- Tests : `Md3WriterTests.Write_rejects_more_surfaces_than_the_model_limit_but_WriteSplit_splits` ; `PlaceableConverterTests.Model_with_more_surfaces_than_the_engine_limit_is_split_into_files` (65 surfaces, 2 instances : 2 fichiers `_p0`/`_p1`, 4 entités, 2 jeux de collision, bilan `PLACEABLES`). Les tests existants confirment que le nom reste sans suffixe pour un modèle d'un seul fichier (`Shared_model_written_once`, `Large_model_gets_modelscale`).

### Important 2 — objets sans maillage rendu

- Conservés entièrement sautés, collision comprise. Seule la raison change : `PLACEABLE_SKIPPED` dit « model '<m>' has no rendered mesh (invisible blocker, collision not added) ».
- Test : `PlaceableConverterTests.Model_without_rendered_mesh_is_skipped_with_its_collision` (aucune entité, aucun MD3, aucune collision, message vérifié).

### Minor — normales sur 256

- `EncodeNormal` utilise `256/2π` ; le moteur décode un octet avec `lat *= (FUNCTABLE_SIZE/256)` (`code/rd-vulkan/tr_model.cpp:1188-1191`), soit 256 pas par tour.
- Test : `Md3WriterTests.Normals_are_encoded_on_256_steps` (X → `0x0040`, +Y → `0x4040`, −Y → `0xC040`, Z → `0x0000`).

### Minor — pas de dossier de jeu

- `PipelineSteps.ConvertObjects` journalise `PLACEABLES_NO_GAME_DIR` : « No game folder available: placeable conversion skipped, the map has no placed objects. ».
- Test : `FinalReviewFixTests.Import_without_game_folder_logs_that_placeables_are_skipped`.

### Réserves

- Aucun essai dans le jeu (consigne). Le modèle découpé en plusieurs entités et la borne de 64 surfaces (non vérifiée par le chargeur) sont à confirmer visuellement ; si un chargement échouait, repasser `MaxSurfaces` à 32 (constante unique).
- Les autres points mineurs de la relecture (relecture du GIT, normales sous échelle non uniforme, horodatage du zip) ne faisaient pas partie de cette correction.
