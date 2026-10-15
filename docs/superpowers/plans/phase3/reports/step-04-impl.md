# Étape 04 — Portes qui s'ouvrent : rapport d'implémentation

Statut : FAIT_AVEC_RESERVES (aucun essai dans le jeu, voir « Réserves »).
Commandes finales : `dotnet build Kotor2Ja.sln` (0 avertissement, 0 erreur) ; `dotnet test Kotor2Ja.sln` vert (Compiler 80, Cli 20, Core 150, Pipeline 61 ; aucun test ignoré). Aucun commit, rien écrit dans le dossier Jedi Academy (exports de test dans des dossiers temporaires).

## Fichiers

Créés : `src/Kotor.Compiler/SubModelInput.cs`, `src/Kotor.Compiler/Bsp/BrushBounds.cs`, `src/Kotor.Pipeline/Entities/DoorConverter.cs`, `tests/Kotor.Compiler.Tests/SubModelTests.cs`, `tests/Kotor.Pipeline.Tests/DoorConverterTests.cs`.

Modifiés : `LayoutFile.cs` (+ `LayoutFileTests.cs`), `CompileInput.cs` (propriété `SubModels`, vide par défaut), `Compiler.cs`, `Bsp/BspWriter.cs` (type `BspSubModel`), `Bsp/BspReader.cs` (`BspFile.Models`, `BrushSideCounts`, `LeafBrushes`, `BspModelInfo`), `ModuleEntity` (`LinkedToModule`, `Locked`), `ModuleEntitiesReader`, `ObjectModelResolver.IsDoorLocked`, `PlaceableConverter` (`BuildModel`/`ModelInfo`/`ResolverAssets` rendus `internal`, `ModelOf` gère les portes, bornes du modèle), `ConvertedEntities.Merge`, `PipelineSteps`, `PlaceableConverterTests` (un MD3 de porte est un `model2`, pas un `model`).

## Doorhooks : ordre des quaternions

Le fichier LYT porte `w x y z`, **w en premier**. Preuve sur le vrai `m02aa.lyt` (tar_m02aa) : identité de rotation = `... 0.0 0.0 0.0 1.0` pour la porte `door_01` dont le Bearing GIT est −π, et `0.923879 0 0 0.382684` pour `door_06` dont le Bearing GIT vaut 0.785 (45°) : 2·atan(z/w) = 45°. `0.707107 0 0 0.707107` = 90° (`door_07`, Bearing 1.5708). Le parseur lisait `x y z w` (erreur de commentaire « confirmé PyKotor » : le lecteur PyKotor `io_lyt.py` passe les jetons dans l'ordre du fichier à son constructeur, ce qui ne dit rien de l'ordre w/x). Corrigé : `new Quaternion(t[7], t[8], t[9], t[6])`. Tests : `Doorhook_quaternion_order` (valeurs asymétriques + rotation de 90° vérifiée sur +X) et le test existant mis à jour. Le yaw d'une porte vient toujours du Bearing du GIT, les doorhooks ne servent pas à placer les portes.
Références : https://raw.githubusercontent.com/NickHugi/PyKotor/master/Libraries/PyKotor/src/pykotor/resource/formats/lyt/io_lyt.py (jetons 0 = pièce, 1 = porte, 3-5 = position, 6-9 = orientation).

## Conventions vérifiées dans OpenJK-SWGL (`C:\Users\lambe\source\repos\OpenJK-SWGL\code`)

