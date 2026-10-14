# Relecture finale de la branche phase2-app (phase 1 + phase 2)

Plage : `d5d500d` (master) .. `e55af10` (phase2-app). Périmètre : `src/`, `tests/`, les deux registres et les rulings ; les documents servent de contexte.
Contrôle : `dotnet test Kotor2Ja.sln` sur `e55af10` : 202 tests réussis, 0 échec, 0 ignoré (KOTOR présent, donc les tests réels ont tourné). L'arbre reste propre après les tests.

## Points forts

- **Découpage clair.** `PipelineSteps` (Prévisualiser / Importer / Exporter / Créer PK3) est le seul chemin d'exécution. La CLI (`ModuleExporter.Export`) et l'application (`KotorWorkflowSteps`) passent toutes les deux par lui, ce qui rend le critère 3 de la spec de la phase 2 (pk3 identiques) vrai par construction.
- **Conventions tenues d'un module à l'autre.** Les UV suivent le ruling de l'étape 03 : images avec la ligne 0 en haut, `FlipV` dans `BuildPlan` et `FromScene`, UV Q3 reprises telles quelles dans `FromImport` et `FromBsp`. L'échelle (mètres × `Scale`) est la même pour la scène, les collisions et les repères. L'enroulement JA est inversé une seule fois, à la relecture du BSP. Les resrefs sont en minuscules partout (`ResourceKey`, textures, noms de shader, entrées du pk3).
- **Journal en grande partie cohérent.** Les codes sont stables, les cas de masse sont regroupés par pièce, les exceptions des lecteurs sont converties en codes (`ROOM_MODEL_UNREADABLE`, `TEXTURE_UNSUPPORTED`, `GIT_*`), et `PipelineStepException` porte un code déjà écrit au journal.
- **Écritures sûres.** Le BSP est construit en mémoire, et rien n'est écrit si une limite est dépassée. Le pk3 passe par un fichier `.tmp` puis un `File.Move` : l'ancien n'est remplacé que si tout réussit. KOTOR n'est ouvert qu'en lecture (`FileShare.Read`). Les tests écrivent dans des dossiers temporaires (`base`/`work` sous `%TEMP%`).
- **Automate sûr entre fils.** Tout l'état passe par `_gate`. Un compteur de génération écarte les résultats périmés. `_busy` est remis à zéro dans un `finally`. Le journal passe par une `ConcurrentQueue`. Le transfert vers le GPU se fait dans le fil de rendu, et les modèles de vue sont immuables. Les tests de l'automate utilisent des `TaskCompletionSource` et non des `Sleep`.
- **Ressources libérées.** `ResourceResolver` libère ses archives même quand l'ouverture échoue (`catch { r.Dispose(); throw; }`). Les BIF sont ouverts à la demande puis libérés. `PreviewRenderer.Clear()` libère tout ce qu'il a envoyé au GPU (le damier partagé à part). Il n'y a plus de `imgui.ini`.
- **En-têtes GPL-2.0 présents** sur tous les fichiers de `Kotor.Compiler`. L'origine du code est documentée dans `THIRD-PARTY-NOTICES.md`.

## Problèmes

### Critique

Aucun.

### Important

