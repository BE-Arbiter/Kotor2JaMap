# Étape 07 — Revue (conformité puis qualité)

Base f6f4e77, tête 5e73d5b. Revue du diff uniquement, plus lecture ciblée de `WalkableCollision.MaxBrushesFor` et de `BspWriter` (plans/côtés). Aucun test relancé.

## Conformité

- ✅ `SurfaceInput`, `CompileInput`, `Compile(CompileInput, bspPath, Limits, ImportLog)` : signatures conformes ; entrée par fichiers supprimée ; `CompileResult` inchangé ; avertissements en `BSP_WARNING`.
- ✅ `RenderSurfaces.FromInput` : découpage 1199 / 7197 conservé, enroulement (v0, v2, v1) conservé, UV1 suivent les sommets, UV0 recopiées sans inversion (convention du contrôleur respectée).
- ✅ `BspWriter` : `lightmap[0]` = UV1 et couleur (255,255,255,255) si `LightmapUvs` existe ; sinon UV 0 et `VertexLighting`. `lightmapNum` reste -3. Le test lit bien les octets du drawvert (offset 20/24 et 64).
- ✅ `DoubleSided` provient uniquement de `SurfaceInput`.
- ✅ Renommages TFU via `CompilerNames` (collision, clés `_kotor_*`) ; en-têtes GPL-2.0 présents ; `Pk3Builder`/`ShaderNames` laissés à l'étape 08.
- ✅ Fusion coplanaire : sens des arêtes, ordre des sommets (b..a de P puis sommets de Q entre a et b) et conservation de l'enroulement corrects ; test de convexité, retrait des colinéaires, détection des doublons de clé. Triangles d'enroulement opposé non fusionnés (sans danger : le prisme est symétrique).
- ✅ `FromPolygon` : normale de Newell, plans d'arête `e × n` = normale sortante pour un polygone direct, faces ±n à `d ± h`, polygone dégénéré renvoie null. Brush valide pour un polygone convexe.
- ✅ Plafond : tri par aire décroissante, `COLLISION_CAPPED` journalisé, pas d'exception ; budget de côtés cohérent avec `BspWriter` (6 plans axiaux + 2 + 1 par arête).
- ⚠️ Bridage à 8 sommets par polygone : hors plan (déclaré). Limite la réduction du nombre de brushes sur de grandes surfaces planes, sans effet sur la validité.
- ⚠️ Tous les éléments non convertis ne sont pas journalisés avec un code (voir Mineurs).

## Points forts

- Fusion gloutonne propre, index d'arêtes cohérent (`Unregister` ne retire que ce qui appartient au polygone), aucune référence périmée possible.
- Plafond déterministe (tri par aire puis par indice), comptabilité en côtés pour que le terrain reste sous la limite totale.
- Aucune géométrie visible n'est retirée : seules les collisions sont plafonnées. Les triangles à indice invalide de `FromInput` sont les seuls ignorés côté rendu.
- Tests de l'étape présents et pertinents (lightmap, plafond, fusion, L convexe).

## Problèmes

### Critique

Aucun.

### Important

1. `src/Kotor.Compiler/Compiler.cs:847` (`CapPolygons`) : le plafond ne protège que contre `MaxBrushes` et les côtés, pas contre `Limits.MaxPlanes` (0x20000 par défaut). `BspWriter` ajoute pour chaque brush 6 plans axiaux presque toujours uniques (bornes propres au brush) plus 2 + k plans de polygone ; à ~11 800 brushes (le plafond effectif, car `MaxBrushSides/11` = 11 915 < 0x8000) on atteint ~10 à 14 plans par brush, soit 120 à 165 k plans, au-delà de 131 072. `BspTree.Build`/`BspWriter` lèvent alors `MAX_MAP_PLANES`, la compilation échoue et aucun BSP n'est écrit : le contrat de la Review Focus (« on plafonne, pas de plantage ») n'est tenu que tant que les plans ne dominent pas, ce qui est le cas des grandes cartes qui justifient le plafond. Correction : compter les plans (au pire 6 + 2 + k par brush, ou les plans uniques réels) dans le budget de `CapPolygons` et de `MaxBrushesFor` côté terrain, et ajouter un test avec `MaxPlanes` réduit.

### Mineur

1. `src/Kotor.Compiler/Compiler.cs:747-749` : avec `margin: 0`, si la collision solide sature son plafond, le terrain marchable ne reçoit que 50 brushes (déclaré en concern 3) : trous de sol possibles dans les grandes cartes, alors que le sol est plus critique que de petits solides. Le message de troncature du terrain porte `BSP_WARNING` et non un code dédié (`COLLISION_CAPPED`). Réserver le budget du terrain avant de plafonner le solide, ou au moins journaliser avec un code distinct.
2. `src/Kotor.Compiler/Bsp/CollisionMerger.cs:262-264` : triangles dégénérés (aire < 1e-4) ou à sommets soudés (`HasDuplicateKey`) ignorés sans aucun journal ; la règle globale demande un code pour tout élément non converti. Compter et journaliser (ex. `COLLISION_DEGENERATE`).
3. `src/Kotor.Compiler/Bsp/RenderSurfaces.cs:475-478` : `Normals`, `Uv0` ou `Uv1` de longueur différente de `Positions` sont remplacés silencieusement (face, zéro, pas de lightmap). Une UV1 mal dimensionnée fait retomber la surface en éclairage par sommet sans trace. Journaliser ou lever `ArgumentException`.
4. `src/Kotor.Compiler/Compiler.cs:742` : le message informatif de `FromPolygons` (« Collision : N brushes… ») est émis via `Warn` et compte donc comme `BSP_WARNING` à chaque compilation ; bruit dans `CompileResult.Warnings`.
5. `src/Kotor.Compiler/Bsp/CollisionMerger.cs:281` : la tolérance de plan est testée contre le plan moyen du polygone courant ; des fusions successives de pentes de 0,5° peuvent dériver jusqu'à ~0,05 unité. Sans conséquence pour la collision (épaisseur par défaut), à noter.

## Évaluation

**Corrections nécessaires** — un point Important : le plafond de collision n'empêche pas le dépassement de `MAX_MAP_PLANES` pour les cartes proches de la limite de brushes.
