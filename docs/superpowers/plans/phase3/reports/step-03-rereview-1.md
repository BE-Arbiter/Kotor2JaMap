# Étape 03 — Re-relecture ciblée de la correction 1 (6741e87..fd306ea)

Périmètre : le diff `review-6741e87..fd306ea.diff` seulement. Aucun code modifié, aucun test relancé (lecture du diff, du code et des sources du moteur `OpenJK-SWGL`).

## Verdicts

| Point | Verdict |
|---|---|
| Critical : limites par surface | ADDRESSED |
| Important 1 : plus de 32 surfaces | ADDRESSED |
| Important 2 : objets sans maillage rendu | ADDRESSED |
| Minor 1 : normales sur 256 | ADDRESSED |
| Minor 2 : pas de dossier de jeu | ADDRESSED |

### Critical — limites par surface : ADDRESSED
- Moteur : `qfiles.h:33-34` définit `SHADER_MAX_VERTEXES 1200` et `SHADER_MAX_INDEXES (6*1200)`. `tr_model.cpp:1105` refuse `numVerts >= SHADER_MAX_VERTEXES` et `tr_model.cpp:1113` refuse `numTriangles * 3 >= SHADER_MAX_INDEXES`. Soit au plus 1199 sommets et 2399 triangles (3 × 2399 = 7197 < 7200 ; 2400 donnerait 7200, refusé). Les valeurs citées dans le code et le rapport sont exactes.
- Code : `Md3Writer.cs:20` (`MaxVerticesPerSurface = 1199, MaxTrianglesPerSurface = 2399`). `Split` (`Md3Writer.cs:160-190`) coupe sur les deux bornes avec `>` : `pos.Count + fresh > 1199` et `idx.Count / 3 + 1 > 2399`, donc une surface ne dépasse jamais les bornes. Le cas « partie entière » (`:162`) utilise `<=`, cohérent.
- Tests : `Engine_limits_are_the_swgl_loader_limits`, `Surface_at_the_limit_stays_whole_and_one_over_is_split` (399 triangles disjoints = 1197 sommets, une surface ; 400 = 1200 sommets, coupée en 1197 + 3), `Triangle_limit_splits_a_part_with_shared_vertices` (2399/2400), `Large_part_is_split_below_engine_limits`, et la relecture des surfaces dans `PlaceableConverterTests`. Ils testent les bornes exactes et plus l'ancienne limite.

### Important 1 — surfaces : ADDRESSED
- Moteur : `MD3_MAX_SURFACES 32 + 32` (`qfiles.h:105`, macro sans parenthèses = 64). Le chargeur ne le vérifie pas (`tr_model.cpp:1087-1091` alloue `numSurfaces`, aucun autre usage dans `code/`), le rendu (`tr_mesh.cpp:411`) boucle sur `model->numSurfaces` sans tableau fixe. La borne 64 est donc la borne déclarée, prudente par rapport au chargeur.
- Code : `Md3Writer.MaxSurfaces = 64` (`:23`) ; `Write` garde l'erreur au-delà ; `WriteSplit` (`:42-56`) répartit en fichiers de 64 au plus, nom sans suffixe si un seul fichier, `_p0`, `_p1`… sinon (nom de l'en-tête identique au nom du fichier), longueur vérifiée (`:53`). `PlaceableConverter.cs` : une entité par fichier avec même origine, angles et `modelscale` (boucle `foreach (Md3Asset asset in info.Assets)`), `converted++` une fois par instance, collision PWK ajoutée une fois par instance (bloc `info.Pwk` hors de la boucle), plafond 2000 compté en entités (`miscEntities + info.Assets.Count > MaxMiscModels`, ligne 64). Les textures d'un fichier sont les noms distincts de ses surfaces seulement ; l'union dédupliquée alimente textures et shaders.
- Tests : `Write_rejects_more_surfaces_than_the_model_limit_but_WriteSplit_splits` (65 → 64 + 1, noms d'en-tête) et `Model_with_more_surfaces_than_the_engine_limit_is_split_into_files` (2 instances : 4 entités, 2 collisions, fichiers `_p0`/`_p1`, bilan `PLACEABLES`).

### Important 2 — sans maillage rendu : ADDRESSED
- Décision appliquée : le modèle est sauté avec sa collision. `PlaceableConverter.cs` (`BuildModel`) : `Fail("... has no rendered mesh (invisible blocker, collision not added)")`, relayé par `PLACEABLE_SKIPPED`. Test `Model_without_rendered_mesh_is_skipped_with_its_collision` : aucune entité, aucun MD3, aucune solide, message vérifié.

### Minor 1 — normales sur 256 : ADDRESSED
- Moteur : `tr_model.cpp` décode `lat *= (FUNCTABLE_SIZE/256)` sur un octet (256 pas de 2π). Code : `Md3Writer.cs` `EncodeNormal`, `* 256.0 / (2π)` pour les deux octets ; le cas axe Z (`lng = 128` pour −Z) est maintenant exactement π. Test `Normals_are_encoded_on_256_steps` (0x0040, 0x4040, 0xC040, 0x0000), valeurs recalculées à la main : justes.

### Minor 2 — code `PLACEABLES_NO_GAME_DIR` : ADDRESSED
- `PipelineSteps.cs` (`ConvertObjects`) journalise `PLACEABLES_NO_GAME_DIR` avant le retour vide ; test `Import_without_game_folder_logs_that_placeables_are_skipped`.

## Nouvelles casses introduites par le diff

Aucun Critical, aucun Important.

Remarques mineures, sans action requise :
- `Convert` construit désormais le modèle (BuildModel) avant de tester le plafond de 2000 entités ; un objet refusé par le plafond peut donc laisser un MD3 inutile dans la sortie (ses MD3 restaient déjà possibles pour d'autres instances). Coût négligeable.
- Un nom de modèle de 61 à 63 caractères échoue avec le suffixe `_p0` (`MD3 not written`, journalisé). Les noms KOTOR font au plus 16 caractères : sans effet.
- Le message `PLACEABLES` compte les MD3 uniques et les modèles : « 2 placeable(s) converted ... 2 unique MD3 » reste cohérent.

## Hors périmètre
- Aucune vérification en jeu : le chargement d'un modèle de 64 surfaces et le rendu en plusieurs entités restent à confirmer visuellement (le chargeur ne vérifie pas 64). Repli : `MaxSurfaces = 32`.
- Seul `rd-vulkan` a été relu ; `rd-vanilla` (autre rendu éventuel) n'a pas été examiné.
- Points Minor 3 à 6 de la relecture initiale non traités (double lecture/validation du GIT, normales sous échelle non uniforme, horodatage du zip, enroulement des triangles non vérifié visuellement) : hors correction 1, inchangés.
- Le plafond de 2000 `misc_model_static` ne tient pas compte de ceux déjà présents dans la carte (préexistant).
