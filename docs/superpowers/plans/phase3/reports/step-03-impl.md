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
