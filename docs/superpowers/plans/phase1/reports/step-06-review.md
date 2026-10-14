# Étape 06 — Revue (conformité puis qualité)

Diff : fbaa5a8..93fb7b8. Revue statique ; la suite n'a pas été relancée (69/69 + 39/39 rapportés, non revérifiés).

## Conformité

- ✅ Interfaces et signatures conformes au plan (Walkmesh, WokReader, TwoDaFile, SceneMesh, SceneWalkmesh, ModuleScene, ModuleSceneBuilder, ObjExporter).
- ✅ Monde = `ModelTransform * Translation(room.Position)` pour les maillages. Normales absentes calculées, `MDL_NO_UV` avec UV à zéro.
- ✅ Codes du tableau présents : ROOM_MODEL_MISSING, MESH_NOT_RENDERED (Aabb inclus), NODE_LIGHT/EMITTER/REFERENCE/CAMERA (comptes par pièce), TEXTURE_MISSING (clé conservée à null), TEXTURE_ANIMATED, TEXTURE_ENVMAP, WALKMESH_MISSING.
- ✅ Rulings du contrôleur : TextureLookup → TEXTURE_UNSUPPORTED (texture = null) ; MdlReader par pièce → ROOM_MODEL_UNREADABLE, pièce sautée ; UV non retournés (SceneMesh et OBJ).
- ✅ Review Focus « ressource absente » : modèle absent/illisible → journal + continuation ; texture absente → PNG magenta 8x8 ; lightmap absente → journalisée, maillage conservé. Code et nom présents dans chaque message.
- ✅ Review Focus « nœud non pris en charge » : lumières, émetteurs, références, caméras comptés et journalisés, aucune exception.
- ✅ CLI : `export-obj <module> --out [--kotor]`, journal `<module>.journal.txt`, résumé, codes 0/1/2.
- ✅ Écarts acceptés (seuil lightmap 0,75 ; WOK déjà en coordonnées module, test d'alignement passant et mesure documentée).
- ⚠️ Étape 6 (Blender) exclue, à faire par le contrôleur.

## Points forts

- Lecteurs WOK et 2DA défensifs (bornes, indices de sommets validés, `InvalidDataException`).
- Journal groupé par pièce avec comptes, journal par texture une seule fois (cache dans `Textures`).
- Pas de plantage sur donnée corrompue : indices de triangles hors limites journalisés (`MESH_BAD_INDEX`), surfacemat absent journalisé.
- Décision WOK isolée dans une constante documentée et vérifiée par une mesure réelle.
- Export OBJ propre : noms assainis, culture invariante, walkmesh séparé, non écrit si vide.

## Problèmes

### Critique
Aucun.

### Important
Aucun.

### Mineur

1. `ModuleSceneBuilder.cs:290-291` : les maillages `Skin`, `Dangly`, `Anim`, `Saber` sont rendus comme des maillages statiques ordinaires sans aucune entrée au journal (pas de déformation dangly/skin, pas d'animation). Contraire à « on n'ignore jamais rien en silence ». Fix : compter ces kinds par pièce et journaliser (ex. `MESH_SKIN_STATIC`, `MESH_DANGLY_STATIC`).
2. `ModuleSceneBuilder.cs:306` : un maillage rendu avec `Triangles.Length == 0` ou `Positions.Length == 0` est écarté sans journal. Fix : `MESH_EMPTY` (compté) ou l'intégrer à MESH_NOT_RENDERED.
3. `ModuleSceneBuilder.cs:326` : `Uv1` absent ou de taille incohérente → `null` sans journal, alors que le maillage a une lightmap (`Texture1`). La lightmap est silencieusement inutilisable. Fix : journaliser `MDL_NO_UV1` quand `lm is not null && uv1 is null`.
4. `ModuleSceneBuilder.cs:290` : un `Aabb` avec `Render == true` est compté dans MESH_NOT_RENDERED, mais le message « non rendu(s) » est inexact pour lui ; l'inverse n'est pas distingué. Fix : message ou code dédié (`MESH_AABB`).
5. `ModuleSceneBuilder.cs:378` : un matériau WOK hors lignes de surfacemat (`Get` renvoie null) est traité comme non praticable sans journal. Fix : compter et journaliser (`WALKMESH_UNKNOWN_SURFACE`).
6. `ModuleSceneBuilder.cs:346` et `ObjExporter.cs:113` : le cache `Textures` est sensible à la casse et partagé entre diffuse et lightmap. Une même texture écrite en casses différentes est résolue et journalisée deux fois ; PNG/mtl dupliqués (sur Windows, même fichier réécrit). Fix : clés `OrdinalIgnoreCase` (et normalisation en minuscules à la sortie).
7. `ExportObjCommand.cs:612` : `TlkFile.Open` est hors du `try` ; un dialog.tlk corrompu produit une exception non gérée au lieu du code 2. Fix : l'inclure dans le `try`.
8. Couverture : aucun test synthétique pour ROOM_MODEL_UNREADABLE, TEXTURE_UNSUPPORTED ni les comptes NODE_* (seulement le test réel tar_m02aa et un cas MISSING). Le Review Focus « nœud non pris en charge » n'est donc pas verrouillé en test sans l'installation KOTOR. Fix : tests avec un MDL minimal corrompu et une texture non supportée.
9. `ObjExporter.cs:147-157` : le walkmesh OBJ utilise `usemtl walk/nowalk` sans fichier mtl associé (couleurs non définies dans Blender). Cosmétique ; fix : écrire un `<module>_walkmesh.mtl`.
10. Résumé CLI : « Textures manquantes » inclut la clé technique `notexture` et les lightmaps ; acceptable et expliqué dans le rapport, mais le libellé peut induire en erreur.

## Évaluation

**Approuvé** (réserves mineures ; le point 1 est à traiter avant la phase 2 par fidélité, mais ne rend pas la tâche non fiable : aucun élément d'une pièce typique n'est perdu hors des cas rares Skin/Dangly).
