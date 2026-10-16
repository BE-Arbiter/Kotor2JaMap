# T7 - Echelle du vaisseau (war_planworking)

**RIEN N'A ETE VERIFIE EN JEU.** Le jeu n'a pas ete lance. Tout vient de mesures hors ligne sur les .gr2, les .dat et le BSP exporte. Le facteur d'echelle choisi est une **estimation** (voir "Incertitude").

## Symptome
Le joueur apparait sur le toit du vaisseau, a l'exterieur, et le vaisseau semble miniature (capture 1.webp : on voit les modules de plafond depuis le dessus).

## Mesures AVANT (export precedent, 40 u/m)
Unites SWTOR = metres d'apres T1/T2 ; JA = metres x 40.

| Element | metres | unites JA |
|---|---|---|
| Plafond des couloirs (`spc_arch_ss_hall_reg32m`, tranche de plafond y = 1,4 a 1,5) | 1,5 | 60 |
| Cadre de porte `spc_arch_ss_doorframe_sm` (3,0 de large) | 1,1 de haut | 44 |
| Ouverture de la porte (sommets internes) | ~0,96 x ~0,8 | ~38 x 32 |
| Feuille de porte `spc_arch_ss_door_lg` | 1,8 x 0,7 | 72 x 28 |
| Cadre de fenetre `windowframe01_half` | 3,0 x 1,1 | 120 x 44 |
| Caisse `all_item_con_square_crate` | 0,15 cube | 6 |
| Colonne `spc_arch_ss_tech_column` | 1,37 x 2,3 | 55 x 92 |
| Dalle de sol `mod_floor_plain` | 3,2 x 3,2 | 128 x 128 |
| Hauteur du joueur JA debout | 1,6 | 64 |
| Boite englobante du vaisseau (scene, hors sky) | x 4,6..11,4 (room_01) ; hall -5,3..5,1 ; sas -2,0..2,0 | |
| Point d'apparition | (8,26 ; 3,92 ; 0,02) m | (330 ; 157 ; 26) |

La boite englobante de la room `room_01` etait de 6,8 x 5,3 m. Le monde BSP complet (hors surfaces de ciel) faisait environ 16 x 14 m.

Constat decisif : le plafond (60 u) est **plus bas que le joueur JA debout (64 u)**. Les facteurs `HasHeadroom` de `ChooseEntry` avaient meme ete abaisses a 1,35 m pour "passer" sous ce plafond. Le joueur est donc coince dans le plafond et JA le fait ressortir au-dessus : il se retrouve sur le toit. La capture le montre.

