# Etude de faisabilite : acteurs (ennemis et PNJ) Jedi Knight dans SWGL

Date : 2026-10-20. Lecture seule : aucun jeu lance, aucun fichier du depot modifie hors ce document. Chiffres mesures par des petits programmes jetables
(lecteurs `Jk.Core` sur les 34 niveaux, lecture directe des pk3 SWGL, lecture du code `C:\Users\lambe\source\repos\OpenJK-SWGL`).
Legende : **[mesure]** releve sur les donnees, **[code]** lu dans OpenJK-SWGL, **[hypothese]** non verifie en jeu.

## 1. Cote JK : les acteurs

### 1.1 Volume

- **1 519 things de type `actor`** (type resolu par la chaine de gabarits) dans **25 niveaux sur 34** ; les 9 niveaux m* / c* (multi, CTF) n'en ont aucun. [mesure]
  Plus charges : 06bbarons 192, 02narshadda 135, 09fuelstation 128, 01narshadda 117, 18ascend 111, 19a 111, 04escapehouse 116, 10cargo 99, 06abarons 97.
  Boss isoles : 07yun 1, 11gorc 2, 15maw 1, 17asarris/17bsarris 1, 20aboc 2 / 20bboc 1, 21ajerec/21bjerec 1.
- **66 gabarits d'acteurs** distincts. Repartition par famille : humanoides hostiles 1 156 ; droides 130 ; civils 97 ; creatures 57 ; tourelles 58 ;
  AT-ST 9 ; boss 12. [mesure]
- Seuls 40 acteurs portent des `frame=` (patrouilles via `00_AIFramePatrol`, `00_AIFrameMove`, voir `cog-catalogue.md` lignes 52-54).
  Les gabarits d'acteurs ont presque tous une cle `cog=` (comportement de scenario) : les COG `00_AI*`, `06_sleepydroids`, `06_pedtorment`, `17a_SarrissSearch`,
  `20_BocSearch` pilotent des acteurs. Les 9 portes `ActorTriggered` (T9, `JK_COG_ACTOR_TRIGGER`) attendent la mort d'un acteur (message `killed`).

### 1.2 Catalogue par gabarit (nom, nombre, niveaux, modele, .pup, .snd, .ai, arme, sante)

Tous : `type=actor`, `move=physics`, `size` = rayon de collision (0,065 = celui du joueur). Colonnes : `model3d` / `puppet` / `soundclass` / `aiclass` / `weapon` / `health` (max).

