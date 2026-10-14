# Etape 06 - Rapport de correction 1

Statut : OK. Rien n'a ete commite.

## Changements par point

1. **MESH_STATIC_FROM_ANIMATED** (`ModuleSceneBuilder.AddModel`) : les maillages Skin/Dangly/Anim/Saber restent exportes en statique ; un compte par piece est journalise.
2. **MESH_EMPTY** : un maillage rendu avec 0 triangle ou 0 sommet est compte (par piece) et journalise au lieu d'etre ecarte en silence (controle deplace de `BuildMesh` vers la boucle de `AddModel`).
3. **MDL_NO_UV1** : si une lightmap (Texture1) est presente mais UV1 absent/incoherent, journal par maillage, `Lightmap = null`, la lightmap n'est pas resolue.
4. **SURFACEMAT_UNKNOWN** : materiau WOK absent de surfacemat.2da (`Get` renvoie null alors que la table est chargee) : compte par piece, triangles non praticables.
5. **Cache Textures** : toutes les cles (et `SceneMesh.Texture/Lightmap`) sont normalisees en minuscules ; `Resolve` normalise aussi, donc une texture n'est resolue/journalisee/ecrite en PNG qu'une fois, meme appelee en diffuse et lightmap sous des casses differentes. Contrat `ModuleScene.Textures` inchange (cle = nom, null = manquante).
6. **TLK_UNREADABLE** (`ExportObjCommand`) : `TlkFile.Open` dans un try (InvalidDataException/IOException/UnauthorizedAccessException) ; avertissement et `tlk = null`.

Refactor de testabilite : `AddModel` (internal) separe de la lecture MDL ; `InternalsVisibleTo Kotor.Core.Tests` ajoute dans Kotor.Core.csproj (aucun constructeur de MDL binaire dans les tests).

## Tests ajoutes (ModuleSceneBuilderTests, dossier KOTOR factice en RIM)

`Animated_meshes_are_exported_static_and_logged`, `Empty_mesh_is_logged`, `Lightmap_without_uv1_is_dropped_and_logged`, `Texture_cache_is_case_insensitive_and_resolved_once`, `Unsupported_texture_is_logged_with_null_entry` (TGA de type inconnu), `Unreadable_room_model_is_logged_and_skipped` (MDL/MDX corrompus), `Unknown_surface_material_is_counted_and_logged`. Pas de test pour TLK_UNREADABLE (exige un faux dossier KOTOR complet pour la CLI).

## Commandes

- `dotnet test tests/Kotor.Core.Tests --filter ModuleSceneBuilderTests` : 10/10.
- `dotnet test Kotor2Ja.sln` : Kotor.Compiler.Tests 39/39, Kotor.Core.Tests 76/76, aucun avertissement.
- `dotnet run -c Release --project src/Kotor2Ja.Cli -- export-obj tar_m02aa --out out/tar_m02aa` (sortie 0) :

```
Module tar_m02aa (Taris - appartements sud)
Pieces : 17
Maillages : 574
Triangles : 41216
Textures manquantes : 4
Journal par code :
  MDL_NO_UV : 5
  MESH_NOT_RENDERED : 15
  MESH_NO_TEXTURE : 4
  NODE_LIGHT : 9
  TEXTURE_ENVMAP : 8
  TEXTURE_MISSING : 3
```

Resume identique a avant : aucun des nouveaux codes ne se declenche sur tar_m02aa (pas de maillage anime/vide, lightmaps toutes avec UV1, surfacemat complet).