- **Sous-modèles**, `qcommon/cm_load.cpp:127-184` (`CMod_LoadSubmodels`) : pour chaque modèle i ≥ 1, le moteur fabrique un « leaf » propre : `numLeafBrushes = numBrushes`, indices `firstBrush + j` (l.170-175), et surfaces `firstSurface + j` (l.177-182). Il n'y a donc **pas besoin de feuille de l'arbre** : les brushes sont juste ajoutés au lump des brushes après ceux du monde et référencés par le lump des modèles (bornes, 0 surface, firstBrush, numBrushes). Le monde (modèle 0) est ignoré (l.165). Mins/maxs sont élargis de 1 (l.154-158).
- **Contenu** : `cm_load.cpp:264-271` (`CMod_LoadBrushes`) : `contents = shaders[brush.shaderNum].contentFlags`. Le contenu solide vient donc du lump des shaders du BSP (pas du fichier `.shader`). Le shader du sous-modèle est ajouté au lump avec le contenu solide (1), et son indice est écrit dans le brush et ses côtés.
- **Bornes** : `cm_load.cpp:229-238` (`CM_BoundBrush`) : les **6 premiers côtés** d'un brush doivent être les bornes axiales (−x, +x, −y, +y, −z, +z). Le writer les produit toujours en tête ; les plans du brush qui coïncident avec une des six bornes ne sont pas répétés pour les sous-modèles. Une boîte alignée sur les axes a donc exactement 6 côtés ; une boîte tournée de yaw a 6 bornes + les plans non axiaux (4 pour une rotation autour de Z).
- **Limite** : `MAX_SUBMODELS = 512` (`qcommon/q_shared.h:656`), le dernier numéro est `BOX_MODEL_HANDLE` (`cm_local.h:31`) : le writer échoue au-delà de 511 modèles.
- **Coordonnées** : `game/g_mover.cpp:1483-1493` (`SP_func_door`) : `pos1 = s.origin`, `SetBrushModel(ent, ent->model)` prend mins/maxs du sous-modèle, `distance = |movedir|·(maxs − mins) − lip`, `pos2 = pos1 + distance·movedir`. Le mover est placé à `origin` : **les brushes du sous-modèle sont relatifs à l'`origin` de la porte**, comme avec une clé `origin` dans q3map2. L'entité porte `"origin" "x y z"` = position × échelle.
- **Dessin** : `g_mover.cpp:1067-1085` (`InitMover`) : `model2` (.md3) sert au dessin, la collision vient des brushes. `cgame/cg_ents.cpp:1296-1360` : le MD3 est dessiné à `lerpOrigin`, tourné de `lerpAngles + modelAngles` par `AnglesToAxis` (l.1354-1358) : même formule que les objets de l'étape 03, donc yaw seul, sans +90.
- **Clés** : `game/g_spawn.cpp:232` (`origin`), `:236` (`model2`), `:265-267` (`angles`, `angle` en F_ANGLEHACK, **`modelAngles` existe**, vecteur). `angle -1` = vers le haut : `G_SetMovedir` (`game/g_utils.cpp:702-718`) reconnaît (0, −1, 0) → direction (0, 0, 1) puis remet `angles` à zéro, donc le −1 ne tourne pas le MD3. La clé `modelAngles` existant, le MD3 est partagé entre portes (l'orientation n'est pas intégrée aux sommets).
- **Verrouillage** : `game/g_local.h:521` `MOVER_LOCKED = 16` ; `Touch_DoorTrigger` (`g_mover.cpp:1205-1208`) ne fait rien si le drapeau est posé, et `SP_func_door` (l.1525-1535) crée quand même le déclencheur de proximité. Sans `targetname`, la porte s'ouvre au toucher.

## Choix du shader de collision

`textures/system/clip` existe dans les pk3 de base de JA (`shaders/system.shader` des `0_JKO_Assets0.pk3` et `assets1.pk3`, qui définissent `textures/system/clip`). On l'utilise donc (`DoorConverter.ClipShader`) ; **aucun shader `door_clip` n'est nécessaire** dans le fichier de shaders de la carte. Comme le moteur lit le contenu dans le lump du BSP (voir plus haut), l'entrée du lump reçoit le contenu SOLID (1) : une porte fermée arrête joueur, PNJ et tirs.

## Compilateur

