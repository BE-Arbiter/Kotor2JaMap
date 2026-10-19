# Formats Jedi Knight utilises par l'import

Date : 2026-10-19. Ce document grandit avec les taches du plan. Etat actuel : GOB (tache 1) et JKL / gabarits (tache 2).
Les unites sont celles de JK (Z vers le haut) ; la conversion vers JA est faite plus tard.

Legende de fiabilite : **[donnees]** = constate sur les 34 niveaux installes ; **[specs]** = valeur connue des specifications
communautaires, non verifiee sur les donnees (a confirmer dans la tache qui l'utilise).

## Sources

- Les 34 niveaux de l'installation Steam (`JK1.GOB`, `JK1MP.GOB`, `JK1CTF.GOB`, `Res2.gob`, `Res1hi.gob`) : toutes les structures
  ci-dessous ont ete relevees en lisant ces fichiers (`01narshadda`, `m10`, `c1`, puis les 34 par test).
- Specifications communautaires JKL / GOB / MAT / 3DO / COG (Massassi, Jedi Knight Specs ; code ouvert du moteur OpenJKDF2 pour le
  sens des drapeaux). Elles servent de reference de lecture : aucun code n'est copie. Les valeurs de drapeaux marquees **[specs]**
  viennent de la memoire de ces references, pas d'une consultation en ligne pendant cette tache.

## GOB

`"GOB "`, `u32` version (0x14), `u32` offset de l'index ; index = `u32` nombre, puis par entree `u32` offset, `u32` taille,
`char[128]` nom (separateur `\`). Resolution : GOB de l'episode, `Resource\Res2.gob`, `Resource\Res1hi.gob` ; insensible a la casse.
Code : `Jk.Core/Gob`.

## JKL : syntaxe generale

- Texte Latin-1. `#` commence un commentaire jusqu'a la fin de ligne ; lignes vides ignorees. Blancs = espaces ou tabulations.
- Une section commence par `SECTION: <NOM>` (la casse varie : `Section: Things`). Les listes ont une ligne `World <chose> <N>`.
  Le compte `N` est une **capacite**, pas un nombre de lignes **[donnees]** (`World things 552` pour 352 lignes ; `World sounds 60`
  pour 0 ligne dans `static.jkl`). Les listes finissent par `end` ou par la section suivante.
- Les lignes indexees commencent par `idx:` ; une faute de frappe existe (`5;	rldg.3do` dans `static.jkl`) : le lecteur accepte `:` et `;`.
- `static.jkl` (dans `Res2.gob`, `jkl\static.jkl`) ne contient ni geometrie ni gabarits **[donnees]** : seulement des listes
  communes (modeles d'armes, sprites). Les gabarits viennent du niveau lui-meme ; `JkTemplates` accepte quand meme `static.jkl` puis le niveau.

## Sections (ordre du fichier)

| Section | Contenu | Lecteur |
|---|---|---|
| `JK` | commentaire de format | ignoree |
| `COPYRIGHT` | dessin ASCII | ignoree |
| `HEADER` | constantes : `Version`, `World Gravity`, `Ceiling Sky Z`, `Horizon Distance`, `Horizon Pixels per Rev`, `Horizon Sky Offset x y`, `Ceiling Sky Offset x y`, `MipMap Distances` (4), `LOD Distances` (4), `Perspective distance`, `Gouraud distance` | `JklHeader` (cle = prefixe non numerique de la ligne, en minuscules, aussi dans `Raw`) |
| `SOUNDS` | un nom `.wav` par ligne, sans index | `Sounds` |
| `MATERIALS` | `idx: nom.mat xTile yTile` | `Materials` (noms ; l'index sert d'identifiant de materiau des surfaces) |
| `GEORESOURCE` | sous-listes ci-dessous | `Colormaps`, `Vertices`, `TexVertices`, `Adjoins`, `Surfaces` |
| `SECTORS` | blocs `SECTOR n` | `Sectors` |
| `AICLASS`, `SPRITES`, `KEYFRAMES`, `ANIMCLASS`, `SOUNDCLASS`, `COGSCRIPTS` | listes de noms de fichiers | lues sans modele (utiles aux taches suivantes) |
| `MODELS` | `idx: nom.3do` | `Models` |
| `COGS` | `idx: fichier.cog valeurs...` | `Cogs` (valeurs gardees en texte) |
| `TEMPLATES` | `nom parent cle=valeur...` | `Templates` |
| `THINGS` | `idx: gabarit nom x y z pitch yaw roll secteur cle=valeur...` | `Things` |
| autre (ex. `PVS`) | - | sautee, nom dans `SkippedSections` (a journaliser en information) |

### GEORESOURCE

- `World Colormaps N` : `idx: fichier.cmp`.
- `World vertices N` : `idx: x y z`. `World texture vertices N` : `idx: u v` (en pixels de texture).
- `World adjoins N` : `idx: flags mirror dist` (`dist` n'est pas garde).
- `World surfaces N` : N lignes puis N normales (`idx: nx ny nz`, sous le commentaire `Surface normals`). Une ligne de surface :
  `idx: mat surfflags faceflags geo light tex adjoin extralight nverts  v,t v,t ...  intensite...`
  - `mat` = index de materiau, **-1 = aucun** (c'est le cas des portails `adjoin` invisibles) ; `adjoin` = index ou **-1**.
  - `v,t` = index de sommet et index de sommet de texture (`-1` = pas de coordonnee, cas des surfaces sans materiau).
  - une intensite par sommet (le lecteur moyenne trois valeurs par sommet si une variante RVB apparait).
  - Les surfaces d'un secteur sont contigues : `SURFACES premiere nombre` dans le bloc secteur.

### Secteur

Mots-cles : `FLAGS`, `AMBIENT LIGHT`, `EXTRA LIGHT`, `COLORMAP`, `TINT r g b`, `BOUNDBOX`, `COLLIDEBOX` (facultatif), `SOUND nom vol`
(facultatif), `THRUST x y z` (facultatif), `CENTER`, `RADIUS`, `VERTICES n` + liste d'index (non gardee : deduite des surfaces),
`SURFACES premiere nombre`. Le dernier secteur ferme le bloc a la fin de la section.

### Thing

`Params` = `cle=valeur` de la ligne (cles insensibles a la casse). Plusieurs `frame=(x/y/z:p/y/r)` sont concatenes (separes par un blanc)
sous la cle `frame` ; `JklReader.ReadFrames` les lit. Les valeurs de gabarit s'heritent de parent en enfant jusqu'a `none` ; l'enfant gagne
(`JkTemplates.Resolve`). Parent absent ou cycle : on garde ce qui est resolu et le nom est dans `JkTemplates.Unresolved`
(`JK_TEMPLATE_UNRESOLVED`).

Types resolus rencontres **[donnees]** : `actor`, `player`, `item`, `cog`, `ghost`, `debris` (vus sur `01narshadda` et `c1` ; `weapon`, `explosion`, `corpse`, `particle`
**[specs]** existent aussi parmi les gabarits ; liste complete a relever par `jk-dump-level` sur les 34 niveaux). Les mouvements sont dans `numframes` / `frame=` et dans `thingflags` (les elements mobiles
portent `thingflags=0x400448` ou `0x408` avec 2 a 3 frames).

## Drapeaux

Valeurs hexadecimales, lues en non signe. Les comptes sont ceux de `01narshadda`.

### Surface (`surfflags`)

Valeurs frequentes **[donnees]** : `0x4` (2572 surfaces, toutes avec materiau : la surface solide ordinaire), `0x0` (1180, les portails
d'adjoin sans materiau), `0x5` (509), `0x14`, `0x2014`, `0x24`, `0x404`, `0x2004`, `0xd`, `0x6`, `0x15`, `0x2`.
Sens **[specs]** : `0x1` sol (floor), `0x2` lie a un COG, `0x4` collision, `0x8`, `0x10` surface dure/metal, `0x20`, `0x200` ciel d'horizon,
`0x400` ciel de plafond, `0x2000`. Le sens exact de chaque bit est a confirmer dans la tache geometrie avant de s'y fier.

### Face (`faceflags`)

**[specs]** : `0x1` double face, `0x2` translucide, `0x4` / `0x8` reglages de texture. Presque toujours `0x0` dans `01narshadda`.

### Mode de geometrie (`geo`), d'eclairage (`light`), de texture (`tex`)

**[specs]** : `geo` 0 non dessinee, 1 points, 2 fil de fer, 3 couleur pleine, 4 texturee. `light` 0 pleine lumiere, 1 non eclairee,
2 diffuse, 3 gouraud. `tex` 0 affine, 1 corrigee en perspective, 2 et 3 variantes. **[donnees]** : les surfaces visibles sont `geo 4 light 3 tex 1`
et les portails `geo 0`.

### Adjoin (`flags`)

**[donnees]** : `0x7` (1195 sur 1206), `0x5`, `0xf`, `0x3`, `0x1f`. **[specs]** : `0x1` visible, `0x2` franchissable par le mouvement,
`0x4` laisse passer le son, `0x8` laisse passer les acteurs / IA ; les bits plus hauts sont des variantes. Un adjoin sans le bit `0x2`
bloque le joueur.

### Secteur (`FLAGS`)

**[donnees]** : `0x1000` (465 secteurs), `0x0`, `0x1010`, `0x1050`, `0x10`, `0x1004`, `0x2`, `0x1002`, `0x50`, `0x4`.
**[specs]** : `0x1` sans gravite, `0x2` sous l'eau, `0x4` lie a un COG, `0x8` poussee (`THRUST`), `0x10` cache, `0x20`, `0x40` precipice (pit),
`0x1000` a confirmer (tres frequent, probablement lie a la boite de collision). A confirmer par la tache eau / zones mortelles.

### Thing (`thingflags`)

Sert surtout au mouvement : `0x400448` et `0x408` sur les portes, ascenseurs et ponts a frames ; `0x20000401` (joueur),
`0x20080501` (departs multi / CTF). Sens bit a bit a documenter dans la tache des elements mobiles.

## Commande de diagnostic

`kotor2ja jk-dump-level <niveau> [--jk <dossier>]` : comptes (sommets, surfaces, secteurs, materiaux, things par type resolu,
COG par fichier), gabarits non resolus et sections sautees. Dossier JK : `--jk`, puis `JK_PATH`, puis le chemin Steam par defaut.

## Constats de la tache 4 (scene : geometrie, lumiere, echelle)

- **Orientation des textures [donnees]** : les rangees d'un `.mat` sont lues telles que stockees (rangee 0 = haut), sans retournement. `03wsign.mat` (symbole
  au-dessus du texte) et `numbers.mat` (chiffres) sont poses sur des murs dont le texvert v **diminue en montant** (31 surfaces sur 31 par niveau CTF) : v est un
  indice de rangee depuis le haut, en pixels. Coordonnee de la scene = (u / largeur, 1 - v / hauteur) de la cellule 0.
- **Sens des polygones [donnees]** : la methode de Newell donne une normale du meme cote que la normale du fichier pour 4595 surfaces sur 4595 de `01narshadda`
  (les trois premiers sommets seuls : 4274 sur 4595, polygones presque alignes au depart) : l'ordre des sommets est antihoraire vu du cote de la normale, repere
  droit Z vers le haut, comme JA.
- **Joueur [donnees]** : origine a 0,120 au-dessus du sol (12 departs sur 12), `eyeoffset` z 0,037, `size` 0,065. Lacet : 0 = +Y, positif = gauche
  (direction `(-sin, cos)`) ; verifie par lancer de rayon sur les 357 departs des 34 niveaux (le sens oppose donne la vue la plus courte 2 fois sur 3).
- **Adjoin [specs, coherent avec les donnees]** : le bit `0x2` rend l'adjoin franchissable ; sans lui (ex. `0x5`) le mur de l'adjoin bloque.
- **Lumiere** : `Intensities` de 0 a 8,5 (la plupart 0..1), `AMBIENT LIGHT` de 0 a 2,22, `EXTRA LIGHT` de secteur 0 a 1, lumiere additionnelle de surface 0 dans 01narshadda.
  Modes `light` rencontres : 3 (gouraud), 0 (pleine lumiere).
- **Marches [donnees]** : contremarches (surface verticale a quatre sommets dont les aretes haute et basse sont des bords de surfaces horizontales) de 0,02 a 0,10.

## Constats de la tache 5 (ciel, animations, defilement, eau, lave, degats)

- **Ciel [donnees]** : surfaces `0x200` (horizon) et/ou `0x400` (plafond) ; 24 niveaux sur 34 en ont (jusqu'a 233 surfaces, 18ascend). Les niveaux ont presque toujours une seule texture de ciel
  (`00starry`, `13cnsky2`, `03sky09k`...), parfois deux (06abarons, m4) ou un intrus (une grille `00wgril6` marquee ciel dans 10cargo). `HEADER` : `Horizon Pixels per Rev` 768 (1000 dans 12escape),
  `Horizon Sky Offset` (0 0 ou 0 -16), `Ceiling Sky Z`, `Horizon Distance`. Derive par COG : `SlideHorizonSky(u, v)`, `SlideCeilingSky(u, v)` (`00_slideceiling`, `C1_SLIDESKIES`), appels de `startup`.
- **COG, valeurs d'instance [donnees]** : positionnelles. Une instance donne une valeur par symbole ni `local` ni `message`, dans l'ordre de declaration (les symboles `nolink` comptent). Vecteur `(x/y/z)`,
  surface = indice, materiau = nom de fichier `.mat`, secteur = indice, -1 = vide. Tableau `nom[i]` : famille de symboles `nom`, `nom1`, `nom2`... declares a la suite.
- **Animations [donnees]** : `SurfaceAnim(surface, fps, flags)` (`00_surfanim`, `00_neonsign`, `02_signanim`, `M5_SURFANIM`) et `MaterialAnim(materiau, fps, flags)` (`00_matanim`, `09_matanim`) dans `startup`.
  Drapeaux **[specs]** : `0x1` boucle, `0x2` saute la derniere image, `0x4` saute les deux premieres. Materiaux a plusieurs cellules : une petite centaine (particules et sprites compris), 13 cellules au plus, toutes de meme taille.
- **Defilement [donnees]** : `SlideWall(surface, vecteur, vitesse)` dans `startup` de `00_conveyor` (108 instances), `12_conveyor`, `M5_CONVBELT`, `17a_Stream`. Les « tapis roulants » sont surtout des
  ECOULEMENTS D'EAU (`13fwater`, `00fwater` : 06abarons, 04escapehouse, 03katarn) de vitesse 0,125 a 9 ; la cascade de 03katarn va vers -Z a 4 (la gravite du niveau est 4). **L'unite de la vitesse
  n'est pas verifiee** : lue en unites JK par seconde (la texture suit le mouvement dans le monde ; densite mesuree : 320 pixels par unite sur une texture de 128, soit 1,3 a 10 largeurs de texture par seconde).
  Reglage : `JkSceneOptions.ScrollSpeedScale`.
- **Eau [donnees]** : secteurs `FLAGS & 0x2` (461 des 604 secteurs teintes, tous teintes : TINT bleu `0.4 0.8 1`, aussi jaune/orange dans 09fuelstation = gaz, rouge sombre dans 14tower). TINT existe aussi
  sur 143 secteurs non marques. Surface de l'eau : adjoin translucide (face `0x2`) entre un secteur sous l'eau et un autre ; dans 06abarons l'eau est opaque (face `0x0`).
- **Degats [donnees]** : aucun drapeau de fichier, tout vient de COG. `00_sectordamage` (secteur, intervalle, degats ; negatif = soin), `12_sectordamage` (48 secteurs, `GetThingSector`, 1 point / 2 s),
  `09_gasdamage`, `M5_FANDAMAGE` (125) ; au contact de surfaces (messages `touched` / `entered`) : `00_deathfloor` (1000), `C2_KILLINGFLOOR` / `C3_KILLINGFLOOR` (1000), `00_damagewall`, `10_damagefloor`,
  `09_damagefloor`, `09_elevdamage`. Aucun materiau de lave dans les 34 niveaux ; liquides dangereux : `00fsldge` (boue), `00Fgwat*`.