| Gabarit | n (niveaux) | .3do | .pup | .snd | .ai | arme | sante |
|---|---|---|---|---|---|---|---|
| stormtroop | 187 (10) | st | st | st | stdefault | elaser | 60 |
| stormtroop2 | 177 (8) | s2 | st | s2 | stdefault | elaser | 70 |
| stormtroop3 | 126 (8) | s3 | s3 | s3 | streapdefault | eball | 70 |
| stormtroop4 | 45 (8) | s4 | s2 | s4 | straildefault | eraildet | 70 |
| iofficer / iofficer2 | 74 / 37 | io | io | io / io2 | iodefault | ebolt | 30 (40) |
| icommando / icommandorpt | 45 / 26 | ic / icrpt | ic | ic / icrpt | icdefault / icreapdefault | elaser / eball | 90 |
| reeyeesgun / bhgun | 55 / 57 | rystr / rhstr | rystr | rystr | rydefault / rybdefault | elaser | 50 / 60 |
| reeyeesfist, bhfist, cw | 52 / 20 / 10 | ry / rh | ryfist | ryfist | rypundefault | ry_punch | 50-60 |
| reeyees, bh, s, bhs (lanceurs) | 13 / 9 / 7 / 3 | ry / rh | ry | ry | rytherdefault | actor_grenade(2) | 50-60 |
| tuskan, tuskanbh, tuskangun, tuskanbhgun | 43 / 54 / 42 / 28 | tk, th, tkstr, thstr | tk / tkstr | tk / tkstr | tuproddefault... | ebow / elaser | 50-60 |
| greedo | 13 (2) | gr | gr | gr | grdefault | ebolt | 40 |
| bossk, bosskrld | 10 / 2 (09fuelstation) | bs, bsrld | bs | bs, bsrld | bodefault, bldefault | econcbullet, eraildet | 120 |
| gammorean | 17 (02narshadda) | gm | gm | gm | ggdefault | gamaxe | 120 |
| mynok | 41 (3) | my | my | mynoc | maidefault | mailocsting | 40 |
| probedroid | 73 (6) | pb | pb | pb | pddefault | probedroidbolt | 90 |
| boxturret, boxturret4, roundturret | 36 / 8 / 14 | tur1, tur4, tur2 | tur1, tur2 | turret | tur*default | turretlaser(2,3) | 80-150 |
| iatst / atst / jatst | 5 / 3 / 1 | at (18 noeuds) | atst | atst | atstdefault, atstfs | lasercannon | 400 |
| robotsentry, remote | 8 / 8 | rs, remo (1 noeud) | aucun | rs, re | sddefault, redefault | bryarbolt, remotelaser | 60, 30 |
| powerdroid(45), r2, r5, c3po (seethreepio, seetoopio), mousebot | 12+4, 4, 6, 6+3, 11 | pd, r2, r5, c3, mb | pd, r2, r5, c3, aucun | gonk, r2, c3po, mousebot | gonk, stpanic, drdefault | aucune | 10-50 |
| civils (man1/2, woman1/2, nman*, mman*, mwoman*, gwoman2, ug, ugsit, u2, nbartender) | 97 | mn, m2, wm, w2, ugalt | meme nom | mn1, wm1, ug | peddefault, pednarsh, pedhome, pedroamnarsh | aucune | 30-50 |
| creatures : octopus 7, sucker 6, kelldragon 3 | 16 | oc (25 noeuds), su (8), kd (30) | oc, su, kd | -, sucker, kd | wcdefault, dudefault, kddefault | cycbite, drugonbite, kellbite | 100, 160, 500 |
| 8t88 / weegee / max / jan | 2 / 2 / 1 / 1 | 8t, wg, mx, ja | 8t, wg, mx, ja | - | default, gonk, max, jan | -, -, mbolt, - | 10 000 / 5 000 / 5 000 / 20 000 (invulnerables) |
| boss : jerec 2, sariss 1, yun 1, yun2 1, gorc 1, pic 1, maw 1, boc 2 | 12 | je, sa, yu, go, pi, ma, bo | idem | idem | jerec, sariss, yun(ds), gorc, pic, maw, boc | gamaxe | 500 a 2 000 |

Notes. Les gabarits `reeyees*` sont des « Reeyees » (race du jeu) ; il n'y a **pas de Boba Fett dans JK** : le chasseur de primes est Bossk (+ Greedo).
`gamaxe` sert d'arme symbolique aux boss : leur combat (Force, sabre) est dans les `.ai` et les COG du boss, pas dans l'arme [hypothese a verifier en lisant `jerec.ai`].
Les `.ai` (97 fichiers `misc\ai\*.ai`) sont des listes d'instincts parametres (`LookForTarget`, `PrimaryFire`, `CircleStrafe`, `ReturnHome`...), exemple `stdefault.ai` :
`alignment=-1.0, rank=0.5, fov=170, sightdist=5.0, heardist=2.0, accuracy=0.20` ; `alignment` -1 = ennemi du joueur.

### 1.3 Modeles : 3DO hierarchiques

