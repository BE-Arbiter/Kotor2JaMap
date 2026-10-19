# T7 Euler - Ordre des rotations d'Euler SWTOR

**RIEN N'A ETE VERIFIE EN JEU.** Le jeu n'a pas ete lance ni touche (`tasklist` sans SWGL avant les exports). Tout vient de calculs hors ligne. Rien n'est commite.

## 1. Symptome et cause
Dans `swtor_dark_council.pk3`, des pieces a rotation sur 2-3 axes (appliques/consoles inclinees derriere les trones) etaient penchees du mauvais cote.
Cause : `AreaTransforms.RotationFromEuler` appliquait **Z, puis Y, puis X** (`qx * qy * qz`, axes du monde). La convention reelle est **X d'abord, puis Z, puis Y**
(tangage X et roulis Z dans le repere de l'objet, puis lacet Y autour de la verticale du parent). Les rotations sans composante X (lacet seul, `(0, +-90, +-Z)`) donnent le meme resultat
dans les deux ordres, d'ou des pieces "normales" a cote de pieces fausses.

Instances a rotation multi-axes (avec X non nul) de `station_dark_council_chamber` : `spc_item_ss_wall_light01` (12 appliques, `(-69,+-45,-90)` et `(-61,+-135,-90)`, ce sont les boitiers inclines a ecran
additif visibles derriere les trones), `all_arch_imp_buildings_pillar_1` x4 `(79,6 ; ~90 ; 180)` et `(-81 ; +-90 ; 0)`, `all_item_imp_space_wall_light_base`/`_red`, `all_arch_imp_fort_light_hallway` `(+-90,+-90,0)`, `all_item_imp_space_wall_light`,
`standard_light_fog_cone_01` `(+-45, 0, 180)`, `kor_item_dark_council_throne_platform_trim_01` `(17 ; -88 ; 2)`. Identification des consoles du screenshot = les appliques `wall_light01` : 12 pieces, une par siege,
tilt X -61/-69, additif blanc (les triangles blancs). Hypothese (le screenshot ne permet pas de verifier l'identite de la piece), forte car seules ces pieces sont inclinees sur 2-3 axes autour des sieges.

## 2. Preuve (calcul, pas statistique)
Methode : 6 ordres x 2 signes (`Ra*Rb*Rc`, matrices colonne) testes sur des groupes de pieces dont la disposition impose la relation.

1. **Appliques `spc_item_ss_wall_light01`** (12, deux familles de 6 en cercle autour du parent `spc_arch_imp_railgun_elev_platform`). Dans une famille, la rotation relative de deux pieces doit etre un lacet pur autour de la verticale. Ecart de la verticale (degres, moyenne ; max) :

| ordre | ecart moyen | max |
|---|---|---|
| `Rx*Ry*Rz` (ancien code, Z d'abord) | **79,9** | 130,1 |
| `Rx*Rz*Ry` | 98,2 | 171,9 |
| `Ry*Rx*Rz` | 4,4 | 8,1 |
| **`Ry*Rz*Rx` (X, Z, Y)** | **0,0** | **0,0** |
| `Rz*Rx*Ry` | 98,2 | 180 |
| `Rz*Ry*Rx` | 96,7 | 174,3 |

   Le signe : avec `+` (sens mathematique direct, +90 sur Y envoie +X vers -Z) l'erreur de sens du lacet par rapport a l'angle de position est <= 21 deg (residu des deux familles de hauteur) ; avec `-` elle est de 160 a 180 deg. Les axes locaux des appliques, avec le bon ordre, tombent exactement sur les axes horizontal/vertical/radial (local X = -Y monde, local Y = radial a 0,94-0,96), alors que l'ancien ordre donne des axes obliques (0,36 ; 0,66...).
2. **Piliers `all_arch_imp_buildings_pillar_1`** (4, enfants de la plateforme) : bounding box monde de la geometrie. Bon ordre : centree sur l'ancre (z -3,87..-3,49 pour une ancre a -3,66) et paires gauche/droite symetriques ; ancien ordre : decalee de 1,1 m (z jusqu'a -4,79), ecart de symetrie 118 deg contre 12 deg.
3. **Ecrans muraux `all_item_tech_wall_monitor`** `(20, +-90, 0)` de `war_planworking` (hallway_01) : bon ordre = tangage (hauteur monde 0,23 m, normale `(-cos20, -sin20, 0)`) ; ancien ordre = roulis de 20 deg dans le plan du mur (hauteur 0,36 m), peu plausible pour un ecran mural.
4. Panneaux `hangar_wall_flat_med` et `tech_panel` `(0, +-90, +-Z)` : identiques dans les deux ordres (X nul), donc pas touches par le bug.

**Chiralite (non tranchee).** Le sens positif des angles est mathematique direct dans le repere des positions (verifie en 1), mais une matrice D3D ligne donnerait les memes nombres, donc cela ne dit pas si le monde est a main gauche. La conversion `(x,y,z)->(x,-z,y)` (rotation propre) est conservee : T4 a verifie que le sens des triangles est coherent avec les normales, et aucune piece asymetrique de ces zones (la crete imperiale est symetrique) ne departage. Si un texte ou un logo apparait inverse en jeu, `--axes mirror` existe.

**war_planworking.** Le cas "`spc_arch_ss_wall_detail01` 4-7 m sous le sol" de T7-echelle **n'est pas** un probleme d'Euler : ces instances ont `(0, -90, 0)` et un parent `(0, 90, 0)`, sans X ; leur boite monde est la meme dans les deux ordres (Y -3,39..1,28 : le modele a une geometrie locale haute, echelle Y 1,5). Il reste donc ouvert (autre cause : geometrie/echelle du modele, a traiter separement). Les pieces de `war_planworking` concernees par le changement : 9 dans room_01 (`vos_arch_gormak_int_pillar_02`, `spc_item_ss_wall_light01`), 21 dans hallway_01 (`ss_pipe_seg01`, `all_arch_imp_building_props_lights_01`, `ss_ceiling_light01`, `tech_wall_monitor`), 4 dans SPACE (`all_item_tech_wall_01`, non exportee par defaut).

## 3. Changement de code
- `src/Swtor.Core/Areas/AreaLayout.cs` : `RotationFromEuler` renvoie `qy * qz * qx` (X d'abord, puis Z, puis Y). Axes (`SwtorAxes`) inchanges.
- Tests : `Euler_yaw_sends_X_to_minus_Z_and_order_is_X_then_Z_then_Y` (synthetique, remplace l'ancien test T4), `DarkCouncil_wall_lights_differ_by_a_pure_yaw` et `ShipInterior_wall_monitors_are_pitched_not_rolled` (SkippableFact, ressources reelles).
- `docs/superpowers/specs/swtor-formats.md` : convention et section "Validation T7 : ordre d'Euler" (preuves ci-dessus), conclusion T4 annotee.

## 4. Exports (jeu non lance)
- `dotnet run --project src/Kotor2Ja.Cli -- export-swtor "4611686142106770000:station_dark_council_chamber" --name dark_council` : 361 instances, 126 998 triangles, BSP 381 030 index (limite 524 288), pk3 `swtor_dark_council.pk3` remplace.
- `dotnet run --project src/Kotor2Ja.Cli -- export-swtor 4611686045100172646` : 149 047 triangles, pk3 `swtor_war_planworking.pk3` remplace.
- Decalage de recentrage dark_council inchange : (-286,69 ; -468,82) unites GR2. JA = ((x - 286,69) x 312, -(z - 468,82) x 312, y x 312).

## 5. Build et tests
`dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur. `dotnet test Kotor2Ja.sln` : Compiler 84, Core 183, Kotor.Pipeline 179, Swtor.Pipeline 49, Cli 41, Swtor.Core 106 = **642 reussis, 0 echec, 0 ignore**.

## 6. A verifier en jeu (NON VERIFIE)
`devmap dark_council`, puis (cap en degres, le sol des trones est a z = 25 949) :
- Appliques du cote ouest-nord : `setviewpos -1700 -430 26120 213` (regarde l'applique en (-2146 ; -720 ; 26062)).
- Appliques du cote ouest-sud : `setviewpos -1700 430 26120 147` (regarde l'applique en (-2143 ; 717 ; 26062)).
- Centre de la salle : `setviewpos -1426 0 26150 180` (plateforme parent de tous les trones au centre).
A regarder : les 12 appliques doivent se presenter de maniere identique tout autour des sieges (meme inclinaison par rapport au mur), les piliers a plat contre leur mur, plus de pieces penchees "a l'envers". Les triangles blancs (materiau additif) sont un autre sujet. Pour war_planworking : les ecrans muraux du couloir doivent etre penches vers l'avant, pas tournes dans leur plan.
