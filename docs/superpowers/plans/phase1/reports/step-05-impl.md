# Étape 05 — Lecteur MDL/MDX : rapport d'implémentation

## Ce qui a été construit
- `src/Kotor.Core/Mdl/` : `NodeKind`, `KotorMesh`, `KotorNode`, `KotorModel` (avec `Walk()`), `MdlReader.Read(mdl, mdx)` et `MdlReader.DecodeCompressedQuaternion(uint)` (public, pour le test unitaire).
- Format KOTOR 1 PC uniquement : pointeur de fonction 1 = 4273776 accepté ; 4285200 (K2 PC) lève `NotSupportedException` avec un message explicite ; toute autre valeur aussi (valeur citée).
- Toute lecture est bornée : `InvalidDataException` nommant le modèle et le nœud (offset hors fichier, nombre invalide, indice de sommet >= nombre de sommets, MDX trop court, hiérarchie > 512 niveaux).
- Contrôleurs : seule la 1re image clé de position (8, 3 colonnes, bézier 19 inclus) et d'orientation (20 ; 4 colonnes ou 2 colonnes compressées) est lue ; le reste est ignoré (figé spec §6).
- Skin / Dangly / Aabb lus comme maillages ordinaires. Une combinaison de drapeaux Light/Emitter/Reference + Mesh lève `InvalidDataException` (jamais rencontrée sur les 2832 modèles de models.bif).

## Décisions / écarts à signaler
1. **Quaternion compressé** : xoreos et PyKotor (`Vector4.from_compressed`) donnent tous deux `w = -sqrt(1-|v|²)`, donc l'identité compressée donne (0,0,0,-1). La spec exige (0,0,0,1) : après décodage selon les références, si `w < 0` on nie les 4 composantes (même rotation, forme canonique w >= 0).
2. **Aucun nœud des 2832 modèles de models.bif ne porte de contrôleur d'orientation compressé** (mesuré avec un compteur temporaire, retiré : 0 compressé, ~111 000 contrôleurs à 4 colonnes). Les quaternions compressés ne se trouvent que dans les animations, non lues ici. Le décodeur n'est donc couvert que par le test unitaire ; `Real_compressed_orientation_model` valide la normalisation des quaternions et la hauteur, mais n'exerce pas le décodeur sur des données réelles.
3. **Taille de sommet MDX = 0xFFFFFFFF** sur 35 modèles (ex. `m01aa_03a`, `m02ac_02q`) : le maillage n'a pas de données MDX ; on garde les positions MDL, `Normals/Uv0/Uv1 = null`. `HasLightmap` est ramené à `false` si le drapeau est posé mais qu'aucune UV de lightmap n'existe (sinon l'invariant `Uv1.Length == Positions.Length` casse).
4. Positions lues dans le MDL (comme la spec). Vérifié avec un contrôle temporaire (retiré) sur tous les maillages de `m02aa_01a`, `p_bastilabb` et des 110 modèles `p_*`/`n_*` : identiques à la position MDX (offset 0 de chaque sommet) à 1e-4.
5. Nom du nœud : index de nom à l'offset 6 (KotorBlender `name_index`) ; xoreos utilise le numéro de nœud (offset 4), identique en pratique.
6. Triangles lus depuis le tableau de faces (structures de 32 octets, indices à +26), comme KotorBlender ; xoreos lit le tableau d'indices séparé (mêmes données).

## TDD
- RED : tests écrits d'abord avec stubs `NotImplementedException` : `dotnet test --filter MdlReaderTests` -> les 6 tests échouent sur `System.NotImplementedException` (aucun ne passe).
- GREEN : implémentation, 6/6 verts. Ajout ensuite de `Real_every_models_bif_model_reads` (tous les MDL de models.bif), qui a révélé le point 3 (stride -1) et la contrainte HasLightmap, corrigés.
- `dotnet test Kotor2Ja.sln` final : Compiler 39/39, Core 59/59, 0 échec, 0 avertissement.

