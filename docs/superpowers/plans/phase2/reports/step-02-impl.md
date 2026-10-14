# Etape 02 - Entites du module : rapport d'implementation

## Construit
- `src/Kotor.Core/Entities/EntityKind.cs`, `ModuleEntity.cs`, `ModuleEntitiesReader.cs` (signatures du plan, verbatim).
- `ModuleEntitiesReader.Read` : lit `<AreaResRef>.git` (neuf listes) puis les noeuds `Light` des modeles de piece du LYT (position = `Walk` + position de la piece, nom = nom du noeud, `Template` = modele de piece, Yaw 0). Une ligne `ENTITIES` (compte par type) est journalisee.
- Journal : `GIT_MISSING` (absent ou illisible), `GIT_ENTRY_INVALID` (champs de position absents/non finis, point de geometrie incomplet), `ROOM_MODEL_UNREADABLE`, plus `ROOM_MODEL_MISSING` (ajoute pour ne rien ignorer en silence, comme `ModuleSceneBuilder`). Liste absente : rien, sans erreur.
- Tests : `tests/Kotor.Core.Tests/Entities/ModuleEntitiesReaderTests.cs` (`Reads_synthetic_git`, `Missing_git_is_logged`, `Invalid_entry_is_skipped_and_logged`, `Real_tar_m02aa_entities`). Liste ecrite dans `out/entities-tar_m02aa.txt`.

## TDD
- ROUGE : stub `NotImplementedException`, 4 tests en echec (3 synthetiques + reel).
- VERT : apres implementation, 4/4. `dotnet test Kotor2Ja.sln` : Core 103, Compiler 68, Pipeline 5, Cli 5, 0 echec, 0 ignore ; build : 0 avertissement.

## Libelles GIT retenus (verifies par dump du vrai m02aa.git)
| Type | Position | Orientation | Nom / modele | Geometrie |
|---|---|---|---|---|
| Placeable List | X, Y, Z | Bearing (rad) | Tag, sinon TemplateResRef | - |
| Door List | X, Y, Z | Bearing (rad) | Tag, sinon TemplateResRef | - |
| WaypointList | XPosition, YPosition, ZPosition | XOrientation, YOrientation | Tag, sinon TemplateResRef | - |
| TriggerList | XPosition... | XOrientation, YOrientation, ZOrientation (non utilise) | Tag, sinon TemplateResRef | Geometry : PointX, PointY, PointZ relatifs (verifie) |
| Creature List | XPosition... | XOrientation, YOrientation | TemplateResRef (pas de Tag dans le GIT) | - |
| SoundList | XPosition... | aucune (Yaw 0) | TemplateResRef | - |
| StoreList | XPosition... | XOrientation, YOrientation | `ResRef` (pas TemplateResRef) | - |
| Encounter List | XPosition... (PyKotor) | idem | TemplateResRef | Geometry : X,Y,Z ou PointX/Y/Z - **non verifie** (liste vide dans m02aa) |
| CameraList | Position (Vector3) | Orientation (quaternion), Yaw = rotation autour de Z | camera<CameraID> | - |
Sources : dump reel (GffReader) ; PyKotor `git.py` de memoire pour les rencontres (non relu dans cette session, aucune reference locale lue).

## Convention de Yaw
Degres autour de Z, anti-horaire, 0 = face a +Y. Bearing converti tel quel ; vecteur (x,y) -> `atan2(-x, y)` (equivalent a `atan2(y,x) - 90`, de memoire PyKotor/xoreos, **non verifie sur donnees** : aucune entite du GIT n'a les deux champs).
Verification faite : sur les 8 portes de m02aa, le `Bearing` correspond exactement a la rotation autour de Z des doorhooks du LYT (135, 90, -45...) lue dans l'ordre w,x,y,z.
Ordre GFF des quaternions (type 16, w,x,y,z) : coherent sur CameraList (W=0,99 dominant, rotation Z faible), mais verification faible (pas d'orientation de camera connue en jeu).

## Bilan tar_m02aa
Placeable 26, Door 8, Waypoint 44, Trigger 6 (4 a 5 points), Creature 14, Sound 1, Store 1, Encounter 0, Camera 9, Light 71 (= somme `NODE_LIGHT`, calculee depuis le journal du test). Total 180. Toutes les positions dans la boite de la scene +5 m.

## Concerns
1. **Bug phase 1 probable dans `LayoutFile`** : les quaternions de doorhook sont construits `new Quaternion(t6,t7,t8,t9)` (x,y,z,w) alors que les valeurs reelles indiquent l'ordre fichier w,x,y,z (ex. 0.38,0,0,0.92 pour une porte a 135 degres : w=0.38, z=0.92). Les orientations de doorhook parsees sont donc fausses (rotation autour de X). Non corrige (hors perimetre, n'affecte pas cette etape) ; a traiter si les doorhooks servent.
2. Convention Yaw par vecteur d'orientation et geometrie des rencontres non verifiees sur donnees reelles.
3. Pas de test synthetique pour les lumieres (pas de constructeur MDL binaire) : couvertes uniquement par le test reel.
