# T7 Euler 2 - Ordre des rotations internes (X et Z) : appliques du Conseil noir

**RIEN N'A ETE VERIFIE EN JEU.** Le jeu n'a pas ete lance ni touche. Au moment de l'intervention, `SWGL_SP.x86_64.exe` (PID 54140) etait en cours d'execution : **aucun re-export n'a ete fait** (ni `dark_council` ni `war_planworking`), les pk3 de la base JA sont inchanges. Rien n'est commite.

## 1. Les instances
12 `spc_item_ss_wall_light01` (echelle X ~0,71, hors 3 petites variantes a echelle 0,13 / 0,23 d'autres usages) de `station_dark_council_chamber`, toutes enfants de `spc_arch_imp_railgun_elev_platform` (rotation `(0, 90, 0)`, sans X ni Z, echelle 0,854, parent racine) : la composition `local * monde(parent)` est donc correcte (parent en lacet pur). Champs bruts : aucun quaternion ni pivot supplementaire (memes champs pour les 12 : parent, position, echelle, rotation, puis booleens/entiers sans lien avec l'orientation). Rotations brutes : `(-69,09 ; +-44,86|45,14 ; -90)` (6 pieces, y local 0,51) et `(-60,98 ; +-134,86|135,14 ; -90)` (6 pieces, y local 0,57), a un rayon de ~3,35 du centre de la plateforme.
Le gr2 est une plaque de 1,1 (X) x 1,6 (Y) x 0,28 (Z) : plaque arriere = plan z = 0 (normale -Z, aire 1,56), face avant plane = plan z ~ 0,20.

## 2. Verite terrain retenue
Le critere demande (plaque arriere a plat contre un mur, ecart ~0) **n'est pas utilisable** : la plaque arriere frole de nombreuses facettes (armoires `all_arch_imp_war_room_tech_cabinet`, murs `vb_wall`, segment `pf_seg02`) sans plan commun ; l'angle plaque/surface la plus proche est de 54 a 71 deg en moyenne pour tous les candidats, et au mieux 29 % des sommets sont "a plat" (< 3 cm, normales a 14 deg) pour des conventions absurdes (le lacet ne serait plus autour de la verticale). Je ne l'ai donc pas retenu.
Le critere decisif est ailleurs : **chaque applique a un cone de brouillard `standard_light_fog_cone_01`** (12 instances racines, y = 83,6101, echelle ~2,16 x 1,07 x 0,47, rotation `(0, +-45|+-135, +-180)`), a 0,15 a 0,22 d'elle (plus proche voisin, 12 cones distincts). Ce cone est le faisceau de l'applique : son origine doit etre sur la face avant de la plaque, donc a **z local = 0,20 pour les 12 pieces**, quelle que soit la famille. Critere strict, invariant par lacet, sensible a l'ordre et au signe de X.
Rappel : le "lacet pur" du rapport precedent ne prouve que "Y en dernier" ; X,Z,Y et Z,X,Y le passent tous deux (0,0 deg pour les deux).

## 3. Tableau des conventions (288 testees : 6 ordres x 8 signes x 6 permutations des champs)
Ecart-type, sur les 12 appliques, de la position locale du cone dans le repere de l'applique (unites gr2). Ordre = axes appliques en premier..dernier ; signes et permutation neutres sauf mention.

| convention | sd z | sd x | sd y | z moyen | commentaire |
|---|---|---|---|---|---|
| **Z, X, Y (`qy*qx*qz`)** | **0,004** | 0,051 | 0,072 | **0,202** | origine du cone sur la face avant (z = 0,20) |
| X, Z, Y (code precedent) | 0,076 | 0,037 | 0,055 | 0,121 | dispersion de 7 cm en z |
| Z, Y, X (code T4) | 0,022 | 0,195 | 0,059 | 0,210 | x disperse de 0,2 |
| Y, Z, X | 0,152 | 0,119 | 0,176 | 0,095 | rejete |
| Y, X, Z | 0,110 | 0,100 | 0,232 | 0,071 | rejete |
| X, Y, Z | 0,129 | 0,180 | 0,140 | 0,064 | rejete |
| Z, X, Y avec Z de signe oppose | 0,004 | 0,051 | 0,072 | 0,202 | plaque retournee de 180 deg dans son plan : indiscernable ici, signe `+` conserve |
| autres permutations / signes | >= 0,05 sur au moins un axe (hors alias de la meme rotation) | | | | |

