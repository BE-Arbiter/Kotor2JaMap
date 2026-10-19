# T7 - Echelle 3 : calibrage cote JA (250 u par unite GR2)

**RIEN N'A ETE VERIFIE EN JEU.** Le jeu n'a pas ete lance ni touche (`tasklist` sans SWGL avant les exports). Tout vient de mesures hors ligne sur les .gr2 et du code source OpenJK-SWGL (lecture seule).

## 1. References JA (unites JA)
| Reference | Valeur | Source |
|---|---|---|
| Capsule du joueur debout | mins z -24, maxs z 40 = **64 u** (hors modele JKA, ~72 u) ; accroupi maxs 16 | `bg_public.h` `DEFAULT_MINS_2/MAXS_2/CROUCH_MAXS_2` |
| Hauteur d'oeil | 36 depuis l'origine (origine a 24 du sol) = ~60 u du sol | `DEFAULT_VIEWHEIGHT` |
| Marche franchissable sans saut | **18 u** (contrainte dure) | `STEPSIZE` |
| Saut | `JUMP_VELOCITY` 225 | `bg_public.h` |
| Chaise / porte JA du jeu de base | **non mesurees** (pas de modele/BSP de base exploite ici) ; convention : assise ~ genou (15 a 20 u), porte 1,2 a 1,6 x joueur | hypothese |
| KOTOR | 40 u/m (`ExportOptions.DefaultScale`) : humain 1,8 m = 72 u, capsule 64 u = 1,6 m. Pas de changement KOTOR ; je n'ai pas pu verifier en jeu | code |

