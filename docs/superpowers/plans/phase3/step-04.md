# Étape 04 — Portes qui s'ouvrent

**But :** chaque porte du GIT devient un `func_door` de JA : un modèle de brush inline, solide et invisible, avec le MD3 de la porte en `model2`. La porte monte au toucher. Elle reste verrouillée si elle mène à un autre module ou si elle est fermée à clé.

**Fichiers :**
- Modifier :
  - `src/Kotor.Core/Layout/LayoutFile.cs` (doorhooks : quaternion w,x,y,z) ;
  - le compilateur (`CompileInput`, `Compiler.cs`, `Bsp/BspWriter.cs`, `Bsp/BspReader.cs`, et les fichiers de l'arbre et des brushes nécessaires) pour les sous-modèles ;
  - `src/Kotor.Pipeline/Entities/*` (convertisseur de portes, qui réutilise le MD3 de l'étape 03) ;
  - `ModuleExporter` / `PipelineSteps`.
- Créer : `src/Kotor.Pipeline/Entities/DoorConverter.cs`
- Tester : `tests/Kotor.Core.Tests/Layout/LayoutFileTests.cs` (ajout), `tests/Kotor.Compiler.Tests/SubModelTests.cs`, `tests/Kotor.Pipeline.Tests/DoorConverterTests.cs`

**Interfaces produites :**
```csharp
namespace Kotor.Compiler;
/// Sous-modèle BSP (*1, *2…) : brushes convexes donnés par leurs plans (normale sortante, distance), en unités JA absolues
/// (le moteur décale le mover via son origin : voir ci-dessous), et sans surface visible.
public sealed record SubModelInput(IReadOnlyList<IReadOnlyList<Plane>> Brushes, string Shader /* shader de collision */);
// CompileInput gagne : IReadOnlyList<SubModelInput> SubModels (vide par défaut). Le i-ème sous-modèle devient "*{i+1}".
// BspFile (lecture) gagne : IReadOnlyList<BspModelInfo> Models, avec BspModelInfo(Vector3 Mins, Vector3 Maxs, int FirstSurface,
// int SurfaceCount, int FirstBrush, int BrushCount).
namespace Kotor.Pipeline.Entities;
public static class DoorConverter
{
    // Ajoute les entités func_door, les MD3 (partagés avec les objets) et les SubModelInput.
    public static (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) Convert(ResourceResolver res, KotorGame game,
        IReadOnlyList<ModuleEntity> doors, float scale, int firstSubModelIndex, ImportLog log);
}
```

**Ce qui est fixé :**
- Doorhooks : le LYT écrit `<nom> <pièce> x y z qx qy qz qw` ou w en premier ; **vérifier sur un vrai LYT et sur la référence** (xoreos `lytfile.cpp`, PyKotor), puis corriger. Test avec un quaternion non trivial.
- Sous-modèles BSP :
  - chaque sous-modèle a ses brushes, ajoutés au lump des brushes après ceux du monde, et son entrée dans le lump des modèles (bornes, premier brush, nombre de brushes, 0 surface) ;
  - les brushes ne vont pas dans l'arbre du monde ;
  - shader de collision : `textures/system/clip` s'il existe dans les pk3 de base de JA, sinon un shader `textures/<dossier>/door_clip` défini dans le fichier de shaders de la carte avec `surfaceparm nodraw`, `surfaceparm playerclip` et `surfaceparm monsterclip` ;
  - **vérifier dans `OpenJK-SWGL/code/qcommon/cm_load.cpp`** (`CMod_LoadSubmodels`) comment le moteur lit les brushes d'un sous-modèle (via les leaf brushes d'une feuille propre ?), et produire exactement la structure attendue. Citer les lignes.
- Coordonnées : comme q3map2 avec une clé `origin`, les brushes du sous-modèle sont **relatifs à l'origine de la porte**, et l'entité porte `"origin" "x y z"`. Vérifier dans `g_mover.cpp`/`InitMover` et `cm_load.cpp` que c'est bien la convention attendue, puis la consigner.
- Brush de la porte : la boîte englobante du modèle de porte (somme des maillages rendus), tournée du yaw de la porte, mise à l'échelle. Ce sont 6 plans orientés.
- Modèle de la porte : UTD du template. `GenericType` donne la ligne de `genericdoors.2da` et sa colonne `modelname`. Sans `GenericType` ou sans modèle : `Appearance` et `doortypes.2da`, colonne `model` (**à vérifier**). Échec : `DOOR_SKIPPED` avec la raison.
- Entité :
  ```
  "classname" "func_door", "model" "*N", "model2" "models/kotor/<m>.md3", "origin" "x y z",
  "modelAngles" "0 <yaw+90> 0", "angle" "-1", "lip" "8", "speed" "200", "wait" "3"
  ```
  Ajouter `"spawnflags" "16"` (LOCKED) si l'UTD a `Locked` = 1 ou si l'entrée GIT a `LinkedToModule` non vide. **Vérifier `modelAngles`** dans `g_mover.cpp`. Si la clé n'existe pas, l'orientation est intégrée au MD3 et le MD3 devient propre à chaque porte.
- Les portes ne sont **pas** ajoutées à la collision du monde.
- Bilan au journal : `DOORS` (converties, verrouillées, sautées).

- [ ] **Étape 1 : tests qui échouent**
  - `Doorhook_quaternion_order` : une ligne de LYT synthétique donne le quaternion attendu.
  - `Submodel_roundtrip` : on compile un monde minimal avec 1 sous-modèle (une boîte), puis on relit le BSP : 2 modèles, et le modèle 1 a 1 brush, 6 côtés, 0 surface, et les bornes de la boîte.
  - `Submodel_not_in_world_tree` : les brushes du sous-modèle ne sont référencés par aucune feuille du monde.
  - `Door_entity_keys` : une porte synthétique donne `func_door`, `model "*1"`, `angle "-1"`, et pas de `spawnflags` sans `LinkedToModule`.
  - `Transition_door_locked` : avec `LinkedToModule` = `tar_m02ab`, on obtient `spawnflags "16"`.
  - `[SkippableFact] Real_tar_m02aa_doors` : au moins 1 `func_door` ; chaque `*N` existe dans le BSP relu ; le pk3 contient chaque `model2`.
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter.**
- [ ] **Étape 4 : `dotnet build` / `dotnet test` verts.** Exporter tar_m02aa vers un dossier temporaire et recopier dans le rapport le lump d'entités des portes.
- [ ] **Étape 5 : rapport** `reports/step-04-impl.md` : conventions vérifiées dans le moteur (lignes citées), choix du shader de collision, bilan des portes.
