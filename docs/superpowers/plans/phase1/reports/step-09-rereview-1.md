# Étape 09 — Re-revue du correctif 1 (a966b2c..c49a055)

Revue sur diff uniquement (aucun test relancé ; chiffres du rapport step-09-fix-1.md non vérifiés).

## Point 1 — Shader dans `shaders/` : ADDRESSED
- `src/Kotor.Compiler/Export/ModuleExporter.cs` (~l.110-112) : écrit `shaders/kotor_<map>.shader`, plus d'entrée `scripts/`.
- Tests mis à jour : `ModuleExporterTests.cs` (~l.136, `shaders/kotor_mymap.shader`), `Pk3BuilderTests.cs` (Seed + `Pk3_contains_expected_entries`).

## Point 2 — Surfaces fusionnées par shader : ADDRESSED
- Regroupement par (Shader, DoubleSided, présence UV lightmap) : `RenderSurfaces.cs` `FromInputs` (clé `gk`, ~l.223). Découpage uniquement par `MaxSurfaceVerts`/`MaxSurfaceIndexes` (~l.235). Triangles invalides ignorés comme avant ; aucun autre drop.
- Cohérence spatiale : chunks compacts au sens de l'ordre des entrées (pas de tri spatial). Acceptable, mais voir observation O1.
- Références multi-feuilles : `LeavesTouching` (~l.268-295) descend des deux côtés du plan ; `BspWriter.cs:116-128` remplit leafsurfaces pour chaque feuille de `sf.Leaves`, bornes de feuille agrandies.
- UV lightmap / couleurs de sommet / enroulement (v0,v2,v1) inchangés ; clé de partage de sommet `(entrée<<32)|indice`, clés négatives distinctes pour le cas sans normale : correct.
- Tests : `Inputs_with_same_shader_merge_into_one_surface`, `Mesh_spanning_several_leaves_...`, `Merged_chunks_respect_limits_and_keep_every_triangle` (somme des triangles), `Different_shaders_never_merge`, `DoubleSided_and_lightmap_presence_split_groups` (`RenderSurfacesTests.cs` l.~528-600). Limite connue : la branche « feuilles non solides » n'est pas testée.

## Point 3 — Échelle 40 : ADDRESSED
- `ExportOptions.cs:14` (`DefaultScale = 40f`), `kotor2ja.config.json:4`, `ExportJaCommand.cs:16` (aide), `ConfigLoaderTests.cs` (40), tests ModuleExporter fixés à `Scale = 36f` explicitement.

## Risque nommé : feuilles de cluster -1 (jamais dessinées) — vérification : OK
Contrôle : écriture du cluster de feuille dans `BspWriter.cs:205` : `w.Write(l.IsSolid && nSurf == 0 ? -1 : 0)`. Toute feuille ayant au moins une leafsurface reçoit cluster 0, solide ou non. Chaque surface a >= 1 feuille (garde `BspWriter.cs:115`, exception sinon), insérée dans `leafSurfaces[leaf]` (`BspWriter.cs:125`), donc chaque surface est référencée par au moins une feuille de cluster 0. Le repli sur feuilles solides (`RenderSurfaces.cs:294-295`) est donc sans danger pour la visibilité. Vis à un seul cluster, `BspWriter.cs:246`. Nuance : les feuilles solides sans surface restent à -1 (voulu).

## Nouvelle casse
Aucune Critique/Importante identifiée.
- Mineure (risque de limite, `BspWriter.cs:142,151`) : leafsurfaces 128 066 / 131 072 (marge 2 %), mesurée par l'auteur seulement. Une carte plus grande échouera proprement (`CheckRenderLimits`), sans BSP partiel.

## Observations hors périmètre
- O1 : comme presque toutes les feuilles sont « solides », la règle « non solides seulement » est quasi toujours en repli ; les boîtes englobantes de chunks non triés spatialement gonflent le nombre de feuilles référencées (cause de la marge de 2 %). Pistes si besoin : tri spatial (p.ex. par centroïde) avant découpage, ou test triangle/région plus fin.
- O2 : surfaces plus grosses = culling moins fin en jeu (compromis voulu pour le gain de nombre de surfaces 18 635 -> 296).
- O3 : docs du plan mentionnent encore 36 (historique, noté dans le rapport).

## Verdict
APPROUVÉ : les 3 points sont ADDRESSED, risque cluster -1 écarté, aucune casse Critique/Importante.