## 2. Mesures SWTOR (unites GR2, outil ad hoc hors depot sur `Gr2Reader`)
- **Humain** (T7-echelle-2) : 0,205 (semelle a crane). Aucun facteur d'echelle d'execution trouve (ni dans les .gr2, ni dans les rapports precedents) ; je n'ai pas rouvert le GOM/les donnees d'apparence : non exclu.
- **Assises** (niveaux horizontaux des .gr2, 14 modeles) : chaises 0,054 a 0,068 (typique 0,06 ; hauteur totale 0,14 a 0,20), tabourets de bar 0,066 a 0,09, bancs 0,065 a 0,11, canape/fauteuil ~0,06. Assise / humain = 0,29 (= 0,5 m si l'humain fait 1,8 m : coherent).
- **Table** 0,101 (mediane de 120 zones, T7-echelle-2).
- **Marches** (252 modeles d'escalier, hauteur de contremarche mediane par modele) : 10e centile 0,013 ; mediane **0,025** (famille `wallset_h3m`, dominante) ; 75e 0,030 ; 90e 0,045 ; vrais escaliers jusqu'a **0,058** (`ori/yav_arch_temple_staircase`, `und_metal_road_stairs`) ; estrades / marches de donjon 0,07 a 0,28 (`landing_pad_stairs` 0,07, `fort_stairwell` 0,18, `asy_dojo` 0,28 : des estrades, pas des escaliers).
- **Portes de batiment** (cadres) : `cop_arch_buildings_stronghold_doorframe` 0,51-0,52 (L 0,42), `cop_arch_building_doorframe_closed_01` 0,41, `mek_arch_doorframe` 0,36-0,48, `ond_arch_palace` 0,55, `elo_arch_ruins` 0,80, `ktd_arch_sm_hall` 1,4 (hall de vaisseau). Humain / porte de batiment = 0,37 a 0,57. Kit capital `spc_arch_ss_doorframe_sm` : 0,8 d'ouverture.
- **Trone du Conseil** (`kor_item_dark_council_throne.gr2`, instance a l'echelle 1,17, pos y 0,09) : assise a y = 0,21-0,22 dans le modele, soit ~0,09 + 0,22 x 1,17 = **0,34** au-dessus du sol = 1,7 humain ; dossier 0,56 x 1,17 = 0,66. `throne_platform` : 10 marches de 0,037 (total 0,37). C'est un trone monumental (estrade d'environ 3 m pour un humain de 1,8 m), son assise ne dit rien de l'echelle des meubles ordinaires. La capture montre exactement cela : le joueur debout sur une marche, assise a l'epaule.

## 3. Candidats
Colonnes en unites JA ; X = hors contrainte. Contraintes : marche max (0,058 vraie, 0,07 estrade) <= 18 ; assise 0,06 entre 15 et 20 ; porte de batiment 0,36 a 0,55 entre 77 et 102 (1,2 a 1,6 x 64) ; humain SWTOR informatif.

| u / unite GR2 (m/unite) | humain 0,205 | assise 0,06 | marche 0,025 / 0,058 / 0,07 | porte 0,36 / 0,41 / 0,52 | trone (assise 0,34) | verdict |
|---|---|---|---|---|---|---|
| 338 (8,45) | 69 | 20 | 8,5 / **19,6 X** / **23,7 X** | 122 X / 139 X / 176 X | 115 | marches > 18, trop grand (essai en jeu) |
| 312 (7,8) | 64 | 18,7 | 7,8 / 18,1 X (limite) / 21,8 X | 112 X / 128 X / 162 X | 106 | trop grand (essai en jeu) |
| 280 (7) | 57 | 16,8 | 7 / 16,2 / 19,6 X | 101 / 115 X / 146 X | 95 | estrades 0,07 non franchissables |
| **250 (6,25)** | 51 | **15,0** | 6,3 / 14,5 / **17,5** | **90 / 103 / 130** | 85 | **choisi** : toutes les marches <= 0,072 OK |
| 220 (5,5) | 45 | 13,2 X | 5,5 / 12,8 / 15,4 | 79 / 90 / 114 | 75 | assise sous le genou |
| 200 (5) | 41 | 12 X | 5 / 11,6 / 14 | 72 X / 82 / 104 | 68 | humain a 64 % du joueur |

Aucun facteur ne satisfait les portes pour **toutes** les portes : celles de SWTOR sont 2 a 2,7 fois l'humain, donc si l'humain vaut 64 u la porte vaut 130 u (2 x). Un facteur qui met la porte a 1,5 x le joueur (96 u) met l'humain a 47 u. Le facteur est un compromis, pas une mesure.

## 4. Choix : **250 u par unite GR2** (6,25 m JA), au lieu de 312 (-20 %)
- Contrainte dure respectee : toute marche <= 0,072 unite est franchissable (<= 18 u), y compris les vraies marches maximales (0,058 -> 14,5 u) ; a 312 les escaliers de temple (0,058) etaient a 18,1 u, deja sur la limite.
- Assise de chaise 15 u : genou du joueur (limite basse de la fourchette 15-20), table 25 u, porte de batiment 90-130 u (1,4 a 2 x joueur), joueur = 1,25 x l'humain SWTOR : le joueur JA parait un peu grand a cote des PNJ SWTOR (nains), c'est la consequence de portes SWTOR 2 x humaines.
- Ce qui ne bouge pas : le facteur reste unique et global ; `--world-scale 0.9` ou `1.1` pour essayer 225 / 275 sans recompiler.
- Code : `SwtorWorldScale.JaUnitsPerGr2Unit = 250` (6,25 m/unite), constantes `JaPlayerHeight` 64 et `JaStepSize` 18 ajoutees, `JaHumanModelHeight` supprimee. Test `Derived_scale_respects_the_ja_gameplay_constraints` (250 ; marche 0,07 <= 18 ; assise 0,06 entre 15 et 20 ; plus petite porte de batiment entre 1,2 et 1,6 x joueur). Doc : section "Echelle du monde" de `swtor-formats.md`. Aide CLI et message `SWTOR_WORLD_SCALE` mis a jour.

## 5. Exports (aucun processus de jeu)
- `dark_council` : 361 instances, 75 modeles, 124 050 triangles, BSP 372 186 index (limite 524 288), 39 597 brosses ; journal sans limite atteinte, pas de `SWTOR_SPAWN_HEADROOM`. pk3 : `...\Jedi Academy - SWGL_DEV\GameData\base\swtor_dark_council.pk3`.
- `war_planworking` : 432 instances, 72 modeles, 149 047 triangles, 447 177 index, 43 690 brosses ; pas d'avertissement de degagement. pk3 `swtor_war_planworking.pk3`.
- Apparition (lue dans le BSP, `info_player_start`) : degagement de 1,75 m verifie par `ChooseEntry` avant d'ecrire le BSP (aucun avertissement `SWTOR_SPAWN_HEADROOM`), mais non revu au plafond reel.

## 6. `setviewpos` (unites JA, NON VERIFIE EN JEU)
Formule dark_council : JA = ((x - 286,69) x 250, -(z - 468,815) x 250, y x 250) ; sol des trones y = 83,17 -> z = 20 792 (+27 pour les pieds).
- Carte : `devmap dark_council`
- **Apparition** : origine BSP (-475,9 ; 8,6 ; 20 819), cap 0 -> `setviewpos -476 9 20819 0`
- **Centre de la salle** (centre du cercle des 12 trones, plate-forme du centre a y = 83,09) : `setviewpos -1142 0 20830 180`
- **A cote d'un trone** (trone de monde (282,39 ; 467,29), a 62 u de lui vers le centre ; cap 80 = vers le trone) : `setviewpos -1086 320 20830 80`. Le trone est a ~380 u du centre (rayon ~1,5 unite GR2). Si on tombe dans un meuble ou une marche, se decaler de quelques unites. A regarder : assise du trone vers 85 u (a l'epaule d'un joueur debout sur l'estrade, c'est normal), marches de l'estrade (~9 u) franchissables en marchant.
- war_planworking : apparition `setviewpos 2038 782 25 180` (avant, a 312 : 2543 976).

## 7. Build et tests
`dotnet build Kotor2Ja.sln` : 0 erreur, 0 avertissement. `dotnet test Kotor2Ja.sln` : Compiler 84, Core 183, Kotor.Pipeline 179, Kotor2Ja.Cli 41, Swtor.Core 106, Swtor.Pipeline 55 = **648 reussis**, 0 echec, 0 ignore. Rien n'est commite. Fichiers modifies : `SwtorWorldScale.cs`, `SwtorSceneBuilder.cs` (message), `ExportSwtorCommand.cs` (aide), `WorldScaleTests.cs`, `swtor-formats.md`.
