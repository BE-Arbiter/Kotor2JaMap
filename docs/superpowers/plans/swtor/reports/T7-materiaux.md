# T7 - Materiaux : cartons blancs de la salle du Conseil noir

**Rien n'a ete verifie en jeu** (aucune capture, jeu non lance, aucun processus du jeu touche). Les captures 4.webp/5.webp ont ete lues ; la cause est deduite des donnees et des shaders exportes.

## Causes (room `station_dark_council_chamber`, 62 materiaux)
1. **`standard_light_fog_cone`** (gr2 `standard_light_fog_cone_01`, 2 208 triangles, double face) : `Derived=AnimatedVFX`, `AlphaMode=Add`. Sa DiffuseMap `fx_cloud_tile_02` est un nuage gris (rgb moyen 130, alpha 0..255, moyenne 173). En SWTOR l'aspect (halo doux, bord fondu) vient de masques animes (`fx_light_mask_01`), d'un fresnel et de `Layer1HSLA` (luminosite 0,3). Nous exportions `blendFunc GL_ONE GL_ONE` avec la texture dont l'alpha etait **mis a 255** : chaque cone devenait une surface plate grise-blanche, aux aretes nettes (celles de la geometrie), ajoutee sur le decor. C'est le carton blanc dentele derriere les trones et autour des murs.
2. **`fx_transparent`** (gr2 `fx_dro_single_imp_logo`, 532 triangles) : texture 2x2 entierement transparente (rgb 0, alpha 0), `AlphaMode=None`. Notre pipeline forcait l'alpha a 255 : carton **opaque** a la place d'un objet invisible.
3. **`util_blue_hidden`, `util_green_hidden`, `occluder_wall`** (`Visibility=Hidden`) : surfaces utilitaires jamais dessinees par le jeu, que nous dessinions en bleu / vert / rose opaques (textures `art/defaultassets/*`). Passes de 1 a 37 pieces cachees.
Journal avant : aucun code (`SWTOR_ALPHA_MODE_APPROX`, `SWTOR_MATERIAL_MISSING` absents ; tous les .mat et textures etaient trouves). `SWTOR_KIND_SKIPPED` (1 FxPlaceable, 3 Engine...) n'est pas en cause : ces instances ne produisent aucun mesh.

## Correspondance implementee (`SwtorMaterials.Classify`, `MaterialDef.Visibility/PolyType`)
Voir la table dans `docs/superpowers/specs/swtor-formats.md` (section Materiaux). Resume : Hidden/`util_collision`/`fx_transparent` non dessines ; cones/faisceaux AnimatedVFX additifs ignores avec `SWTOR_VFX_SKIPPED` ; autres additifs gardes (`GL_ONE GL_ONE`, `cull none`, `nonsolid trans`) avec rgb premultiplie par l'alpha (`PremultiplyAlpha`) ; materiaux ignores ou additifs exclus de la collision de repli (`IsNonSolid`) et donc du spawn. Glass inchange (alpha 110 uniforme).

## Sondage (26 958 .mat, et la room)
Global (Derived|AlphaMode, top) : Uber|None|Collide 8 434, Garment|None|Ignore 4 070, Garment|None|Collide 3 243, Uber|None|None 1 217, SkinB 1 135, AnimatedUV|Add ≥ 1 803 (830 Collide + 703 Ignore + 270 None), Uber|Test ≥ 665, DiffuseFlat|Full ≥ 207, AnimatedUVAlphaBlend|Full ≥ 177, AnimatedVFX|Add 119, Glass|Full 112, EmissiveOnly|Add ≥ 185 (comptes minimaux : seules les 60 lignes les plus frequentes ont ete relevees), Skydome 85. 26 noms contiennent fog_cone/light_cone/lightbeam/light_shaft/godray.
Room Dark Council : Uber|None 53 (opaque), Uber|Test 1, AnimatedUV|Add 1 (champ de force), AnimatedVFX|Add 1 (cone), Hidden 3, fx_transparent 1, util_collision_hidden 1. Les textures Uber ont souvent un alpha variable (brillance/emissif) : il ne faut pas l'utiliser comme opacite (c'est le cas).

## Avant / apres (extrait du .shader)
Avant : `standard_light_fog_cone` { nonsolid, trans, cull none, map ...tga, blendFunc GL_ONE GL_ONE, rgbGen identity } ; `fx_transparent`, `util_blue_hidden`, `util_green_hidden`, `occluder_wall` { map ...tga rgbGen vertex } (opaques).
Apres : ces 5 shaders n'existent plus (61 -> 56 shaders). Les additifs restants gardent le meme texte, TGA a rgb premultiplie par l'alpha.

## Verification (hors jeu)
Tests ajoutes (`SwtorPipelineTests`, 5 synthetiques + 1 SkippableFact Dark Council) ; la SkippableFact s'execute (assets presents). `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur. `dotnet test` : 648 reussis, 0 echec (84 + 183 + 55 + 179 + 106 + 41). Re-export de `dark_council` et `war_planworking` dans la base JA effectue (aucun processus de jeu en cours). Rien n'est commite.

## A verifier en jeu
`setviewpos -1700 -430 26120 213` (mur avec lumiere, d'apres T7-euler) : plus de cartons blancs derriere les trones ni autour des murs. Perte connue : les cones de lumiere ne sont plus visibles du tout (pas de faisceau). Si des cartons blancs persistent, noter le shader (`r_showtris`/`com_shaderinfo`) pour cibler un autre materiau.
