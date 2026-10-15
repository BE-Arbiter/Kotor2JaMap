# Étape 01 — Rendu : rapport d'implémentation

Statut : FAIT_AVEC_RESERVES (voir « Réserves »). `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur. `dotnet test Kotor2Ja.sln` : vert (Cli 8, Compiler 72, Core 120, Pipeline 34 ; aucun test ignoré).

Remarque de méthode : j'ai écrit le code puis les tests (les tests de l'étape ne sont donc pas passés par une phase « rouge » séparée). Les tests ajoutés couvrent tous les cas listés dans l'étape.

## Ce qui a été fait

- **Lightmaps** : `Textures/LightmapRepair.cs` (dilatation 8-voisins itérée, voisins orthogonaux testés avant les diagonaux ; aucun texel valide : gris 128 ; renvoie `(Repaired, NoValidTexel)`). `TextureLookup.Find` (type `Lightmap`) répare, force `HasAlpha = false` et appelle un rappel optionnel `onLightmapRepaired(repaired, none)`. `ModuleSceneBuilder` journalise `LIGHTMAP_REPAIRED` (une ligne de bilan par module) et `LIGHTMAP_NO_VALID_TEXEL` (une ligne par lightmap). La vue (Prévisualiser/Importer/Exporter) lit les mêmes textures de la scène : elle montre les lightmaps réparées sans changement de `PreviewBuilder`.
- **`HasAlpha`** : `TpcDecoder` met `HasAlpha` à vrai seulement si un pixel a alpha < 255 (DXT1, DXT5 et non compressé 32 bits).
- **Alpha de reflet** : `ShaderPlanner.Plan` ignore l'alpha de la texture quand le TXI a `envmaptexture`/`bumpyshinytexture`, sauf `blending punchthrough` ou `decal 1`.
- **`nopicmip` / `nomipmaps`** : écrits au niveau du shader pour `Lightmapped` et `LightmappedCutout` (les tests de texte exact ont été mis à jour).
- **Lumières** :
  - `Mdl/KotorLight.cs`, `KotorNode.Light` ; `MdlReader` lit les contrôleurs des nœuds lumière ;
  - `Scene/SceneLight.cs` ; `ModuleScene` gagne `Lights` et `AmbientColor` (propriétés `init`, valeurs par défaut vide / (0.2,0.2,0.2) : les constructions existantes restent valides) ;
  - `ModuleSceneBuilder` remplit les lumières (position monde = transformation du nœud + position de la pièce, comme `ModuleEntitiesReader`) et l'ambiante (ARE) ;
  - `Compiler/Bsp/PointLight.cs`, `VertexLighting` : propriété `Lights`, fabrique `ForKotor(ambient, lights, shift)` (soleil nul), `Evaluate(position, normal, doubleSided)` (l'ancienne surcharge `Evaluate(normal, doubleSided)` est conservée) ;
  - `CompileInput` gagne le paramètre optionnel `Lights` (liste vide ou nulle = comportement actuel, soleil DAG_1) ; `Compiler.EffectiveLighting` bascule en mode KOTOR dès qu'il y a au moins une lumière ; `BspWriter` passe la position du sommet ;
  - `ModuleExporter.BuildPlan` transmet les lumières à l'échelle JA (position et rayon × `Scale`) et l'ambiante de l'ARE.
- Le journal `NODE_LIGHT` n'est plus « non converti » : message anglais « N light node(s) used for the vertex lighting. » (le test `Real_tar_m02aa_entities` qui lisait l'ancien message a été adapté). Nouveaux codes : `LIGHT_DEFAULTS`, `AMBIENT_DEFAULT`, `LIGHTMAP_REPAIRED`, `LIGHTMAP_NO_VALID_TEXEL`, tous en anglais.

## Décalages du nœud lumière

- Source : xoreos `src/graphics/aurora/model_kotor.cpp` (https://raw.githubusercontent.com/xoreos/xoreos/master/src/graphics/aurora/model_kotor.cpp) : drapeau `kNodeFlagHasLight = 0x0002`, en-tête de lumière sauté (`skip(0x5C)`, TODO non interprété), constantes de contrôleur `kControllerTypeColor = 76`, `kControllerTypeRadius = 88`, `kControllerTypeMultiplier = 140`.
- Source : KotorBlender `io_scene_kotor/format/mdl/reader.py` (https://raw.githubusercontent.com/OpenKotOR/kotorblender/master/io_scene_kotor/format/mdl/reader.py) : en-tête de lumière de 92 octets (0x5C) juste après l'en-tête de nœud de 80 octets (flare_radius f32, 5 définitions de tableau de 12 octets, puis 7 uint32 : priorité, ambient_only, dynamic_type, affect_dynamic, shadow, flare, fading_light). Couleur, rayon et multiplicateur ne sont **pas** dans cet en-tête mais dans les contrôleurs du nœud ; la première valeur de chaque contrôleur sert de valeur statique ; défauts de KotorBlender : rayon 1, multiplicateur 1, couleur blanche.
- Conséquence pour le code : l'en-tête de lumière n'a pas à être lu ; il suffit de décoder les contrôleurs du nœud (même table de clés que position/orientation : 16 octets par clé, type u32 en 0, lignes u16 en 6, index de temps u16 en 8, index de valeur u16 en 10, colonnes u8 en 12) aux types 76 (3 floats RGB), 88 (1 float) et 140 (1 float), uniquement sur les nœuds portant le drapeau `Light` (88 et 140 entrent en collision avec des contrôleurs d'émetteur).
- Valeurs par défaut si un contrôleur manque : blanc, 5 m, ×1, avec `LIGHT_DEFAULTS` (le défaut de rayon suit l'étape ; KotorBlender prend 1).
- Vérification sur de vrais fichiers : `tar_m02aa` 71 lumières, rayons de 2,5 à 337,5 m, multiplicateurs de 1 à 3, couleurs dans [0,1] (par exemple (0,61 ; 0,95 ; 1,0), rayon 2,5, ×2) ; `ebo_m41aa` 54 lumières, rayons de 1,5 à 200 m, multiplicateurs de 1 à 5. Aucun `LIGHT_DEFAULTS` n'est émis sur ces deux modules. Les valeurs sont plausibles (bleu-cyan, ambre), ce qui valide les types de contrôleur et leurs décalages.
- Ambiante : ARE `DynAmbientColor` sinon `SunAmbientColor`, entier 0x00BBGGRR (R = octet de poids faible ; ordre cohérent avec la teinte : `tar_m02aa` donne (0,39 ; 0,47 ; 0,55), un bleu froid ; `ebo_m41aa` : `DynAmbientColor = 0x003C414B` donne (0,29 ; 0,25 ; 0,24), gris chaud). Dans les deux modules `SunAmbientColor` vaut 0 et `DynAmbientColor` est renseignée. Limite connue : si `DynAmbientColor` existe mais vaut 0, elle est utilisée telle quelle (noir).

## Mots-clés vérifiés dans le renderer

Renderer réellement utilisé : `rdsp-vulkan_x86.dll` (option CMake `BuildSPVulkan`), source `C:\Users\lambe\source\repos\OpenJK-SWGL\code\rd-vulkan\tr_shader.cpp`, fonction `ParseShader` (niveau shader, pas niveau étape) :

- ligne 2714 : `else if (!Q_stricmp(token, "nomipmaps"))` puis `shader.noMipMaps = 1; shader.noPicMip = 1;`
- ligne 2721 : `else if (!Q_stricmp(token, "nopicmip"))` puis `shader.noPicMip = 1;`
- lignes 1287-1291 : lors de la lecture de `map`, les drapeaux d'image `IMGFLAG_MIPMAP` / `IMGFLAG_PICMIP` ne sont ajoutés que si ces drapeaux ne sont pas posés ; les mots-clés doivent donc précéder les étapes, ce que fait `ShaderPlanner.Write` (juste après l'accolade ouvrante).
- Le renderer par défaut `rd-vanilla/tr_shader.cpp` (lignes 2301 et 2308) les accepte aussi.

## Zone blanche de Taris : mesures (remplace la section « cause » du premier rapport)

Le premier rapport concluait à tort que `depthFunc equal` échouait aux frontières de pièces ; cette conclusion est retirée (elle n'a jamais été prouvée). Mesures de l'orchestrateur en jeu, après le correctif de découpe :

- La zone blanche reste. Un rayon lancé depuis le point d'apparition montre que les murs blancs sont des surfaces AVEC lightmap : `lts_pwall01i__m02aa_09b_lm0`, `lts_nwall04i__m02aa_09b_lm3` et `lts_nwall04i__m02aa_09a_lm0`.
- Leurs lightmaps sont claires (p90 = 255).
- Le moteur rdsp-vulkan applique un facteur effectif d'environ ×2,8 à texture × lightmap (mesuré : pixel ≈ 14 + 319·L). La saturation vient de là.
- La luminosité des lightmaps reste à 1 par défaut. La cause n'est plus recherchée (décision de l'orchestrateur).

La correction de l'alpha de reflet (152 `LightmappedCutout` avant, 0 après sur `tar_m02aa`) reste justifiée en soi (découpe abusive), mais elle n'est pas la cause de la zone blanche.

## Avant / après : nombre de shaders par type

Exports faits avec la CLI Release dans des dossiers temporaires (`%TEMP%\exp_base` = commit `24da9d0`, `%TEMP%\exp_new` = cet arbre de travail), `jaBaseDir` et `workDir` temporaires. Le dossier `GameData\base` du jeu n'a pas été touché. Classement par le texte du bloc shader.

| Module | Version | Total | Lightmapped | LightmappedCutout | Blend | Additive | Unlit |
|---|---|---|---|---|---|---|---|
| tar_m02aa | avant | 262 | 98 | 152 | 0 | 0 | 12 |
| tar_m02aa | après | 262 | 250 | 0 | 0 | 0 | 12 |
| ebo_m41aa | avant | 329 | 46 | 262 | 1 | 0 | 20 |
| ebo_m41aa | après | 329 | 304 | 4 | 1 | 0 | 20 |

Les 4 `LightmappedCutout` restants sur `ebo_m41aa` sont de vraies textures à alpha sans reflet (ou en `punchthrough`).

## TGA de lightmap du staging

TGA de `textures/kotor/lm/` du staging (`%TEMP%\exp_*\work\<module>\pk3`), alpha différent de 255 :

| Module | Version | Fichiers | Avec alpha ≠ 255 |
|---|---|---|---|
| tar_m02aa | avant | 72 | 19 |
| tar_m02aa | après | 72 | **0** |
| ebo_m41aa | avant | 97 | 23 |
| ebo_m41aa | après | 97 | **0** |

Journal après : `LIGHTMAP_REPAIRED` (1 ligne par module : `tar_m02aa` 19 lightmaps, 1966 texels ; `ebo_m41aa` 23 lightmaps, 280 texels).

## Écart avec les données de l'orchestrateur

Les trois lightmaps citées sont bien réparées (`m12aa_01f_lm11` 4×4 : 8 texels invalides sur 16 ; `m12aa_01f_lm3` : 4 ; `m12aa_01k_lm5` : 28), mais **`lm11` n'est pas entièrement invalide** : il reste 8 texels valides, donc pas de `LIGHTMAP_NO_VALID_TEXEL` sur ces modules. Ce cas n'est couvert que par le test unitaire `Repair_all_invalid_gives_gray`.

## Éclairage : effet mesuré sur les surfaces sans lightmap

Moyenne des couleurs de sommet cuites (échelle 0-255, ×4 de surbrillance appliqué, avant/après) :

- `tar_m02aa` : `lts_nums` 128 → 192, `lts_lite08` 129 → 179, `lts_pwall01i` (lightmap manquante) 117 → 176, `lts_glass01` 128 → 254 (près des lumières ambre).
- `ebo_m41aa` : ciels (`leh_*sky`) 112 → 67, `leh_scre01` 144 → 183, `leh_light02` 112 → 165.

Les valeurs suivent les lumières réelles de chaque pièce au lieu d'un soleil uniforme. Pas de noir ni de saturation généralisée.

## Tests ajoutés / modifiés

- `LightmapRepairTests` (4) : `Repair_fills_invalid_from_nearest`, `Repair_all_invalid_gives_gray`, image valide intacte, propagation multi-lignes.
- `ShaderPlannerTests` : `Envmap_alpha_is_not_cutout`, `Punchthrough_still_cutout`, `Envmap_with_decal_keeps_alpha`, `Lightmapped_shader_has_nopicmip` ; deux tests de texte exact mis à jour (`nopicmip`, `nomipmaps`).
- `TpcDecoderTests.Dxt5_opaque_has_no_alpha`.
- `VertexLightingTests` : `Point_light_attenuates` (1, 0,5, 0, 0 à 0/50/100/150 unités), `Point_light_respects_facing_and_double_sided`, `Compile_input_lights_switch_to_kotor_mode`.
- `ModuleSceneBuilderTests` : `Ambient_colour_is_read_as_bgr`, `[SkippableFact] Real_ebo_m41aa_no_invalid_lightmap_texel`, `[SkippableFact] Real_tar_m02aa_lights_read` (71 lumières).
- `ModuleEntitiesReaderTests.Real_tar_m02aa_entities` : l'expression régulière suit le nouveau message `NODE_LIGHT`.

## Réserves

1. Cause de la zone blanche de Taris établie par sondage des données et du texte des shaders, non confirmée par un rendu (jeu non lancé) ; hypothèse sur l'échec de `depthFunc equal` non prouvée dans la source du renderer.
2. L'éclairage par sommet utilise les lumières telles que lues (couleur × multiplicateur × atténuation linéaire, somme puis écrêtage à 1 avant la division par 2^`OverbrightShift`). Aucune calibration visuelle ; une lumière de 337 m de rayon (`M02aa_10a`) est présente sur `tar_m02aa`.
3. Pas de changement de `PreviewBuilder` : la vue des surfaces sans lightmap ne montre pas l'éclairage par sommet (elle ne l'a jamais fait), seulement les lightmaps réparées.
4. Un module sans nœud lumière conserve l'ancien faux soleil (liste vide = comportement actuel).
5. L'ambiante est utilisée telle quelle même si `DynAmbientColor` vaut 0.
6. Pas de commit (consigne).

## Correction 1

Statut : FAIT_AVEC_RESERVES. Commandes : `dotnet build Kotor2Ja.sln` (0 avertissement, 0 erreur) puis `dotnet test Kotor2Ja.sln` : vert (Cli 8, Compiler 76, Core 130, Pipeline 34 ; aucun test ignoré, donc les tests sur jeu installé ont bien tourné). Cette section remplace les réserves 1, 2, 4 et 5 de la section « Réserves » ci-dessus. Les tests ont été écrits en même temps que le code (pas de phase rouge séparée).

### Décisions de l'orchestrateur appliquées

- **Zone blanche (écart 1, Important 2)** : la section « cause » a été remplacée par les mesures de l'orchestrateur (surfaces avec lightmap claire, facteur ×2,8 du moteur) ; la conclusion erronée sur `depthFunc equal` est retirée. Aucune nouvelle recherche de cause. Luminosité des lightmaps inchangée (1).
- **Surbrillance des couleurs de sommet (Important 1)** : `VertexLighting.DefaultOverbrightShift` passe de 2 à 0 (`r_mapOverBrightBits` = 0 par défaut dans OpenJK-SWGL `rd-vulkan/tr_init.cpp:989`, décalage = max(0, r_mapOverBrightBits − overbrightBits) = 0 dans `tr_bsp.cpp:132`) ; les couleurs de sommet sont donc dans la même unité que les texels de lightmap. Documentation du record mise à jour. Tests : `Default_shift_is_zero_like_swgl_and_can_be_overridden` ; `Overbright_shift_divides_stored_colour` utilise désormais explicitement un décalage 2. Le plafond par lumière n'est pas ajouté : la somme est écrêtée à [0,1] par canal (`Point_light_sum_is_clamped_per_channel`) et la lumière de 337 m reste telle que lue.
- **Module sans nœud lumière (écart 2)** : `ModuleScene` gagne `SunDiffuseColor` et `AreaLightingKnown` ; `ModuleSceneBuilder.ReadAreaLighting` (remplace `ReadAmbient`) lit `DynAmbientColor`/`SunAmbientColor` et `SunDiffuseColor` (BGR). `ModuleExporter.BuildPlan` : sans lumière et ARE lisible = ambiante ARE + soleil directionnel de couleur `SunDiffuseColor` (direction par défaut) ; ARE illisible = défauts du compilateur ; avec lumières = ambiante ARE + lumières, sans soleil. Tests : `Module_without_light_node_uses_area_ambient_and_sun_diffuse`, `Module_without_light_node_and_unreadable_area_keeps_the_compiler_defaults`, `Lights_position_and_radius_are_scaled_and_ambient_comes_from_the_area` (ModuleExporterTests), `Area_lighting_reads_ambient_and_sun_diffuse_as_bgr`, `Area_without_colours_is_known_with_default_ambient_and_black_sun`.
- **`ambient_only` (Important 5)** : l'en-tête de lumière (92 octets après l'en-tête de nœud ; `ambient_only` = 2e uint32 des 7 finaux, décalage 68 dans l'en-tête, KotorBlender `reader.py`) est lu par `MdlReader` (`KotorLight.AmbientOnly`), transmis par `SceneLight.AmbientOnly` et `PointLight.AmbientOnly`. Une lumière ambient_only s'ajoute à l'ambiante (couleur × multiplicateur), sans terme directionnel ni atténuation. Tests : `Light_node_reads_controllers_and_ambient_only` (MDL binaire synthétique), `Ambient_only_light_adds_to_ambient_without_direction_or_attenuation`, `Invalid_lights_are_skipped_individually_with_LIGHT_INVALID` (le drapeau traverse la scène).
- **Garde `LIGHT_INVALID` / `ToByte` (Important 5, Minor 5)** : une lumière dont le rayon ou le multiplicateur est non fini ou ≤ 0, ou dont la couleur contient un NaN, est ignorée avec `LIGHT_INVALID` (message anglais, une ligne par lumière) ; un contrôleur de lumière hors limites ne fait plus ignorer toute la pièce : seule la lumière est sautée (`KotorLight.Invalid`). `VertexLighting.Evaluate` applique la même garde et `ToByte` renvoie 0 pour NaN. Le compte `NODE_LIGHT` ne compte que les lumières utilisées. Tests : `Invalid_lights_are_skipped_individually_with_LIGHT_INVALID` (9 cas invalides + 2 valides), `Light_controller_out_of_bounds_marks_only_the_light_invalid`, `Invalid_lights_are_ignored_and_ToByte_is_safe_for_nan`.

### Constats de relecture

- **Important 3 (décalages et types de contrôleur non prouvés)** : `Real_tar_m02aa_lights_read` vérifie maintenant 71 lumières, aucun `LIGHT_DEFAULTS`, aucun `LIGHT_INVALID`, des rayons variés et une lumière réelle connue : `M02aa_03a` en (88,83 ; 132,30 ; 1,5), couleur (155/255 ; 242/255 ; 1), rayon 2,5 m, multiplicateur 2. En plus, `Light_node_reads_controllers_and_ambient_only` lit un MDL binaire synthétique et vérifie couleur, rayon et multiplicateur aux types 76/88/140.
- **Important 4 (câblage non testé)** : échelle (position et rayon × `Scale`, multiplicateur inchangé), filtre des lumières invalides, repli sans lumière et repli ARE illisible : tests de `ModuleExporterTests` ci-dessus. `LIGHTMAP_NO_VALID_TEXEL` via le chemin réel (`ModuleSceneBuilder` + `TextureLookup`, TGA 2×2 d'alpha 0) : `Lightmap_without_valid_texel_is_logged_and_has_no_alpha` (gris 128, alpha 255, `HasAlpha` faux). `HasAlpha` faux pour une lightmap réparée dont la source avait de l'alpha, tandis que la diffuse garde son alpha : `Repaired_lightmap_has_no_alpha_flag_even_if_source_had_alpha`.
- **Minor 1 (raccourci de distance nulle)** : `Point_light_attenuates` utilise des sommets éloignés (25, 50, 100, 150 unités sur l'axe, un point hors axe à 50, une normale inclinée à 60°) : le produit scalaire est réellement évalué.
- **Les autres constats Minor** ne sont pas traités (consigne).

### Valeurs mesurées sur le jeu (sondage, jeu non lancé)

`tar_m02aa` : 71 lumières lues, aucune ambient_only, aucun `LIGHT_DEFAULTS`/`LIGHT_INVALID` ; ambiante (0,39 ; 0,47 ; 0,55) ; `SunDiffuseColor` = 0 (soleil noir si le module n'avait aucune lumière).

### Réserves

1. Aucun essai visuel : l'effet du décalage 0 et de l'écrêtage n'est pas confirmé en jeu. Une lumière de 337,5 m de rayon (`M02aa_10a`) et des multiplicateurs jusqu'à 3 saturent toujours les sommets à portée (écrêtage à 1) ; aucun plafond par lumière n'a été ajouté.
2. Avec un décalage 0, les défauts du compilateur (`VertexLighting.Default`, soleil 0,75 + ambiante 0,45) saturent les faces tournées vers le soleil : ils servent seulement quand l'ARE est illisible.
3. `DynAmbientColor` à 0 reste utilisée telle quelle (non traité, Minor 7).
4. Un module dont toutes les lumières sont invalides retombe sur le cas « sans lumière » (ambiante et soleil de l'ARE).
5. Pas de commit.