## Fichiers modifiés
- Nouveaux : `src/Kotor.Core/Mdl/NodeKind.cs`, `KotorMesh.cs`, `KotorNode.cs`, `KotorModel.cs`, `MdlReader.cs`
- Nouveau : `tests/Kotor.Core.Tests/Mdl/MdlReaderTests.cs` (`Real_room_model_reads`, `Real_room_bbox_is_plausible`, `Real_all_tar_m02aa_rooms_read`, `Real_compressed_orientation_model`, `Real_every_models_bif_model_reads`, `Compressed_quaternion_decodes_identity`, `Truncated_file_is_invalid_data`)
- `out/mdl-stats.txt` (git-ignoré)

## Tailles et offsets retenus
Offsets MDL relatifs à l'octet 12 ; offsets MDX non décalés. Sources : X = xoreos `model_kotor.cpp` (Model_KotOR::load, ModelNode_KotOR::load/readMesh/readNodeControllers) ; KB = KotorBlender `io_scene_kotor/format/mdl/reader.py` et `types.py` ; PK = PyKotor `io_mdl.py` / `Vector4.from_compressed`. « réel » = vérifié sur les fichiers du jeu.

| Élément | Taille / offset | Source |
|---|---|---|
| En-tête de fichier | 12 octets (0, mdlSize, mdxSize) ; tailles de l'en-tête = tailles réelles de `m02aa_01a` | X, KB ; réel |
| Pointeur de fonction 1 (géométrie) | +0 ; K1 PC = 4273776, K2 PC = 4285200 | KB types.py ; réel (2832 modèles lus) |
| Nom du modèle | +8, 32 octets | X, KB |
| Offset noeud racine / nombre de noeuds | +40 / +44 | X, KB |
| En-tête de géométrie / de modèle | 80 octets (type @76) ; modèle 80..192 | X, KB |
| BBox min / max | +104 / +116 (3 floats chacun) | X, KB |
| Nom du supermodèle | +136, 32 octets | X, KB |
| Tableau des noms (offset, count) | +184 ; table de u32 -> chaînes ASCII terminées par 0 | X, KB |
| Noeud : flags / index de nom | +0 (u16) / +6 (u16) | X, KB |
| Noeud : position | +16 (3 floats) | X, KB |
| Noeud : orientation | +28, ordre w,x,y,z | X (acos du 1er = w), KB |
| Noeud : enfants / clés / données de contrôleurs | +44 / +56 / +68 (array def = offset, count, count : 12 octets) | X, KB |
| Taille de l'en-tête de noeud | 80 octets ; en-tête de maillage juste après | X, KB |
| Clé de contrôleur | 16 octets : type u32, 2 inutilisés, lignes u16 @+6, index temps u16 @+8, index valeurs u16 @+10, colonnes u8 @+12, 3 de bourrage | X, KB |
| Données de contrôleur | floats ; index en nombre de floats depuis l'offset de données | X, KB |
| Contrôleurs retenus | 8 = position (colonnes 3 ou 19), 20 = orientation (4 colonnes, ou 2 colonnes compressées) | X |
| Quaternion compressé | x = 1-(t&0x7FF)/1023, y = 1-((t>>11)&0x7FF)/1023, z = 1-(t>>22)/511 ; w = -sqrt(1-\|v\|²), sinon normalisation et w=0 | X, PK |
| Faces (array def) | mesh+8 ; structure de 32 octets (normale 12, distance 4, matériau 4, adjacents 6, indices 3×u16 @+26) | KB, X |
| Transparency hint | mesh+84 (u32) | X, KB |
| Texture 0 / Texture 1 (lightmap) | mesh+88 / mesh+120, 32 octets ASCII | X, KB |
| Taille de sommet MDX | mesh+252 (u32 ; 0xFFFFFFFF = pas de MDX) | X (mdxStructSize), KB ; réel (35 modèles à -1) |
| Offsets normales / UV0 / UV1 dans le sommet MDX | mesh+264 / +272 / +276 (i32, -1 = absent) ; 3 / 2 / 2 floats | X, KB |
| Nombre de sommets / de textures | mesh+304 / +306 (u16) | X, KB |
| Drapeau lightmap | mesh+308 (u8) | KB `has_lightmap` ; réel (cohérent avec Texture1 non vide) |
| Drapeau Render | mesh+313 (u8) | X, KB |
| Offset des données MDX | mesh+324 (u32, depuis le début du MDX) | KB `mdx_offset`, X `offNodeData` |
| Offset du tableau de positions MDL | mesh+328 (u32, depuis 12) ; 3 floats par sommet | KB `off_vert_arr`, X `offVertsCoords` ; réel (= positions MDX) |
| En-tête de maillage K1 PC | 332 octets | KB, X |
| Sommet i dans le MDX | mdxOff + i × stride, plus l'offset de la donnée | X |

