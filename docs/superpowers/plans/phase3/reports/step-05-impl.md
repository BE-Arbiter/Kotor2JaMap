# Étape 05 — rapport d'implémentation

Statut : FAIT_AVEC_RESERVES (voir « Réserves »). Rien n'est commité, aucun sous-agent lancé, rien écrit dans le dossier Jedi Academy (essai en `--run-to export`).

## Ce qui a été fait

### PNJ retirés
- `ModuleEntitiesReader` : `Creature List` et `Encounter List` ne sont plus dans les `Specs` (l'enum `EntityKind` est inchangée). `Read` écrit une ligne `NPC_SKIPPED` (anglais) avec le nombre de créatures et de rencontres du GIT, seulement s'il y en a (`... NPCs are not converted (N creature(s), M encounter(s) skipped).`). `ReadObjects` (étape Importer) ne la journalise pas.
- Le bilan `ENTITIES` ne compte plus `Creature` ni `Encounter`.
- Conséquence : un GIT sans listes journalise 7 `GIT_LIST_MISSING` au lieu de 9 (test existant mis à jour).

### Modèles dans les vues
- `PreviewModel` gagne `Models` (cle = chemin du MD3 -> lots) et `Instances` (`PreviewModelInstance(ModelKey, Matrix4x4 Transform, EntityKind Kind, string Label)`), en propriétés `init` (les appels existants ne changent pas).
- Nouveau `Kotor.Pipeline/Preview/Md3Reader.cs` : lecteur MD3 minimal (IDP3 v15, 1re image, 1er shader par surface, positions 1/64, normales lat/lng 256 pas). UV pris tels quels du fichier (t = 0 en haut, déjà la convention de la vue).
- Nouveau `PreviewBuilder.Models.cs` (`PreviewBuilder` devient `partial`) :
  - `FromImport` : si `imported.Converted` contient des MD3, les lots sont construits depuis `Md3Asset.Data`, les textures depuis `TextureData` (clé `md3:<texture>`), les instances depuis les entités converties (`misc_model_static` : `model`/`angles`/`modelscale` ; `func_door` : `model2`/`modelAngles`, porte fermée à son `origin`). Transform = échelle `modelscale` puis rotation Z (lacet) puis translation. Les repères Placeable/Door qui ont un maillage (même type, même position) sont retirés ; les autres types et les objets non convertis gardent leur repère.
  - `WithStagedModels(model, bsp, stagingDir, game, log)` (vue Exporter) : lit les MD3 du staging référencés par les entités du lump, leurs TGA dans `textures/<dossier>/`, et déduit le test alpha du fichier `.shader` du staging (`alphaFunc`). Codes `PREVIEW_MD3_MISSING` / `PREVIEW_MD3_UNREADABLE` (anglais) si un fichier manque ou est illisible.
  - `Md3ShaderWriter.HasCutout(KotorTexture?)` extrait la règle d'alpha du writer (comportement du shader inchangé) pour que la vue Importer utilise la même.
- `KotorWorkflowSteps` : l'étape Exporter appelle `WithStagedModels` ; les messages de progression donnent le nombre de modèles posés.
- Application (`PreviewRenderer`) : un jeu de tampons GPU par modèle, dessiné une fois par instance (matrice monde), `DrawBatch` prend la matrice monde. Les cases à cocher par type incluent `Placeable`/`Door` dès qu'il y a des instances de ce type. Éclairage : texture seule, sans lightmap (BasicEffect / AlphaTest).

### Pilotage en ligne de commande
- `Kotor.Pipeline/Workflow/AutomationOptions.cs` : record `AutomationOptions` (+ `Stages`, `StageIndex`) et `AutomationParser.Parse(args, out error)`. Sans `--module` : `null` sans erreur. Argument inconnu, valeur manquante, `--game` hors kotor|kotor2, `--run-to` hors preview|import|export|pk3, `--camera` qui n'a pas 5 nombres : `null` + message anglais. `--game` vaut kotor par défaut, `--run-to` preview.
- `Program.cs` : analyse avant toute fenêtre ; erreur -> stderr + usage, code 2. `AttachConsole(-1)` pour que les messages s'affichent depuis la console parente (application WinExe). Retourne `game.ExitCode`.
- `AppGame` (mode automatisé) : sélection du module (`SelectModule(game, module)`), `PreviewAsync` puis `NextAsync` jusqu'à `--run-to` ; attend que la vue soit envoyée au GPU ; place la caméra si `--camera` ; attend 3 images ; capture le back buffer (`GetBackBufferData`, alpha forcé à 255) et l'écrit avec `PngWriter` ; `--exit` quitte ensuite. Étape en échec (`LastError`) avec `--exit` : message sur stderr, code 1. Le journal est recopié sur la console (`MainScreen.LogSink`). La liste des modules n'est pas chargée en mode automatisé.

## Tests
- `ModuleEntitiesReaderTests.Creatures_not_read` (1 créature + 1 rencontre + 1 objet -> 1 entité, `NPC_SKIPPED`, bilan sans Creature/Encounter) ; `Git_without_lists...` passe de 9 à 7.
- `PreviewBuilderTests` : `Import_view_has_model_instances` (1 instance, 1 modèle, texture `md3:`, transform, marqueur d'objet remplacé, waypoint conservé), `Import_view_without_converted_has_no_instances`, `Md3Reader_round_trips_writer_output`, `[SkippableFact] Real_tar_m02aa_export_view_has_md3` (>= 10 instances, >= 1 porte, tous les modèles des instances présents, aucune créature/rencontre dans les repères de la vue Importer).
- `AutomationOptionsTests` : `Automation_parse_full`, `Automation_defaults`, `Automation_parse_invalid_runto`, valeurs invalides (théorie), argument inconnu/incomplet, `Automation_none_without_module`.
- Remarque de méthode : j'ai écrit le code avant de lancer les tests ; l'étape « tests qui échouent » n'a donc pas été observée.
- `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur. `dotnet test Kotor2Ja.sln` : vert (Compiler 80, Cli 20, Core 151, Pipeline 73, aucun test ignoré : les tests réels ont tourné).

## Essai réel
Commande exacte (le premier essai, sans `--camera`, donne un écran jaune uni car la caméra par défaut est à l'intérieur du cube du point d'apparition, comportement antérieur à cette étape) :

```
dotnet run -c Release --project src/Kotor2Ja.App -- --game kotor --module tar_m02aa --run-to export --screenshot C:\Users\lambe\AppData\Local\Temp\shot.png --exit
```
Code de sortie 0, PNG de 1600x900 écrit (48 736 octets), mais vue 3D jaune uniforme à cause de ce cube.

Essai retenu, avec caméra (derrière et au-dessus du point d'apparition, trouvé dans le `.bsp` du staging : origine 3678.195 5309.426 25, angle -45) :

```
dotnet run -c Release --project src/Kotor2Ja.App -- --game kotor --module tar_m02aa --run-to export --camera "3466 5521 175 -45 -15" --screenshot C:\Users\lambe\AppData\Local\Temp\shot2.png --exit
```
Code de sortie 0. La capture n'est pas noire : la vue Exporter montre le BSP texturé avec lightmaps, les repères (Light en jaune, waypoints, caméras) et les modèles posés, dont une porte au fond. L'interface (colonne gauche, cases à cocher) est dans l'image ; le journal contient `NPC_SKIPPED`, `PLACEABLES : 23 placeable(s) converted, 3 ...`, `DOORS : 8 door(s) converted (6 locked, ...)`.

Capture : `C:\Users\lambe\AppData\Local\Temp\shot2.png`, copiée dans le dépôt : `C:\Users\lambe\source\repos\SWGLKotor-Map\docs\superpowers\plans\phase3\reports\step-05-shot.png`.

L'essai a écrit dans `C:\jka_maps\kotor_work` (WorkDir configuré), pas dans le dossier du jeu. Arguments invalides vérifiés avec l'exécutable Release : message + usage sur stderr, code 2, aucune fenêtre. Module inexistant avec `--exit` : code 1.

## Réserves
- Le code de sortie et le PNG sont vérifiés ; les messages stderr ne sont visibles que si le processus est lancé directement depuis une console (via `dotnet run` redirigé, l'application WinExe ne les transmet pas ; le code 2 reste correct).
- La caméra par défaut (point d'apparition) est dans le cube jaune du spawn : pour une capture utile, passer `--camera`. Non modifié car hors périmètre (comportement antérieur).
- Les instances n'ont pas d'étiquette dessinée (trop nombreuses) ; seules les cases à cocher par type les masquent.
- Les rotations des modèles n'utilisent que le lacet (`angles`/`modelAngles` ne contiennent que le lacet dans nos entités).
- Le sens de rotation et la mise à l'échelle sont vérifiés par test unitaire ; l'orientation visuelle de chaque objet n'a pas été comparée une à une avec le jeu.

## Correction 1

Rien n'est commité, aucun sous-agent lancé, rien écrit dans le dossier Jedi Academy (essais en `--run-to export`).

### C1 — enroulement MD3
- `Md3Writer` écrit chaque triangle dans l'ordre (i0, i2, i1), comme `RenderSurfaces` pour le BSP. `Md3Reader` rétablit l'ordre KOTOR en échangeant les deux derniers indices, comme `FromBsp` : la vue Exporter montre à l'endroit ce que le moteur montrera à l'endroit, et la vue Importer (qui relit les octets de l'écrivain) aussi.
- Surfaces Cutout (`cull none`) : non concernées, l'enroulement n'a pas d'effet sur elles (le shader désactive l'élimination) ; la vue les dessine en double face comme avant.
- Empreinte figée du BSP sans sous-modèle (étape 04) : inchangée (le BSP n'est pas touché ; tests Compiler verts).
- Tests : `Md3WriterTests.Md3_triangles_are_written_in_JA_winding` (ordre des indices dans le fichier : 0,2,1,1,2,3 pour 0,1,2,1,3,2) ; `PreviewBuilderTests.Md3Reader_restores_KOTOR_winding_after_write` (aller-retour qui retrouve l'ordre KOTOR) ; `Md3Reader_round_trips_writer_output` existant.
- Essai réel : `--game kotor --module ebo_m41aa --run-to export --camera "1235 1330 100 90 0" --screenshot %TEMP%\ebo_crates.png --exit`, code de sortie 0, copié dans `reports/step-05-fix-shot.png` : les conteneurs sont pleins et texturés (plus creux). Vérifié dans la vue à l'oeil ; non revérifié en jeu (le jeu n'est pas lancé).

### Texte français
- `KotorWorkflowSteps.cs:89` et `:126` : « placed model(s) ». `Compiler.cs:81` : « Sub-model *N: degenerate or unbounded brush ignored. » (aucun test ne dépendait du texte).

### Mode automatisé
- `AppGame` : la liste des modules est toujours chargée (tâche hors fil d'interface) ; le mode automatisé l'attend avant de sélectionner le module, donc l'arbre montre le module surligné (visible dans les captures).
- Sans `--camera` et avec un point d'apparition : caméra 64 unités en arrière du regard et 48 plus haut, inclinée vers le point (pitch -36,9°). Essai : `--game kotor --module tar_m02aa --run-to export --screenshot %TEMP%\tar_default.png --exit`, code 0, copié dans `reports/step-05-fix-default.png` : scène lisible, cube jaune à distance, plus d'aplat.

### Minor
- `modelscale` ignoré pour les portes (`InstancesOf`, seul `misc_model_static` l'applique).
- Modèle sans maillage dans la vue Importer : `PREVIEW_MODEL_MISSING` ; MD3 illisible : `PREVIEW_MODEL_UNREADABLE` ; `origin` invalide : `PREVIEW_MODEL_BAD_ORIGIN` (anglais). Vue Exporter : `PREVIEW_MD3_MISSING` / `PREVIEW_MD3_UNREADABLE` existants (pas de doublon). `FromImport` reçoit un `ImportLog?` optionnel. Pas de test dédié aux nouveaux codes ni à `modelscale` des portes (réserve).
- `--screenshot`, `--exit`, `--game`, etc. sans `--module` : message `--module is required ...` et code 2 (test `Automation_none_without_module_and_error_for_options_alone`).

### Vérification
- `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur. `dotnet test Kotor2Ja.sln` : vert (Cli 20, Compiler 80, Core 152, Pipeline 74, aucun ignoré).