Sur les 288, 4 seulement ont sd z < 0,02 : Z,X,Y, un alias (permutation + signes equivalents) et leurs variantes de signe de Z.
Orientation obtenue : l'avant (+Z) de l'applique regarde vers le haut (+Y 0,87 a 0,93) et vers l'axe de la plateforme (cosinus horizontal 0,96 a 0,99), plaque inclinee de 21 a 29 deg par rapport a l'horizontale, grand cote tangent au cercle : un couvercle de pupitre incline face aux sieges. Avec X,Z,Y la plaque etait debout, de champ, avant tangentiel (cosinus 0,13) : le symptome decrit ("boites debout tournees de cote"). Equivalent : l'ordre `D3DXMatrixRotationYawPitchRoll` (roulis Z, tangage X, lacet Y).

## 4. Correctif
`src/Swtor.Core/Areas/AreaLayout.cs` : `RotationFromEuler` renvoie `qy * qx * qz` (avant `qy * qz * qx`). Axes (`SwtorAxes`) et echelle (250 u / unite gr2) inchanges.
Tests : `Euler_yaw_sends_X_to_minus_Z_and_order_is_Z_then_X_then_Y` (synthetique, renomme et mis a jour : `(90,90,90)` sur +X donne +X ; `(-69,0,-90)` sur +Z donne (0 ; 0,934 ; 0,358)) ; nouveau `DarkCouncil_wall_light_beam_origin_lies_on_the_front_face_and_the_light_faces_up_and_inward` (SkippableFact : z local du cone dans [0,18 ; 0,23] et dispersion < 0,02 sur les 12 pieces ; avant +Y > 0,85 et tourne vers l'axe, cosinus > 0,9). `DarkCouncil_wall_lights_differ_by_a_pure_yaw` et `ShipInterior_wall_monitors_are_pitched_not_rolled` restent verts (commentaire ajuste). `docs/superpowers/specs/swtor-formats.md` mis a jour (convention, validation, tests).

## 5. Autres instances modifiees
Instances modele dont la rotation monde change (X et Z tous deux non triviaux, chaine de parents incluse) :
- `station_dark_council_chamber` (361 modeles) : **24** = 12 appliques, 8 cones `standard_light_fog_cone_01` `(+-45, 0, 180)` (meme axe de faisceau, orientation de l'ellipse differente), 2 `all_arch_imp_buildings_pillar_1` `(79,6 ; +-90 ; -180)` (ils s'etendent maintenant vers le centre, comme les 2 autres a Z = 0, et non vers l'exterieur), 2 `kor_item_dark_council_throne_platform_trim_01` `(17 ; -88 ; 2)` (effet < 2 deg).
- `war_planworking` (442 modeles) : **0** (ecrans muraux `(20, +-90, 0)` : Z = 0, inchanges).
Tout le reste est inchange. Note : l'argument "bbox centree" des piliers dans T7-euler ne departage pas les deux ordres (memes dimensions, miroirs l'une de l'autre) ; il n'est pas contredit.

## 6. A verifier en jeu (NON VERIFIE), apres re-export
Re-exporter jeu ferme (non fait ici) : `dotnet run --project src/Kotor2Ja.Cli -- export-swtor "4611686142106770000:station_dark_council_chamber" --name dark_council` (`war_planworking` : aucun changement, re-export inutile).
Puis `devmap dark_council` et (cap en degres JA ; echelle 250 u par unite gr2 ; recentrage inchange (286,69 ; 468,82) unites gr2 : JA = ((x - 286,69) x 250, -(z - 468,82) x 250, y x 250) ; sol des trones z = 20 792) :
- Applique `...FAB5` (famille -69, ouest-nord) : `setviewpos -1432 -290 20860 -135` (applique en (-1715 ; -573 ; 20897)).
- Applique `...FAB8` (famille -61) : `setviewpos -911 -371 20860 -58` (applique en (-698 ; -709 ; 20908)).
- Applique `...FABB` (famille -61) : `setviewpos -849 291 20860 45` (applique en (-566 ; 573 ; 20908)).
- Applique `...FAC3` (famille -69, ouest-sud) : `setviewpos -1508 225 20860 148` (applique en (-1848 ; 436 ; 20897)).
A regarder : les appliques doivent former un pupitre incline (~21 a 29 deg), face vers le haut et le centre, au lieu de boites debout de cote ; les 2 piliers de la couronne haute s'inclinent vers le centre. Le signe de Z (+) est conserve par convention : si une applique est retournee de 180 deg dans son plan, c'est ce signe (indiscernable par calcul).

## 7. Build et tests
`dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur. `dotnet test Kotor2Ja.sln` : Compiler 84, Core 183, Kotor.Pipeline 179, Swtor.Pipeline 55, Swtor.Core 107, Cli 41 = **649 reussis, 0 echec, 0 ignore**.
