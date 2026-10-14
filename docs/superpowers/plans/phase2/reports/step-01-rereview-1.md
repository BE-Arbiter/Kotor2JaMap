# Étape 01 — Seconde relecture (correctif 1)

Périmètre : diff `dfdac54..88b239f` + `Compiler.cs:92-99` (condition d'ajout de la boîte de ciel). Rien relancé.

## Verdict par constat

1. **BspReader accepte un BSP vide/tronqué — ADDRESSED**
   - `src/Kotor.Compiler/Bsp/BspReader.cs` (après le contrôle des bornes, ~l.72-74) : `InvalidDataException` pour `numVerts == 0`, `numIndexes == 0`, `numIndexes % 3 != 0`, message avec « Surface {s} ».
   - `src/Kotor.Pipeline/PipelineSteps.cs:~100-114` : `ReadBack` (appelé par `Export`) convertit l'exception du lecteur en `BSP_READBACK_FAILED` journalisé (`Fail`), et échoue aussi si zéro surface ou si le total d'index relus n'est ni `plan` ni `plan + 36`. `CreatePk3` ne reçoit donc plus un `.bsp` vide.
   - Tests : `BspReaderTests` (3 nouveaux : 0 index, index non multiple de 3, 0 sommet) ; `PipelineStepsTests` : `ReadBack_of_real_compile_matches_plan` et `ReadBack_fails_when_bsp_is_empty_or_inconsistent_with_plan` (lump vide, surface sans index, total incohérent, code + entrée de journal). Couvre les cas du constat.
2. **`TestEnv.JaBaseDir` — ADDRESSED** : propriété supprimée, `tests/Kotor.Pipeline.Tests/TestEnv.cs` (fin de classe).
3. **Assertion de shader faible — ADDRESSED** : `PipelineStepsTests.cs:~39`, `s.Shader == "textures/kotor/t"`.

## Règle « plan ou plan + 36 »

- `Compiler.cs:96-99` : la boîte de ciel est ajoutée dès qu'il existe de la géométrie (surfaces ou brushes), sinon avertissement sans ciel. Un plan avec au moins une surface produit donc toujours +36 ; le cas « pas de ciel » ne se produit que sans géométrie, où le plan a 0 index et la relecture 0 surface, rejetée à juste titre. Aucun compile légitime n'est rejeté par ce biais.
- Cas limite : un plan sans surface mais avec des brushes produirait un ciel seul (36 index, planned = 0) : rejeté car 36 = 0 + 36 est accepté, en fait. Accepté de façon cohérente ; sans conséquence pratique.
- Accepter aussi « plan » sans ciel est plus permissif que nécessaire (le ciel est toujours présent), mais ne laisse passer aucun BSP vide ni tronqué de façon réaliste. Une troncature de exactement 36 index (ou une perte compensée par autre chose) ne serait pas détectée : contrôle de cohérence globale, pas d'égalité par surface. Acceptable.
- Risque non vérifié ici : si le compilateur retire/fusionne des triangles (dégénérés) sur d'autres cartes, le contrôle strict échouerait à tort ; non observé (tar_m02aa et scène de test passent selon le rapport de correctif).

## Nouvelles casses

aucune (constatée à la lecture : `InternalsVisibleTo` ajouté au csproj, constante publique `SkyBox.IndexCount` cohérente avec 6 faces x 2 triangles x 3 index).

## Hors périmètre

- Le contrôle ne compare pas les surfaces une à une ; suffisant pour l'étape 01.
- Un cas de test « ciel absent » (plan sans géométrie) n'existe pas ; peu utile.

## Verdict

**APPROUVÉ** — les 3 constats sont corrigés, aucune régression identifiée.
