# T7 - Echelle recalee (312 u) et export de la zone "Dark Council"

**RIEN N'A ETE VERIFIE EN JEU.** Le jeu n'a pas ete lance, aucun processus de jeu touche (aucun `SWGL_SP` en cours : `tasklist` vide avant les exports). Tout vient de mesures et de lectures hors ligne.

## 1. Nouvelle echelle : 312 unites JA par unite GR2 (7,8 m JA), avant 338 (8,45 m)
- Reference retenue : l'humain SWTOR (0,205 unite GR2, semelle a sommet du crane, mesure sur les .gr2, cf. T7-echelle-2 section 1) = **64 unites JA**, la hauteur de la capsule de collision du joueur JA. 64 / 0,205 = 312,2, arrondi a **312** (x0,923 de 338).
- Pourquoi : 338 venait du modele JKA (72 u, cou -> joint `cervical`, cheveux compris : 72 / 0,213). Essai en jeu : joueur JA petit a cote d'une capsule de stase, carte "un peu trop grande". Le joueur JA mesure 64 u pour la collision et c'est ce qui compte pour ressentir les proportions. Ma mesure ne propose pas de meilleure reference (meubles 5,7 a 7,8 m par unite, escalier, couverts : tous du meme ordre ; la valeur 7,8 est dans la fourchette des meubles : table 7,4, rambarde 7,8, canape 7,6).
- Code : `SwtorWorldScale` (`JaUnitsPerGr2Unit = 312`, `HumanHeightGr2 = 0,205`, `JaHumanModelHeight = 64`, `MetresPerGr2Unit = 7,8`) integre dans la constante, pas via `--world-scale` (reste un multiplicateur manuel, defaut 1). Aide CLI mise a jour. Doc : section "Echelle du monde" de `swtor-formats.md`.
- Tests : `WorldScaleTests` (7,8 ; 0,205 x 7,8 x 40 = 64 u ; commentaires), et `CollisionLightingTests` (position de lumiere : comparaison avec tolerance 1e-4, le facteur 1/7,8 n'est plus exact en float).
- `war_planworking` re-exporte (base JA) : BSP 53 shaders, 14 612 surfaces, 447 177 index, 43 710 brosses, 267 623 plans ; journal : `BSP_WARNING` 2, `SWTOR_ALPHA_MODE_APPROX`, `SWTOR_COLLISION_MESH_SKIPPED`, `SWTOR_FOG_IGNORED`, `SWTOR_KIND_SKIPPED` 8, `SWTOR_LIGHTS_APPROX`, `SWTOR_WORLD_SCALE` ; aucun avertissement de limite. Apparition : `setviewpos 2543 976 27 180` (l'ancien (2755 ; 1057) x 0,923).

## 2. Comment j'ai cherche la zone "Dark Council"
- `swtor-list-areas --filter` : "conseil", "council", "citadel", "dkr" ne donnent rien ; "kaas"/"dromund" : `dro_main` (4611686019618382666, "Dromund Kaas", 355 rooms), `dro_knight`, `dromund_kaas_expanse`, `stronghold_dromund_kaas`. La Citadelle impériale est dans **`dro_main`** (103 rooms `dro_citadel_*` : `center_wing`, `mandalorian`, `military`, `intel_hq`, `intel_archives`, `secret_library`, `vengean`, `thanaton_chamber`).
- Aucune room, aucun modele ni aucun nom de .gr2 ne contient "council" cote Empire (les seuls "council" des .gr2 sont le Conseil Jedi republicain et un boss de raid). Dans les donnees de `dro_main`, la seule reference est le moteur `...\sith_sorcerer\chapter_3\the_dark_council\loc_thanatons_chamber.mpn` (dans la room `dro_citadel_thanaton_chamber`) et le point `...\the_dark_council\finale_turn_in.spn_p` (room `dro_citadel_center_wing_rm_13`, qui ouvre sur la chambre de Thanaton) ; `area.dat` ne cite que `the_dark_council`. `mapnotes.not` : rien d'autre. La salle a douze trones n'est donc **pas** une zone navigable des donnees (probablement une scene de cinematique non presente dans le catalogue). Les rooms a douze chaises (`center_wing_rm_04`, `rm_10`) sont des salles de reunion/interrogatoire, pas un conseil.
- Choix : la chambre qui porte le nom de la mission "The Dark Council", **`dro_citadel_thanaton_chamber`** (modele `all_arch_imp_citadel_mainroom_meditation_chamber`, artefact sith, 4 podiums `dro_item_sithtomb_podium01`, 6 statues, 6 banderoles, rambardes et colonnes), avec son acces depuis le hall de la Citadelle : `dro_citadel_center_wing_rm_13` -> `center_wing_hall_05` -> `center_wing_lobby` (le hall donne sur l'exterieur de Kaas City). C'est mon interpretation : **ce n'est peut-etre pas la salle que vous attendez**; il n'y a pas de "trones du Conseil" a verifier, regardez plutot les podiums et l'artefact.
- Essai abandonne : avec `center_wing_mainroom` (atrium, 76 475 triangles), `hall_04` et les deux ascenseurs, `BSP_ERROR` : `MAX_MAP_DRAW_INDEXES` 653 769 > 524 288. La chambre de Thanaton seule fait 100 213 triangles de rendu. Retirer ces rooms suffit.

## 3. Export
Commande exacte (depuis le depot, base JA non occupee) :
```
dotnet run --project src/Kotor2Ja.Cli -- export-swtor "dro_main:dro_citadel_center_wing_lobby,dro_citadel_center_wing_hall_05,dro_citadel_center_wing_rm_13,dro_citadel_thanaton_chamber" --name dark_council
```
- Nom : `--name` existait deja ; le pk3 prend le prefixe `swtor_` tout seul (avec `--name swtor_dark_council` on obtenait `swtor_swtor_dark_council.pk3`, que j'ai supprime : c'etait mon fichier). Resultat : carte **`dark_council`**, pk3 `C:\Program Files (x86)\Steam\steamapps\common\Jedi Academy - SWGL_DEV\GameData\base\swtor_dark_council.pk3` (ecrit directement dans la base). `swtor_war_planworking.pk3` conserve et re-exporte.
- Stats : 4 rooms, 375 instances de modele, 69 modeles distincts (0 manquant), 34 lumieres, 85 textures, 0 texture manquante ; 129 701 triangles ; collision : 371 instances via meshes de collision (19 override), 4 en repli ; marchables 7 288 / solides 38 274.
- BSP : 87 shaders, 9 508 surfaces, 250 252 sommets de rendu, **389 139 index** (limite 524 288), 36 051 brosses, 405 372 cotes, 293 104 plans. Pas de `RTX_ESTIMATE_HIGH`, pas de `COLLISION_CAPPED`.
- Journal : `BSP_WARNING` 2 (informatifs), `COLLISION_DEGENERATE` 1, `SWTOR_COLLISION_MESH_SKIPPED` 1, `SWTOR_FOG_IGNORED` 1, `SWTOR_KIND_SKIPPED` 7, `SWTOR_LIGHTS_APPROX` 1, `SWTOR_WORLD_SCALE` 1.
- Ciel : la room `skyscene` est exclue comme d'habitude ; la zone est `is_outdoor`, donc le ciel par defaut (`hevil`) et le soleil sont gardes (pas de pieces "space" ici). Ce n'est pas le ciel de Dromund Kaas, non verifie.

## 4. Correction du point d'apparition (SwtorSceneBuilder.ChooseEntry)
Le choix du niveau de sol prenait le niveau portant le plus de surface marchable de la room. Dans la chambre de Thanaton, un plan de collision geant a -123 m (5 108 m2) et d'autres niveaux de toit faussaient le choix (premiers essais : z = 2430 puis -4895 u, ni l'un ni l'autre le sol). Maintenant : le niveau (tranches de 10 cm) de plus grande surface **parmi ceux a +-4 m de la mediane des Z des sommets visibles de la room** (repli : comportement precedent). `war_planworking` : apparition inchangee a l'echelle pres. Pas de test dedie ajoute pour ce cas ; les 49 tests Swtor.Pipeline passent.

## 5. Verification en jeu a faire par vous (unites JA ; non verifie)
Les coordonnees sont (x, -z, y) x 312 des positions des donnees (la carte est loin de l'origine : y ~ 28 000).
- Carte : `devmap dark_council`
- Apparition (centre du sol de la chambre, cap vers la room suivante) : `setviewpos 895 28058 540 -119`
- Artefact sith au centre : `setviewpos 1429 28086 590 180` (donnees : (4,58 ; 1,86 ; -90,02)) ; la chambre est centree vers (2,7 ; -90) soit `setviewpos 842 28080 540 0`.
- Entree (hall `center_wing_rm_13`, point `finale_turn_in` ~ (-5,5 ; 1,9 ; -89,4)) : `setviewpos -1716 27890 600 0`.
- A regarder : joueur vs. portes / podiums / statues (echelle 7,8 m par unite), plancher sans chute, ciel, lumieres.
Le point (1429 ; 28086) peut tomber sur l'artefact ou un meuble ; deplacez-vous de quelques unites.

## 6. Build et tests
`dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur. `dotnet test Kotor2Ja.sln` : Kotor.Compiler 84, Kotor.Core 183, Kotor.Pipeline 179, Swtor.Pipeline 49, Swtor.Core 104, Kotor2Ja.Cli 41 = **640 reussis, 0 echec**. Rien n'est commite.
