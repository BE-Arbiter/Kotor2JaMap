# Étape 06 — Passe anglais : rapport d'implémentation

Statut : FAIT. Aucun commit, rien écrit dans le dossier Jedi Academy.

## 1. Inventaire des restes français (avant)

Recherche sur les accents hors commentaires, puis sur les mots courants (« introuvable », « maillage », « piece », « Echelle », « aucun », « hors », « sans »…), puis relecture de toutes les chaînes de `src/**` contenant un espace. Fichiers touchés (40 fichiers de `src`, 9 de `tests`) :

- **Interface** (`MainScreen`, `AppGame`, `WorkflowController`) : `Prévisualiser`, `Ciel`, `Double face`, `Chargement des modules...`, `Importer/Exporter/Créer PK3` (`NextLabel`), préfixes `ERREUR` / `AVERTISSEMENT` / `Avertissement`, messages de configuration, `trouvé(s)`, `échelle`, `Exception inattendue`.
- **Phrases de progression** (`KotorWorkflowSteps`) : `Previsualiser/Importer/Exporter : …`, `triangle(s) relus dans`, `repere(s)`, `entite(s)`, `Creer PK3`, `... d'abord.`, `.lyt introuvable`, `Entites du module illisibles`, avertissement `EXPORT_VIEW_COLLISION_STALE`.
- **CLI** (`Program`, `ListModules`, `ExportObj`, `ExportJa`) : descriptions et options, `Module inconnu`, `Module illisible`, `Options invalides`, `Echec`, `Pieces/Maillages/Textures manquantes`, `Journal par code`, `Sortie`, `(illisible : …)`.
- **Journal et exceptions** : `ModuleExporter` (`Resume par code` devenu `Summary by code`, `Journal non ecrit`, lightmap, conflit de shader, point d'apparition), `PipelineSteps`, `ModuleSceneBuilder` (y compris `NODE_EMITTER`, `NODE_REFERENCE`, `NODE_CAMERA`, `TEXTURE_ENVMAP`, `RenderSurfaces:64-68`), `ModuleEntitiesReader`, `Compiler`, `BrushBuilder`, `WalkableCollision`, `CollisionMerger`, `LimitCheck`, `Limits`.
- **Messages d'exception** des lecteurs : GFF, 2DA, TLK, WOK/BWM, LYT, MDL, TPC, DXT, TGA, PNG, BSP, archives, `ModuleCatalog`, `Pk3Builder`, `ExportOptions.Validate`, `MapNames.Validate`, `ObjExporter` (en-têtes `.obj`).
- **README.md** : réécrit entièrement en anglais.

## 2. Traductions

- Libellés : `Preview`, `Import`, `Export`, `Create PK3`, `KOTOR`, `KOTOR 2`, `Collisions`, `Sky`, `Double-sided`. Un en-tête `Log` est affiché au-dessus du panneau de journal. Titre `KOTOR → Jedi Academy` déjà conforme.
- Préfixes de la zone de journal : `ERROR` (rouge) et `Warning` / `WARN` (orange) ; `MainScreen` mis à jour en conséquence. Sortie CLI : `Rooms`, `Meshes`, `Triangles`, `Missing textures`, `Log by code`, `Output`, `Failed`, `Unknown module`, `Invalid options`.
- Codes du journal inchangés. Noms de zones du TLK non touchés. Les noms de types d'entités sont ceux de l'enum, déjà en anglais (`Placeable`, `Door`, `Waypoint`, `Trigger`, `Sound`, `Store`, `Camera`, `Light`).
- `README.md` : téléchargement (renvoi aux releases GitHub, section build portable laissée à l'étape 07), prérequis, build/test, application (arbres `KOTOR` / `KOTOR 2`, étapes, caméra), options `--game --module --run-to --camera --screenshot --exit`, CLI (`list-modules`, `export-obj`, `export-ja` et leurs options, codes de sortie), configuration (`kotorDir`, `kotor2Dir`, `jaBaseDir`, `workDir`, `scale`, `lightmapBrightness`, `bspLimits`, variables `KOTOR_PATH` / `KOTOR2_PATH` prioritaires, `--kotor` prioritaire sur les deux), et ce qui est converti (MD3 `misc_model_static`, `func_door`, PNJ non convertis).

## 3. Tests adaptés

`WorkflowControllerTests` (libellés, `PK3: x.pk3`), `WalkableCollisionTests`, `CollisionMergerTests`, `BspReaderTests`, `ModuleEntitiesReaderTests`, `ModuleSceneBuilderTests`, `ModuleExporterTests`, `FinalReviewFixTests`, `ExportJaCommandTests` : les sous-chaînes françaises attendues sont remplacées par leur équivalent anglais.

## 4. Vérification

- `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur.
- `dotnet test Kotor2Ja.sln` : Cli 20, Compiler 80, Core 152, Pipeline 74, tous verts (0 échec).
- Recherche finale (hors commentaires) :

```
rg -n --pcre2 '^(?!\s*(//|\*|/\*)).*[àâäçéèêëîïôöùûüÿÀÂÉÈÊÎÔÙÛÇœŒ«»…]' src tests
```

Résultat : `MainScreen.cs:181` (le glyphe `…` du bouton occupé, signe de ponctuation sans mot français), un commentaire de fin de ligne dans `CollisionMerger.cs:21`, `RenderSurfaces.cs:76`, `BspWriter.cs:17` (commentaires), et des données de test (`"café"`, `"Café Taris"`, volontaires pour tester l'encodage). Une seconde passe sur toutes les chaînes à espaces de `src/**` (script de balayage, relu à la main) ne montre plus de français.

## 5. Réserves

- `THIRD-PARTY-NOTICES.md` est encore en français (hors de la liste de l'étape) ; il sera livré dans le zip de l'étape 07, à traduire à ce moment-là si souhaité.
- Les commentaires de code, `docs/**` et les noms de zones du TLK restent en français, comme prévu.
- Le glyphe `…` du bouton occupé n'a pas été changé en `...` : à voir si la police ImGui l'affiche (comportement antérieur inchangé).
- Pas d'essai visuel (consigne : l'orchestrateur le fait) ; les libellés ImGui ne sont vérifiés que par compilation.
