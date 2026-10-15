# Étape 01 — Re-relecture de la correction 1

Diff relu : `3bedb5f..bcd4954` (fichier `.superpowers/sdd/2026-10-15-phase3-contenu/review-3bedb5f..bcd4954.diff`). Tests non relancés (rapport : build 0 avertissement, suite verte). Références au code courant.

## Verdicts

| Constat initial | Verdict | Preuve |
|---|---|---|
| Écart 1 / Important 2 — zone blanche : preuve | ADDRESSED (par décision de l'orchestrateur) | `step-01-impl.md:40-47` : la section « cause » est remplacée par les mesures (surfaces avec lightmap, p90 = 255, facteur ×2,8) et la conclusion `depthFunc equal` est retirée. La cause n'est plus recherchée, conformément à la décision. À valider visuellement par l'orchestrateur. |
| Écart 2 — module sans lumière | ADDRESSED | `ModuleExporter.cs:183-187` : sans lumière et ARE lisible = `Default with { Ambient, Sun = SunDiffuseColor }` ; avec lumières, `Compiler.cs:141-142` (`ForKotor`) annule le soleil. Lecture ARE : `ModuleSceneBuilder.cs:52-58,72-96` (`SunDiffuseColor`, `AreaLightingKnown`). Tests : `ModuleExporterTests.cs:60,97,116`, `ModuleSceneBuilderTests.cs:373`. Résidu : ARE illisible = faux soleil du compilateur, avec `AMBIENT_DEFAULT` au journal (`ModuleSceneBuilder.cs:77,94`). |
| Important 1 — saturation / rayons géants | ADDRESSED (par décision) | `VertexLighting.cs:20` (`DefaultOverbrightShift = 0`), somme écrêtée à 1 avant division (ligne 60-70 de `Evaluate`), lumières invalides écartées (`:60`). Pas de plafond par lumière ni borne de rayon : conforme à la décision. Tests : `VertexLightingTests.cs:104` (écrêtage), `:150` (shift 0). La lumière de 337 m reste saturante, à juger en jeu. |
| Important 3 — contrôleurs 76/88/140 non prouvés | ADDRESSED | `ModuleSceneBuilderTests.cs:260` : plus de `LIGHT_DEFAULTS`/`LIGHT_INVALID`, lumière connue `M02aa_03a` (couleur, 2,5 m, ×2, position), rayons variés. `MdlReaderTests.cs:200` : MDL binaire synthétique avec les trois types. Le décalage de l'en-tête reste auto-cohérent entre test synthétique et lecteur ; il correspond à la disposition KotorBlender (flare_radius 4, 5 tableaux de 12, puis priority à 64, ambient_only à 68 : `MdlReader.cs:135`). |
| Important 4 — câblage non testé | ADDRESSED | Échelle position/rayon, multiplicateur inchangé, filtre, repli : `ModuleExporterTests.cs:60,97,116`. `LIGHTMAP_NO_VALID_TEXEL` par `ModuleSceneBuilder` + `TextureLookup`, `HasAlpha` faux : `ModuleSceneBuilderTests.cs:332,353`. |
| Important 5 — `ambient_only` et gardes NaN/négatif | ADDRESSED | Lecture : `MdlReader.cs:114-120` ; transmission `ModuleSceneBuilder.cs:156`, `ModuleExporter.cs:181` ; effet `VertexLighting.cs:61`. Gardes : `ModuleSceneBuilder.cs:148-153` (`LIGHT_INVALID`), `ModuleExporter.cs:180`, `VertexLighting.cs:60`, `ToByte` NaN = 0 (`:84`). Tests : `VertexLightingTests.cs:119,130`, `ModuleSceneBuilderTests.cs:301`, `MdlReaderTests.cs:216`. |

Minor 1 (raccourci distance 0) et Minor 5 (contrôleur hors limites qui ignorait la pièce) sont aussi corrigés : `VertexLightingTests.cs:88`, `MdlReader.cs:158`.

## Nouvelles casses introduites par le diff

Aucune Critical ni Important.

Observations (non bloquantes) :
- `DefaultOverbrightShift` passe à 0 pour tout le compilateur, pas seulement KOTOR : une carte compilée par la CLI avec les défauts (soleil 0,75 + ambiante 0,45) sature désormais les faces face au soleil. Voulu (décision shift 0) et signalé en réserve 2 par l'implémenteur.
- `step-01-impl.md`, section « Réserves » (lignes ~100-107) garde les anciennes réserves 1, 2, 4, 5 périmées ; seule une phrase de la section « Correction 1 » indique qu'elles sont remplacées.
- `AmbientOnly` à 0 sur les 71 lumières de Taris : le décalage 68 n'est donc pas prouvé par une valeur réelle non nulle, seulement par le test synthétique et la source KotorBlender.

## Hors périmètre (sans verdict)

Minor 2, 3, 4, 6, 7, 8 de la relecture initiale (test `Repair_fills_invalid_from_nearest` 1D, départage Chebyshev, effet de `nomipmaps` sur la diffuse, alpha de reflet + `TransparencyHint`, `DynAmbientColor` = 0, surcharges worldspawn ignorées avec lumières) ; processus « tests après le code » ; calibration visuelle des lumières et de la zone blanche ; `PreviewBuilder` sans éclairage par sommet.
