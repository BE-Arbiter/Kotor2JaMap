# T7 - Echelle 2 : facteur derive du personnage humain (war_planworking)

**RIEN N'A ETE VERIFIE EN JEU.** Le jeu n'a pas ete lance, aucun processus de jeu touche (aucun `SWGL_SP` en cours au moment de l'export). Tout vient de mesures hors ligne.

## Resultat
**1 unite GR2 SWTOR = 338 unites JA = 8,45 m JA** (un seul facteur global : positions, sommets, collision, lumieres, point d'apparition). Avant : x1 = 40 u par unite, puis x2 = 80 (estimation). Le nouveau facteur vaut 8,45 fois le x1 et 4,2 fois le x2.

## 1. Taille d'un humain SWTOR (unites GR2, mesures sur les .gr2)
- Corps `chest_tight_bms_light_ss_mtx12_body` : y 0,139..0,198 ; jambes `leg_armor01_bms_archetype` : 0,041..0,143 ; bottes `boot_naked_bms_archetype` : -0,001..0,042 ; tete `head_human_bmn_caucasian_a01` : 0,172..0,205 (autre tete bmn : 0,169..0,204).
- Humain masculin debout : semelle -0,001, haut du crane 0,205 ; avec cheveux et cou ~0,213 (valeur du Character-maker : 72 / 338).
- Tete, corps et bottes sont dans le meme espace (pose de liaison, pieds a y = 0) : pas de difference d'unite entre squelette et sommets. La phrase "~1/10 m par unite" du CLAUDE.md du Character-maker veut dire que la tete est a y = 0,19 (donc 0,1 unite ~ 1 m, 1 unite ~ 10 m), ce qui concorde avec T1 ("1 unite = 10 m pour les personnages"). 8,45 m par unite est la valeur qui donne un humain de 1,8 m.

## 2. Comment le Character-maker met les personnages a l'echelle de JA
`src/Swtor.Formats/Jka/JkaRetarget.cs` : `Scale = JkaOrigin("cervical").Z / SwtorWorld("Neck").Y`, environ 338, facteur uniforme (axes (x,-z,y)) qui fait coincider le cou des deux squelettes ; le modele JKA humain fait 72 unites debout (semelle a z = 2,7). 72 / 0,213 = 338. Le pipeline KOTOR : 40 u/m, humain de 1,8 m = 72 u. Je retiens **72 u** (hauteur d'un humain JKA / KOTOR) et non la capsule de 64 u : 64 / 0,205 = 312 serait l'autre lecture (ecart 8 %).

## 3. Autres donnees SWTOR, meme echelle (preuves independantes)
Mediane sur les instances de 120 zones (taille du modele x echelle d'instance ; echelle d'instance mediane = 1) :

| objet | hauteur (unite GR2) | taille reelle supposee | m par unite |
|---|---|---|---|
| chaise (n = 17 144) | 0,157 | dossier 0,9 m | 5,7 |
| table (11 230) | 0,101 | 0,75 m | 7,4 |
| rambarde (9 946) | 0,135 | 1,05 m | 7,8 |
| canape (3 192) | 0,118 | 0,9 m | 7,6 |
| humain | 0,213 | 1,8 m | 8,45 |

- Escalier `wallset_h3m_stairs` : 12 marches de 0,025 (total 0,3, "h3m" = 3 m dans le nom) : marche de 21 cm a 8,45 m par unite (2,5 cm a 1 m par unite, impossible).
- Points de couvert (toutes zones, n = 8 760) : plus proche voisin median 0,18 unite = 1,5 m ; a 1 m par unite ce serait 18 cm. Acteurs de cinematique de `war_planworking` (stg et spawns) : sept points dans 0,4 unite = 3,4 m.
- Donc tous les types d'asset SWTOR partagent un espace de ~8 a 10 m par unite ; les affirmations "1 m par unite pour l'architecture" (T1) et "x2" (T7) etaient fausses.

## 4. Coherence avec l'architecture de CE vaisseau : le test "humain = 70-85 % de la porte" ECHOUE
Kit `spc_space/arch`, converti a 338 u par unite :

| element | GR2 | JA (u) | metres |
|---|---|---|---|
| humain | 0,213 | 72 | 1,8 |
| plafond de couloir `hall_reg32m` (tranche y 1,4..1,5) | 1,4 | 473 | 11,8 |
| largeur de couloir (3,06) | 3,06 | 1 030 | 25,8 |
| ouverture de `doorframe_sm` | 0,96 x 0,8 | 324 x 270 | 8,1 x 6,8 |
| cadre `doorframe_sm/lg` | 3,0 x 1,1 | 1 014 x 372 | 25,4 x 9,3 |
| vantail `door_lg` | 1,8 x 0,7 | 608 x 237 | 15,2 x 5,9 |
| portails `portal.p` de la zone | 0,857 x 0,7 ; 1,77 x 0,7 | | 7,2 x 5,9 ; 15 x 5,9 |
| `hall_tall` | 3,06 x 5,3 | | 25,8 x 44,8 |

- Humain / ouverture de porte = 0,213 / 0,8 = **27 %** (attendu 70-85 %) ; humain / plafond = 15 %. Les portes du kit `spc_arch_ss` ne sont pas a taille humaine : portes de vaisseau capital (ouverture 8 x 7 m, vantail 15 m). Dans d'autres kits, a la meme echelle, les portes humaines existent : `cop_arch_building_doorframe_closed_01` 0,33 x 0,41 (2,8 x 3,5 m), `mek_arch_doorframe_01_closed` 0,29 x 0,36 (2,5 x 3,1 m) : humain / cadre = 52-59 %.
- Facteur qui mettrait l'humain a 78 % de la porte de ce kit : ~115 u par unite (x2,9 du x1), plafond de 4 m. C'est le chiffre "ajuste sur la porte" ; il contredit tout le reste (chaises de 30 cm, marches de 7 cm). **Non retenu** : un facteur global doit valoir aussi pour les personnages et les accessoires.
- **Non uniforme ?** Indices de compression verticale du kit : couloir 3,06 de large pour 1,4 de haut (0,46), ouverture 0,96 x 0,8, vantail 1,8 x 0,7. Mais aucun mecanisme : echelle d'instance 1,0 pour toute l'architecture (seules des caisses a 1,87 et des plaques de coque a 3,0 different), aucune transformation dans les .gr2 statiques (en-tete : version, comptes, boite englobante), aucune propriete d'area/room. Un facteur vertical seul n'est pas etaye ; les proportions sont celles du modele.
- Consequence : a 338 u le vaisseau est immense (voir ci-dessous) et le joueur y est petit. Si c'est trop grand en jeu, `--world-scale 0.35` (~120 u par unite) ou 0.5 donne un compromis sans recompiler.

## 5. Comparaison x1 / x2 / nouveau (war_planworking, unites JA)
| | x1 (40) | x2 (80) | nouveau (338) |
|---|---|---|---|
| plafond de couloir | 56-60 | 112-120 | 473 |
| ouverture de porte (haut x large) | 32 x 38 | 64 x 77 | 270 x 324 |
| joueur (64) / hauteur de porte | 200 % | 100 % | 24 % (72 u : 27 %) |
| dalle de sol (3,2 u) | 128 | 256 | 1 082 |
| boite du BSP hors ciel | - | x -421..914, y -154..466 | x -1778..3860, y -649..1970, z -1147..781 (des pieces descendent sous le sol, cf. T7-echelle) |
| spawn | (330 ; 157 ; 26) | (660 ; 314 ; 26) | **(2755 ; 1057 ; 25)** |

En x2 l'ouverture de porte (64 u) vaut la taille du joueur : c'est ce que montre la capture (joueur aussi haut que la porte).

## 6. Mise en oeuvre
- `src/Swtor.Pipeline/SwtorWorldScale.cs` : plus de `Detect`/`SpaceKit`. Constantes `JaUnitsPerGr2Unit = 338`, `JaUnitsPerMetre = 40`, `MetresPerGr2Unit = 8,45`, `HumanHeightGr2 = 0,213`, `JaHumanModelHeight = 72`, `Resolve(multiplicateur)`.
- `SwtorSceneBuilder.Build` : facteur = 8,45 x `WorldScale` (0 ou 1 = rien) ; appliquee a la matrice monde (sommets, normales, collision), aux positions et rayons de lumieres, donc au point d'apparition. Avertissement `SWTOR_WORLD_SCALE` mis a jour (mentionne "Not verified in game").
- `--world-scale <x>` : **multiplicateur manuel en plus** (defaut 1) ; avant : facteur absolu.
- KOTOR inchange. `swtor-formats.md` : section "Echelle du monde".
- Limites moteur : l'export passe (14 576 surfaces, 43 730 brosses, 268 420 plans, aucun `RTX_ESTIMATE_HIGH` ni `COLLISION_CAPPED`).

## 7. Tests
- `WorldScaleTests` : constante 8,45 sur positions et sommets ; multiplicateur manuel ; 0,213 x 8,45 x 40 = 72 u ; lumieres ; deux tests d'integration (plafond de 4 a 16 m au-dessus du spawn, > 100 m de long, BSP > 4000 u de large, spawn dans la boite). Les autres tests Swtor passent un facteur 1 explicite (attendus en unites GR2).
- `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur. `dotnet test Kotor2Ja.sln` : Kotor.Compiler 84, Kotor.Core 183, Swtor.Pipeline 49, Kotor.Pipeline 179, Swtor.Core 104, Kotor2Ja.Cli 41 = **640 reussis, 0 echec, 0 ignore**. Rien n'est commite.

## 8. Export et teleportation (unites JA ; non verifie en jeu)
pk3 reecrit dans la base JA : `C:\Program Files (x86)\Steam\steamapps\common\Jedi Academy - SWGL_DEV\GameData\base\swtor_war_planworking.pk3`. `info_player_start` = (2754,99 ; 1056,88 ; 25), cap 179,9 (plafond de 6 m au-dessus).
- Apparition : `setviewpos 2755 1057 27 180`
- Centre de `room_01` : `setviewpos 2712 1082 27 0` ; `hallway_01` : `setviewpos -76 1082 27 0` ; `airlock_01` : `setviewpos 0 -51 27 0` (centres approximatifs, ancien centre x4,225, peuvent tomber sur du mobilier).
- Si l'echelle SWTOR globale est bonne : joueur petit devant des portes de ~7 m, salle principale ~100 m. Sinon, reessayer avec `--world-scale 0.35`.

## Incertitude
- La hauteur humaine (0,205-0,213) est mesuree ; le lien avec 72 u JA vient du Character-maker et de KOTOR. Aucune donnee ne donne une echelle "moteur" propre a un kit : le facteur est global et suppose que ce kit suit l'echelle des personnages et des accessoires comme le reste de SWTOR (chaises, rambardes, escaliers, couverts concordent). Que les portes de ce kit fassent 3,7 fois la taille humaine est une propriete des donnees, pas du facteur ; si le jeu montre un vaisseau trop grand, le compromis par la porte (~115 u par unite) est le repli.
- Les tailles reelles supposees des accessoires et les distances de couvert sont des plausibilites, pas des constantes du jeu.
