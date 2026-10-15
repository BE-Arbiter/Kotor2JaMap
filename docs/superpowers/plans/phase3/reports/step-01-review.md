# Étape 01 — Rapport de relecture

Diff relu : `24da9d0..3bedb5f`. Tests non relancés (aucun doute précis). Les « diff:N » renvoient au fichier `.superpowers/sdd/2026-10-15-phase3-contenu/review-24da9d0..3bedb5f.diff` ; les autres références au code courant.

## Verdict 1 — Conformité à la spec : ❌

Écarts :

1. **Zone blanche de Taris : preuve non établie** (plan « Ce qui est fixé » ; spec §1 « cause exacte à confirmer »). Le rapport (step-01-impl.md, section « Zone blanche ») avance une hypothèse (la passe `depthFunc equal` échouerait aux frontières de pièces) qu'il avoue ne pas avoir prouvée. Elle n'explique pas pourquoi une seule zone est touchée alors que 152 shaders sur 262 ont la même structure. Aucun sondage du shader ni de la lightmap des surfaces réellement visibles depuis `3678 5309 25` à -45° n'est donné. La correction par l'alpha de reflet est légitime en soi (spec « Découpe abusive »), mais elle n'est pas démontrée comme cause de la zone blanche.
2. **Module sans lumière** : `ModuleExporter.cs` (diff:922-929) ne passe l'ambiante ARE que si `lights.Count > 0` ; sinon `VertexLighting.Default` (faux soleil cyan DAG_1) et l'ARE est ignorée. La spec (« Éclairage par sommet KOTOR ») dit que le faux soleil est remplacé par l'ambiante de la zone plus les lumières ; le plan dit « liste vide = comportement actuel ». Les deux textes se contredisent et l'implémenteur a suivi le plan sans signaler l'écart avec la spec. `ModuleScene.AmbientColor` est alors calculée et journalisée (`AMBIENT_DEFAULT`) pour rien.
3. Processus : tests écrits après le code (aveu du rapport), pas de phase rouge.

Conformes : dilatation 8-voisins, alpha 255, gris 128 + `LIGHTMAP_NO_VALID_TEXEL` ; réparation dans `TextureLookup.Find` donc aussi dans la vue ; `HasAlpha` réel (DXT1/DXT5/32 bits) ; règle envmap/punchthrough/decal ; `nopicmip`/`nomipmaps` (relus dans `rd-vulkan/tr_shader.cpp` lignes 2714-2723) ; messages anglais et codes au journal ; lumières transmises à l'échelle JA.

## Verdict 2 — Qualité

### Critical
Aucun.

### Important

1. **Saturation et rayons géants non bornés** — `VertexLighting.cs` (diff:219-230), `ModuleExporter.cs` (diff:923-926). Les contributions sont additionnées sans plafond par lumière et sans borne de rayon. Scénario : `tar_m02aa` a une lumière de 337,5 m (`M02aa_10a`), multiplicateurs jusqu'à 3. À 100 m, atténuation 0,70 × 3 = 2,1 : tout sommet sans lightmap à portée sature à 1, puis /4 puis ×4 du moteur : blanc. L'ambiante ARE (0,39 à 0,55) s'ajoute. C'est le symptôme « surexposé » que l'étape doit supprimer. Le rapport relève `lts_glass01` 128 → 254 et l'absence de calibration (réserve 2). Pas d'occlusion : un sommet de couloir reçoit les lumières des pièces voisines à travers la paroi (voulu par la spec, mais aggrave le point).
2. **Preuve de la zone blanche insuffisante** (écart 1). Risque : la correction est livrée et la zone reste blanche (causes possibles non écartées : surfaces sans lightmap en `rgbGen vertex` comme `lts_pwall01i` « lightmap manquante » 117 → 176 dans le rapport, lightmap elle-même claire, ou lumière géante du point 1). L'orchestrateur doit contrôler visuellement au point d'apparition.
3. **`Real_tar_m02aa_lights_read` ne prouve ni les décalages ni les types de contrôleur** (diff:1142-1156). Si les contrôleurs 76/88/140 étaient mal lus, tous les nœuds recevraient les défauts (blanc, 5 m, ×1) et toutes les assertions (rayon > 0, couleur dans [0,1], multiplicateur > 0, position finie) passeraient. Il n'asserte ni l'absence de `LIGHT_DEFAULTS` ni une valeur connue (le rapport cite (0,61 ; 0,95 ; 1,0), 2,5 m, ×2). Pas de test synthétique de `MdlReader` pour les contrôleurs de lumière. La justesse repose sur le seul sondage manuel.
4. **Câblage non testé** : `ModuleExporter.BuildPlan` (échelle × `Scale` sur position et rayon, repli faux soleil sans lumière, filtre `Radius > 0`) ; journal `LIGHTMAP_NO_VALID_TEXEL` via `TextureLookup`/`ModuleSceneBuilder` ; `HasAlpha=false` des lightmaps. Une erreur d'échelle sur le rayon serait invisible.
5. **Lumières lues sans filtre sur leurs propriétés** — `MdlReader.cs` (diff:451-454). `ambientonly`, `shadow`, `fading`, `affectdynamic` de l'en-tête ne sont pas lus : une lumière « ambient only » ou à usage dynamique est traitée comme ponctuelle. Aucune garde sur un multiplicateur négatif ou non fini : un NaN donne `(int)MathF.Round(NaN)` (octet indéfini) dans `VertexLighting.ToByte`.

