# Formats SWTOR : zones (`area.dat`, rooms `.dat`)

Document de rétro-ingénierie écrit pendant la tâche T2 du plan `docs/superpowers/plans/2026-10-16-swtor-maps.md`.
Code correspondant : `src/Swtor.Core/Areas/` (`AreaDatReader`, `RoomDatReader`, `AreaLayout`, `AreaTransforms`) et commande
`kotor2ja swtor-dump-area`.

Niveaux de confiance : **Haute** = vérifié sur les 267 zones du dépôt (0 échec de lecture, tailles d'objets cohérentes
à l'octet près) ; **Moyenne** = cohérent sur les exemples examinés mais non prouvé ; **Basse** = hypothèse.

Tous les entiers sont little-endian. « Chaîne ASCII » = `u32 longueur` (terminateur nul compris) + octets UTF-8 ;
« chaîne UTF-16 » = `u32 longueur en octets` (nul compris) + UTF-16LE.

## Sources et licences

- Aucune spécification publique de ces deux formats n'a été trouvée : recherches web sur les chaînes `AREA_DAT_BINARY_FORMAT_` /
  `ROOM_DAT_BINARY_FORMAT_`, sur le wiki des SWTOR Slicers (<https://github.com/SWTOR-Slicers/WikiPedia>), sur l'addon Blender
  *SWTOR Area Assembler* (ZeroGravitas / Crunch) et sur les outils GomLib / TorLib / PugTools.
- Le wiki des Slicers indique que l'*Area Assembler* **ne lit pas les `.dat` directement** : il importe des exports JSON produits par
  le World Viewer / File Reader de Jedipedia.net. Il n'y a donc pas de code de lecture `.dat` à reprendre, et **aucun code GPL n'a été copié**.
- Le format ci-dessous est entièrement issu de l'analyse hexadécimale de `C:\jka_tor_assets\resources\world\areas\*` et de
  recoupements statistiques (parcours des 16 640 fichiers de room, 4,6 millions d'objets). Le code est original.
- Le wiki des Slicers (page *GR2 File Structure*) a seulement servi de contexte pour les modèles, déjà lus par `Gr2Reader` (T1).

## Contenu d'un dossier de zone

`world/areas/<id numérique 64 bits>/` :

| Fichier | Rôle |
|---|---|
| `area.dat` | Liste des rooms, table des assets (id → chemin), points d'arrivée, réglages d'ambiance. |
| `<room>.dat` | Objets placés d'une room (un fichier par nom listé dans `area.dat`). `_everywhere_.dat` = objets valables partout (triggers, régions). |
| `mapnotes.not` | XML (texte) des notes de carte. Non exploité. |
| `*.dds`, `minimaps/` | Textures de minicarte (`map_<nom>_r.dds`). Le nom du fichier donne souvent le nom de la zone (ex. `map_war_planworking_main_r.dds`). |
| `skyscene.dat`, `space.dat`, `default.dat`, `base.dat`... | Rooms comme les autres ; `skyscene` contient le décor de ciel (modèle + caméra à `(0,-100,-100)`). |

Les rooms sont reliées (voir « rooms liées »). La room nommée par la propriété `skyscene_room` d'`area.dat` n'est pas
du niveau jouable, c'est le ciel (étoiles, planète...).

## area.dat (`AREA_DAT_BINARY_FORMAT_`)

```
u32 24 + "AREA_DAT_BINARY_FORMAT_\0"        entête (Haute)
u32[9]  offsets de sections               (Haute pour 0..3, Basse pour 4..8)
u16[11] mots de version/drapeaux          = 1,1,1,1,1,1,1,1,2,1,1 sur tous les fichiers vus (ignorés)
```

L'entête fait toujours 0x56 octets, la section 0 commence donc en `0x56`.

| Section | Offset (index) | Contenu | Confiance |
|---|---|---|---|
| 0 | `[0]` | `u32 n` puis `n` chaînes ASCII : noms de rooms (ex. `_everywhere_`, `room_01`, `SPACE`, `skyscene`). Fichier = `<nom>.dat`. | Haute |
| 1 | `[1]` | **Table des assets** : `u32 n` puis `n × { u64 id; chaîne ASCII chemin }`. Chemins style `\art\static\area\...\x.gr2`, `\engine\light.lit`, `\spn\...\x.spn_c`, `\server\mpn\...`, `\environments\...\x.fxp`. | Haute |
| 2 | `[2]` | Objets de zone : points d'arrivée de classe (`arrival_sith_warrior`, `arrival_spy`...). Enregistrement `{ u64 id (0x40000006xxxxxxxx); chaîne ASCII nom; ... }`, la suite (booléen, 4 floats à 1.0, 2 ids, 5 floats dont un quaternion probable) n'est pas décodée ; le lecteur retrouve les noms par balayage `[id][longueur][ASCII imprimable][0]`. | Moyenne (noms) / Basse (transformations) |
| 3 | `[3]` | **Propriétés** : `u32 n` puis `n × { chaîne ASCII clé; chaîne ASCII valeur }`. Clés : `Area` (zone entière) puis une entrée par room ayant des réglages propres (`SPACE`, `skyscene`...). | Haute |
| 4–8 | `[4..8]` | Quatre compteurs nuls (`u32 0`) puis un dernier bloc `{ u64 id; chaîne nom de zone (ex. "war_planworking"); ... }`. Non décodé. | Basse |

### Chaîne de propriétés (valeur de la section 3)

Format texte : `~0|<N>|` puis `N` fois `<longueurNom>|<nom>|<longueurValeur>|<valeur>|` (longueurs en caractères).
Exemple : `~0|35|15|default_minimap|0||23|diffuse_bloom_intensity|1|0|18|static_light_front|8|#1,1,1,1|...`.
Clés observées (Haute) : `is_outdoor` (`true`/`false`), `skyscene_room`, `environment_map`, `illumination_map` (rampe de lumière),
`static_light_front/side/back` (couleurs `#r,g,b,a`), `static_light_intensity`, `static_light_model`, `static_fog_*`
(`ramp`, `min`, `max`, `start`, `colors`, `colors_start`, `colors_end`, `color_sky`), `static_clip_distance`,
`static_clip_extended`, `light_azimuth`, `light_polar` (direction du soleil, degrés), `bloom_intensity`, `diffuse_bloom_*`,
`specular_bloom_*`, `skybox_bloom_*`, `skyspec_intensity`, `rimlight_intensity`, `emissive_bloom_intensity`,
`cast_directional_shadows`, `default_minimap`, `timeline`, `modified_by`.

## Room `.dat` (`ROOM_DAT_BINARY_FORMAT_`)

```
u32 24 + "ROOM_DAT_BINARY_FORMAT_\0"
u32 A   offset de la liste d'objets            (Haute)
u32 B   offset de la liste des rooms liées      (Haute)
u32 C   offset de la paire de chaînes finale    (Moyenne)
u16[4]  = 3,1,1,1 (ignorés)
chaîne ASCII  chemin interne : "/world/areas/<id>/<room>.dat"
@A: u32 n  puis n objets
@B: u32 m  puis m chaînes UTF-16 (noms de rooms liées / voisines : room_01 -> airlock_01, hallway_01, SPACE)   (Moyenne)
@C: [optionnel] chaîne UTF-16 clé + chaîne UTF-16 valeur (ex. "Area" -> "1", ou "SPACE" -> "1"), sans compteur   (Basse)
    puis u32 2, 40 octets nuls, u64 id, 0x00 0x01 : bloc final non décodé
```

### Objet

```
u32  0xABCD1234                magique (octets 34 12 CD AB)
u8   version                   0 = modèle, 2 = fxplaceable, 3 = lumière... (variante de la classe)
u64  id                        unique dans la zone (0x40000006BB962168...)
u64  typeId                    clé de la table d'assets d'area.dat : c'est la CLASSE ET LE MODÈLE de l'objet
u8   drapeau (toujours 0)
u32  nFields
u32  taille                    nombre d'octets qui suivent ce champ jusqu'à la fin de l'objet (contrôle d'intégrité)
u8   répétition de 'version'
nFields x champ
```

L'objet suivant commence à `offset(champ taille) + 4 + taille`. 100 % des objets de 16 640 fichiers vérifient cette égalité.

**Le type d'un objet est un chemin d'asset** : pour un modèle statique, `typeId` désigne directement le `.gr2`
(`\art\static\area\all_all\item\tech\all_item_techcave_pillar.gr2`) ; pour une lumière c'est `\engine\light.lit`, pour un
effet `\engine\fxplaceable.fxp`, etc. Il n'y a pas d'indirection supplémentaire : chemin gr2 → `SwtorAssets.Resolve`.
Les `typeId` absents de la table (352 objets sur 4,6 M) sont conservés en `InstanceKind.Unknown`.

### Champ

```
u8  type
u32 hash du nom (le nom d'origine n'est pas stocké ; la même valeur désigne le même champ dans tous les fichiers)
valeur selon le type :
```

| type | taille / forme | Remarque |
|---|---|---|
| 0 | 1 octet | booléen (Haute) |
| 1 | 4 octets | entier 32 bits non interprété (valeur toujours petite ou nulle ; sens inconnu) |
| 3 | `i32` | entier |
| 4 | `f32` | flottant |
| 5 | `u64` | référence d'objet (0 = aucune) |
| 6 | 3 × `f32` | vecteur |
| 7 | 4 × `f32` | vecteur 4 / couleur RGBA |
| 8 | chaîne UTF-16 | |
| 9 | `u32 n` + `n` octets | blob (flux zlib `78 9C` : données de terrain `heightmap.hms`, 26 781 occurrences) |

Les types 0 à 9 sont les seuls rencontrés ; un autre type lève `GameFormatException` (jamais observé).

Champs communs (tous les objets, confiance Haute pour la position/échelle/parent, Moyenne pour la rotation) :

| hash | type | sens |
|---|---|---|
| `0x40865E7F` | 5 | **parent** (id d'un autre objet de la zone, 0 = racine) |
| `0x4F77E269` | 6 | **position** (m) |
| `0xB181622A` | 6 | **échelle** par axe (1,1,1 par défaut) |
| `0x8C64AF1E` | 6 | **rotation Euler en degrés** (X, Y, Z) |
| `0xA67AE663` | 7 | couleur RGBA d'une **lumière** |
| `0xC2676F38` | 8 | nom du fxspec d'un `fxplaceable` (ex. `environments/all/env_all_radiator_steam`) |

Les modèles ont typiquement 28 champs, les `fxplaceable` 30, les lumières 42 (le reste : drapeaux booléens, valeurs de profil de
lumière `art/lights/fill_235_210_207`, `falloff_linear`, `lum_3point`...). Leur signification n'est pas établie.
Pour une lumière l'échelle `(sx,sy,sz)` (de l'ordre de 0,5 à 8) semble être l'extension de l'ellipsoïde d'éclairage ; aucun rayon
dédié n'a été identifié (Basse).

### Familles d'assets rencontrées (table d'assets de toutes les zones)

`.gr2` 133 099 (modèles), `.spn_c/.spn_p/.spn_*` (spawns), `.mpn` (missions), `.enc`, `.stg`, `.cos` (costumes de PNJ), `.prt` (particules),
`.mag` (modèles animés `art\dynamic\spec\...`), `.fxp` (effets `\environments\...`), puis les classes moteur présentes dans chaque zone :
`.lit` lumière, `.trg` déclencheur, `.p` portail, `.cvr`/`.cdr` couverture, `.rgn`/`.env` régions, `.rbd` volume de room, `.hms` terrain,
`.box`, `.cam`, `.wtr` eau, `.wws` son, `.mir`, `.fla`, `.bil`, `.bon`, `.zzp`, `.fol`, `.phy`, `.phj`, `.amk`, `.pfn`, `.sfq`, `.ext`, `.apl`.
Il n'y a pas d'extension SpeedTree dans la table : les arbres ne sont pas des objets d'area visibles ici (à confirmer en T5).

### Terrain (`\engine\heightmap.hms`) (decode : Haute pour la grille de hauteurs, Moyenne pour le masque, Basse pour les textures)

Un objet heightmap est une **dalle de terrain** (centaines par zone exterieure : ex. 52 a 163 par room de Tatooine). Le champ de type 9 est un
blob compresse : **zlib** (`78 ..`, ~80 %), **zstd** (`28 B5 2F FD`) ou **gzip** (`1F 8B`, voir version 9 plus bas). Flux decompresse (version 1 ou 2) :

| Offset | Contenu |
|---|---|
| 0 | `u8` version (1 ou 2) |
| 1, 5 | `u32 R` (echantillons le long de X, Â« rangees Â»), `u32 C` (le long de Z) ; 65 x 65 pour une dalle standard, 32 x 32 a 71 x 71 sinon, rarement non carre |
| 9 | `u8` drapeau : 0 = rien ; 1 = un masque de trous suit |
| 10 | si drapeau = 1 : masque, `ceil(R*C/8)` octets, un bit par echantillon (indice `r*C+c`, **bit de poids fort d'abord**), 1 = present |
| ensuite | pourtour : `2*(R-1) + 2*(C-1)` `f32` (hauteurs en unites du monde) |
| ensuite | interieur : `(R-2)*(C-2)` `u16` little-endian, indice `r*(C-2)+c`, **hauteur = valeur / 512** |
| ensuite | donnees non lues : carte de melange des materiaux (4 x `u16` par texel, valeurs multiples de 256, sommes 0xFF00 : 2 a 4 couches), maillage de rendu (sommets + index `u16` d'un TIN), magique `AB EF CD AB` en fin de flux |

Pourtour : quatre bords de `R-1` ou `C-1` elements, stockes par **paires entrelacees** : d'abord `(bord -Z, bord +Z)` pour `k = 0..R-2` (le long de X),
puis `(bord -X, bord +X)` pour `k = 0..C-2` (le long de Z). L'element `k` est l'echantillon de la grille complete `R x C` a l'indice `k` le long du bord ;
l'element 0 est un coin. Le coin (+X, +Z) n'est stocke nulle part. Les valeurs du pourtour sont egales a celles du pourtour de la dalle voisine
(verifie : bords +Z / -Z de dalles voisines identiques a 0,0000 pres ; coin different, car partage par quatre dalles). L'interieur occupe les
echantillons `1..R-2` et `1..C-2`.

Placement (verifie sur 2 237 modeles poses sur le sol de Tatooine : mediane des ecarts de hauteur 0,04 pour le quartile bas) :
- la **position de l'objet est le centre** de la dalle ; l'echantillon `(r, c)` est a `X = px + (r - (R-1)/2) * 0,2`, `Z = pz + (c - (C-1)/2) * 0,2`,
  `Y = py + hauteur` (les rangees courent donc le long de **X**, les colonnes le long de **Z**) ; les dalles voisines sont a `(R-1) * 0,2` (12,8 pour 65).
- **pas constant de 0,2 unite du monde** (verifie : espacement minimal des dalles = 12,8 / 12,6 / 12,4 pour 65 / 64 / 63 echantillons ; le champ f32
  `D9DDF326` / `1B205D23` des objets n'est PAS la taille : 32, 64, 65, 128 ou 129 sans rapport fiable).
- echelle et rotation de l'objet : toujours 1 et 0 observes ; la matrice monde de l'objet est appliquee quand meme.
- masque de trous : 1 = present (hypothese, a confirmer en jeu : les 0 forment des diagonales/zones sous falaises et batiments).

Non gere (rejete avec le code `SWTOR_TERRAIN_UNSUPPORTED`, ~5 % des dalles) : **version 9** (gzip, dalles `Default` de 61 x 68 : flottants, tres
differentes), versions 8, dalles de 2 x 2 a 3 x n (fictives), et toute dalle tronquee. Orientation des dalles non carrees : la grille a `C-2` colonnes
par rangee (verifie par lissage sur ~3 000 dalles), `R` = premier `u32`.

**Textures du sol : non decodees.** Le terrain n'a aucun champ de materiau lisible (les champs texte de l'objet sont vides sauf `STANDARD`) ; la carte
de melange indexe des couches dont les noms n'ont pas ete retrouves (ni dans le blob, ni dans area.dat, ni dans `art/shaders/materials`). L'export
prend donc comme texture du sol le materiau le plus represente parmi les surfaces tournees vers le haut posees sur le terrain (roches, dalles), sinon
une couleur de sable unie.

Conversion (Swtor.Pipeline `SwtorTerrain`, `SwtorSceneBuilder.AddTerrain`) : meme matrice, recentrage et echelle que les modeles ; grille decimee au pas entier
(1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 21, 32, 64 ; dernier echantillon toujours garde pour que les dalles se raccordent sans fente) qui tient dans
`TerrainMaxTriangles` (0 par defaut = aucune limite, pleine resolution : le PVS par room evite de tout dessiner) ; cellule percee si ses 4 sommets ne sont pas tous presents (au pas > 1 :
si au moins une cellule fine l'est) ; diagonale choisie pour suivre le relief ; un mesh par room (noeud `terrain`) et collision dans le walkmesh de la room.
## Hiérarchie et composition des transformations (Haute pour la hiérarchie, Moyenne pour la rotation)

- `parent` référence l'`id` d'un autre objet (de la même zone, parfois d'une autre room) : 1 646 171 des 1 646 401 références
  (99,99 %) pointent vers un objet existant. Cas fréquent : un `gr2` « enfant » (renfort, panneau, appareil) attaché à un
  pilier/cadre de porte, ou un `fxplaceable` attaché à un objet.
- Position, rotation et échelle d'un enfant sont **locales au parent**. Vérifié sur des paires pilier/renfort : la position
  locale d'un renfort enfant `(1,2 ; 0,04 ; -0,008)` n'a de sens qu'exprimée dans le repère du pilier (qui est couché avec une rotation de ±90° sur Z).
- Matrice locale (vecteur ligne, System.Numerics) : `M = S * R * T`. Monde : `M_monde(enfant) = M_local(enfant) * M_monde(parent)` (récursif).
  `AreaLayout.World()` l'implémente avec garde-fou anti-cycle.
- Unités : 1 unité = 1 m pour l'architecture (modules de 3,2 m : dalles `spc_arch_ss_mod_floor_plain` de 3,2 × 3,2 m posées à
  des multiples de 3,2 ; murs `32m` de 3,2 m). Y vers le haut. **Exception** : les intérieurs de vaisseaux joueurs `spc_ps_*_int_*`
  et les pièces de stronghold sont modélisés à environ 1/5 (porte `spc_ps_sith_int_door_large` : 0,73 × 0,47 ; hauteur de
  salle ~1 au lieu de ~2,5) : un facteur d'échelle d'affichage est appliqué ailleurs (non trouvé) ; ne pas prendre ces zones
  pour valider l'échelle.
- Convention d'Euler (**T7 Euler 2 ; confiance Haute pour l'ordre, Moyenne pour le sens du repère**) : angles en degrés,
  sens mathématique direct (+90° sur Y envoie +X vers -Z), ordre **Z d'abord (roulis), puis X (tangage), puis Y (lacet autour de la verticale du
  parent)**, soit l'ordre « yaw, pitch, roll » de `D3DXMatrixRotationYawPitchRoll` ; matrice colonne `Ry * Rx * Rz`, soit `qy * qx * qz` en System.Numerics.
  Les rotations sans X, sans Z, ou à un seul axe (la quasi-totalité : lacet seul, ou `(0, ±90, ±90)`) ne dépendent pas de l'ordre des deux rotations internes
  X et Z ; le lacet en dernier est établi par T7 Euler 1, l'ordre Z puis X par T7 Euler 2. Voir « Validation T7 : ordre d'Euler ».

## Résultats de validation

- 267 zones, 16 640 fichiers : 0 échec ; 4 627 430 objets ; 2 860 567 modèles `gr2` dont 100,00 % (2 860 498) existent sur disque
  (25 543 chemins distincts sur 25 560 trouvés).
- Zone `4611686045100172646` (« war_planworking », vaisseau Sith, flashpoint de chapitre 1) : 6 rooms, 135 assets, 75 gr2 distincts tous présents ;
  dalles de sol à des multiples exacts de 3,2 m ; emprise des modèles `room_01` 6,9 × 5,3 × 5,3 m, `hallway_01` 10,3 × 4,4 × 4,5 m,
  `airlock_01` 3,9 × 2,6 × 3,7 m.

## Noms des zones (T3)

- **Nom interne** : le dernier bloc d'`area.dat` (après la section 8) contient `[id 64 bits 0x4...][u32 longueur][nom ASCII][0]` ; pris par `AreaDatReader.ScanInternalName` (267/267 zones, confiance élevée). Ex. `war_planworking`, `tat_main`.
- **GOM** (`systemgenerated/buckets`, lecteur copié dans `Swtor.Core/Gom`) : l'objet `mapAreasDataProto` est une table `id de zone -> { texte (champ 0x400000080027BF7F), clé de chat (0x400000085CD72A38) }` ; le texte est un identifiant de chaîne 64 bits, résolu dans `fr-fr/str/sys/worldmap.stb`, puis `gui/map.stb`, `spvp/misc.stb`, `rdd.stb` (la table de chaînes n'est pas indiquée, on les essaie dans cet ordre). Les objets `world.areas.<id>.mapdata` donnent la liste des pages de minimap de la zone (nom `map_*`, identifiant de chaîne) ; `sysAreaInfoTablePrototype` associe nom interne et id de zone ; `hydAreaIdToPlanetAliasDataPrototype` associe id de zone et alias de planète (non localisé).
- Seul le dossier `fr-fr` des chaînes est présent dans l'extraction (`gamedata/str` ne contient que `stb.manifest`) : les noms lisibles sont en français.

## Validation T4 : axes et Euler

- **Ordre effectif (corrigé ensuite par T7 Euler 2 : c est Z, X, Y)** : `qx * qy * qz` (System.Numerics, produit de Hamilton) applique **Z d'abord, puis Y, puis X**
  (test `Euler_yaw_sends_X_to_minus_Z_and_Z_is_applied_first` : `(90,90,0)` envoie +Y vers +Z, pas vers +X). La phrase « X d'abord » de T2 décrivait
  l'intention, pas le code. Le code est inchangé : les rotations à un seul axe (lacet Y) ne dépendent pas de l'ordre, et aucun test
  statistique T4 ne départage les ordres pour les rotations multi-axes (étiquetage par nom « floor/wall », 120 zones : 54 à 61 % de
  verdicts conformes pour tous les ordres, écart non significatif). Reste **ouvert** : à confirmer visuellement (T7) sur une poutre couchée.
- **Axes** : le repère SWTOR est (X, Y haut, Z) ; la scène est (X, Y, Z haut). Conversion retenue : rotation propre `(x, y, z) -> (x, -z, y)`
  (déterminant +1). Elle est cohérente avec les données : sur les 33 906 triangles non collision de `war_planworking`, le produit
  vectoriel des arêtes est toujours du même côté que les normales (sens anti-horaire, repère direct) et le reste après conversion
  (159 617 triangles, 0 incohérent). Une symétrie (`--axes mirror`) existe si on découvrait un monde gaucher (texte inversé sur un panneau
  visible en jeu) ; indétectable sans géométrie asymétrique, donc non tranché par calcul.

## Validation T7 : ordre d'Euler (remplace la conclusion « ordre Z-Y-X » de T4 ; l'ordre X, Z, Y de T7 Euler 1 est lui-même corrigé en Z, X, Y par T7 Euler 2)

**T7 Euler 2 (ordre Z, X, Y, vérité terrain).** Le test de lacet pur ci-dessous ne prouve que « Y en dernier » : X, Z, Y et Z, X, Y le passent tous deux. Les appliques à trois axes
(`(-69,±45,-90)`, `(-61,±135,-90)`) les départagent, et elles étaient « debout de côté » avec X, Z, Y (avant tangentiel). Preuve retenue : chaque applique `spc_item_ss_wall_light01` a un cône de brouillard
`standard_light_fog_cone_01` (12 cônes racines, `y = 83,6101`, échelle ~2,16 x 1,07 x 0,47, `(0, ±45|±135, ±180)`) à 0,15..0,22 d'elle. Le gr2 de l'applique est une plaque
1,1 (X) x 1,6 (Y) dont la plaque arrière est le plan z = 0 (normale -Z, aire 1,56) et la face avant plane le plan z = 0,20 (aire 0,45 + 0,22) : l'origine du cône doit être sur la face
avant, soit z local = 0,20, pour les 12 pièces. Sur 288 conventions (6 ordres x 8 signes x 6 permutations de champs) : **Z, X, Y (`qy*qx*qz`) : écart-type de z local 0,004** (z moyen 0,202,
x 0,21, y 0,00) ; X, Z, Y (ancien) 0,076 ; Z, Y, X (T4) 0,022 mais x dispersé de 0,195 ; toutes les autres (hors alias) >= 0,05. Les seuls autres candidats à z constant sont des alias de la même
rotation (permutation + signes équivalents) ou le même ordre avec Z de signe opposé (plaque retournée de 180° dans son plan, indiscernable par ce test ; le signe `+` est conservé comme
pour les autres axes). Avec Z, X, Y l'avant des appliques (+Z local) regarde vers le haut et vers l'axe de la plateforme parente (+Y 0,87..0,93, cosinus horizontal avec la direction de l'axe 0,96..0,99) :
couvercle incliné de 21..29° d'un pupitre en couronne, face vers les sièges, et non plaque verticale de champ. Aucun mur plan ne sert de référence : la plaque arrière touche de
multiples facettes (armoires `all_arch_imp_war_room_tech_cabinet`, murs `vb_wall`) sans plan commun (alignement < 30 % des sommets pour tout candidat) ; ce critère « face de montage contre un mur »
n'a donc pas été retenu. Effet du changement : 24 instances de la salle (12 appliques, 8 cônes de brouillard `(±45, 0, 180)`, 2 piliers `all_arch_imp_buildings_pillar_1` `(79,6 ; ±90 ; -180)`,
2 garnitures `kor_item_dark_council_throne_platform_trim_01`) ; `war_planworking` : 0. Les affirmations de T7 Euler 1 ci-dessous sur les piliers à Z = 180 (boîte centrée) sont à lire comme
valables pour l'ordre « lacet en dernier » (les deux ordres donnent des boîtes de même taille, symétriques l'une de l'autre).

Les 6 ordres x 2 signes ont été comparés sur des pièces dont la disposition impose une relation géométrique (pas sur des statistiques) :

- **Appliques `spc_item_ss_wall_light01`** de `station_dark_council_chamber` (zone `4611686142106770000`) : 12 pièces rangées en cercle autour
  du parent (rotations `(-69,±45,-90)` et `(-61,±135,-90)`). Si l'ordre est le bon, la rotation relative de deux pièces d'une même famille est un lacet
  pur (la verticale reste la verticale) et ce lacet amène la position de l'une sur celle de l'autre. Écart moyen de la verticale, en degrés :
  `Rx*Ry*Rz` (ancien code) **79,9** ; `Rx*Rz*Ry` 98,2 ; `Ry*Rx*Rz` 4,4 ; **`Ry*Rz*Rx` 0,0** ; `Rz*Rx*Ry` 98,2 ; `Rz*Ry*Rx` 96,7. Le signe
  `+` est le bon : avec `-` l'erreur de sens du lacet est de 160 à 180° (alors qu'elle est <= 21° avec `+`, résidu dû aux deux familles de hauteurs).
- **Piliers `all_arch_imp_buildings_pillar_1`** `(79,6 ; ±90 ; -180)` et `(-81,7 ; ±90 ; 0)` : avec `Ry*Rz*Rx` leur boîte englobante monde est centrée
  sur leur ancre (z = -3,66, boîte -3,87..-3,49) et les paires gauche/droite sont symétriques ; avec l'ancien ordre elles débordent de 1,1 m
  (z jusqu'à -4,79). Écart de symétrie 12 deg (contre 118).
- **Écrans muraux `all_item_tech_wall_monitor`** `(20, ±90, 0)` de `war_planworking` : avec `Ry*Rz*Rx` l'écran de 0,41 x 0,24 m est incliné vers l'avant
  (tangage, hauteur 0,23 m, normale `(-cos20, -sin20, 0)`) ; avec l'ancien ordre il est tourné de 20° dans son plan (roulis, hauteur 0,36 m).
- Les cas `(0, ±90, ±Z)` (panneaux de hangar `all_arch_imp_space_mod_hangar_wall_flat_med`, `spc_arch_imp_mod_prop_tech_panel`) ne dépendent pas de l'ordre
  (X nul : `Rx*Ry*Rz` = `Ry*Rz*Rx`).
- **Repère (chiralité)** : non tranché par calcul. Le sens positif d'un angle est mathématique direct dans le repère des positions (vérifié ci-dessus), ce qui
  vaut aussi pour une matrice D3D ligne ; la cohérence des normales et du sens des triangles (T4) est compatible avec la rotation propre `(x, y, z) -> (x, -z, y)`.
  Aucune pièce asymétrique de la salle ne départage une symétrie (la crête impériale est symétrique) : l'option `--axes mirror` reste le recours si un texte apparaît inversé en jeu.
- Tests : `DarkCouncil_wall_lights_differ_by_a_pure_yaw`, `ShipInterior_wall_monitors_are_pitched_not_rolled` (SkippableFact, ressources réelles) et
  `Euler_yaw_sends_X_to_minus_Z_and_order_is_Z_then_X_then_Y` (synthétique) et `DarkCouncil_wall_light_beam_origin_lies_on_the_front_face_and_the_light_faces_up_and_inward` (T7 Euler 2, ressources réelles).
- Le « mur de détail `spc_arch_ss_wall_detail01` 4 à 7 m sous le sol » de T7-échelle **n'est pas** lié à l'Euler : ses rotations `(0, -90, 0)` et celles de son parent
  n'ont pas de composante X ; voir le rapport T7-euler.
- **Grille 3,2 m** : toutes les dalles `spc_arch_ss_mod_floor_plain` de `war_planworking` ont (x, z) multiples de 3,2 après composition
  parent/enfant (test d'intégration).

## Collision et lumieres (T5)

### Meshes de collision d'un .gr2 (confiance haute)
Sur les 9 563 modeles distincts de 60 zones : `generated_collision` seul 8 582, aucun mesh de collision 530, `collision_override*` seul 352, `generated_collision` + un mesh explicite (`collision`, `collision01`, `collision_hidden`, `xx_collision`...) environ 55.
Les meshes de collision reprennent le materiau du mesh visible (parfois `util_collision*`) et ont en moyenne la moitie des triangles. Regle retenue : `collision_override*` seul s'il existe ; sinon les meshes explicites remplacent `generated_collision` ; sinon `generated_collision` ; sinon repli sur la geometrie visible.

### Champs d'une lumiere `\engine\light.lit` (118 810 instances, 267 zones ; confiance indiquee par champ)
| hash | type | sens retenu | confiance |
|---|---|---|---|
| `0xA67AE663` | 7 | couleur RGBA | haute |
| `0xB181622A` | 6 | echelle = etendue du volume d'eclairage (mediane 1,2 m, p95 4 m, max 785) | moyenne |
| `0x36A29A30` | 3 | type : 2 = volume ponctuel (92 %), 3 = projecteur (60 % ont `falloff_spotlight`), 1 = etendue (echelle p95 6,9 m) | moyenne |
| `0xBC68CFF3` | 8 (texte decimal, point) | multiplicateur de luminosite (1 415 valeurs, mediane 0,9, 1 pour 34 %, max 100) | moyenne-basse |
| `0xEA6E4F13` | 4 | gain secondaire (0,82 / 1 / 0,707 pour 87 %) | basse |
| `0x3B92D7B4` | 8 | rampe d'attenuation `art/lights/falloff_*` (texture 64 x 2 le long de l'axe : `falloff_linear` monte de 0 a 1 puis tombe a 0) | haute (nom) |
| `0x6C84A1CD` | 8 | profil de section `art/lights/lum_*` (64 x 64) | haute (nom) |
| `0x429C878A` | 8 | rampe de couleur `fill_RRR_GGG_BBB` / `defaultLightRamp` | haute (nom) |
Non interpretes : `0x6CB280CE` (0..1, nul pour 75 %), `0x1BE9D670` (0,01 pour 85 %, sans doute un biais), `0x16E929FD` (texte, 1 pour 89 %), `0x71A31565`, une vingtaine de booleens (`0xDBCFD77F`, `0x279A7732`, `0xC5B29609`...). Les noms de champs ne sont pas stockes ; le hash n'est pas un hashlittle2 d'un nom simple (testes : position, scale, color, rotation, parent, chaines de `client.gom`).
Les rampes ne sont pas reproduites : le compilateur n'a que des lumieres ponctuelles a attenuation lineaire.

### Reglages d'area.dat utilises (cle `Area`)
`is_outdoor`, `static_light_front/side/back` (+ `static_light_intensity`), `light_azimuth` / `light_polar` (direction du soleil ; polaire > 90 = la lumiere descend : orientation horizontale non verifiee), `static_fog_*` (decode, non exporte). Les valeurs par defaut (front blanc, side 0,5, back noir) sont presentes dans presque toutes les zones : elles servent peu d'information.

## Echelle du monde (T7-echelle-3)
**Unite GR2 SWTOR = 6,25 m JA (250 unites JA).** Un seul facteur global (positions, sommets, collision, lumieres). Calibre cote JA (details et tableau des candidats : `plans/swtor/reports/T7-echelle-3.md`). Historique : x1 (40) trop petit, x2 faux, 338 (humain -> modele JKA 72 u) trop grand, 312 (humain -> capsule 64 u) "un peu trop grand".
- Les accessoires SWTOR ne sont pas a la taille du personnage (humain bmn 0,205 unite) : assise de chaise ~0,06 (0,29 humain), table 0,10, marche d'escalier 0,025 (mediane des 252 modeles d'escalier ; 90e centile 0,045 ; vrais escaliers jusqu'a 0,058, petites estrades jusqu'a 0,07), porte de batiment 0,36 a 0,55, porte de kit capital `spc_arch_ss` 0,8.
- Contraintes JA (OpenJK-SWGL) : capsule du joueur 64 u (mins z -24, maxs z 40, oeil a 60 u du sol), `STEPSIZE` 18 u (marche franchissable sans saut), saut ~ `JUMP_VELOCITY` 225. A 250 : marche 0,025 = 6 u, 0,058 = 14,5 u, 0,07 = 17,5 u ; assise 15 u ; porte de batiment 90 a 130 u ; humain SWTOR 51 u.
- Pas de facteur d'echelle dans les .gr2 (en-tete, aucun noeud de transformation pour les modeles statiques) ; instances d'architecture a l'echelle 1 (le trone du Conseil noir est a 1,17).
- Le kit `spc_space/arch` (war_planworking) est un kit de vaisseau capital (porte `doorframe_sm` 0,96 x 0,8 d'ouverture) : ce ne sont pas des portes a taille humaine, quel que soit le facteur.
- Code : `SwtorWorldScale.MetresPerGr2Unit` (= 250 / 40 = 6,25) ; `--world-scale <x>` est un multiplicateur manuel en plus (defaut 1).

## Materiaux .mat et correspondance JA (T7-materiaux)
Champs XML lus : `Derived` (shader), `AlphaMode`, `Visibility`, `PolyType`, `IsTwoSided`, entree `DiffuseMap`. Sondage des 26 958 `.mat` : Uber ≥ 10 000 (opaque surtout), Garment/SkinB/Creature (personnages, hors scope), `AnimatedUV|Add` ≥ 1 800, `AnimatedVFX|Add` 119 (dont 26 noms de type cone/faisceau/fog_cone), `EmissiveOnly|Add` ≥ 185, `DiffuseFlat|Full` ≥ 207, `Glass|Full` 112, `Uber|Test` ≥ 665, `Vegetation*|Test`, `Skydome` 85.
| Source | Rendu JA | Collision |
|---|---|---|
| `Visibility=Hidden` (`util_*_hidden`, `occluder_wall`), `util_collision*`, `fx_transparent` (texture 100 % transparente) | non dessine | gardee seulement si `PolyType` n'est pas `Ignore` (ou `util_collision`) |
| `AnimatedVFX` + `Add` nomme cone/faisceau (`fog_cone`, `light_beam`, `light_shaft`, `godray`...) | ignore, code `SWTOR_VFX_SKIPPED` | aucune |
| `Add` (AnimatedUV, EmissiveOnly, autres AnimatedVFX) | `blendFunc GL_ONE GL_ONE`, `cull none`, `nonsolid trans`, sans lightmap ; **rgb premultiplie par l'alpha** de la texture (l'alpha est le masque de la lueur) | aucune |
| `Full`, `Multiply`, `Glass` | `GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA` (Glass : alpha 110 uniforme) | oui |
| `Test` | `alphaFunc GE128`, double face | oui |
| autre (Uber `None`) | opaque ; l'alpha de la texture est ignore (brillance/emissif, pas une opacite) | oui |
