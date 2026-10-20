# Rapport MAP1 : .map d'edition avec la geometrie visible texturee

Statut : DONE_WITH_CONCERNS (pas de commit, a la charge de l'orchestrateur).

## Probleme

Le `.map` ecrit a cote du `.bsp` ne contenait que la collision (`textures/common/caulk`, `textures/system/clip`), les entites et les sous-modeles : ouvert dans NetRadiant on ne voyait pas la carte.

## Decision

- **Deux fichiers.** Le `.map` du pk3 (`maps/<carte>.map`, collision + entites) est inchange, octet pour octet : le pk3 de jeu et le BSP ne bougent pas. Le `.map` d'edition (collision + entites + geometrie visible) est ecrit hors du pk3, dans le dossier de travail, parce qu'il est gros (jk_01narshadda : 7,5 Mo contre 2,9 Mo ; KOTOR danm14aa : 49,5 Mo contre 25,7 Mo) et inutile au jeu.
- **Emplacement** : `<travail>\<carte>\edit\base\maps\<carte>.map`, avec `edit\base\textures\...` (copie des .tga du staging + un `common/caulk.tga` gris) et `edit\base\shaders\<carte>.shader`. Le dossier `edit` est un « chemin du moteur » autonome (structure `base\...` d'un dossier de jeu JA) : on le donne tel quel a l'editeur (ou on ajoute `edit\base` en chemin de ressources supplementaire). Verifie avec `q3map2` de NetRadiant (`-game ja -fs_basepath ...\edit -fs_game base`) : les textures sont trouvees (204 shaders pour jk_01narshadda, seuls 3 noms non visuels manquent : `jk/mover_nonsolid`, `jk/trigger_hurt`, `system/clip`).
- **Option** : `ExportOptions.MapVisuals` (defaut `true`), CLI `export-ja --map-visuals on|off`. `off` : aucun dossier `edit` (l'ancien est supprime), BSP et pk3 identiques.
- **Geometrie** : `func_group` portant `"_kotor_visual" "1"`, place en fin de fichier, separe du worldspawn (qui ne garde que la collision). Un prisme triangulaire (5 faces) par triangle de rendu (`SurfaceInput`) : la face visible est dans le plan exact de la surface, normale selon l'enroulement du triangle, epaisseur 2 u derriere ; les 4 autres faces sont `caulk`. Surface double face : les deux grandes faces portent la texture. Pas de fusion de triangles (simple, et la texture reste fidele par triangle).
- **Nom de texture** : la texture de base (`textures/jk/01wbolts`), pas le shader a lightmap (`..__lm`, `__jk_light`) que l'editeur ne sait pas afficher. `SurfaceInput.VisualTexture` (+ `VisualTextureSize`) est renseigne par `ModuleExporter.BuildPlan` ; il n'est lu que par le `.map` d'edition.
- **Prefixe `textures/`** : constat fait avec `q3map2` (`Couldn't find image for shader textures/textures/common/caulk`) : Radiant et q3map2 ajoutent eux-memes `textures/`. Le `.map` d'edition ecrit donc `jk/01wbolts` et `common/caulk` (y compris pour la collision et les portes). Le `.map` du pk3 garde `textures/common/caulk` (inchange par contrainte ; il n'est pas pense pour l'editeur).
- **Format de mappage** : Quake 3 classique, pas `brushDef` ni Valve 220. Raisons : q3map2 detecte un seul format par fichier (« detected brushType = QUAKE (Axial Projection) ») et la collision existante est en format classique ; un editeur configure pour JA lit toujours ce format, pas necessairement les deux autres. Projection axiale (`TextureAxisFromPlane` de q3map2) : on resout exactement decalage, rotation, echelles X et Y a partir des UV des 3 sommets (formule de q3map2 reecrite dans les tests, independante du code). Exact pour tout mappage sans cisaillement (murs, sols, plafonds, panneaux, miroirs). Limite : un triangle aux UV cisailles (relief organique) n'est qu'approche (meilleur ajustement, decalage au barycentre).
- **Bornes** : 300 000 brushes visuels maximum (`MAP_VISUAL_CAPPED`, les plus petits triangles sont retires d'abord) ; triangles d'aire quasi nulle ou en aiguille (hauteur < 0,05 u) ignores (sinon plan degenere a l'arrondi, rejete par q3map2).
- **Modeles** : inchange. Les `misc_model_*` restent des entites avec leur cle `model` ; leur geometrie n'est pas dessinee dans l'editeur.

## Mesures (exports CLI dans un dossier temporaire, GameData\base jamais touche)

| Carte | .map d'edition | lignes | brushes visuels | textures (hors caulk) | pk3 .map | dossier edit |
| --- | --- | --- | --- | --- | --- | --- |
| jk_01narshadda | 7 475 589 octets | 96 810 | 7 852 | 103 | 2 905 538 | 16 Mo |
| danm14aa (KOTOR) | 49 521 936 octets | 565 033 | 38 785 | 20 | 25 666 465 | 66 Mo |

Pas des centaines de milliers de lignes pour JK : 01narshadda n'a que ~7 900 triangles de rendu utiles (97 000 lignes dont ~55 000 pour la partie visuelle). Toutes les textures citees par le groupe visuel existent en `.tga` dans `edit\base\textures` (verifie par script : 0 manquante) ; jk_01narshadda : textures dominantes `jk/00wbig3` (630 triangles), `jk/01f01` (484), `jk/00wbig2` (462).
Qualite du mappage : jk_01narshadda 108 triangles sur 7 852 depassent 0,2 % de texture d'ecart ; danm14aa 29 348 sur 38 785 (7 700 depassent 5 %), presque tous des arbres, rochers et lits de riviere (`lda_leaf02`, `lda_rock04/06`, `lda_bark04`, `lda_riverbed01`).

## Preuves : BSP et pk3 inchanges

- Hash SHA-256 du `.bsp`, du `.map` du pk3 et du `.pk3`, avant (build du commit 750e879) et apres, sur `export-ja 01narshadda --game jk` et `export-ja danm14aa` : **identiques** (6 fichiers, `diff` vide). Fichiers : `jk_01narshadda.bsp` 3607b011..., `jk_01narshadda.map` ad9f3867..., `danm14aa.bsp` 106566ec..., `danm14aa.map` 683133bd..., pk3 8b26303c... et d24b81dd....
- Tests : `Compile_BspHashStaysTheGoldenOne_WhenAVisualMapIsRequested` (hash dore du BSP de `SubModelTests`), `Compile_WithVisualMap_LeavesBspAndPk3MapUnchanged_AndWritesTheEditMap`, `WithoutVisualSurfaces_MapIsByteIdenticalToTheCollisionOnlyMap`, `Map_visuals_off_writes_no_edit_folder_and_leaves_the_bsp_and_pk3_map_byte_identical`, `Export_writes_the_textured_edit_map_next_to_the_work_files_and_not_in_the_pk3`.

## TDD

RED : tests ecrits d'abord (`tests/Kotor.Compiler.Tests/VisualMapWriterTests.cs`), `dotnet test tests/Kotor.Compiler.Tests` : `error CS0117: 'SurfaceInput' ne contient pas de definition pour 'VisualTexture'`, `CS1501: Aucune surcharge pour la methode 'Write' n'accepte les arguments 5`, `CS0103: Le nom 'VisualMapWriter' n'existe pas` (echec de compilation). Verification de sensibilite : un signe inverse dans l'ajustement de l'echelle T fait echouer 5 tests sur 14.
GREEN : 105/105 pour Kotor.Compiler.Tests. Deux corrections ensuite, decouvertes en passant le `.map` dans `q3map2` (pas ecrites en RED d'abord, les tests ont ete ajustes dans la foulee) : prefixe `textures/` ; aiguilles degenerees (`Needle_TooThinForTheMapPrecision_IsSkipped`, `EditMap_DoorBrushesToo_UseNamesWithoutTexturesPrefix`).
Tests du compilateur : prisme valide (5 plans, 6 sommets, normales vers l'exterieur, epaisseur 2 > 0, chaque plan porte une vraie face), texture reelle sur la face visible et `common/caulk` ailleurs, dimensions (sol 100 x 100, z de -2 a 0), UV reproduites par la projection de q3map2 (sol tourne, mur axial, mur oblique, plafond avec miroir), double face, repli sur le nom du shader, triangles degeneres, plafond de brushes (garde les plus grands), noms de textures, groupe `_kotor_visual` separe de la collision (worldspawn sans brush visuel). Tests du pipeline : `Surfaces_carry_the_base_texture_and_its_size_for_the_edit_map`, export vers le dossier `edit`, pk3 sans `_kotor_visual`.
Final : `dotnet test Kotor2Ja.sln` entier, 0 echec : Kotor.Compiler 105, Jk.Core 50, Kotor.Core 225, Kotor.Pipeline 199, Swtor.Pipeline 84, Swtor.Core 132, Kotor2Ja.Cli 55, Jk.Pipeline 303 (aucun verrou de DLL, la solution a ete lancee d'un bloc).

## Fichiers

Crees : `src/Kotor.Compiler/Bsp/VisualMapWriter.cs`, `tests/Kotor.Compiler.Tests/VisualMapWriterTests.cs`, ce rapport.
Modifies : `src/Kotor.Compiler/Bsp/MapWriter.cs` (surcharge `TextWriter`, noms d'edition), `src/Kotor.Compiler/Compiler.cs` (`WriteEditMap`), `src/Kotor.Compiler/CompileInput.cs` (`VisualTexture`, `VisualTextureSize`, `VisualMapPath`), `src/Kotor.Pipeline/ModuleExporter.cs`, `src/Kotor.Pipeline/PipelineSteps.cs` (dossier `edit`), `src/Kotor.Core/Export/ExportOptions.cs` (`MapVisuals`), `src/Kotor2Ja.Cli/Commands/ExportJaCommand.cs` (`--map-visuals`), `README.md` (section « Editing the map »), `tests/Kotor.Core.Tests/Export/ModuleExporterTests.cs`.
Journal : `MAP_VISUAL_CAPPED`, `MAP_VISUAL_UV_APPROX`, `MAP_VISUAL_WRITE_FAILED` (une ecriture en echec du `.map` d'edition est un avertissement, jamais un echec d'export).

## Limites et preoccupations

1. **Pas ouvert dans l'interface de NetRadiant** (aucun pilotage d'interface graphique ici). Valide par `q3map2` de l'installation NetRadiant (`C:\jka_maps\netradiant`) : le fichier est lu en format QUAKE, 12 355 brushes du monde, textures trouvees, 31 surfaces degenerees retirees. A faire juger : ouvrir `edit\base\maps\jk_01narshadda.map`.
2. **q3map2 plante (segfault) pendant le `-meta`** sur ce `.map`, mais aussi sur le `.map` de collision seul du pk3 de l'etat precedent (4 503 brushes) : probleme anterieur de la collision, sans lien avec cette tache. La recompilation du `.map` d'edition n'est donc pas garantie.
3. Surfaces a exactement 45 degrees : l'axe de projection choisi par l'editeur peut etre l'autre (egalite a l'arrondi pres), la texture est alors decalee sur ces faces.
4. Mappage approche sur les maillages organiques KOTOR (voir chiffres) ; `brushDef` ou Valve 220 les rendrait exacts mais exigent un fichier d'un seul format (collision comprise) et un jeu d'editeur qui le lit : non fait, a decider.
5. Le dossier `edit` copie les textures (16 Mo JK, 66 Mo pour `danm14aa`) a chaque export ; desactivable avec `--map-visuals off`. Pas de cle `mapVisuals` dans `kotor2ja.config.json` ni de case dans l'application (option d'export et CLI seulement).
6. Le `.map` du pk3 garde `textures/common/caulk` (nom invalide pour q3map2, qui y ajoute `textures/`) : volontairement non modifie.
7. Les modeles (`misc_model_*`) ne sont pas dessines dans l'editeur ; les shaders de portes/triggers (`jk/mover_nonsolid`, `jk/trigger_hurt`, `system/clip`) n'ont pas de .tga (non visuels).

## Correctif de revue

Changements :
- `src/Kotor.Pipeline/PipelineSteps.cs` : le `Directory.Delete(editDir)` inconditionnel est supprime. Avec `--map-visuals on`, le `.map` d'edition existant est renomme en `<carte>.<aaaammjj-hhmmss>.map.bak` (5 sauvegardes au plus, `PipelineSteps.MaxEditBackups`, les plus anciennes sont retirees) avant l'ecriture du nouveau. Si la sauvegarde echoue (fichier verrouille), le nouveau `.map` n'est pas ecrit et l'avertissement `MAP_VISUAL_WRITE_FAILED` est journalise ; toute erreur d'E/S du dossier `edit` (sauvegarde, ecriture du `.map`, copie des textures) est un avertissement, jamais `EXPORT_IO_ERROR`. Avec `--map-visuals off`, rien n'est cree, modifie ni supprime dans `edit`.
- `src/Kotor.Compiler/Bsp/VisualMapWriter.cs` : `Valid` ecarte un triangle dont un UV est NaN/infini (compte dans `DegenerateTriangles`, donc dans le journal `Edit map: ... degenerate triangle(s) skipped`).
- `README.md` (« Editing the map ») : regeneration a chaque export avec sauvegarde, modifications d'editeur a garder ailleurs, groupe `_kotor_visual` qui double la geometrie si on recompile tel quel, plantage de `q3map2 -meta` (recompilation non garantie), `MAP_VISUAL_WRITE_FAILED`.

Tests (RED d'abord) :
- `VisualMapWriterTests.NonFiniteUv_TriangleIsSkippedAndCounted` (NaN, infini) : RED `echec : 2, reussite : 0` sans le filtre, GREEN `reussite : 2`.
- `ModuleExporterTests` : `Reexport_backs_up_the_existing_edit_map_instead_of_deleting_it_and_caps_the_backups`, `Locked_edit_map_gives_a_warning_and_the_export_still_succeeds` (fichier ouvert en `FileShare.None`), `Unusable_edit_folder_gives_a_warning_and_the_export_still_succeeds` (« edit » est un fichier), `Map_visuals_off_leaves_the_user_edited_map_untouched`, et `Map_visuals_off_...` adapte (le dossier `edit` n'est plus retire). RED : erreur de compilation `'PipelineSteps' ne contient pas de definition pour 'MaxEditBackups'` ; GREEN : `ModuleExporterTests` 22/22.

Commandes et sorties :
- `dotnet test Kotor2Ja.sln` entier, 0 echec : Kotor.Compiler 107, Jk.Core 50, Kotor.Core 229, Kotor.Pipeline 199, Swtor.Pipeline 84, Swtor.Core 132, Kotor2Ja.Cli 55, Jk.Pipeline 303 (aucun verrou de DLL).
- `Kotor2Ja.Cli.exe export-ja 01narshadda --game jk --config C:/Users/lambe/AppData/Local/Temp/jkedit/cfg.json` (jaBaseDir temporaire, GameData\base jamais touche), en `on` puis en `--map-visuals off` : SHA-256 du pk3 `8b26303c73fc...` dans les deux cas, identique au hash de reference de ce rapport. Les golden tests du BSP (`Compile_BspHashStaysTheGoldenOne_...`) restent verts ; KOTOR/SWTOR non touches (suites vertes).
- Limite : le `.bak` n'est pas teste par une vraie session d'editeur (verrou simule par `FileShare.None`).