## Choix du modèle humain
`p_bastilabb` (Bastila, pose de repos debout), cité par la spec et présent dans models.bif (110 modèles `p_*`/`n_*` listés via `KeyBifArchive.Keys` et lus sans erreur). Corps entier : 94 noeuds, 73 maillages dont 4 Skin, supermodèle `s_female03` ; la boîte englobante couvre z de -0.006 à 1.742 (pieds au sol, tête incluse).

## Contenu de out/mdl-stats.txt
```
# p_bastilabb: nodes=94, bbox min=(-0.527,-0.145,-0.006) max=(0.526,0.146,1.742), height_Z=1.747 m
# tar_m02aa rooms
m02aa_03a: Header=65, Header, Light=2, Header, Mesh=20, Header, Mesh, Aabb=1; triangles=626
m02aa_02a: Header=59, Header, Light=6, Header, Mesh=54, Header, Mesh, Aabb=1; triangles=2730
m02aa_08a: Header=3, Header, Light=2, Header, Mesh=31, Header, Mesh, Aabb=1; triangles=1178
m02aa_09h: Header=50, Header, Mesh=22, Header, Mesh, Aabb=1; triangles=3093
m02aa_01a: Header=61, Header, Light=9, Header, Mesh=56, Header, Mesh, Aabb=1; triangles=2568
m02aa_06a: Header=81, Header, Light=2, Header, Mesh=28, Header, Mesh, Aabb=1; triangles=1924
m02aa_10a: Header=4, Header, Light=33, Header, Mesh=62; triangles=2916
m02aa_04a: Header=68, Header, Light=5, Header, Mesh=58, Header, Mesh, Aabb=1; triangles=2556
m02aa_05a: Header=60, Header, Light=11, Header, Mesh=60, Header, Mesh, Aabb=1; triangles=2581
m02aa_09g: Header=55, Header, Mesh=22, Header, Mesh, Aabb=1; triangles=3013
m02aa_09f: Header=52, Header, Mesh=23, Header, Mesh, Aabb=1; triangles=3053
m02aa_09e: Header=52, Header, Mesh=28, Header, Mesh, Aabb=1; triangles=3053
m02aa_09d: Header=37, Header, Mesh=24, Header, Mesh, Aabb=1; triangles=3053
m02aa_09c: Header=38, Header, Mesh=24, Header, Mesh, Aabb=1; triangles=2995
m02aa_09b: Header=51, Header, Mesh=33, Header, Mesh, Aabb=1; triangles=3319
m02aa_09a: Header=65, Header, Mesh=23, Header, Mesh, Aabb=1; triangles=3059
m02aa_sky: Header=1, Header, Light=1, Header, Mesh=6; triangles=162
# models.bif: 2832 models read without exception
```
(Les noms de type sont ceux du `[Flags]` : « Header, Mesh » = flags 0x021, « Header, Mesh, Aabb » = 0x221. Le décompte des lignes `Header=65` est le nombre de noeuds de flags 0x001 seuls.)

**Hauteur du modèle humain (p_bastilabb, axe Z) : 1.747 m** (X de -0.527 à 0.526, Y de -0.145 à 0.146). Avec l'échelle par défaut 36 : environ 63 unités JA.

## Préoccupations
- Le décodeur de quaternions compressés n'est exercé que par le test unitaire (aucune donnée réelle dans les noeuds). Si une étape ultérieure lit les animations, la convention de signe de w (références : négatif ; ici canonicalisé w >= 0) devra être revérifiée sur des animations réelles.
- Le nom du modèle garde la casse du fichier (`M02aa_01a`) ; `SuperModel` est en minuscules (null si NULL ou vide). Les noms de noeuds gardent leur casse.
- Light/Emitter/Reference + Mesh refusés (inexistant dans les données) ; Saber lu comme maillage ordinaire, non vérifié visuellement.
- Pas de `ImportLog` dans l'API (conforme à la signature de la spec) : seuls sont ignorés sans trace les contrôleurs hors position/orientation (figé spec §6) et les maillages sans MDX (UV et normales null).
