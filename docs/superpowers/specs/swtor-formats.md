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

### Terrain (`\engine\heightmap.hms`)

Blob zlib dont le flux décompressé commence par `02`, `u32 66`, `u32 62` (largeur × hauteur en cellules ? 66 et 62 sont aussi
deux champs `f32` de l'objet) puis des flottants. Non décodé (Basse) : sujet de T5.

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
- Convention d'Euler (**confiance moyenne-basse pour les rotations multi-axes**) : angles en degrés, sens mathématique direct
  (+90° sur Y envoie +X vers -Z), ordre « X d'abord, puis Y, puis Z » autour d'axes monde (`qx * qy * qz`). La majorité des objets n'ont
  qu'un lacet sur Y (0, ±90, ±180, ±45...) et sont indépendants de l'ordre ; les combinaisons usuelles `(0, ±90, ±90)`, `(±90, ±90, 0)`,
  `(0, ±90, 180)` ne se départagent pas par les tests statistiques réalisés (recouvrement d'AABB, appartenance enfant/parent) : à
  valider visuellement en T4/T7 sur un mur ou une poutre couchée, quitte à changer `AreaTransforms.RotationFromEuler`.

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

- **Ordre effectif corrigé dans la doc** : `qx * qy * qz` (System.Numerics, produit de Hamilton) applique **Z d'abord, puis Y, puis X**
  (test `Euler_yaw_sends_X_to_minus_Z_and_Z_is_applied_first` : `(90,90,0)` envoie +Y vers +Z, pas vers +X). La phrase « X d'abord » de T2 décrivait
  l'intention, pas le code. Le code est inchangé : les rotations à un seul axe (lacet Y) ne dépendent pas de l'ordre, et aucun test
  statistique T4 ne départage les ordres pour les rotations multi-axes (étiquetage par nom « floor/wall », 120 zones : 54 à 61 % de
  verdicts conformes pour tous les ordres, écart non significatif). Reste **ouvert** : à confirmer visuellement (T7) sur une poutre couchée.
- **Axes** : le repère SWTOR est (X, Y haut, Z) ; la scène est (X, Y, Z haut). Conversion retenue : rotation propre `(x, y, z) -> (x, -z, y)`
  (déterminant +1). Elle est cohérente avec les données : sur les 33 906 triangles non collision de `war_planworking`, le produit
  vectoriel des arêtes est toujours du même côté que les normales (sens anti-horaire, repère direct) et le reste après conversion
  (159 617 triangles, 0 incohérent). Une symétrie (`--axes mirror`) existe si on découvrait un monde gaucher (texte inversé sur un panneau
  visible en jeu) ; indétectable sans géométrie asymétrique, donc non tranché par calcul.
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

## Echelle du monde (T7-echelle-2)
**Unite GR2 SWTOR = 7,8 m JA (312 unites JA).** Un seul facteur global, derive du personnage humain mis a la hauteur de la capsule du joueur JA, 64 unites (details : `plans/swtor/reports/T7-echelle-2.md`, recalibrage : `T7-dark-council.md`). Avant l essai en jeu : 338 (8,45 m), humain = modele JKA de 72 u, jugee "un peu trop grande".
- Humain masculin (bmn) debout : semelle -0,001, haut du crane 0,205, cou/cheveux ~0,213 unite GR2 (meme espace pour tete, corps, bottes ; maillages en pose de liaison).
- SWTOR-Character-maker convertit les personnages en JKA avec un facteur uniforme (cou SWTOR -> joint `cervical`) de 338 : humain SWTOR = modele JKA de 72 unites (KOTOR : 40 u/m, humain 1,8 m = 72 u). Nous retenons 64 u (capsule de collision du joueur, 64 / 0,205 = 312) car 72 donnait une carte trop grande en jeu.
- Autres donnees SWTOR dans le meme ordre de grandeur (mediane de 120 zones) : chaise 0,157, table 0,10, rambarde 0,135, marche d'escalier 0,025 (21 cm), points de couvert espaces de 0,18 (~1,5 m), noms de modeles `h3m` = 0,3 unite = 3 m. Les anciennes affirmations "1 unite = 1 m pour l'architecture" (T1) et "x2" (T7) ne sont pas etayees ; "1 unite = 10 m" etait l'ordre de grandeur correct.
- Pas de facteur d'echelle dans les .gr2 (en-tete, aucun noeud de transformation pour les modeles statiques) ; instances d'architecture a l'echelle 1.
- Le kit `spc_space/arch` (war_planworking) est un kit de vaisseau capital : porte `doorframe_sm` 0,96 x 0,8 d'ouverture (8,1 x 6,8 m), plafond 1,4 (11,8 m). L'humain y fait 27 % de la hauteur de porte : ce ne sont pas des portes a taille humaine.
- Code : `SwtorWorldScale.MetresPerGr2Unit` (= 312 / 40 = 7,8) ; `--world-scale <x>` est un multiplicateur manuel en plus (defaut 1).
