# Étape 02 — Entités du module (GIT et lumières des pièces)

**But :** lister toutes les entités d'un module, avec leur type, leur nom et leur position en mètres KOTOR, pour les afficher à l'étape Importer. Elles ne sont pas converties en entités JA à cette étape.

**Fichiers :**
- Créer : `src/Kotor.Core/Entities/{EntityKind,ModuleEntity,ModuleEntitiesReader}.cs`
- Tester : `tests/Kotor.Core.Tests/Entities/ModuleEntitiesReaderTests.cs`

**Interfaces :**
- Consomme : `ResourceResolver`, `GffReader`/`GffStruct`, `ModuleInfo` (`AreaResRef`), `LayoutFile`, `MdlReader`/`KotorModel.Walk`/`NodeKind.Light`, `ImportLog`.
- Produit :
```csharp
public enum EntityKind { Placeable, Door, Waypoint, Trigger, Creature, Sound, Store, Encounter, Camera, Light }
public sealed record ModuleEntity(EntityKind Kind, string Name /* Tag, sinon TemplateResRef, sinon nœud */, string Template,
    Vector3 Position /* mètres KOTOR, repère du module */, float Yaw /* degrés */, Vector3[]? Polygon /* déclencheurs et rencontres, absolu */);
public static class ModuleEntitiesReader
{
    public static IReadOnlyList<ModuleEntity> Read(ResourceResolver res, ModuleInfo info, LayoutFile layout, ImportLog log);
}
```
**Ce qui est fixé :**
- Le GIT est `<AreaResRef>.git` (GFF). Les listes lues sont `Placeable List`, `Door List`, `WaypointList`, `TriggerList`, `Creature List`, `SoundList`, `StoreList`, `Encounter List` et `CameraList`.
  - **Les libellés exacts des champs (position, orientation, géométrie) sont à vérifier sur le vrai `m02aa.git`** (dumper les libellés) et dans les références (xoreos `src/engines/kotorbase/area.cpp` et les `situated`/`waypoint`/`trigger`, PyKotor `resource/generics/git.py`). Indiquer dans le rapport le libellé retenu pour chaque type.
  - Orientation : `Bearing` (radians) ou `XOrientation`/`YOrientation` (vecteur), convertie en `Yaw` en degrés.
  - Géométrie des déclencheurs et rencontres : liste de points relatifs à la position, convertie en coordonnées absolues dans `Polygon`.
- Lumières : nœuds `Light` des modèles de pièce du LYT. Position = transformation du nœud (`Walk`) + position de la pièce. Nom = nom du nœud.
- GIT absent : liste sans entités du GIT, avec `GIT_MISSING` au journal. Liste absente dans le GIT : rien pour ce type, sans erreur. Entrée illisible : sautée, avec `GIT_ENTRY_INVALID`. Modèle de pièce illisible : on garde les autres pièces, avec `ROOM_MODEL_UNREADABLE`.
- Le journal reçoit une ligne de bilan `ENTITIES` avec le nombre d'entités par type.

- [ ] **Étape 1 : tests qui échouent**
  - `Reads_synthetic_git` : un GIT construit en mémoire (GFF) avec 1 objet placé, 1 porte, 1 point de passage et 1 déclencheur à 3 points. On obtient 4 entités, les positions et le Yaw attendus, et le polygone absolu.
  - `Missing_git_is_logged` : `GIT_MISSING`, pas d'exception.
  - `[SkippableFact] Real_tar_m02aa_entities` : au moins 1 porte, au moins 1 objet placé, au moins 1 point de passage, et autant de lumières que la somme des nœuds lumière journalisés par `ModuleSceneBuilder` (`NODE_LIGHT`, un compte par pièce ; 71 pour tar_m02aa d'après le journal de la phase 1 : 2+6+2+9+2+33+5+11+1). Toutes les positions sont dans la boîte englobante de la scène, élargie de 5 m. Écrire la liste dans `out/entities-tar_m02aa.txt`.
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter.**
- [ ] **Étape 4 : `dotnet test Kotor2Ja.sln`** tout vert. Recopier dans le rapport le bilan par type pour tar_m02aa.
- [ ] **Étape 5 : rapport** `reports/step-02-impl.md`.
