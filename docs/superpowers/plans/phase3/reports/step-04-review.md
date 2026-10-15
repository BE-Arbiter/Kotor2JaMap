# Étape 04 — Relecture (portes -> func_door, sous-modèles BSP)

Plage relue : fab5d4f..b0003d8. Aucun code modifié, aucun test relancé (les résultats du rapport d'implémentation sont pris tels quels).

## 1. Conformité : ❌ (un seul écart, mineur à corriger)

Écart :
- `src/Kotor.Compiler/Compiler.cs:81` : nouveau message `"Sous-modele *{n} : brush degenere ou non borne ignore."` en français, émis dans le journal sous le code `BSP_WARNING` (donc visible par l'utilisateur). La contrainte « tout nouveau texte visible par l'utilisateur est en anglais » n'est pas respectée. (Les messages voisins des lignes 62 et 111 sont français, mais ils existaient déjà.)

Tout le reste est conforme :
- Aucun abandon silencieux : `DOOR_SKIPPED` (modèle absent, sans maillage, trop grand pour un MD3) et `DOORS` (bilan) ; brush dégénéré signalé par `BSP_WARNING`.
- Fichiers de l'application et de la CLI identiques : les deux passent par `PipelineSteps.Import`. Déterminisme : `Merge` trie les MD3, textures et shaders, et les entités gardent l'ordre objets puis portes dans l'ordre du GIT.
- Messages du convertisseur en anglais.
- Écarts d'interface acceptables : `BspPlane` au lieu de `Plane` (conflit avec `System.Numerics`). Le shader `door_clip` n'est pas nécessaire (voir plus bas).
- `modelAngles` : yaw seul, sans +90, conforme à la décision de l'orchestrateur (le plan disait `yaw+90`, la décision l'emporte).

## 2. Points d'attention vérifiés dans le moteur

- **Sous-modèle sans surface** : `cm_load.cpp:127-184` fabrique un « leaf » propre avec `numLeafBrushes = numBrushes` et les indices `firstBrush + j`. Les brushes n'ont pas besoin d'être dans l'arbre ; 0 surface est accepté (boucle vide). Le writer écrit bien `FirstSurface = 0, NumSurfaces = 0, FirstBrush, NumBrushes` (`BspWriter.cs:270-279`). Les index sont justes : `firstBrush = brushes.Count` avant l'ajout (l.156), le modèle 0 garde `tree.Brushes.Count` (l.273, identique à l'ancien `brushes.Count`). Le renderer (`rd-vulkan/tr_bsp.cpp:2016-2065`, `R_LoadSubmodels`) lit seulement bornes, `firstSurface`, `numSurfaces` : 0 surface passe, aucune dépendance aux brushes.
- **Contenu** : `CMod_LoadBrushes` prend `contents` du lump des shaders. Le writer marque le shader du sous-modèle SOLID (l.222) et écrit son index dans le brush (`brushes` porte maintenant le shader) et les côtés. Correct.
- **Les 6 premiers côtés** : `CM_BoundBrush` (`cm_load.cpp:229-238`) lit `sides[0..5]` comme bornes axiales -X,+X,-Y,+Y,-Z,+Z avec `dist` ; le writer les écrit dans cet ordre (l.160-166), avec -min en dist pour les normales négatives. Correct. La déduplication `axialKeys` (arrondi 4 décimales) ne supprime qu'un plan qui coïncide avec une borne : sans risque.
- **Brushes relatifs à l'origine** : `SP_func_door` (`g_mover.cpp:1483-1493`) : `pos1 = s.origin`, `SetBrushModel` prend les bornes du modèle, `pos2 = pos1 + distance*movedir`. Convention q3map2 « origin » respectée ; le MD3 est dessiné à `lerpOrigin` (`cg_ents.cpp:1296-1360`), donc brush et MD3 sont dans le même repère (sommets MD3 = sommets MDL x échelle ; la boîte vient des mêmes sommets, `PlaceableConverter.cs` BuildModel).
- **Brush orienté** (`DoorConverter.cs` `Box`) : normales sortantes (`ex`,`-ex`,`ey`,`-ey`,`±Z`) avec dist = max / -min dans le repère du modèle ; rotation anti-horaire de `yaw` identique à `AnglesToAxis` (`cg_ents.cpp:1357`). Les bornes AABB du brush viennent de `BrushBounds.FromPlanes` (intersections de trois plans, retenues si dans tous les demi-espaces) : juste. Pour un yaw non axial, 6 bornes + 4 plans = 10 côtés (testé pour 45 degrés).
- **`model2`, `angle -1`, `modelAngles`** : `g_spawn.cpp:266-267` (`angle` en ANGLEHACK donne `angles = (0,-1,0)`, `modelAngles` vecteur) ; `G_SetMovedir` (`g_utils.cpp:702-718`) donne la montée et remet `angles` à 0, donc `modelAngles` seul oriente le MD3. Partage du MD3 entre portes correct (`Merge` : un exemplaire par chemin).
- **`spawnflags 16`** : `MOVER_LOCKED = 16` (`g_local.h:521`), `Touch_DoorTrigger` (`g_mover.cpp:1207`) ne fait rien si le drapeau est posé ; le déclencheur est quand même créé. Correct.
- **Quaternion des doorhooks** : `LayoutFile.cs` lit maintenant `w x y z`. Aucun autre code ne consomme `LayoutDoorHook.Orientation` (recherche dans `src`) : aucun effet de bord sur la scène. Le lecteur GFF (`GffReader.cs:112-114`) lisait déjà w,x,y,z : cohérent.
- **Identité octet pour octet** : `World_without_submodels_is_byte_identical_to_before` compare à un SHA-256 figé. Les modifications du writer (champ shader des brushes à 0, `solidShaders = {CollisionShader}`, entrée du modèle 0) sont neutres sans sous-modèle ; je n'ai pas pu remesurer le hash d'avant (il faudrait compiler fab5d4f), je m'appuie sur le rapport et sur la lecture du diff.

## 3. Qualité

### Critical
Aucun.

### Important
1. `src/Kotor.Compiler/Compiler.cs:81` : message français dans le journal (voir conformité). Scénario : une porte dont le brush est NaN ou non borné donne dans le journal « Sous-modele *3 : brush degenere... » dans une application censée être en anglais. Correction : `$"Sub-model *{sm + 1}: degenerate or unbounded brush ignored."`.

### Minor
1. `src/Kotor.Compiler/Compiler.cs:76-82` : si tous les brushes d'un sous-modèle sont rejetés, le sous-modèle reste dans la liste avec 0 brush (bornes 0) et la porte est quand même émise. En jeu : `distance = 0 - lip = -8`, la porte descend de 8 unités et ne bloque rien, sans `DOOR_SKIPPED`. Atteignable seulement avec des sommets non finis ; l'épaisseur minimale de 2 unités (`DoorConverter.cs` `MinThickness`) l'évite en pratique. Journal `BSP_WARNING` seul.
2. `src/Kotor.Compiler/Bsp/BspWriter.cs:177` : `CheckMax("MAX_SUBMODELS")` est marqué `collisionRelated: true`, donc `Compiler.cs` retente en réduisant la collision du monde. Scénario théorique : plus de 511 portes, le compilateur rogne le monde en vain avant d'échouer. Sans portée réelle (tar_m02aa : 8 portes).
3. `src/Kotor.Pipeline/Entities/DoorConverter.cs:45` : les portes sont toujours converties, mais `Locked` et `LinkedToModule` ne sont couverts par aucun test de lecture GFF réel (seul `Transition_door_locked` sur `ModuleEntity` synthétique, et le bilan « 6 verrouillées » de tar_m02aa sans ventilation Locked/LinkedToModule). Scénario : si un champ `LinkedToModule` était d'un autre type GFF que ResRef/CExoString, `Get<string>` renverrait null silencieusement (vide) et la porte de transition resterait ouverte. Un test sur le vrai GIT qui compte les portes par cause suffirait.
4. `src/Kotor.Pipeline/Entities/DoorConverter.cs:25-28` : la présence de `textures/system/clip` dans les pk3 de JA n'est affirmée que par le rapport ; le code n'a pas de repli `door_clip`. Impact nul tant que le contenu vient du lump du BSP (vérifié dans `cm_load.cpp`), mais le renderer n'est jamais appelé pour ce shader, donc rien ne casse non plus s'il manquait. À garder en réserve visuelle.
5. Réserve du rapport confirmée : aucun essai en jeu. L'origine du MDL de porte supposée au pied et au centre de la porte (brush et MD3 décalés ensemble si ce n'est pas le cas, mais le brush peut alors ne pas recouvrir l'ouverture visible) reste à contrôler visuellement, ainsi que la hauteur de montée (taille Z - lip) pour une porte de 2,6 m.