## Cause racine
1. **L'echelle 40 est bien appliquee partout** (verifie : `ModuleExporter.BuildPlan` multiplie positions, sommets, lumieres ; `PlaceableConverter`/`DoorConverter` ne concernent pas SWTOR). KOTOR utilise le meme chemin et est correct. Ce n'est donc pas une perte d'echelle dans la chaine.
2. **Les modeles du kit `spc_space/arch` (`spc_arch_ss_*`) sont a environ la moitie de la taille humaine**, comme T2 l'avait signale pour d'autres zones (spc_ps_* a ~1/5). Les positions d'instance sont sur la grille de 3,2 comme les sommets (dalles de 3,2 posees tous les 3,2) : un facteur d'echelle ne peut etre que **global** (positions et sommets ensemble).
3. **Aucun facteur compensateur dans les donnees** (cherche, rien trouve) :
   - echelle des instances de modele : 1,0 pour tous les elements d'architecture (hall, dalles, cadres, portes) ; seules quelques caisses (1,87) et plaques de coque (3,0) different ;
   - en-tete `.gr2` (0x00..0x80) : seulement version, nombres et boite englobante egale aux sommets, aucun champ d'echelle ;
   - champs bruts des instances (hash `4F77E269` position, `B181622A` echelle, `8C64AF1E` rotation, etc.) : aucun champ supplementaire d'echelle ;
   - proprietes de l'`area.dat`, en-tetes des `.dat` de room, GOM du nom de la zone : rien.
   Le facteur est donc applique ailleurs dans le moteur (propre a l'asset ou au kit), non decodable ici. T2 le disait deja.
4. Les noms de fichiers (`hall_reg32m`, `grate_16m03`...) donnent des dimensions en decimetres egales aux largeurs mesurees (3,2 ; 1,6 ; 0,8) : les largeurs sont donc en metres reels ; ce sont les **hauteurs** qui sont trop basses (portes de 1,1 m, plafonds de 1,5 m). Une echelle globale x2 donne plafond 3 m, cadre de porte 2,2 m, fenetre 2,2 m, colonne 2,8 a 4,6 m, caisse carree 0,3 m : proportions humaines coherentes. C'est un indice, pas une preuve.

## Correctif
- `src/Swtor.Pipeline/SwtorWorldScale.cs` (nouveau) : `Detect(chemins)` renvoie 2 si la majorite des modeles d'architecture (`/arch/`) vient de `art/static/area/spc_space/arch/`, sinon 1.
- `SwtorBuildOptions.WorldScale` (0 = automatique) ; `SwtorSceneBuilder.Build` multiplie la matrice monde de chaque instance (donc sommets, normales, collision) **et** la position des lumieres, et le rayon des lumieres, par ce facteur. Un avertissement `SWTOR_WORLD_SCALE` est journalise.
- `ChooseEntry` : degagement requis au point d'apparition porte de 1,1+0,25 a 1,75+0,25 = 2 m (joueur JA 1,6 m).
- CLI : option `--world-scale <x>` sur `export-swtor` pour regler le facteur sans recompiler (par exemple `--world-scale 2.5` ou `1` pour revenir en arriere).
- KOTOR : aucun fichier KOTOR modifie ; les 183 + 179 + 84 tests KOTOR passent.

## Tests ajoutes (`tests/Swtor.Pipeline.Tests/WorldScaleTests.cs`)
1. `World_scale_multiplies_positions_and_vertices_together` : echelle 2 double positions et sommets (synthetique).
2. `World_scale_is_applied_to_light_position_and_radius`.
3. `Space_station_kit_is_detected_and_other_kits_stay_at_one`.
4. `WarPlanworking_has_human_scale_in_the_scene` : plafond au-dessus du point d'apparition entre 2,5 m et 8 m ; ouverture du vaisseau > 25 m. Ce test **echoue sans le correctif** (plafond 1,35 m).
5. `WarPlanworking_bsp_has_human_scale_in_ja_units_and_spawn_is_inside` : BSP exporte, hauteur hors ciel entre 120 et 600 u, largeur > 1000 u, `info_player_start` dans la boite englobante et au moins 100 u sous le sommet.
(Les tests 4 et 5 sont ignores si les ressources SWTOR sont absentes.)

## Mesures APRES (BSP exporte, `C:\jka_maps\kotor_work\war_planworking\pk3\maps\war_planworking.bsp`, hors surfaces `textures/skies`)
- Boite englobante du BSP : x -421..914, y -154..466, z -272..185 unites JA, soit 33 x 15,5 m en plan et 11,4 m de haut en tout (une partie descend sous le sol : voir "Autres anomalies").
- Plafond des couloirs : 3,0 m = 120 u ; cadre de porte 2,2 m = 88 u ; dalle de sol 6,4 m = 256 u.
- `room_01` : sol de x 9,3..22,8 m, y 1,1..11,7 m (soit 13,5 x 10,5 m) ; en JA x 371..912, y 46..466.
- `hallway_01` : sol x -10,5..9,6 m, y 2,8..10 m ; JA x -421..384, y 112..400.
- `airlock_01` : sol x -3,9..3,9 m, y -3,8..3,2 m ; JA x -157..157, y -154..128.
- Collision : 43 413 brosses (limites BSP respectees, pas de `COLLISION_CAPPED`).
- Export : 4 rooms, 432 instances, 72 modeles, 0 manquant, 149 047 triangles, 447 177 index. pk3 ecrit dans la base JA (aucun processus de jeu en cours) : `...\Jedi Academy - SWGL_DEV\GameData\base\swtor_war_planworking.pk3`.

## Le joueur est-il dans le vaisseau ? (a l'echelle corrigee)
`info_player_start` : origine JA **(660,4 ; 313,9 ; 26,4)**, cap -175. Soit (16,5 ; 7,8) m, **dans `room_01`** (sol x 9,3..22,8, y 1,1..11,7), a 1,4 u de hauteur du sol plus 25 pour les pieds du joueur, avec au moins 2 m de degagement garanti puis un plafond a ~3 m (test 4 : plafond le plus bas au-dessus du point entre 2,5 et 8 m). Calcule hors ligne, non verifie en jeu.

## Commandes setviewpos (unites JA ; z = 27 pour avoir les pieds sur le sol)
- Apparition : `setviewpos 660 314 27 -175`
- `room_01` (centre du sol) : `setviewpos 642 256 27 0`
- `hallway_01` (centre du sol) : `setviewpos -18 256 27 0`
- `airlock_01` (centre du sol) : `setviewpos 0 -12 27 0`
(Le centre peut tomber sur un meuble ; avancer de quelques pas.) Attendu : plafond a hauteur de 3 m environ (le sommet de l'ecran apres un saut de 2 m n'est pas touche), la porte se traverse sans se baisser.

## Incertitude (important)
- Le **x2 est une estimation de proportions humaines**, pas une valeur lue dans le jeu. Si en jeu les portes sont encore trop basses ou les pieces trop vastes, relancer avec `--world-scale 2.5` (ou 3) / `--world-scale 1.5` ; seul ce chiffre change. Les couloirs de 6,4 m de large peuvent paraitre larges : c'est la consequence d'un facteur global (les largeurs mesurees etaient deja metriques d'apres les noms de fichiers, alors que les hauteurs sont trop basses). Si c'est le cas, un facteur vertical seul serait l'autre hypothese, **non retenue** : elle etirerait les textures et aucun mecanisme n'est connu.
- Les autres kits (techcave `all_all`, portes de 1,6 m de haut pour 2,2 a 3,2 m de large) semblent aussi bas ; non change, le correctif ne cible que `spc_space/arch` (les tests existants sur d'autres zones ne bougent pas). A reconsiderer pour inq_pyron et les autres zones.

## Autres anomalies relevees
- `spc_arch_ss_wall_detail01` (rooms room_01 et hallway_01) descend jusqu'a z = -6,8 m / -4,1 m sous le sol (avant correction : -3,4 / -2,1 m) : une instance mal placee ou une rotation multi-axes (ordre d'Euler non verifie, cf. T2/T4). Les surfaces sous le sol n'empechent pas de marcher mais elles agrandissent le BSP et peuvent sortir par un trou.
- L'orientation de la capture (vue depuis le dessus, joueur sur la face superieure des tranches de plafond) est cohérente avec l'explication ci-dessus, pas avec une erreur d'orientation des axes. Aucun plafond manquant constate dans les mesures : le plafond existe (`spc_arch_ss_mod_ceiling_trench_triple`, y = 1,4..1,5 avant correction).
- Le BSP contient des surfaces `textures/skies/space` (ciel) dont la boite depasse largement le vaisseau (z -784..697) : normal, a exclure des mesures de taille.

## Resultats
- `dotnet build Kotor2Ja.sln` : 0 erreur, 0 avertissement.
- `dotnet test Kotor2Ja.sln` : Kotor.Compiler 84, Kotor.Core 183, Kotor.Pipeline 179, Kotor2Ja.Cli 41, Swtor.Core 104, Swtor.Pipeline 48 ; 0 echec ni ignore. Total 639. Rien n'est commite.

## Fichiers
Modifies : `src/Swtor.Pipeline/SwtorSceneBuilder.cs`, `src/Kotor2Ja.Cli/Commands/ExportSwtorCommand.cs`.
Nouveaux : `src/Swtor.Pipeline/SwtorWorldScale.cs`, `tests/Swtor.Pipeline.Tests/WorldScaleTests.cs`.
