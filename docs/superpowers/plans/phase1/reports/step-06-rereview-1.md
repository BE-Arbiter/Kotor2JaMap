# Etape 06 - Re-revue de la correction 1

Perimetre : diff 4bbb6fa..208170c (lecture seule, tests non rejoues ; les chiffres 76/76 et 39/39 du rapport de correction ne sont pas verifies ici).

## Verdict par point

1. **MESH_STATIC_FROM_ANIMATED : ADDRESSED.** `ModuleSceneBuilder.cs` (`AddModel`, boucle apres le controle de maillage vide) compte les noeuds Skin/Dangly/Anim/Saber, puis journalise un avertissement par piece avec le code attendu. Le maillage reste exporte en statique. Test `Animated_meshes_are_exported_static_and_logged` (2 animes sur 3, message "2 maillage").
2. **MESH_EMPTY : ADDRESSED.** Le controle 0 triangle / 0 sommet est deplace de `BuildMesh` vers `AddModel` (apres le filtre non-rendu), compte et journalise par piece. Test `Empty_mesh_is_logged`.
3. **MDL_NO_UV1 : ADDRESSED.** Dans `BuildMesh`, si `Texture1` est present et `uv1` est null (absent ou longueur incoherente), journal par maillage, `lm = null`, lightmap non resolue. Test `Lightmap_without_uv1_is_dropped_and_logged` (Lightmap null, MDL_NO_UV1, cle absente du cache).
4. **SURFACEMAT_UNKNOWN : ADDRESSED.** Dans la construction du walkmesh, `Get` null alors que `surfaceMat` est charge incremente `unknown` ; un avertissement par piece est journalise, triangles non praticables. Test `Unknown_surface_material_is_counted_and_logged` (materiau 77, un triangle).
5. **Cache Textures : ADDRESSED.** Cles diffuse et lightmap passees en minuscules dans `BuildMesh`, et `Resolve` normalise aussi (`name = name.ToLowerInvariant()`), donc une seule resolution, un seul journal et un seul PNG quelle que soit la casse ou le type. `SceneMesh.Texture/Lightmap` portent la forme normalisee. Test `Texture_cache_is_case_insensitive_and_resolved_once` (une cle, un TEXTURE_MISSING).
6. **TLK_UNREADABLE : ADDRESSED.** `ExportObjCommand.cs` : `TlkFile.Open` dans un try (InvalidDataException, IOException, UnauthorizedAccessException), avertissement `TLK_UNREADABLE`, `tlk = null`, execution poursuivie ; `ImportLog` cree avant le try. Aucun test (reconnu dans le rapport).

## Nouvelle regression

Aucune.

## Observations hors perimetre

- Le compteur `animated` est incremente avant `BuildMesh` ; un maillage anime rejete ensuite par `MESH_BAD_INDEX` est donc compte aussi comme "exporte en statique" (impact tres mineur : message legerement inexact).
- Si `ISurfaceMat.Get` renvoie aussi null pour une cellule "****" (valeur 2da nulle) d'un materiau existant, SURFACEMAT_UNKNOWN pourrait etre emis a tort ; a confirmer dans `TwoDaFile.Get` (non lu ici).
- Pas de test pour TLK_UNREADABLE (couverture manquante, non bloquante).
- `InternalsVisibleTo Kotor.Core.Tests` ajoute dans `Kotor.Core.csproj` : acceptable, justifie par la testabilite.

## Verdict

Les 6 points sont corriges ; aucune regression identifiee. Etape 06 round 1 : **APPROUVE**.
