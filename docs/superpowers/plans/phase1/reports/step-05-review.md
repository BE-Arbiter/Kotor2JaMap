# Étape 05 — Revue (MDL/MDX) : base ceefb68, tête a14344b

Revue sur le diff uniquement. Les affirmations du rapport (2832 modèles, 1,747 m, etc.) n'ont pas été rejouées. Les offsets ont été recoupés de mémoire avec xoreos `model_kotor.cpp` (décodage du quaternion compressé, clé de contrôleur de 16 octets) et KotorBlender, et ils sont cohérents avec le code.

## Conformité
- ✅ Interfaces conformes : `NodeKind` ; `KotorNode` (Position, Orientation, Mesh, Children) ; `KotorModel.Walk()` ; `MdlReader.Read(byte[], byte[])`.
- ✅ Offsets relatifs à 12 pour le MDL, non décalés pour le MDX. Positions lues dans le MDL, UV et normales dans le MDX.
- ✅ Contrôleurs : seule la 1re image clé de position (8) et d'orientation (20, 4 colonnes ou 2 colonnes compressées) est lue. Les autres sont ignorés.
- ✅ Skin, Dangly, Saber et Aabb sont lus comme des maillages ordinaires. L'en-tête de maillage suit l'en-tête de nœud et les en-têtes supplémentaires sont ignorés, donc ils sont exportables comme statiques.
- ✅ K2 : `NotSupportedException` avec un message clair.
- ✅ Garde-fous : les lectures passent par `Need` / `Off` / `Count` et `InvalidDataException` nomme le modèle et le nœud. Profondeur limitée à 512, indices de sommet vérifiés, MDX borné.
- ✅ Fidélité : aucune simplification de la géométrie. Les triangles viennent du tableau de faces complet.
- ✅ Walk : `Local * parent` en vecteurs ligne est correct (rotation puis translation).
- ✅ Quaternion compressé : formule conforme à xoreos. La canonicalisation `w >= 0` (négation des 4 composantes) donne (0,0,0,1) pour l'identité et représente la même rotation. L'écart est documenté.
- ⚠️ `Real_compressed_orientation_model` n'exerce pas le décodeur sur des données réelles (aucun contrôleur compressé dans les nœuds, signalé honnêtement). Le décodeur n'est couvert que par le test unitaire.
- ⚠️ Review Focus « rien ne plante » : le cas Light/Emitter/Reference combiné à Mesh lève une exception (voir Mineur 1). Les nœuds Light, Emitter, Reference, Camera, ainsi que Skin et Dangly, sont traités sans aucune exception.

## Points forts
- Code propre : constantes d'offsets documentées avec leurs sources, contexte `Ctx` qui centralise les bornes.
- Tous les accès passent par `Need`, y compris les lectures de u16/u32/f32 et le MDX.
- Cas MDX `0xFFFFFFFF` (35 modèles) géré, avec l'invariant `HasLightmap` ⇒ `Uv1.Length == Positions.Length` préservé.
- Un test de lecture exhaustive des 2832 modèles de models.bif, avec invariants vérifiés (indices, finitude, norme des quaternions).
- Rapport clair, avec tableau des offsets sourcés et écarts signalés.

## Problèmes

### Critique
Aucun.

### Important
Aucun.

### Mineur
1. **`MdlReader.cs:304-305` — Light/Emitter/Reference + Mesh lève `InvalidDataException`, ce qui abandonne tout le modèle.**
   - Ce cas n'existe pas dans les 2832 modèles vanilla, donc un export de module vanilla n'est pas affecté.
   - Un modèle de mod pourrait toutefois faire échouer toute la pièce, voire le module, si l'appelant (étape 06 et suivantes) n'attrape pas l'exception par modèle. C'est une exception nommée et non un crash, mais cela va à l'encontre de « rien ne plante ».
   - Correctif au choix : (a) laisser `Mesh = null` pour ce nœud et continuer. Les enfants restent lisibles et l'en-tête de maillage ne serait de toute façon pas à `+80`. (b) Imposer à l'exporteur d'attraper `InvalidDataException` par modèle, de journaliser et de continuer.
   - À vérifier à l'étape 06 en tout cas.
2. **`MdlReader.cs:397` — allocation avant validation.** `new int[faceCount * 3]` précède le `Need` des faces. Un `faceCount` malveillant jusqu'à ~33 M alloue ~400 Mo avant l'exception. Déplacer le `Need` avant l'allocation.
3. **`MdlReader.cs:311-315` — pas de protection contre le partage de nœuds.** `MaxDepth` borne la profondeur mais pas le nombre total de nœuds. Un fichier forgé dont plusieurs enfants pointent vers le même sous-arbre explose en temps. Un compteur global de nœuds (par exemple ≤ nombre d'octets / 80) suffirait.
4. **`MdlReader.cs:273-281` — `NodeName` périmé dans les messages d'erreur.** Il est mis à jour après la lecture de l'en-tête (`c.NodeName = name` à la ligne 282), donc l'erreur « noeud @off hors du fichier » nomme le nœud précédent. `NodeName` reste aussi celui du dernier enfant après le retour de la récursion. Mineur.
5. **Tests : la robustesse demandée par le Review Focus n'est pas testée.**
   - Aucun test pour K2 → `NotSupportedException`.
   - Aucun test pour Light + Mesh ou pour un flag inconnu.
   - Aucun test d'offset hors limites autre que `byte[8]`.
   - Un test à bas coût existe : copier un MDL réel et altérer le flag ou un offset.
6. **Orientation non normalisée.** Le reader ne normalise pas les quaternions d'en-tête ou de contrôleur ; seul un test le vérifie (0,99-1,01 sur les modèles vanilla). Un mod peut produire des matrices avec échelle parasite. Normaliser à la lecture (si la longueur est non nulle) serait plus sûr.
7. **`out/mdl-stats.txt` en `AppendAllText`.** Il s'accumule sans être réinitialisé entre les runs et contient des doublons. Cosmétique.

## Évaluation
**Approuvé** (avec réserves mineures). L'implémentation est conforme à la spec, les offsets sont cohérents avec les références et les garde-fous de lecture sont solides.

Sur le risque soulevé : la levée pour Light/Emitter/Reference + Mesh ne peut pas se produire sur les données du jeu. Elle n'abandonne un export que pour du contenu hors norme, et seulement si l'appelant n'isole pas l'échec par modèle. Un nœud Light, Emitter, Reference ou Camera seul, ou un Skin ou Dangly, ne fait jamais échouer le module.