**I1 — Le compilateur retire des collisions pour corriger un dépassement de limite qui n'a rien à voir avec elles (`MAX_MAP_LEAFFACES`) ; le ruling qui relève cette limite n'est pas appliqué.**
`src/Kotor.Compiler/Compiler.cs:106`, `src/Kotor.Compiler/Bsp/LimitCheck.cs:14,36`, `src/Kotor.Compiler/Bsp/Limits.cs:18`.
- **Pourquoi.** La boucle de réduction intercepte *toute* `BspLimitException` et retire 20 % de la collision solide à chaque tour, puis le sol marchable. Sur tar_m02aa, les leafsurfaces sont à 128 066 pour une limite de 131 072 (marge de 2 %, `step-09-fix-1.md`), et ce nombre dépend du nombre de feuilles, donc du nombre de brushes. Sur un module un peu plus grand, le compilateur va donc « réussir » en supprimant les murs les plus petits jusqu'à passer sous la limite de leafsurfaces. Le journal dira `COLLISION_CAPPED … pour tenir dans les limites BSP`, et la vue Exporter ne le montrera pas, puisqu'elle reprend la collision de l'étape Importer. On marche alors à travers les murs, voire on tombe à travers le sol, ce qui est exactement un anti-objectif de la spec. Le rapport de l'étape 09 dit que le dépassement donne un « échec propre » : c'est faux depuis l'ajout de la boucle de réduction. Le registre (ruling de l'étape 09) prévoyait de relever `MAX_MAP_LEAFFACES` dans le profil Swgl « dans la vague de corrections finale », et ce n'est pas fait (`LimitCheck.MaxLeafSurfaces` est une constante, indépendante du profil). Pour les limites de rendu qui ne dépendent pas de l'arbre (sommets, index, shaders), la même boucle fait jusqu'à 80 reconstructions inutiles avant d'échouer : c'est le mineur de l'étape 07.
- **Correction.** (a) Déplacer `MaxLeafSurfaces` dans `Limits`, à `0x100000` pour Swgl et `0x20000` pour Vanilla. (b) Donner à `BspLimitException` l'information « limite de collision ou non » (le nom du lump suffit), et n'intercepter dans la boucle que les limites de collision (brushes, cotés, plans, nœuds, feuilles, leafbrushes). Une limite de rendu doit échouer tout de suite avec `BSP_ERROR`. (c) Ajouter un test : une scène qui dépasse les leafsurfaces avec un profil réduit doit échouer sans aucune collision retirée.

**I2 — Le nom de carte n'est pas validé : il sert à une suppression récursive et au chemin du pk3.**
`src/Kotor.Pipeline/PipelineSteps.cs:53,71,124-125`, `src/Kotor.Pipeline/ModuleExporter.cs:37`, `src/Kotor2Ja.Cli/Commands/ExportJaCommand.cs:16`.
- **Pourquoi.** `--name` est seulement mis en minuscules, puis passé à `Path.Combine(WorkDir, name, "pk3")` suivi de `Directory.Delete(stage, true)`, et à `Path.Combine(JaBaseDir, "kotor_" + name + ".pk3")`. Avec `--name ..\..\x`, on supprime récursivement un dossier hors du dossier de travail et on écrit le pk3 hors de `base`. La spec dit pourtant « dans GameData, on n'écrit que le pk3 ». Un cas bien plus probable : `--name "Taris Appart"` (espace, majuscules ou accents) produit une carte que `devmap` ne peut pas charger, et un nom long dépasse MAX_QPATH (`maps/<nom>.bsp`, `shaders/kotor_<nom>.shader`) sans que rien ne le signale. Le défaut est le même pour la CLI et pour l'application ; dans l'application, le nom vient du module, ce qui le rend sûr aujourd'hui.
- **Correction.** Valider le nom dans `ExportOptions`/`PipelineSteps.Import` (`^[a-z0-9_]+$`, longueur bornée pour que `textures/kotor/`, `maps/<nom>.bsp` et `kotor_<nom>.pk3` tiennent dans 63 caractères). En cas d'échec, lever `PipelineStepException("MAP_NAME_INVALID", …)`. Ajouter un test.

### Mineur

| # | Fichier:ligne | Problème | Pourquoi | Correction |
|---|---|---|---|---|
| M1 | `src/Kotor.Pipeline/PipelineSteps.cs:124` | `Directory.CreateDirectory(opt.JaBaseDir)` (mineur de l'étape 08) | Une faute dans la config crée une arborescence fantôme (sous Program Files si l'UAC le permet). Le pk3 y atterrit « avec succès » alors que le jeu ne le verra jamais. Le défaut touche l'application comme la CLI. | Refuser si le dossier n'existe pas : `PipelineStepException("JA_BASE_MISSING", …)`. |
| M2 | `tests/Kotor.Core.Tests/TestEnv.cs:24` | `TestEnv.JaBaseDir` pointe toujours vers `GameData\base` (mineur de l'étape 01) | Il n'est plus utilisé, mais un futur test pourrait écrire dans le jeu. Il a déjà été retiré de `Kotor.Pipeline.Tests` pour cette raison (ruling de la correction 1 de l'étape 01 de la phase 2). | Le supprimer. |
| M3 | `src/Kotor2Ja.App/Ui/MainScreen.cs:16,50` | Le journal de l'application est plafonné à 5 000 lignes, les plus anciennes sont jetées, et aucun fichier `.journal.txt` n'est écrit (contrairement à la CLI) | Sur un gros module, les codes par maillage (`MDL_NO_UV`, `LIGHTMAP_MISSING`, `SHADER_NAME_CONFLICT`…) peuvent dépasser 5 000 lignes : des pertes signalées disparaissent alors sans trace, ce qui contredit « rien en silence ». | Écrire le journal de chaque étape dans `<WorkDir>\<carte>\<carte>.journal.txt` (même format que `ModuleExporter.WriteJournal`), ou ne jamais supprimer l'historique et utiliser `ImGuiListClipper`. |
| M4 | `src/Kotor.Pipeline/Workflow/KotorWorkflowSteps.cs:29,40` | `Modules()` avale les erreurs TLK et module (`catch … { }`) | Un module illisible apparaît sous son resref, sans aucune ligne au journal ; c'est le pendant, côté application, du mineur de l'étape 03 sur `ListModulesCommand`. | Renvoyer un avertissement par module (par exemple « (illisible : …) » dans `AreaName`, comme la CLI) et journaliser `TLK_UNREADABLE`. |
| M5 | `src/Kotor2Ja.Cli/Commands/ExportJaCommand.cs:26` | `ConfigLoader.Load` n'est pas protégé dans la CLI | Un JSON invalide fait planter la CLI avec une pile d'appels. L'application, elle, a été protégée (correction 1 de l'étape 04). | Même `catch` que `AppGame.Initialize`, code de sortie 1. |
| M6 | `src/Kotor.Core/Scene/ModuleSceneBuilder.cs:75`, `src/Kotor.Pipeline/PipelineSteps.cs:37`, `src/Kotor.Core/Entities/ModuleEntitiesReader.cs:161` | Filtres d'exceptions incohérents | `ModuleEntitiesReader` intercepte `ArgumentException`/`IndexOutOfRange`/`EndOfStream` sur un MDL tronqué, mais pas `ModuleSceneBuilder` ni `PipelineSteps.Preview`. Un fichier Override abîmé tue donc la CLI `export-ja` sans écrire de journal ; dans l'application, il donne « Exception inattendue » sans code. Ce point rejoint le mineur « GffReader offsets non bornés ». | Aligner les filtres : un seul helper `IsReadError(ex)` dans `Kotor.Core`. |
| M7 | `src/Kotor.Compiler/Compiler.cs:185`, `src/Kotor.Compiler/Bsp/BspWriter.cs:176`, `src/Kotor.Pipeline/ModuleExporter.cs:155` | Entités écrites sans échappement, en ASCII | `message` = nom de zone du TLK (français) : un `"` casse le lump d'entités, et les accents deviennent `?`. | Remplacer `"` par `'` et retirer les caractères de contrôle. Au choix, encoder en 1252 ou translittérer. |
| M8 | `src/Kotor2Ja.App/ImGuiRenderer.cs:51`, `src/Kotor2Ja.App/Ui/MainScreen.cs:192,313` | Police ImGui par défaut (plage 0x20-0xFF) | « — » (libellés des modules, format imposé par la spec) et « … » (bouton occupé) s'affichent `?`. Il en va de même pour `’` dans les noms TLK. L'utilisateur va le voir dès la vérification manuelle (étape 05). | Remplacer par `-` et `...`, ou charger une police TTF avec les plages voulues. |
| M9 | `src/Kotor.Core/Export/ShaderWriter.cs`, `MaterialAlpha.cs`, `ShaderNames.cs:21-22`, `src/Kotor.Compiler/Bsp/GlobalFog.cs`, `src/Kotor.Core/Diagnostics/ImportLog.cs:3` | Code mort repris de TFU | `ShaderWriter` et `MaterialAlpha` ne servent qu'à leurs tests ; `NoisePrefixes` contient des préfixes TFU (`game_disc_`). `_kotor_fog` n'est jamais émis, donc `GlobalFog` ne sert à rien. La doc d'`ImportLog` cite des codes TFU (`bad-xml`, `marker-no-actor`…). | Supprimer `ShaderWriter`, `MaterialAlpha` et leurs tests, ainsi que `NoisePrefixes`. Corriger les commentaires. Garder `GlobalFog` si le brouillard est prévu (phase 3), sinon le retirer. |
| M10 | `THIRD-PARTY-NOTICES.md`, `src/Kotor2Ja.App/ImGuiRenderer.cs:1-2` | Notice MIT incomplète | La licence MIT exige l'avis de copyright et le texte de permission. L'en-tête est une paraphrase française, et la notice dit « l'en-tête d'origine est conservé ». | Ajouter le texte MIT complet d'ImGui.NET (copyright Eric Mellino et contributeurs) dans la notice. |
| M11 | dépôt (pas de `LICENSE`), `src/Kotor.Compiler/*` | Licence du produit assemblé | Le compilateur est sous GPL-2.0 et lié dans `Kotor2Ja.App` avec MonoGame (MS-PL, que la FSF considère incompatible avec la GPL). L'utilisateur est l'auteur du code GPL (TFU, aucun code de q3map2), donc il peut le régler, mais rien ne le dit aujourd'hui. Cela ne compte qu'en cas de diffusion. | À trancher par l'utilisateur : un `LICENSE` à la racine, plus une exception de liaison pour MonoGame dans les en-têtes, ou une autre licence. |
| M12 | `src/Kotor.Core/Entities/ModuleEntity.cs:7`, `src/Kotor.Pipeline/Preview/PreviewModel.cs` (`PreviewMarker`), `PreviewBuilder.cs:61` | Deux conventions de yaw | `ModuleEntity.Yaw` vaut 0 face à +Y (Bearing), alors que le yaw du point d'apparition et celui de la caméra valent 0 vers +X (`angle` Q3). `PreviewMarker` dit seulement « autour de Z ». Ce n'est pas encore dessiné, mais la phase 3 convertira ces yaws en `angle`. | Écrire la convention dans `PreviewMarker`, ou convertir en convention Q3 (+90°) dès `FromImport`. |
| M13 | `src/Kotor.Core/Entities/ModuleEntitiesReader.cs:148-173` | Chaque MDL de pièce est relu, et `ROOM_MODEL_MISSING`/`UNREADABLE` est journalisé deux fois à l'étape Prévisualiser de l'application | Le décompte par code est faux (doublé), et la lecture se fait deux fois. | Faire exposer les lumières par `ModuleSceneBuilder`, ou passer les modèles déjà lus au lecteur d'entités. |
| M14 | `src/Kotor2Ja.Cli/Commands/ListModulesCommand.cs:10`, `ExportObjCommand.cs:20` | `list-modules` et `export-obj` ignorent la config (`kotorDir`) alors que `export-ja` la lit ; `DefaultKotorDir` est défini en double | La même commande ne trouve pas le même KOTOR selon la sous-commande. | Une seule résolution via `ConfigLoader`. |
| M15 | `src/Kotor2Ja.App/Rendering/PreviewRenderer.cs:126-141` | Fuite partielle si `Upload` échoue au milieu d'un lot | Le `vb` d'un lot n'est pas encore dans `_batches` quand la création de l'`ib` lève, donc `Upload(null)` ne le libère pas. | Ajouter le lot avant de créer l'IB, ou utiliser un `try`/`Dispose` local. |
| M16 | `PreviewBuilder.cs`, `PreviewRenderer.cs`, `AppGame.cs`, `ImGuiRenderer.cs` | Fichiers en UTF-8 avec BOM | C'est incohérent avec le ruling qui a restauré `Compiler.cs` sans BOM (sans effet sur le fonctionnement). | Réenregistrer sans BOM. |
| M17 | `src/Kotor2Ja.Cli/Commands/ExportObjCommand.cs:52-60` | `catch` limité à `InvalidDataException` ; les écritures OBJ et journal ne sont pas protégées | Une erreur d'E/S donne un plantage avec pile d'appels. | Intercepter `IOException`/`UnauthorizedAccessException`, code de sortie 1. |

## Tri des mineurs du registre

### À corriger avant fusion

- **Ruling de l'étape 09 de la phase 1 : `MAX_MAP_LEAFFACES` relevé dans le profil Swgl.** Le registre l'a explicitement renvoyé à « la vague de corrections finale ». Sans lui, I1 retire des collisions. Traité dans I1.
- **Étape 07 : limites non liées à la collision → jusqu'à 80 reconstructions inutiles.** C'est le même mécanisme qu'I1. Dès qu'on distingue les limites de collision des autres, ce mineur disparaît.
- **Étape 08 : `Directory.CreateDirectory(JaBaseDir)` peut créer un dossier** (M1). Un export « réussi » vers un dossier que le jeu ne lit pas, et des écritures hors du seul pk3 autorisé dans GameData. La correction tient en une ligne.
- **Étape 01 : `TestEnv.JaBaseDir` en dur** (M2). C'est la même raison que le ruling de la phase 2 qui l'a supprimé de `Kotor.Pipeline.Tests`. Le risque est d'écrire dans le jeu, et le coût est nul.
- **Étape 03 de la phase 2 : collision de la vue Exporter reprise de l'Import.** Pas la refonte : la correction minimale. Tant qu'I1 n'est pas corrigé, la vue Exporter affiche des collisions qui ne sont pas dans le BSP, ce qui contredit « la vue montre exactement ce qui part dans la carte ». Une fois I1 corrigé, ce cas n'arrive plus qu'avec `COLLISION_CAPPED`/`WALKABLE_CAPPED`. Il suffit alors que l'étape Exporter ajoute une ligne d'erreur visible (« collision affichée = collision prévue ; N polygones retirés au BSP ») quand ces codes sont présents. La relecture des brushes du BSP peut attendre.

### Peut attendre (raison)

- **Phase 1, étape 01.**
  - MeshSimplifier et TriangleBudget sans tests propres : déjà couverts indirectement par `WalkableCollisionTests`.
  - Commentaires TFU : cosmétique, à regrouper avec M9.
  - PngTestReader inutilisé, PngWriter sans test : ne sert qu'au contrôle OBJ.
  - Kotor2Ja.Cli.Tests vide : **résolu**, 5 tests.
- **Phase 1, étape 02.**
  - Override sensible à la casse hors Windows : l'outil vise Windows.
  - Type inconnu → extension numérique : aucun type inconnu n'est demandé.
  - resRef non validé avant Path.Combine : lecture seule, et données du jeu.
  - RimArchive en ushort : les types KOTOR tiennent sur 16 bits.
  - KeyBif varCount*16 hors `checked` : seulement sur un fichier corrompu.
  - Pas de test d'ordre `_s.rim` : comportement vérifié sur des données réelles.
  - Archives non thread-safe : chaque étape a son propre résolveur ; aucun partage aujourd'hui.
- **Phase 1, étape 03.**
  - GffReader non borné : seulement sur un fichier corrompu, à regrouper avec M6.
  - catch de ListModulesCommand : CLI de diagnostic.
  - TLK/ARE absent sans avertissement : cosmétique (nom affiché).
  - Ordre w,x,y,z du GFF : ne sert qu'au yaw des caméras dans la vue ; à voir en phase 3.
  - CodePagesEncodingProvider dupliqué, test TLK prioritaire : sans effet.
  - ModuleInfo.Module pas en minuscules : les appelants mettent déjà en minuscules.
- **Phase 1, étape 04.**
  - EnvMapTexture retombe sur bumpyshinytexture : seulement un message de journal.
  - TPC d'encodage non géré : déjà converti en `TEXTURE_UNSUPPORTED` (ruling).
  - Ordre des champs des doorhooks : parqué jusqu'à la conversion des portes (ruling de la phase 2).
  - Ordre tpa/BIF non testé, tests cube DXT et TXI séparé, Find sans validation de null : couverture seulement.
- **Phase 1, étape 05.** Light/Emitter/Reference+Mesh, allocation `faceCount*3`, MaxDepth, NodeName périmé, tests K2, quaternions non normalisés, quaternions compressés : rien de tout cela n'arrive en vanilla, et chaque cas est journalisé (`ROOM_MODEL_UNREADABLE`) ou porte sur des données corrompues. `out/mdl-stats.txt` qui s'accumule : le dossier est ignoré par git.
- **Phase 1, étape 06.**
  - Aabb avec Render=true : seulement une étiquette de code.
  - `usemtl` OBJ sans mtl : sortie de contrôle.
  - Libellé « Textures manquantes » : cosmétique.
  - Tests synthétiques des codes : couverture seulement.
- **Phase 1, étape 07.**
  - « Collision : N brushes » compté comme avertissement : cosmétique.
  - Dérive de tolérance des plans (≈ 0,05 unité) : validé en jeu.
  - BOM de Compiler.cs : **résolu**.
  - Plafond de 80 tentatives : devient sans objet avec I1.
- **Phase 1, étape 08.**
  - SHADER_NAME_CONFLICT par maillage : du bruit dans le journal (aggrave M3, sans plus).
  - Repli d'apparition sur un walkmesh sans triangle marchable : ne joue que si l'entrée est hors walkmesh, et c'est journalisé (`SPAWN_FALLBACK`).
  - Test de texture manquante sans vérifier `TEXTURE_MISSING` : couverture seulement.
- **Phase 2, étape 01.**
  - Pas de tests SHADER_LIMIT, OPTIONS_INVALID, MODULE_UNREADABLE ; assertion de shader faible ; mise en forme de BspReaderTests ; BSP_ERROR non testé ; relecture « plan ou plan+36 » permissive : couverture seulement.
  - Lump vide à l'offset 0 : `BspReader` ne lit que ce qu'écrit `BspWriter`.
- **Phase 2, étape 02.**
  - Tests synthétiques des lumières et autres : couverture seulement.
  - Convention du yaw et géométrie des rencontres : phase 3 (voir M12).
  - ENTITIES compté comme avertissement : cosmétique.
- **Phase 2, étape 03.**
  - Repères de la vue Exporter repris de l'Import : les entités ne partent pas encore dans le BSP, c'est donc fidèle.
  - Collision différente entre Prévisualiser et Importer : les ensembles sont en fait les mêmes (tous les triangles du WOK).
  - Clé commune diffuse/lightmap : les noms des lightmaps KOTOR (`*_lm*`) ne recoupent pas ceux des diffuses.
- **Phase 2, étape 04.** Lots transparents non triés, étiquettes sans occlusion, journal sans clipper, `OrderBy` à chaque image : rendu de contrôle, sans effet sur la carte (le clipper est repris dans M3).

## Écarté

- Le `.bsp` est écrit dans `<WorkDir>\<carte>\pk3\maps\` et non dans `<WorkDir>\<carte>\<carte>.bsp` comme le dit la spec de la phase 2 (§4) : seul l'emplacement du staging change, et le pk3 est identique.
- La vue Exporter ne dessine pas les brushes de collision relus du BSP : traité dans le tri (correction minimale exigée, la relecture peut attendre).
- `kotor_<carte>.pk3.tmp` est écrit brièvement dans `GameData\base` : c'est le prix du remplacement atomique, et le fichier est supprimé dans tous les cas.
- Ordre de résolution Override avant les RIM du module : il suit la spec (§ résolveur) ; la fidélité au moteur pour GIT/ARE dans Override n'est pas demandée.
- `DualTextureEffect` (diffuse × lightmap × 2) plus clair que le rendu en jeu : vue de contrôle, et la spec demande ce composant.
- Point d'apparition de la vue Prévisualiser posé au sol (`Entry * scale`, sans hauteur ni repli) : la vue montre la scène KOTOR brute ; le point réel apparaît à l'étape Importer.
- Recliquer sur le module déjà sélectionné remet le déroulé à zéro : c'est conforme à « changer de module revient à l'état initial ».
- `Reset()` pendant une étape laisse `KotorWorkflowSteps` avec l'état de l'étape écartée : sans effet, puisque l'automate exige un nouveau Prévisualiser, qui remet tout à zéro.
- AZERTY : A et Q font tous deux un pas à gauche : choix assumé de la caméra TFU (§7 de la spec).
- Limites Swgl de collision à `0x100000` : ruling de l'utilisateur, validé en jeu.
- Les 4 maillages « magenta » de m02aa : la validation en jeu de l'étape 09 n'en a pas fait un défaut.
- Performances en jeu (FPS) : hors périmètre (pilote NVIDIA, puis optimisation mise de côté par l'utilisateur).
- Licence MS-PL de MonoGame et notice des paquets de test : correctes telles quelles (seul M11 porte sur l'assemblage).

## Verdict

**Prêt après corrections.** Il faut corriger I1 et I2, puis les cinq mineurs du registre exigés plus haut (dont M1 et M2). La vérification manuelle de l'application par l'utilisateur (étape 05 de la phase 2) reste à faire avant la fusion. M8 (police) mérite d'être corrigé avant cette vérification pour ne pas la parasiter.