- `SubModelInput(IReadOnlyList<IReadOnlyList<BspPlane>> Brushes, string Shader)` (le type de plan du compilateur est `BspPlane`, `Plane` étant pris par `System.Numerics`). Les bornes de chaque brush sont calculées depuis ses plans (`BrushBounds.FromPlanes`, intersections de trois plans) ; un brush dégénéré ou non borné est ignoré avec un avertissement.
- `CompileInput.SubModels` (propriété `init`, vide par défaut). Le i-ème sous-modèle est `*{i+1}`.
- `BspWriter` : brushes du sous-modèle ajoutés après ceux du monde, **non référencés par les feuilles** ; lump des modèles = monde (bornes de l'arbre, brushes du monde seulement) puis un modèle par sous-modèle (0 surface).
- **Monde sans sous-modèle : identique octet pour octet.** Test `World_without_submodels_is_byte_identical_to_before` : empreinte SHA-256 du BSP du monde de test (`TestScenes.Input(sky: ...)`, limites SWGL) mesurée **avant** toute modification du compilateur (`73E8A0E8…C390`) et figée dans le test ; le test passe après les modifications.
- Les 76+ tests existants du compilateur restent verts.

## Convertisseur de portes (`DoorConverter`)

- Modèle : `ObjectModelResolver.ResolveDoor` (UTD `GenericType` → `genericdoors.2da` colonne `modelname`, sinon `Appearance` → `doortypes.2da` colonne `model`), déjà résolu par le lecteur du GIT dans `ModuleEntity.Model`. Colonnes vérifiées sur les vrais 2DA de KOTOR 1 à l'étape 03 ; sur tar_m02aa les 8 portes résolvent en `dor_lts02`.
- Le MD3 est produit par le même générateur que les objets (`PlaceableConverter.BuildModel`, sans lecture de PWK) : `models/kotor/<modèle>.md3`, shaders et textures partagés. `ConvertedEntities.Merge` réunit objets et portes en ne gardant qu'un exemplaire d'un MD3, d'une texture ou d'un bloc shader commun ; sans porte, le résultat est exactement celui d'avant.
- Brush : boîte englobante des maillages rendus du modèle × échelle, épaisseur minimale 2 unités par axe (une porte plate ne donne pas de brush dégénéré), plans tournés du yaw (rotation anti-horaire, comme `AnglesToAxis`). Sur dor_lts02 : 5,05 m × 0,22 m × 2,60 m, soit environ 202 × 9 × 104 unités à l'échelle 40.
- Entité : `classname func_door`, `model *N`, `model2`, `origin`, `modelAngles "0 <yaw> 0"` (yaw du GIT normalisé dans [0, 360), **sans +90**), `angle -1`, `lip 8`, `speed 200`, `wait 3`, `spawnflags 16` si UTD `Locked` ≠ 0 ou `LinkedToModule` non vide. `ModuleEntity` gagne `LinkedToModule` et `Locked` (lus par `ModuleEntitiesReader`).
- Les portes ne sont pas ajoutées à la collision du monde (`ExtraSolid` vide).
- Sautées avec `DOOR_SKIPPED` (message en anglais, raison précise) : template/UTD/apparence/modèle absents ou illisibles, modèle sans maillage rendu, et `door model too large for one MD3` si le MD3 est découpé en plusieurs fichiers ou réduit par `modelscale`.
- Bilan au journal : `DOORS` « N door(s) converted (L locked), S skipped. ».
- Branchement : `PipelineSteps.Import` appelle `DoorConverter` après `PlaceableConverter`, fusionne et passe les sous-modèles à `CompileInput.SubModels` (via `ApplyObjects`) ; `Export` écrit déjà tous les MD3 du résultat fusionné.

## Bilan tar_m02aa (KOTOR 1, échelle 40)

8 portes au GIT : **8 converties, 6 verrouillées** (2 sans verrou : `sw_door_taris003` au Tag `TarisDoor2` et `sw_door_taris002` au Tag `static`), **0 sautée**. 1 MD3 de porte (`models/kotor/dor_lts02.md3`). Lump d'entités (extrait par `Real_tar_m02aa_doors`, aussi dans `out/doors-tar_m02aa.txt`) :

```
{ "classname" "func_door" "model" "*1" "model2" "models/kotor/dor_lts02.md3" "origin" "3630 3630 0" "modelAngles" "0 135 0" "angle" "-1" "lip" "8" "speed" "200" "wait" "3" "spawnflags" "16" }
{ "classname" "func_door" "model" "*2" "model2" "models/kotor/dor_lts02.md3" "origin" "3270 4500 0" "modelAngles" "0 90 0" "angle" "-1" "lip" "8" "speed" "200" "wait" "3" "spawnflags" "16" }
{ "classname" "func_door" "model" "*3" "model2" "models/kotor/dor_lts02.md3" "origin" "5369.92 5370.52 0" "modelAngles" "0 315 0" "angle" "-1" "lip" "8" "speed" "200" "wait" "3" "spawnflags" "16" }
{ "classname" "func_door" "model" "*4" "model2" "models/kotor/dor_lts02.md3" "origin" "5370.32 3629.992 0" "modelAngles" "0 225 0" "angle" "-1" "lip" "8" "speed" "200" "wait" "3" "spawnflags" "16" }
{ "classname" "func_door" "model" "*5" "model2" "models/kotor/dor_lts02.md3" "origin" "3630 5370 0" "modelAngles" "0 45 0" "angle" "-1" "lip" "8" "speed" "200" "wait" "3" "spawnflags" "16" }
{ "classname" "func_door" "model" "*6" "model2" "models/kotor/dor_lts02.md3" "origin" "4500 3270 0" "modelAngles" "0 180.003 0" "angle" "-1" "lip" "8" "speed" "200" "wait" "3" "spawnflags" "16" }
{ "classname" "func_door" "model" "*7" "model2" "models/kotor/dor_lts02.md3" "origin" "3531.716 5468.28 0" "modelAngles" "0 45 0" "angle" "-1" "lip" "8" "speed" "200" "wait" "3" }
{ "classname" "func_door" "model" "*8" "model2" "models/kotor/dor_lts02.md3" "origin" "4500 2850 0" "modelAngles" "0 180.001 0" "angle" "-1" "lip" "8" "speed" "200" "wait" "3" }
```

Les yaw (45°, 90°, 135°, 180°, 225°, 315°) concordent avec les quaternions du LYT (door_06 45°, door_07 90°, etc.).

## Tests ajoutés

- `LayoutFileTests` : `Doorhook_quaternion_order`.
- `SubModelTests` (4) : `World_without_submodels_is_byte_identical_to_before`, `Submodel_roundtrip` (2 modèles, modèle 1 : 1 brush, 6 côtés, 0 surface, bornes de la boîte, après les brushes du monde), `Submodel_not_in_world_tree` (aucun leaf brush ≥ premier brush de porte), `Oriented_submodel_brush_keeps_its_planes_and_bounds`.
- `DoorConverterTests` (8) : `Door_entity_keys`, `Transition_door_locked` (`tar_m02ab` → `spawnflags "16"`), UTD verrouillé + bilan `DOORS` + `DOOR_SKIPPED`, numérotation des `*N` et brush tourné, modèle découpé sauté, `Merge`, compilation avec sous-modèles, et `[SkippableFact] Real_tar_m02aa_doors` (≥ 1 `func_door`, chaque `*N` existe dans le BSP relu avec 1 brush et 0 surface, le pk3 contient chaque `model2`, aucun leaf brush ne référence un brush de porte).

## Réserves

1. **Aucun essai dans le jeu** : l'ouverture (la porte monte de `taille Z − lip`), la position du brush par rapport au MD3 (origine du MDL supposée sur l'axe de la porte, comme pour les objets) et le sens du yaw sont à confirmer visuellement par l'orchestrateur. Si le brush est décalé, c'est l'origine du modèle de porte qu'il faut corriger.
2. Le contenu de collision vient du lump des shaders (SOLID) ; `textures/system/clip` n'est vérifié que par sa présence dans les shaders de base de JA, pas par un chargement du jeu.
3. Les portes de KOTOR pivotent ou coulissent : toutes montent (hors périmètre de la spec). Une porte ouverte par KOTOR par défaut reste fermée ici.
4. Chaque `model2` distinct occupe un indice de modèle du moteur (`MAX_MODELS = 256`, `q_shared.h:646`), partagé avec les autres modèles : sans risque sur tar_m02aa (1 MD3 de porte), pas de plafond côté convertisseur.
5. Les portes verrouillées (UTD `Locked` ou transition) restent fermées, sans clé ni script : conforme à la spec.
6. Aucun commit.