### Minor

1. `Point_light_attenuates` teste la distance 0 via le raccourci `d < 1e-4` (diff:226), donc `facing = 1` sans exercer le produit scalaire ; le commentaire « normal facing the light » est faux pour ce cas. Les points 50/100/150 prouvent bien 0,5 puis 0 (ambiante nulle dans ce test, ambiante couverte par le test suivant).
2. `Real_ebo_m41aa_no_invalid_lightmap_texel` ne prouve que alpha = 255 : un gris uniforme ou du bruit conservé passerait. `Repair_fills_invalid_from_nearest` est 1D (diagonales, égalités, priorité orthogonale non couvertes).
3. Dilatation correcte (remplissage simultané par passe, donc indépendant de l'ordre de balayage), mais « le plus proche » est au sens de Chebyshev avec départage arbitraire gauche > droite > haut > bas. Acceptable pour la spec.
4. `nomipmaps`/`nopicmip` au niveau du shader s'appliquent aussi à l'étape diffuse (`tr_shader.cpp:1287-1291`) : les textures de murs perdent leurs mipmaps (scintillement au loin) et ignorent `r_picmip`. Conforme au plan, effet de bord non signalé.
5. Contrôleur de lumière hors limites (diff:491) : `c.Bad` fait ignorer toute la pièce (`ROOM_MODEL_UNREADABLE`), alors qu'avant un contrôleur lumière illisible était sans effet. Journalisé, mais régression de robustesse ; un repli `LIGHT_DEFAULTS` serait plus doux.
6. Alpha de reflet et `TransparencyHint` (diff:340-348) : un mesh à hint de transparence avec envmap, sans decal ni punchthrough, n'est plus `Blend`. Conforme à la spec (« opaque »), mais des vitres à reflet perdent leur transparence ; à vérifier visuellement. Aucun test pour envmap + hint ni envmap sans lightmap (Unlit).
7. `DynAmbientColor` = 0 utilisée telle quelle (réserve 5) : ambiante noire silencieuse, sans code au journal.
8. `Compiler.EffectiveLighting` ignore les surcharges `_kotor_sun`/`_kotor_ambient` du worldspawn dès qu'il y a des lumières ; non documenté.

### Points jugés

- **Dilatation** : correcte, trois cas du plan couverts en unitaire.
- **Nœuds lumière** : sources citées (xoreos `model_kotor.cpp` : drapeau 0x0002, `skip(0x5C)`, contrôleurs 76/88/140 ; kotorblender `reader.py`). Couleur/rayon/multiplicateur dans les contrôleurs et non dans l'en-tête : cohérent avec ces sources ; collision 88/140 avec les émetteurs évitée par le drapeau `Light`. Position monde cohérente avec les maillages. Défaut de preuve par test : Important 3.
- **Atténuation et surbrillance** : formule conforme ; division par 2^shift après écrêtage conforme à l'existant ; saturation : Important 1.
- **Module sans lumière** : écart 2.
- **Alpha de reflet vs decal/punchthrough** : conforme et testée sur 3 cas ; `TxiInfo` couvre `bumpyshinytexture` (TxiInfo.cs:27).
- **Zone blanche** : écart 1, Important 2.
- **Tests sans valeur probante** : Important 3, Important 4, Minor 2.
