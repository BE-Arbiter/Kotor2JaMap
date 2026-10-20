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
COG par fichier), gabarits non resolus et sections sautees. Dossier JK : `--jk`, puis `JK_DIR`, puis `JK_PATH` (ancien nom), puis le chemin Steam par defaut.

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

## Constats de la tache 6 (modeles 3DO et animations KEY)

Code : `Jk.Core/Models` (`ThreeDoReader`, `KeyReader`), `Jk.Pipeline/JkModelConverter`. Releves sur `Res2.gob` : 477 `.3do` (dossier `3do\`), 504 `.key` (dossier `3do\key\`) ;
les deux se lisent sans erreur et avec des comptes declares exacts (sommets, faces, noeuds, entrees).

### 3DO (texte Latin-1, « 3DO 2.1 »)

- Sections : `HEADER`, `MODELRESOURCE` (`MATERIALS n`, lignes `idx: nom.mat`), `GEOMETRYDEF` (`RADIUS`, `INSERT OFFSET x y z`, `GEOSETS n`, par geoset `MESHES n` puis par maillage
  `NAME`, `RADIUS`, `GEOMETRYMODE`, `LIGHTINGMODE`, `TEXTUREMODE`, `VERTICES n` (`idx: x y z intensite`), `TEXTURE VERTICES n` (`idx: u v`), `VERTEX NORMALS`, `FACES n`, `FACE NORMALS`),
  `HIERARCHYDEF` (`HIERARCHY NODES n`).
- **Geosets [donnees]** : 424 modeles en ont 1, 53 en ont 4 (niveaux de detail) ; le geoset 0 est le plus detaille et le seul converti. Un seul modele (`kd.3do`) a un maillage que
  aucun noeud ne reference (le moteur ne le dessine pas : ignore).
- **Face [donnees]** : `idx: materiau type geo light tex extralight n  v,t v,t ...`. Les paires `v,t` sont ecrites avec des blancs apres la virgule (`0,  0   1,  1`). `type` : `0x0` (38 561 faces),
  `0x1` double face ; autres bits vus (`0x2`, `0x4`...) non exploites. `geo` : 4 texturee (24 788 faces), 3 couleur pleine (653), 1 points (196, tous dans `sphere.3do`, aucune face dessinable).
  Materiau toujours valide (aucun -1). Dans les faces `geo` 3 / 1 l'indice de sommet de texture peut valoir 0 alors que le maillage n'a aucune coordonnee de texture.
- **Sens des faces [donnees]** : la methode de Newell donne une normale du cote de la normale de face du fichier pour 25 619 faces sur 25 625 : sens antihoraire vu du cote de la normale
  (comme les surfaces du .jkl). Pas d'inversion.
- **UV [donnees, comme le .jkl]** : `u`, `v` en pixels de la cellule 0 du materiau ; `v` est un indice de rangee depuis le haut et les valeurs sortent de la texture (jusqu'a -191 sur une
  texture de 128, 256 en `u` : repetition). Coordonnee de modele = (u / largeur, 1 - v / hauteur), donc `t` du fichier MD3 = v / hauteur.
- **Hierarchie [donnees]** : `idx: flags type mesh parent child sibling numChildren x y z pitch yaw roll pivotx pivoty pivotz nom`. `mesh` = -1 pour un noeud sans maillage (`$$$DUMMY` racine des
  personnages) ; `x y z` et `pitch yaw roll` sont relatifs au parent. 373 modeles ont un seul noeud ; les armes / personnages ont 4 a 18 noeuds.
- **Pivot [donnees, mesure]** : les sommets d'un maillage sont dans un repere decale du pivot : sommet dans le repere du noeud = `v + pivot`. Mesure sur les 1 207 couples parent / enfant de
  la hierarchie : la distance (relative a la diagonale de la boite) entre l'origine de l'enfant et la boite du maillage du parent vaut 0,198 sans pivot, 0,461 avec `pos + pivot` et
  **0,014 avec `pos - pivot`** (origine de l'enfant dans les coordonnees brutes du maillage parent). La rotation se fait autour de l'origine du noeud, apres ce decalage.
- **Rotation [specs + coherence]** : angles en degres, Z vers le haut, sens trigonometrique ; matrice noeud = roll (axe Y), pitch (axe X), yaw (axe Z), puis translation, composee avec le parent.
  Lacet 0 = modele tourne vers +Y, comme les things du .jkl.
- **`INSERT OFFSET` [specs]** : point d'insertion de l'editeur ; ignore (non verifie sur les donnees).

### KEY (texte Latin-1)

- `HEADER` : `FLAGS` (0x0 : 338, 0x3 : 163 ; 0x1 et 0xb rares), `TYPE`, `FRAMES`, `FPS` (15 dans 498 fichiers sur 504 ; 8, 10, 12, 55), `JOINTS`. Section `MARKERS` facultative (124 fichiers) : `MARKERS n`
  puis `temps type` (pas, impacts) ; non utilisee. `KEYFRAME NODES` : par `NODE n`, `MESH NAME` (nom du noeud du 3DO ; apparie par nom, 3 321 couples modele / animation ont la meme liste de noms et aucun noeud n est reste sans piste), `ENTRIES k`.
- Entree (deux lignes) : `idx: image drapeaux x y z p y r` puis `dx dy dz dp dy dr`. Positions et angles sont **absolus** (le noeud est place par la cle, pas par la pose de repos + cle ;
  la position cle est proche de celle de la hierarchie). Les images cles sont creuses (a l interpolation pres, une entree par image dans certains fichiers, une toutes les 8 images dans d autres).
- **Variations [donnees]** : `d*` est la variation par image ; `d x (image suivante - image)` = difference des deux entrees (positions exactes, angles modulo 360). Exemple `sphr2anm.key` :
  lacet 0 -> 280 en 8 images avec `-10` par image. L'interpolation est donc lineaire ; la variation sert a choisir le sens (nombre de tours) quand l'ecart depasse 180 degres.
- Plus longue animation : 411 images (`macamera.key`) ; le MD3 le plus gros produit (`tk.3do` + `tkidle.key`, 283 images) fait environ 1 Mo.
- Animation entierement constante : une seule image. Rotation pure (un seul noeud mobile, position fixe, axe et angle constants entre images) : aucun cas dans les `.key` de `Res2.gob` ; les objets
  qui tournent dans les niveaux le font par vitesse angulaire de gabarit ou par COG (taches 9 et 10).

## Constats de la tache 8 (COG : lecture et classement des comportements)

Code : `Jk.Core/Cog` (`CogReader`, `CogScript`, `CogBinding`/`BoundCog`), `Jk.Pipeline/Movers` (`CogClassifier`, `MoverBehavior`). Catalogue complet :
`docs/superpowers/plans/jk/cog-catalogue.md` (354 fichiers COG distincts, 1 436 instances sur les 34 niveaux).

- **Syntaxe [donnees]** : sections `symbols ... end` et `code ... end`. Symbole : `type nom[=defaut] [local] [nolink] [desc=..] [mask=0x..] [linkid=N]`. Etiquettes `message:` (dont `activate:`, sans « d », dans
  `00_door` : la casse et la graphie du nom viennent du symbole `message` declare) et sous-routines appelees par `call nom;`. Une etiquette sans `return` tombe dans la suivante (`crossed:` puis `activate:` de
  `00_elev_switch`). `switch` est aussi un nom de variable (`00_elevcall`). Les verbes sont insensibles a la casse (`movetoframe`, `MoveToFrame`).
- **Verbes de mouvement [donnees]** : `MoveToFrame` dans 133 fichiers ; `JumpToFrame` / `SkipToFrame` (portes de tuyaux, contrepoids) ; `RotatePivot` (9 fichiers : `09_pipedoor*`, `03_paddlepool`...) ;
  `SetThingRotVel` (ventilateurs) ; `SetThingVel` / `SetThingPos` / `StopThing` (poussees, drapeaux CTF).
- **Declencheurs [donnees]** : `activate` / `activated` (porte utilisee, interrupteur), `crossed` (adjoin), `entered` (secteur), `touched`, `killed` (acteur mort : `07_yun`, `08_irpuzzle`...), `damaged`,
  `pulse` / `timer` (suite), `userN` (message envoye par un autre COG). Les chaines `crossed -> SetTimer -> timer -> MoveToFrame` sont frequentes (`16a_calamity`).
- **Acteurs [donnees]** : `00_AIFramePatrol` / `00_AIFrameMove` deplacent un acteur le long de ses images (`AISetMoveFrame`) : acteurs ignores, donc pas d'element mobile.
- **COG absent [donnees]** : `17a_Stream.cog` (niveau `m_boss17`) est cite mais absent de `JK1MP.GOB` et de `Res2.gob`.

## Constats de la tache 9 (elements mobiles -> entites JA)

Code : `Jk.Pipeline/Movers/MoverConverter.cs` (`MoverConverter`, `JkMoverSpeed`), branche dans `JkObjects.Convert` (classement des COG, modeles charges dans le meme lot que les decors, sous-modeles numerotes a la suite).

- **Images d'un thing [donnees]** : `frame=(x/y/z:p/y/r)` sont des positions et orientations **absolues** ; l'image 0 est la position du thing (ecart nul sur les 811 comportements verifies). Seuls les ecarts a l'image 0
  servent (l'orientation de la ligne du thing peut differer de l'image 0 : 180 degres pour `09slice`). Beaucoup de portes ont 0,1 a 0,8 degre d'ecart d'orientation entre images : du bruit d'editeur, ignore sous 2 degres.
- **Vitesse d'un `MoveToFrame` [hypothese, non verifiee en jeu]** : lue comme 0,1 unite JK par seconde par unite de vitesse (`00_door` : 8 -> 0,3 s pour 0,25 u ; `00_elev_switch` : 4 -> 6 s pour 2,4 u). Lue comme des unites JK par seconde,
  une porte s'ouvrirait en 0,03 s. A confirmer par l'utilisateur au premier essai.
- **Entites de SWGL [lu dans OpenJK-SWGL `code/game`]** : `func_door_rotating` n'existe pas (table de `g_spawn.cpp`) ; `func_rotating` arrete (`func_rotating_use`) repasse a son angle de base (le `trBase` n'est pas mis a jour) : une porte qui tourne
  puis s'arrete n'a pas d'equivalent. `func_door` avec `targetname` n'a pas de plaque de proximite (`SP_func_door`) ; `wait` negatif : la porte reste ouverte pour toujours et ne se reutilise plus ; `TOGGLE` (8) : la porte attend la pression suivante.
  Un `func_train` repart au coin suivant de lui-meme (`Reached_Train`) et un second `use` en cours de course saute au coin suivant. `func_plat` : l'origine est la position haute, repos en bas de `height`, plaque de declenchement du moteur.
  `trigger_multiple` : `USE_BUTTON` (4) demande l'appui sur « utiliser » ; deux `use` pendant la course d'une porte inversent son sens (d'ou `wait` 1 s sur les declencheurs). `MAX_SUBMODELS` 512 : le niveau le plus charge en utilise 111.
- **Modele de mover** : `CG_Mover` ne tient pas compte de `modelscale` (seul `CG_General` le fait) : un modele reduit par division (plus de 511 unites) s'affiche trop petit sur un mobile, alors que sa boite de collision a la vraie taille.
  16 mobiles sur 544 sont concernes (`16grnd` x31, `cargo2` x24, `bayd` x3, `09slice` x3, `08dbrg` x2, `lift` x2, `statpole` x2, `09bogus` x2) : `JK_MOVER_MODEL_SCALE`.
- **Choix de conversion** : voir le rapport `docs/superpowers/plans/jk/reports/T9.md`.

## Constats de la tache L2 (poussee de secteur, sons des mobiles)

- **Poussee [donnees]** : `00_sectorthrust.cog` (115 instances) : `SectorThrust(secteur, vecteur, vitesse)` dans le seul message `startup`, jusqu'a 8 secteurs de meme vecteur et de meme vitesse (« les poussees tournent en continu »).
  Vecteurs unitaires ou presque (`(0/0/1)`, `(1/0/0)`...), vitesses de 0,5 a 10 (les poussees verticales de 5 a 10). Aucun `THRUST` dans les fichiers de niveau (0 secteur). `M5_ANTIGRAV` fait la meme chose avec un vecteur litteral.
  Les autres scripts changent la poussee par message (interrupteur, minuterie, pulse : `04_specialthrust`, `08_vents2`, `10_powercore`, `10_sectorthrust`, `18_*`, `19_thrustpuzzle`, `21_Core`, `C3_FANSPUZZLE`, `M4_reactorthrust`) : non convertis.
  Le joueur JK a `maxthrust=2.0` (unites JK par seconde) et `World Gravity 4.0` ; unite de la poussee non verifiee (lue comme une vitesse en unites JK par seconde).
- **Entite JA [lu dans OpenJK-SWGL `code/game/g_trigger.cpp`]** : seul `trigger_push` pousse (pas de `target_push` sans clic). `touch` **affecte** `ps.velocity` du joueur a chaque image de contact (jamais addition). Trois modes :
  arc vers un `target_position` (vitesse imposee par la hauteur de la cible), `LINEAR` (4 : direction unitaire x `speed`, mais `SP_trigger_push` remet `speed` a 1000 apres la lecture des cles) et `RELATIVE` (16 :
  direction du joueur vers l'origine de la cible, normalisee puis multipliee par la cle `speed` ; sans `speed`, la distance). Seul RELATIVE a une vitesse reglable. `AimAtTarget` (pense a `START_TIME_LINK_ENTS`) libere le
  trigger si sa cible est absente. `CG_TouchTriggerPrediction` copie `origin2` dans la vitesse predite (inexact en RELATIVE : seulement la derniere commande, corrigee a l'image suivante). Pas de `PUSH_CONVEYOR` ici (le joueur
  doit etre au sol).
- **Sons de mobiles [donnees]** : les COG des portes ne jouent aucun son de mouvement ; il vient de `soundclass=<nom>.snd` du gabarit (fichiers `misc\snd\*.snd` : lignes `mode wav drapeaux rayons`, modes `startmove`, `stopmove`, `moving`
  = boucle `0x1`). 18 classes de mobiles (`sm_door`, `med_door`, `lg_door`, `sm_elev`, `med_elev`, `lg_elev`, `03wood_door`, `06stone_door`, `19stone_door`, `06drawbridge`, `funicular`, `payload`...) ; certaines n'ont que `moving`.
  Les `PlaySound*` des COG sont ceux des interrupteurs (clic, bip), des alarmes et des effets, pas du mouvement.
- **Sons de mobiles : entites JA [lu dans `g_mover.cpp`, `g_target.cpp`, `snd_ambient.cpp`]** : un `func_door` / `func_plat` n'a pas de cle de son (`sound1to2` et `soundPos1` sont en commentaire) ; `soundSet` nomme un groupe `bmodelSet` de
  `sound/sound.txt` (un seul fichier, pas extensible par un pk3) et un nom inconnu fait echouer `AS_ParseSets` (`ERR_DROP`). `func_train` : `noise` est une boucle posee au chargement et jamais arretee. Repli : `target_speaker` tire par
  `target` (`Use_BinaryMover_Go`, au depart), `opentarget` et `closetarget` (`Reached_BinaryMover`). Aucun evenement au depart du retour. Un `target_speaker` avec `wait` repete tout seul cote client : il n'en a pas.

## Constats de la tache M1 (musique)

- **Pistes** : `MUSIC\Track12..18.ogg` et `Track22..32.ogg` (18 fichiers, Ogg Vorbis stereo 44 100 Hz, ~320 kbit/s, 115 a 254 s, 105,7 Mo). Pas de pistes 1 a 11 ni 19 a 21.
- **Choix de la musique** : un COG par niveau, au `startup` : `PlaySong(debut, fin, retour)` (pistes CD). `00_soundtrack.cog` (arguments de l'instance), `01_SoundTrack.cog` (meme appel puis `SetMusicVol(0.0)` : muette jusqu'au bar), `06a_startlevel.cog` et `09_startlevel.cog` (litteraux 6/7/6 et 2/4/2, pistes absentes), `levelsong.cog` (une piste, jamais instancie). Suite jouee = le cycle `retour..fin` a partir de `debut`. Multi et CTF : aucun script.
- **JA / SWGL** : cle `music` du worldspawn (`g_spawn.cpp:1499`, `cg_main.cpp:CG_StartMusic`) ; un fichier existant avec un `/` dans son nom est joue en boucle sans `.mus` (`snd_dma.cpp:S_StartBackgroundTrack`) ; MP3 ou wav PCM (reechantillonnage au plus proche voisin), jamais ogg.
- Detail et chiffres : `plans/jk/reports/M1.md`.
