# Étape 05 — relecture (PNJ retirés, modèles dans les vues, pilotage en ligne de commande)

Diff relu : `bfcd150..b1d5035`. Sources lues dans l'arbre de travail (HEAD). Moteur de référence relu : `OpenJK-SWGL` (`g_misc.cpp`, `cg_main.cpp`, `cg_ents.cpp`, `rd-vulkan`). Les tests n'ont pas été relancés. Un seul essai : l'exécutable Release avec `--run-to foo --module x` (stderr redirigé vers un fichier) donne bien le message, l'usage et le code 2.

## Verdict 1 — Conformité : ❌ (un écart de texte, deux remarques de méthode)

Conforme :
- PNJ : `Creature List` / `Encounter List` ne sont plus dans `Specs`, `NPC_SKIPPED` (anglais) donne les nombres par type, `ENTITIES` ne compte plus Creature/Encounter (`ModuleEntitiesReader.cs`, `WarnNpcs`, ligne `counts`).
- Vue Importer : maillage à la place des cubes pour objets et portes, autres types gardent leurs repères, cases à cocher conservées.
- Vue Exporter : BSP relu + MD3 du staging via `Md3Reader` (dans `Kotor.Pipeline`), portes fermées à `origin`.
- Ligne de commande : analyse avant toute fenêtre, code 2 sur argument invalide, `AutomationParser` testable dans `Kotor.Pipeline`, ordre des étapes, `pk3` uniquement avec `--run-to pk3` (la boucle n'appelle `NextAsync` que tant que `reached < StageIndex`), code 1 sur échec avec `--exit`.
- Fichiers produits inchangés : `Md3ShaderWriter.HasCutout` est une extraction à comportement identique ; rien n'est écrit par le code ajouté (relecture seule du staging).
- Écriture dans `GameData\base` : aucune hors `--run-to pk3`.
- Tests demandés présents (`Creatures_not_read`, `Import_view_has_model_instances`, `Automation_*`, `Real_tar_m02aa_export_view_has_md3`).

Écarts :
1. **Texte visible nouveau en français** : `src/Kotor.Pipeline/Workflow/KotorWorkflowSteps.cs:89` et `:126` ajoutent `{n} modele(s) pose(s)` aux messages de progression affichés dans le journal. Contrainte « tout nouveau texte visible en anglais » non respectée (le reste du message est ancien, mais ce fragment est neuf).
2. Méthode : le rapport reconnaît que l'étape « tests qui échouent » n'a pas été observée (code écrit avant les tests). Non bloquant pour le résultat, mais contraire au protocole du plan.
3. Essai réel : la caméra par défaut est dans le cube du point d'apparition, donc une capture sans `--camera` est un aplat jaune (signalé dans le rapport). Le plan demandait une capture non uniforme ; elle l'est avec `--camera` (`step-05-shot.png` non uniforme, vérifié à l'œil).

## Verdict 2 — Qualité

### Critical

**C1. Sens d'enroulement des triangles MD3 : le lecteur et la vue Exporter ne reflètent pas le moteur (et l'écrivain MD3 est probablement faux).**
- Faits : le compilateur émet l'enroulement JA `(v0, v2, v1)` pour le BSP (`src/Kotor.Compiler/Bsp/RenderSurfaces.cs:84`, test `Horizontal_triangle_with_up_normal_uses_JA_winding`, mesuré sur des cartes JA d'origine : face avant = horaire). `FromBsp` rétablit la convention de la vue en échangeant deux indices (`PreviewBuilder.cs:155-159`). Le moteur utilise `frontFace = CLOCKWISE` pour tout (`rd-vulkan/vk_pipelines.cpp:1458`) et charge les MD3 sans toucher aux triangles (`rd-vulkan/tr_model.cpp:1160-1169`). Or `Md3Writer` écrit les indices tels quels (`Md3Writer.cs:126`, rapport étape 03 : « conservé tel que KOTOR, CCW, non vérifié visuellement »), et `Md3Reader` les relit tels quels (`Md3Reader.cs:221-225`), sans l'échange fait pour le BSP (`PreviewBuilder.Models.cs:37`).
- Scénario d'échec : en jeu, chaque objet et chaque porte converti apparaît à l'envers (faces avant supprimées, on voit l'intérieur du modèle, ou rien pour un modèle fermé vu de l'extérieur). La vue Exporter, elle, affiche les modèles à l'endroit (CCW + `CullClockwiseFace`) : elle valide donc visuellement un fichier que le moteur affichera mal, ce qui contredit le but « poser comme le moteur le fera ». La capture `step-05-shot.png` ne peut pas détecter le défaut.
- Correction attendue : (a) corriger `Md3Writer` (émettre `v0, v2, v1`) après confirmation en jeu ; (b) dans `Md3Reader`/`ModelBatches`, échanger deux indices comme `FromBsp` pour que la vue Exporter montre l'état moteur, et l'échanger aussi côté vue Importer (qui part des octets de l'écrivain) ; (c) un test qui compare le sens du premier triangle d'un MD3 écrit à celui d'une surface BSP de même entrée. Le défaut d'origine est à l'étape 03, mais l'étape 05 est celle qui devait le rendre visible.

### Important

Aucun autre constat Important. (Le point sur la liste des modules ci-dessous est classé Minor : aucun effet sur les fichiers produits.)

### Minor

- **M1. Liste des modules jamais chargée en mode automatisé** (`src/Kotor2Ja.App/AppGame.cs:81`). La capture montre « Chargement des modules... » dans les deux arbres, aucun module surligné, bouton « Prévisualiser » actif. Le workflow n'en dépend pas (`SelectModule` sans le catalogue) et il n'y a ni gel ni attente infinie, mais les captures automatiques ne montrent pas l'état réel de l'interface (module sélectionné). Idéal : charger la liste (hors fil d'interface) et attendre `SetModules` avant la capture, ou surligner le module sélectionné.
- **M2. `modelscale` appliqué aux portes** (`PreviewBuilder.Models.cs:63-65`) alors que le moteur ne lit pas `modelscale` pour `func_door` (la mise à l'échelle n'existe que dans `SP_misc_model_static`, `g_misc.cpp:395-417`). `DoorConverter` n'écrit jamais `modelscale` aujourd'hui (portes trop grandes sautées), donc sans effet actuel ; incohérent si une porte l'obtient un jour. Le reste du placement est cohérent : `origin`, rotation yaw anti-horaire (`CreateRotationZ` en vecteurs ligne = `AnglesToAxis`), échelle avant rotation (équivalent à `ScaleModelAxis`), porte à `origin` avec `modelAngles` (`cg_ents.cpp:1349-1358`), conformes aux entités de `PlaceableConverter` et `DoorConverter` (étapes 03 et 04). Roulis et tangage ignorés (nos entités n'ont que le lacet).
- **M3. Abandons non journalisés dans la construction des instances.** `InstancesOf` (`PreviewBuilder.Models.cs:59-61`) ignore sans code une entité dont `origin` est invalide ou dont le modèle est inconnu ; l'`InvalidDataException` de la vue Importer est avalée sans code (`:96`). Contraire à « aucun abandon silencieux » (rare : nos propres fichiers). De plus, un objet dont le MD3 manque au staging (`PREVIEW_MD3_MISSING`, journalisé) n'a plus de repère dans la vue Exporter, puisque son repère a été retiré à l'étape Importer : il disparaît de la vue.
- **M4. Retrait de repère par proximité** (`PreviewBuilder.Models.cs:102-112`) : un placeable sauté (`PLACEABLE_SKIPPED`) situé au même point (à 0,1 unité) qu'un placeable converti perd son repère ; coût O(n×m).
- **M5. `KindsPresent` alloue à chaque image** (`PreviewRenderer.cs:100`, appelé deux fois par image dans `MainScreen.cs:151-154`) : `Concat/Distinct/ToList`. À mettre en cache dans `Upload`.
- **M6. Sortie console** : `AttachConsole(-1)` ne rend les messages visibles que si la sortie est redirigée vers un fichier ou depuis une console ; via `dotnet run` redirigé ils sont perdus (réserve déjà notée). Application WinExe : un shell ne l'attend pas (`$LASTEXITCODE` non fiable sans `Start-Process -Wait`). À documenter pour les essais automatiques.
- **M7. Options ignorées sans `--module`** : `--screenshot`, `--exit`, `--game` seuls ouvrent l'application normalement et sans message (`AutomationOptions.cs:89`). Conforme à « sans `--module`, rien ne change », mais silencieux.
- **M8. Couverture de tests** : le test réel ne vérifie que des comptes (≥ 10 instances, ≥ 1 porte) ; aucun test ne compare la matrice d'une instance Exporter à son entité (`origin`/`angles`/porte à `origin`), ni le sens des triangles (cf. C1). La machine d'états de `AppGame` n'est pas testable (acceptable).

### Vérifié sans défaut

- **Lecteur MD3 / écrivain** : décalages d'en-tête (108 octets) et de surface (108), shader 68 octets, positions 1/64, normales lat/lng sur 256 (octet haut = lat, comme `R_LatLongToNormal`), UV tels quels (cohérents avec `FlipV` des autres vues), bornes vérifiées par `Need` ; l'aller-retour est testé.
- **Mode automatisé** : `PreviewAsync`/`NextAsync` positionnent `_busy` de façon synchrone sous verrou, donc pas de fausse détection d'échec ; pas de boucle infinie (étape non lançable → `reached < 0` → échec) ; échec d'envoi GPU détecté par `_failedPreview` ; `FailAutomation` met la phase à 4 et sort avec 1 seulement avec `--exit` ; la capture est prise après 3 images, après envoi GPU et placement de la caméra ; échec d'écriture du PNG géré avec code 1.
- **Ressources GPU** : `Upload` appelle `Clear()` qui libère lots du monde, lots de modèles, textures (clés `md3:` comprises), repères ; `Dispose` idem. Les lots de modèles sont partagés entre instances et libérés une fois.
- **PNJ** : `NPC_SKIPPED` n'est émis que pour la lecture complète (pas pour `ReadObjects`), seulement si le nombre est non nul, avec comptes par type ; `ENTITIES` exclut Creature/Encounter ; `GIT_LIST_MISSING` passe de 9 à 7 (test adapté).
