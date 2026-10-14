# Étape 04 — Revue (LYT, TPC/TGA/TXI)

Diff relu une fois (49c2fc6..c7ed7f8). Le rapport d'implémentation est traité comme des affirmations non vérifiées. La suite de tests n'a pas été relancée. Le contrôle visuel des PNG (logo, `lbl_abilities`) n'est pas vérifié ici : il repose sur le rapport.

## Conformité

- ✅ Fichiers et interfaces conformes au plan (`LayoutFile`, `LayoutRoom`, `LayoutDoorHook`, `KotorTexture`, `DxtDecoder`, `TpcDecoder`, `TxiInfo`, `TextureKind`, `TextureLookup`).
- ✅ LYT : culture invariante, modèles et pièces en minuscules, sections track et obstacle lues et ignorées, `\r` toléré.
- ✅ DXT1 : mode 3 couleurs sur `c0 <= c1`, indice 3 transparent. DXT5 : tables d'alpha 8 et 6 valeurs correctes. Recadrage des dimensions non multiples de 4.
- ✅ TPC : en-tête, encodages 1, 2 et 4, premier mip seulement, texte TXI après toutes les mips (tailles calculées par niveau, DXT avec minimum d'un bloc), cube = première face avec `Raw["cube"]`.
- ✅ Contrat « ligne 0 = haut » : les lignes TPC sont retournées (stockage bas-en-haut). Le TGA l'est via le bit d'origine. Le choix est justifié par xoreos (aucun flip à la lecture, v=0 en bas) et par une vérification visuelle déclarée, et il est testé par `Tpc_uncompressed_rgba_roundtrip`.
- ✅ Casse (Review Focus) : `Find` met le nom en minuscules, et le test demande `"LTA_X"`.
- ✅ Ordre diffuse : TPC puis TGA (+TXI). Ordre lightmap : TGA puis TPC. Les deux sont testés avec la même ressource en TGA dans `Override` et en TPC dans `swpc_tex_tpa.erf`.
- ✅ TGA RLE (type 10) ajouté avec test. Le test `Rejects_compressed_tga` est remplacé à juste titre, car il était devenu faux.
- ✅ `IsAnimated` : `proceduretype`, `numx`, `numy` ou `defaultwidth`.
- ✅ Tests réels (`SkippableFact`) présents, avec écriture des PNG.
- ⚠️ Ajout non demandé : `EnvMapTexture` retombe sur `bumpyshinytexture` (voir Mineur 1).

## Points forts

- Décodeurs courts et corrects : les formules DXT sont exactes, les tailles tronquées sont validées, `checked` est utilisé sur les tailles.
- Le TGA RLE gère les paquets qui chevauchent les lignes (tampon plat) et rejette les dépassements.
- Le rapport est honnête sur ses limites : encodage 0x0C, gris compressé, doorhook réel non vérifié.
- Le drapeau `StoredBottomUp` et son commentaire documentent le choix d'orientation.

## Problèmes

### Critique
Aucun.

### Important
Aucun.

### Mineur
1. `src/Kotor.Core/Textures/TxiInfo.cs:523` : le repli de `EnvMapTexture` sur `bumpyshinytexture` mélange deux propriétés TXI distinctes, et le plan ne le demande pas. Un appelant ne peut plus distinguer une envmap d'une bumpyshiny. Fix : ne lire que `envmaptexture`, et laisser l'étape matériaux lire `Raw["bumpyshinytexture"]`.
2. `src/Kotor.Core/Textures/TpcDecoder.cs:418` et `TextureLookup.cs:352-356` : un TPC d'encodage non géré (0x0C, gris compressé) lève une exception dans `Find`. Un TPC de diffuse trouvé mais non décodable interrompt donc toute la conversion au lieu de passer au TGA ou de renvoyer `null`. Fix : décider à l'étape 06 (journaliser et ignorer), ou rattraper `InvalidDataException` dans `Find` pour essayer l'autre source.
3. `tests/.../LayoutFileTests.cs` : le format des doorhooks (ordre `room name 0 x y z qx qy qz qw`) n'est testé que sur du texte synthétique écrit d'après PyKotor. Le test réel ne regarde pas les `DoorHooks`. Fix : ajouter une assertion sur un doorhook réel de `m02aa` (nom et pièce plausibles).
4. `tests/.../TextureLookupTests.cs` : l'ordre « TPC de `tpa` avant BIF » n'est pas testé. C'est du ressort du résolveur (étape 02), mais le Review Focus le cite. Le test couvre seulement TPC `tpa` contre TGA `Override`. Fix : ajouter un cas avec le même TPC dans `tpa` et dans un BIF (étape 02 ou ici).
5. `tests/.../TpcDecoderTests.cs` : `Tpc_cube_keeps_first_face` ne couvre que l'encodage 2 non compressé, pas un cube DXT. De même, aucun test du TXI séparé en TGA (`.txi` à côté).
6. `TextureLookup.Find` : `name` null n'est pas validé (`ArgumentNullException.ThrowIfNull` manquant).

## Évaluation

**Approuvé** (aucun problème Critique ou Important ; 6 points Mineur à traiter à la discrétion de l'intégrateur). Le contrôle visuel final d'orientation reste prévu aux étapes 06 et 09.