- Tous les acteurs ont un `.3do` dans `Res2.gob` (`3do\`), lisible par `ThreeDoReader` (deja utilise par T6/T7). Les gabarits `ug` et `u2` pointent vers `ugalt.3do`.
- **Humanoides** : 15 a 24 noeuds, 109 a 250 faces par modele (tres bas poly), noeud 0 = `$$$dummy`. Mesure [mesure] :
  `st/s2/s3/s4` 17 noeuds (16 maillages) ; `io`, `ic`, `icrpt`, `ja`, `ky` (le joueur) 17-19 ; `bs`, `bsrld`, `sa` 18 ; `th`/`tk`/`tkstr`/`thstr` 20 ; `ry*`/`rh*` 24 (oreilles, yeux) ;
  `gm` 16 ; `mn`/`m2`/`wm`/`w2` 15 ; `gr` 22 ; `je` 24 ; `yu` 22 ; `bo` 25 ; `c3`/`c2` 19.
- **Non humanoides** : `tur1/2/4` 3 noeuds (base, barrel, head) ; `pb` 13 ; `pd` 6 ; `r2`/`r5` 9 ; `mb`, `rs`, `remo` 1 ; `at` 18 ; `oc` 25 ; `su` 8 ; `kd` 30 ; `8t` 21 ; `wg` 15 ; `my` 11.
- Squelette JK du stormtrooper (`st.3do`, HIERARCHYDEF, 17 noeuds, noms [mesure]) : `$$$dummy`, `stgun`, `sthead`, `sthip`, `sttorso`, `stlshouldr`/`stlforearm`/`stlhand`,
  `stlthigh`/`stlcalf`/`stlfoot` et les memes a droite (`str*`). Les types de noeud (colonne `type`) sont des masques : 0x1 torse, 0x2 bras gauche, 0x4 bras droit, 0x8 tete, 0x10 hanche,
  0x20 jambe gauche, 0x40 jambe droite [mesure sur `st.3do`]. Les humanoides partagent donc un schema « 1 torse + hanche + tete + 2x(epaule, avant-bras, main) + 2x(cuisse, mollet, pied) ».
- Taille mesuree, noeuds au repos, a l'echelle retenue (344 u JA par unite JK, `JkWorldScale`) : st 57 u, io 65 u, th 63 u, ry 71 u, gm 74 u, bs 66 u, je 65 u, mn 67 u, ky (joueur) 62 u ;
  `pb` 133 u, `at` 233 u. Les humanoides tombent deja a ~64 u, la taille du joueur JA.

### 1.4 Animations : `.pup` et `.key`

- 49 fichiers `.pup` (`misc\pup\`), texte, un par type de squelette. Ils associent des **actions** a des `.key` dans plusieurs **modes** (`Mode=0` sans arme, `Mode=1` arme sortie, `Mode=3` etranglement...), plus une table
  `joints` (tete, cou, torse, bouches a feu). Extrait `st.pup` : `walk stpatrol.key 0x01 2 2`, `run`, `death stcrumpl.key 0x14 5 5`, `death2 stdie.key`, `hit sthit.key`, `fire stfire.key 0x28 1 5`, `fidget stidle.key`, `victory stwin.key`.
- Actions les plus frequentes (nombre de .pup qui les definissent) : stand 81, walk 70, walkback 69, strafeleft/right 68, run 67, death 45, death2 43, rising 41, hit 41, turnleft/right 37, drop 36, fire 31,
  fall 31, land 24, choke 21, fire2 18, jump 13, fidget 12, victory 10, magic 8, block 7... (40 noms d'action au total). [mesure]
- 389 `.key` distincts sont references, tous presents dans les GOB (0 manquant). 504 `.key` au total. Exemples `st` : `stwalk` 64 images, `strun` 126, `stfire` 7, `stdie` 26, `ststand` 41, `stidle` 91, 15 images/s.
  Les `.key` sont des pistes par **nom de noeud 3DO** (position et angles absolus, voir `jk-formats.md`), donc directement lisibles par `KeyReader`.

### 1.5 Correspondance avec les classes d'ennemis JA (proposition)

| JK | Equivalent JA (`classname NPC_*` ou `NPC_type`) | Remarque |
|---|---|---|
| stormtroop, 2 | `NPC_Stormtrooper` (`stormtrooper`), `NPC_Stormtrooper` + officier | arme blaster ; sante 60 / 70 |
| stormtroop3 / 4, icommando(rpt) | `stormtrooper2`, `rockettrooper`, `shadowtrooper`/`scout_trooper`/`death_trooper` (SWGL_NPC_EMPIRE) | `eball`/`eraildet` ~ repeater / disruptor |
| iofficer | `NPC_StormtrooperOfficer` / `impofficer` | pistolet |
| Reeyees, Greedo | `NPC_Rodian` (`rodian`, `rodian_female`) | pas d'equivalent exact |
| Tuskan | `NPC_Tusken` (`tusken`, `tuskensniper`) | `ebow` ~ bowcaster |
| Gammorean, Mynok, Octopus, Sucker, Kell | pas d'equivalent humanoide ; `NPC_Monster_*` ou `NPC_Gran` | a decider (rancor, howler, murjj, claw...) |
| Bossk | `NPC_Trandoshan` | pas de Boba Fett dans JK |
| Ugnaught, civils | `NPC_Ugnaught`, `NPC_Rebel`/`NPC_Prisoner`/`NPC_Merchant`, `NPC_Bartender` | `weapon WP_NONE` |
| droides : probe, sentry, remote, gonk, r2, r5, c3po, mousebot | `NPC_Droid_Probe`, `sentry`/`seeker` (`remote`), `gonk`, `r2d2`, `r5d2`, `protocol`, `mouse` (dans `assets1.pk3`) | existent dans JA de base |
| AT-ST | `NPC_Atst` (`atst.npc`, ou `atst_vehicle`) | squelette propre |
| tourelles | `misc_turret` / `emplaced_gun` | pas des NPC |
| Jerec, Sariss, Yun, Gorc, Pic, Maw, Boc | `NPC_Reborn_New`, `NPC_Tavion`, `NPC_Alora`, `NPC_Desann`, `NPC_Jedi`, `NPC_Cultist_*`, `NPC_ShadowTrooper` | une classe de boss par personnage |

## 2. Cote JA / SWGL

### 2.1 Definition d'un PNJ

- Entite `NPC_spawner` (+ raccourcis `NPC_Stormtrooper`, `NPC_Imperial`, `NPC_Rodian`, `NPC_Gran`, `NPC_Tusken`, `NPC_BobaFett`, `NPC_Desann`, `NPC_Reborn`, `NPC_Jedi`, `NPC_Droid_Probe`, `NPC_Monster_*`...
  table de `code/game/g_spawn.cpp` lignes ~755-830). Cles de spawner utiles : `NPC_type`, `NPC_team`, `NPC_Weapon`, `NPC_model`, `NPC_skin`, `health`, `NPC_targetname`, `NPC_target`
  (**tire a la mort du PNJ**), `NPC_target2` (assomme), `targetname` (le spawner n'apparait qu'a l'activation), `count`, `delay`, `wait`.
  Drapeaux de `NPC_spawner` : DROPTOFLOOR (16), CINEMATIC (32, sans IA), NOTSOLID, STARTINSOLID, SHY (2048), SAFE. [code NPC_spawn.cpp:2296-2325]
- `NPC_type` vient de `ext_data/npcs/*.npc` : `NPC_stats.cpp:5075` liste `ext_data/npcs` par `FS_GetFileList` (tous les pk3 montes, un `.npc` dans le pk3 de la carte suffit). [code]
  Exemple `stofficer.npc` (SWGL_NPC_EMPIRE.pk3) : `playerModel stormtrooper`, `customSkin officer`, `weapon WP_FLECHETTE`, `health 60`, `scale 105`, `playerTeam TEAM_ENEMY`,
  `enemyTeam TEAM_PLAYER`, `class CLASS_STORMTROOPER`, `height 68`, `walkSpeed`, `runSpeed`, `snd`..., `dismemberProb*`. Le `.npc` accepte aussi `animOverride` (NPC_stats.cpp, parse du jeton).
- Le modele est charge par `G_SetG2PlayerModel` (g_client.cpp:2111) : **`models/players/<playerModel>/model_sp.glm`, sinon `model.glm`**, sinon repli sur le stormtrooper (`stormtrooper`). Le skin est
  `model_<customSkin>.skin`. Un PNJ n'est jamais un MD3 : en cas d'echec le code parle d'un « mouse md3 » de repli mais charge un `.glm`. [code]
- Les PNJ SP se deplacent avec le navigateur (`maps/<carte>.navNEW`, `waypoint_navgoal`, `g_navigator.cpp:1194`). Le depot n'emet ni waypoints ni `.navNEW` (recherche `waypoint|navgoal|.nav` dans `src` : seulement le
  lecteur KOTOR des waypoints). [code + mesure]

### 2.2 Format de modele skinne

- `*.glm` (ident `2LGM`, version 6) = maillage skinne ; `*.gla` (`2LGA`, version 6) = squelette + animations (`code/rd-common/mdx_format.h`). Le GLM contient un nom de GLA
  (`animName`), des surfaces (hierarchie avec parent/enfants, shader, drapeaux), des LOD (pas de 1 a 4), et par sommet jusqu'a 4 poids de bones (champ `uiNmWeightsAndBoneIndexes` + 4 octets de poids). [code]
- `models/players/_humanoid/_humanoid.gla` (SWGL_Animations.pk3) : **53 bones**, 21 384 images, echelle 0,64 [mesure]. Bones : `model_root`, `pelvis`, `Motion`, `lfemurYZ/X`, `ltibia`, `ltalus` (et `r*`),
  `lower_lumbar`, `upper_lumbar`, `thoracic`, `cervical`, `cranium`, `face` (+ yeux, paupieres, levres), `rclavical`, `rhumerus(X)`, `rradius(X)`, `rhand`, doigts `r_d*_j*`, `rhang_tag_bone` (et `l*`).
- `animation.cfg` (1 423 lignes de definitions dans `_humanoid`) mappe les noms d'animation JA (BOTH_STAND1, LEGS_WALK1, BOTH_DEATH*...) vers des plages d'images. Le stormtrooper de base
  n'a **pas** d'`animation.cfg` propre : le jeu prend celui du GLA du modele (`G_LoadAnimFileSet`, NPC_stats.cpp:1509 : nom de GLA -> `_humanoid`). [code + mesure `assets1.pk3`]
- `stormtrooper/model.glm` (370 884 octets, `assets1.pk3`) : GLA `models/players/_humanoid/_humanoid`, 53 bones, 4 LOD, **81 surfaces** dont ~28 **surfaces-etiquettes** `*r_hand`, `*l_hand`, `*head_eyes`,
  `*l_arm_elbow`, `*l_leg_foot`... (drapeau 1) et des surfaces de coupe `*_off` (drapeau 2, demembrement). [mesure]
  Le code cherche ces points d'attache par nom (`G_SetG2PlayerModelInfo`, g_client.cpp:1440-1480 : `*head_eyes`, `cervical`, `thoracic`, `*r_hand`, `*l_hand`, `*hips_l_knee`, `*l_leg_foot`...) ; `G2_Add_Bolt`
  cherche d'abord une surface, puis un bone du meme nom (rd-rend2/G2_bolts.cpp:113). **Un GLM maison doit porter ces etiquettes**, sinon yeux, arme et effets n'ont pas de point d'attache (-1) [hypothese : degrade ou plante, a tester].
- **Surcharges d'animation SWGL** (`docs/SWGL_Animation_Override_Guide.md`) : un petit GLA de **meme squelette que `_humanoid.gla`** (53 bones, memes noms et parents, meme ordre) peut remplacer des animations
  nommees ; total d'images < 65 535 (reste ~44 000 apres `_humanoid`). Reference dans `animoverride.cfg` du modele ou `animOverride` du `.npc`. Existe deja : `_kotor`, `_clones`, `_vader`... dans SWGL_Animations.pk3.

### 2.3 Ce que le depot sait ecrire

- **MD3 seulement** : `Kotor.Core/Export/Md3Writer.cs` (`Write`, `WriteSplit`, `WriteFrames` ajoute en T6, jusqu'a 1 024 images, une partie par materiau).
  Recherche `glm|gla|ghoul|MDXM|MDXA` dans `src` et `tests` (hors binaires) : **aucun ecrivain ni lecteur GLM/GLA**. `Swtor.Core/Gr2` lit des modeles skinnes SWTOR (utile pour la logique de poids/bones, pas d'ecriture).
- **SWGL accepte-t-il un MD3 anime comme PNJ ?** Non, d'apres le code : `NPC_Spawn` -> `G_SetG2PlayerModel` n'appelle que `G2API_InitGhoul2Model(... model.glm)` (g_client.cpp:2167-2186) ; le drapeau `md3Model`
  de `NPC_stats.cpp` est mis a faux des qu'un `playerModel` est lu (ligne 2994) et la branche MD3 (`CG_RegisterClientRenderInfo`) est un reste du moteur d'origine. Pas de `NPC_model` en `.md3`. [code]
- **MD3 anime comme entite non combattante : oui** (deja fait en T7) : `misc_model_breakable` avec AUTOANIMATE (2), boucle sur toutes les images a 10 images/s (cg_ents.cpp:365-368 ; T7.md). Pas de sante, pas d'IA,
  pas d'armes ; vitesse fixe, ne suit pas le `fps` du `.key`.

## 3. Options d'architecture

### Option A : PNJ JA existants aux positions des acteurs (aucun modele JK)

- Principe : nouveau `JkActorConverter` (a cote de `JkThingConverter`) : table gabarit JK -> `NPC_type` (+ arme, equipe, sante) ; chaque acteur devient une entite `NPC_spawner` posee a la position convertie
  (`pos * 344`, lacet converti comme les departs : `angle = lacet + 90`) ; PNJ civils `CINEMATIC` ou `WP_NONE`. Un `.npc` maison dans le pk3 (`ext_data/npcs/jk_<gabarit>.npc`) fixe `health`, `playerTeam`, `weapon`, `scale`
  a partir des valeurs JK, sans toucher aux `.npc` de SWGL. Les tourelles : `misc_turret`/`emplaced_gun`.
- Bonus : l'`NPC_target` du spawner (tir a la mort) remplace le contournement `JK_COG_ACTOR_TRIGGER` de T9 : la porte s'ouvre vraiment a la mort de l'acteur (le COG `killed` identifie le thing).
- Rendu : un monde JK avec des PNJ JA (stormtroopers, rodians, tuskens, droides JA). Gameplay complet (tir, mort, IA, sons JA). Les modeles JK ne sont pas visibles.
- Effort : **~2-4 jours** (table ~20 lignes, convertisseur, journal `JK_ACTOR_MAPPED` / `JK_ACTOR_NO_EQUIVALENT` / `JK_ACTOR_CAPPED`, tests, bilan T11 etendu, mise a jour de la spec et de `jk-log-codes.md`).
- Risques : (1) **nombre** : 192 acteurs dans 06bbarons ; chaque PNJ est un ghoul2 avec IA, cout CPU et `MAX_GENTITIES` ; il faut plafonner et/ou declencher l'apparition a l'approche (`targetname` + `trigger_multiple` par secteur,
  ou SHY) ; (2) **navigation** : pas de `.navNEW` ni de waypoints ; les PNJ marchent-ils sans ? [hypothese] ; (3) equilibrage : la sante JK (30-120) et les degats ne correspondent pas a JA ; (4) types sans equivalent
  (mynok, kell, sucker, octopus, boss) -> substitutions arbitraires ; (5) echelle : le monde est a 344 u/unite JK, les PNJ JA font ~64-70 u, comme le joueur, donc coherent ; (6) acteurs scriptes par COG
  (`00_AIFramePatrol` : patrouilles de 40 acteurs) : ignores, les PNJ restent en garde.

### Option B : PNJ JA portant le modele JK (GLM skinne rigide sur le squelette `_humanoid`)

- Principe : pour chaque modele humanoide JK, ecrire un `models/players/jk_<modele>/model.glm` (+ `model_default.skin`, `.npc`) dont les parties sont rattachees a 100 % a un bone JA, sur le squelette `_humanoid.gla`
  (`animName models/players/_humanoid/_humanoid`). Les animations JA (marche, course, tir, mort, ~1 400 definitions d'`animation.cfg`) sont **gratuites** puisque le squelette est celui de JA : aucun retargeting d'animation.
- Correspondance de noeuds (proposee) : `sttorso` -> `thoracic` (ou `upper_lumbar`), `sthip` -> `pelvis` / `lower_lumbar`, `sthead` -> `cranium`, `stl/rshouldr` -> `l/rhumerus`, `stl/rforearm` -> `l/rradius`,
  `stl/rhand` -> `l/rhand`, `stl/rthigh` -> `l/rfemurX`, `stl/rcalf` -> `l/rtibia`, `stl/rfoot` -> `l/rtalus`, `stgun` -> supprime (JA dessine l'arme via `*r_hand`). Les schemas `k_*` (`io`, `ic`, `bs`, `ja`, `ky`),
  `ry*`, `tk`, `gm`, `je`... se rattachent par le **type de noeud** (masques 0x1/0x2/0x4/0x8/0x10/0x20/0x40) et par le nom. Appariement automatique propose + table de surcharge par modele.
- Etapes techniques : (a) ecrire un **ecrivain GLM** (header, hierarchie de surfaces, LOD unique ou 4, sommets avec poids 100 %, triangles) ; lecture d'un GLM JA existant (stormtrooper) comme **gabarit** pour recopier
  les surfaces-etiquettes et les surfaces `*_off` avec leurs poids ; (b) pour chaque piece 3DO, placer ses sommets (repere noeud + pivot, T6) dans l'espace **de la pose de reference du GLA JA** : translation vers l'origine du bone
  correspondant et rotation pour aligner l'axe du membre (la pose de repos JK a les bras et jambes dans une autre attitude que la pose de bind de `_humanoid`) ; lire `BasePoseMat` du GLA (l'ecrivain doit donc lire le `.gla`) ;
  (c) re-echelle a ~64 u (342-344 u/unite JK, deja la valeur du monde) et recalage au sol (origine JK a 0,12 au-dessus du sol) ; (d) UV et shaders `models/jk/<texture>` (deja produits par T6 : un shader par `.mat`) ;
  (e) `.npc` et `.skin` generes. Le nombre de surfaces (un par materiau et par piece) doit rester raisonnable.
- Rendu : les vrais personnages JK (stormtroopers carres a 200 faces, Reeyees, Tuskens, Gammorean, Jerec...) en « poupees articulees » animees comme des PNJ JA. Les defauts attendus : liaisons visibles entre pieces rigides
  (pas de peau continue), proportions JK qui ne suivent pas exactement les os JA (pieds qui glissent, pieces qui se detachent aux coudes), mains vides (pas de maillage de doigts).
- Effort : **~2-3 semaines** apres l'option A (ecrivain/lecteur GLM+GLA ~5-8 jours ; rattachement des 15-25 modeles humanoides ~4-6 jours ; verification visuelle en jeu et ajustements ~4-6 jours). Seuls ~35 modeles sont des
  humanoides rigidifiables (hostiles 1 156 acteurs + civils 97 + boss 12). Les droides, creatures, tourelles, AT-ST ne sont pas dans la portee (squelettes differents).
- Risques : GLM invalide = plantage au chargement (pas de debogueur de format cote SWGL en dehors du jeu) ; etiquettes manquantes (voir 2.2) ; bone « `Motion` » et racine du GLA ; poids : 100 % sur un seul bone suffit au format
  (`uiNmWeightsAndBoneIndexes` avec 1 poids) ; **ne pas** reutiliser les animations `.key` JK (hors perimetre de B). Option B+ eventuelle (hors recommandation) : cuire les `.key` JK (marche, tir, mort) dans un **GLA de
  surcharge** (meme squelette `_humanoid`) : necessite un ecrivain GLA complet (os compresses, 14 octets par os et par image) et du retargeting des rotations ; effort supplementaire estime 2-3 semaines.

### Option C : MD3 anime JK en boucle (decor non combattant)

- Deja possible sans nouveau code : `JkModelConverter.ToMd3` avec `.pup`/`.key` (ex. `st.3do` + `stidle.key`, 91 images ; `tk.3do` + `tkidle.key`, 283 images, ~1 Mo) + `misc_model_breakable` AUTOANIMATE (T7).
  Il suffit de ne plus ignorer les acteurs et de choisir une animation de repos (`stand`/`fidget`/`walk` du `.pup`).
- Rendu : figurines JK animees sur place a 10 images/s (ne suit pas le `fps`), sans collision de combat, sans IA ni degats ; ne repond pas au besoin « ennemis en jeu ».
- Effort : **~1-2 jours** ; budget pk3 : ~100 acteurs animes distincts x 0,3-1 Mo chacun (a mutualiser par modele : un MD3 par gabarit/animation, pas par acteur).
  Risque : une porte `killed` ne peut pas etre declenchee ; le joueur peut croire a de vrais ennemis.

## 4. Recommandation et phases

**Recommandation : A d'abord (livrable rapide et jouable), puis B pour les humanoides si le resultat en jeu justifie l'investissement ; C seulement en secours pour les acteurs sans equivalent (ex. mynok, octopus).**
A donne des ennemis combattants aux 25 niveaux avec peu de code ; B est un projet de modelisation/format a part entiere qui s'appuie sur A (meme conversion de position et de `.npc`).

Phases :
- **Phase 0 (0,5 j, utilisateur en jeu)** : placer a la main un `NPC_Stormtrooper` et un `NPC_Rodian` dans un pk3 JK existant (ex. `jk_01narshadda.pk3`) pour tester la navigation sans `.navNEW`, l'IA, le tir, le cout CPU.
  Aucun agent ne lance le jeu.
- **Phase 1 = Option A, noyau (2-3 j)** : `JkActorMap` (table gabarit -> `NPC_type`, arme, equipe, sante), `NPC_spawner` immediats, plafond par niveau (`JK_ACTOR_CAPPED`), journal, remplacement de `JK_ACTOR_SKIPPED` ;
  `.npc` maison pour sante/equipe ; tests (comptes par gabarit, orientation, plafond).
- **Phase 2 = Option A, portes et zones (1-2 j)** : `NPC_target` -> portes `ActorTriggered` (fin de `JK_COG_ACTOR_TRIGGER` quand l'acteur existe) ; apparition a l'approche (`trigger_multiple` par secteur) pour les niveaux
  denses ; tourelles en `misc_turret` ; boss.
- **Phase 3 = B, outils (5-8 j)** : lecteur/ecrivain GLM + lecteur GLA ; test d'aller-retour sur `stormtrooper/model.glm` ; gabarit d'etiquettes.
- **Phase 4 = B, un modele pilote (3-4 j)** : stormtrooper `st.3do` seul ; essai en jeu par l'utilisateur ; reglage de la pose de reference et de l'echelle.
- **Phase 5 = B, generalisation (4-6 j)** : les autres humanoides (s2-s4, io, ic, ry*, tk*, gr, gm, civils, boss) avec table de surcharge ; budget pk3 (un GLM par modele, partage entre niveaux).
- **Option B+ / animations JK (hors plan)** : GLA de surcharge a partir des `.key`.

## 5. Risques, inconnues et tests en jeu

1. **Navigation sans `.navNEW`** : les PNJ poursuivent-ils le joueur dans des salles sans waypoint ? [inconnue] Mitigation : `waypoint_navgoal` aux positions des acteurs et un `.navNEW` genere (outil `nav` du jeu, a lancer par l'utilisateur).
2. **Charge** : 1 519 acteurs au total, jusqu'a 192 par niveau ; limite `MAX_GENTITIES`, CPU des PNJ, memoire ghoul2. A mesurer sur 06bbarons et 02narshadda.
3. **Equilibrage** : sante JK 30-120 (boss jusqu'a 2 000 ; Jan/Max/8t88/weegee quasi invulnerables) contre sante JA ; degats des armes `+elaser` etc. non mappes ; precision `accuracy` des `.ai`.
4. **Scripts COG d'acteurs** (1 492 acteurs avec `cog=`, patrouilles `00_AIFrame*`, `06_sleepydroids`, boss) : non reproduits ; les acteurs de patrouille resteront figes ou erreront selon l'IA JA.
5. **Entites sans equivalent** (mynok 41, octopus, sucker, kell dragon, 8t88, weegee) : substitution ou decor C ; a trancher avec l'utilisateur (regle du projet : rien ne manque sans journal).
6. **Echelle et collision** : modeles humanoides ~64 u (57-74), `pb` 133 u, `at` 233 u ; la boite des PNJ JA (`height`/`width` du `.npc`) doit suivre ; le joueur est de la meme taille que les stormtroopers.
7. **GLM maison** (option B) : format, etiquettes `*head_eyes`/`*r_hand`/..., hierarchie de surfaces, `numBones` = 53, nom de GLA, version 6 ; un GLM invalide plante au chargement (comme l'avertissement
   « Cannot fall back to default model » dans g_client.cpp:2193). Tests en jeu : chargement, pose neutre, marche/course/tir/mort, port d'arme, demembrement (mettre `dismemberProb* 0`), eclairage/shaders `models/jk/*`, collision du joueur.
8. **Pk3 perime** : rappel memoire du projet (les anciens `kotor_*.pk3`/`jk_*.pk3` de `base` masquent les nouveaux ; les noms `jk_*` existent deja dans `base`) : nettoyer avant de tester les `.npc` et GLM.
9. **Sons** : les `.snd` JK (102 fichiers de classes de sons, repliques de stormtroopers `i00s*.wav`) ne sont pas branches ; les PNJ JA utilisent leurs sons (`snd` du `.npc`). Optionnel plus tard.
10. **Licence / distribution** : modeles et textures JK dans des pk3 : meme regle que les niveaux (usage local de l'utilisateur).

## Annexe : sources lues

`docs/superpowers/specs/2026-10-19-jedi-knight-design.md` (§5.5), `jk-formats.md`, `jk-log-codes.md`, `plans/jk/reports/T6.md`, `T7.md`, `plans/jk/cog-catalogue.md` ;
`src/Jk.Pipeline/JkThingConverter.cs` (acteurs comptes ligne 440), `JkWorldScale.cs`, `src/Kotor.Core/Export/Md3Writer.cs` ; GOB JK (`Res2.gob`, `JK1.GOB`, `JK1MP.GOB`, `JK1CTF.GOB`) ;
SWGL : `SWGL_Animations.pk3` (`_humanoid.gla`, `animation.cfg`, `_kotor/_kotor.cfg`), `SWGL_NPC_*.pk3`, `base/assets1.pk3` (`stormtrooper/model.glm`) ; moteur : `code/game/NPC_spawn.cpp`,
`NPC_stats.cpp`, `g_client.cpp`, `g_spawn.cpp`, `g_navigator.cpp`, `code/rd-common/mdx_format.h`, `code/rd-rend2/G2_bolts.cpp`, `docs/SWGL_Animation_Override_Guide.md`, `docs/SWGL_Character_Guide.md`.
