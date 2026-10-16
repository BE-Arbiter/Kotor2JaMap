# T7 - Plantage du jeu au chargement de swtor_war_planworking (rapport)

## Preuves
- Journal d'evenements Windows : 4 plantages `SWGL_SP.x86_64.exe` (14:59, 15:02, 15:04, 15:05), tous `VCRUNTIME140.dll` offset `0x1e2f0`, exception `0xc0000005`, ecriture a l'adresse `0x40`.
- Dump `%LOCALAPPDATA%\CrashDumps\SWGL_SP.x86_64.exe.52948.dmp` (14:59) analyse hors jeu : le dernier message de la console en memoire est `...found 193 VBO surfaces`, juste apres la construction du VBO du monde.
- Pile du thread fautif (adresses de retour en RVA de `rdsp-vulkan_x86_64.dll`, desassemblee avec dumpbin) : `R_PreparePT` (0xda493, rendu RTX, `r_rtx 1` actif sur cette machine) -> `vk_rtx_estimate_geometry` (0xe3ee0) -> `memset` (VCRUNTIME). Registres : rax = 0 (resultat de `calloc` nul), r8 = 0x242D4B5C30 = 155 378 736 176 octets = 747 013 160 primitives x 208 octets.
- Les logs `Can't find textures/swtor/..._rmos.tga` etc. ne sont pas la cause : ce sont les recherches de textures PBR facultatives (elles existent aussi pour KOTOR).

## Cause racine
`vk_rtx_estimate_geometry_recursive` (OpenJK-SWGL, `code/rd-vulkan/rtx/vk_rtx_bsp.cpp`) additionne, pour **chaque entree de leafsurface** de chaque feuille, les triangles de la surface (`tess.numIndexes / 3`), sans dedoublonner (alors que la collecte ulterieure ignore les doublons via `surf->added`). Le total est donc environ (feuilles listant la surface) x (triangles). Le moteur fait ensuite `calloc(total, 208)` puis `memset` : si le calloc echoue (limite de commit, ici ~109 Gio), le `memset` ecrit a NULL.
Notre BSP : 56 290 feuilles, surfaces fusionnees par shader dans l'ordre des instances (donc dispersees dans toute la carte, ~723 triangles chacune) ; chaque surface recoupe en moyenne 4 768 feuilles (jusqu'a 51 628 sur 56 290) : estimation de 747 millions de primitives = 145 Gio. Les autres hypotheses ont ete ecartees : lumps coherents (indices, plages de feuilles, noeuds), maxima par surface 1 199 sommets / 4 521 indices sous les limites du moteur (1200 / 7200), aucun lump hors profil.
(Estimation de la meme formule sur des BSP KOTOR existants : danm16 20 Gio, tar_m02aa 15 Gio, ebo_m40ad 53 Gio, korr_m35aa 89 Gio ; ils tiennent sous le commit de cette machine, sans marge pour les plus gros.)

## Difference 19,4 Mo / 83,6 Mo
Aucune : 19 363 752 octets est la taille du pk3 compresse (zip) ; 83 622 594 est la taille decompressee des 53 fichiers (bsp 39,6 Mo + textures TGA). T5 donnait la seconde.

## Correctif (compilateur / pipeline)
- `RenderSurfaces.FromInputs(..., compact)` : mode compact (`CompileInput.CompactSurfaces`, `ModuleScene.CompactRenderSurfaces`, active par `SwtorSceneBuilder` uniquement) : triangles d'un meme shader tries par code de Morton, puis bissection tant que la surface depasse les limites moteur ou que (triangles x feuilles recoupees par sa boite) > `MaxLeafTriangleCost` (1000). KOTOR : sortie inchangee (mode desactive par defaut, test dedie).
- `Limits.MaxRtxPrimitives` (profil swgl : 500 millions, vanilla : sans limite) et `LimitCheck.EstimateRtxPrimitives / CheckRtxEstimate` : l'export echoue proprement (`RTX_PRIMITIVE_ESTIMATE`) au lieu de planter le jeu ; avertissement `RTX_ESTIMATE_HIGH` au-dessus de 50 millions (~10 Gio). Le seuil de 500 millions est un choix (747 M plante, 460 M charge) et depend du commit de la machine.
- Resultat war_planworking : estimation 747 M -> 6,2 M primitives (1,2 Gio) ; 14 669 surfaces (au lieu de 206), 310 k sommets de rendu (204 k avant), 447 177 indices (inchanges). Les 14 669 surfaces restent sous `MAX_MAP_DRAW_SURFS` (131 072).
- Tests : +3 (Kotor.Compiler.Tests : compact regroupe et reduit le cout, mode par defaut inchange, seuil RTX). `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur ; `dotnet test` : 84 + 183 + 43 + 179 + 104 + 41 = **634 reussis, 0 echec** (T5 : 631).

## Verification en jeu
Faite une seule fois, selon les regles : aucun jeu ne tournait, commande de `START.ps1` + `+devmap swtor_dbg_war` (pk3 de test `swtor_swtor_dbg_war.pk3`, supprime ensuite). Apres 120 s le processus tournait, repondait, le journal montrait `client active, server running (game)` sans plantage ni evenement WER. Ensuite seulement j'ai ferme mon propre processus (PID 17872). Aucune capture d'ecran du rendu : le rendu n'a pas ete juge.
Le pk3 `swtor_war_planworking.pk3` de la base a ete regenere avec le correctif (meme commande d'export) mais **non relance** sous ce nom ; la carte testee est identique (nom de map different).

## Notes
- Le moteur est a corriger idealement aussi (dedoublonner l'estimation par `surf->added`) ; non fait, hors perimetre (autre depot).
- `docs/.../reports/img/` (captures de menus) supprime.
