# T7 bis - Vraie salle du Conseil noir (Flotte Imperiale)

**RIEN N'A ETE VERIFIE EN JEU.** Le jeu n'a pas ete lance ni touche (`tasklist` sans SWGL avant l'export). Tout vient de lectures hors ligne. Rien n'est commite.

## 1. Methode
- Balayage de **tous** les fichiers des 267 dossiers `world/areas/<id>/` (`area.dat`, rooms `*.dat`, `mapnotes.not`, noms de fichiers dont minimaps `map_*.dds`) : chaines ASCII >= 4 caracteres contenant council / dkc / throne / trone / sith_council / darkcouncil / conseil / sith_lord / inner_circle. Le dernier mot-cle n'a rien donne de pertinent ; les occurrences "dkc" sont du bruit binaire.
- Cle de la recherche : le modele **`art/static/area/kor_korriban/item/kor_item_dark_council_throne.gr2`** (le trone du Conseil noir) et le suffixe `_throne_platform(_trim_01)`. Les zones qui le placent : voir 2. Puis dump de chaque zone candidate (`swtor-dump-area <id> --max 100000`), comptage des modeles par room et ajustement d'un cercle sur les positions monde des trones.
- Pas de recherche dans les chaines fr-fr ni dans le GOM : la cle modele a suffi (cf. rapport T7 : la zone "Conseil noir" n'a pas de nom d'affichage).

## 2. Zones qui contiennent le trone du Conseil
| Zone | Rooms avec trones | Geometrie |
|---|---|---|
| **4611686142106770000 `imperial_fleet` ("Flotte Imperiale")** | **`station_dark_council_chamber`** (aussi 2 dans `station_prison_cel`/quadrants : autres emplacements) | **13 trones**, 12 en cercle autour d'un centre (rayon moyen 1,6 unite GR2 = ~500 u JA, ecart-type 0,2 ; ajuste sur les 12), le 13e est a l'ouest en tete de salle (279,69 ; 468,82), rotY de chaque siege orientee vers le centre |
| 4611686022825732973 `kor_rebuild_test` (Korriban, Academie sith, minimaps `map_int_academy_council_warrior/inquisitor`) | `sith_acad_floor3_rm2`, `sith_acad_floor3_rm4` | 12 trones **en deux rangees de 6 face a face** (rotY +-79/+-101), pas un cercle : salle du conseil de l'academie (versions Guerrier / Inquisiteur), pas la salle des douze sieges |
| `dro_knight`, `story_flashpoint_korriban`, `raid_ori_dreadpalace`, `mini_flashpoint_vengeance`, `stronghold_fleet_imp`, `elom_digsite`, `elom_library` | non examinees en detail | le trone figure dans leur table d'assets (reutilisation) |

Indices complementaires de `imperial_fleet` : propriete `environment_map = ilum_flashpoint_2_station/throne_center`, asset `scene_6_finale_council.stg`, `imp_intro_council.stg` (Onslaught), `phase_dark_council.mpn` (exp 03), liens `wnkdest_station_elevator_west_dark_council.mpn` et `to_dark_council_chambers.mpn`, minimap `map_imperial_fleet_station_dark_council_r.dds`. La Flotte est la station orbitale impériale : c'est la salle du Conseil de la Flotte, **pas** une salle de `dro_main` (Citadelle de Kaas), ce qui explique l'echec du rapport precedent. La Citadelle de `dro_main` ne place aucun trone du Conseil.

Confiance : bonne (nom de room, minimap, modele de trone, 12 sieges en cercle). Reserve : les 3 autres candidats Korriban sont des salles d'academie rectangulaires.

## 3. Export
Commande exacte :
```
dotnet run --project src/Kotor2Ja.Cli -- export-swtor "4611686142106770000:station_dark_council_chamber" --name dark_council
```
(le pk3 `swtor_dark_council.pk3` de la base JA a ete **remplace** ; `swtor_war_planworking.pk3` intact). La room est isolee (`linked rooms []`), une seule room.
- 361 instances de modele, 75 modeles distincts (0 manquant), 67 lumieres, 61 textures (0 manquante), 126 998 triangles.
- BSP : 63 shaders, 12 296 surfaces, 257 448 sommets, **381 030 index** (limite 524 288), 40 251 brosses, 455 320 cotes, 333 402 plans. Marchables 8 286, solides 45 575. Pas de `RTX_ESTIMATE_HIGH` ni `COLLISION_CAPPED`. Journal : `BSP_WARNING` 2 (informatifs), `SWTOR_RECENTERED` 1, `SWTOR_COLLISION_MESH_SKIPPED`, `SWTOR_HIDDEN_PIECE_SKIPPED`, `SWTOR_KIND_SKIPPED` 5, `SWTOR_LIGHTS_APPROX`, `SWTOR_WORLD_SCALE`.

## 4. Changement de code (necessaire)
La room est a (282 ; 469) unites GR2, soit (88 000 ; -146 000) unites JA : **hors des limites du monde JA (+-131 072)**. Ajout dans `SwtorSceneBuilder` : `SwtorBuildOptions.OriginOffset` et `ComputeOriginOffset` : si le centre X/Z des modeles exportes (plus sa demi-etendue) depasse 60 000 u JA de l'origine, la scene est translatee (modeles, lumieres, donc murs/sol/point d'apparition) pour centrer la zone sur l'origine, journal `SWTOR_RECENTERED`. Sans effet pour les exports precedents (`war_planworking`, Citadelle a y ~28 000). Pas de test dedie ajoute. Decalage ici : (-286,69 ; -468,815) unites GR2 (centre X/Z de la boite des modeles).

## 5. Verification en jeu (unites JA, NON VERIFIE)
Formule : JA = ((x - 286,69) x 312, -(z - 468,815) x 312, y x 312). Le sol des trones est a y = 83,17 (25 949 u) ; l'apparition choisie par le builder est a z = 26 108.
- Carte : `devmap dark_council`
- Apparition automatique (cote est, ~4 unites GR2 du cercle) : `setviewpos -453 161 26108 180` (cap 180 = vers l'ouest, vers les sieges).
- Centre du cercle des 12 sieges, face au trone de tete a l'ouest : `setviewpos -1485 0 26150 180` (z un peu haut : on retombe sur le sol ; si on est coince dans un meuble, deplacez-vous de quelques unites).
- Rayon du cercle ~500 u : les sieges sont autour de ce point.
A regarder : sol sans chute, echelle joueur/sieges, plateforme (`kor_item_dark_council_throne_platform`), ciel/lumieres (zone interieure, ciel de la Flotte non verifie).

## 6. Build et tests
`dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur. `dotnet test Kotor2Ja.sln` : Compiler 84, Core 183, Kotor.Pipeline 179, Swtor.Pipeline 49, Swtor.Core 104, Cli 41 = **640 reussis, 0 echec**.
