# Importeur de cartes KOTOR → Jedi Academy (SWGL) — Conception

Date : 2026-10-14
Statut : relu par un constructeur, réponses de l'utilisateur intégrées ; en attente d'approbation

## 1. Intention

Convertir les modules de *Star Wars: Knights of the Old Republic* (KOTOR 1, version Steam) en **cartes solo jouables** pour Jedi Academy dans sa version modifiée **SWGL** (moteur OpenJK 64 bits recompilé par l'utilisateur, dépôt `c:\Users\lambe\source\repos\OpenJK-SWGL`).

Le logiciel importe un module, affiche le résultat converti dans une vue 3D, puis exporte une carte que l'on charge en jeu.

**Pourquoi.** SWGL contient déjà un `Taris.bsp` refait à la main en 2009 (`SWGL/SWGL_Map_Taris.pk3`). L'utilisateur veut **une meilleure fidélité** : le vrai Taris de KOTOR, et non une réinterprétation.

**Origine.** Un projet similaire a été mené pour *The Force Unleashed* (`c:\Users\lambe\source\repos\SWGLFU-Map-importer`), puis abandonné parce que les niveaux ne convenaient pas (« niveaux trop petits », d'après l'utilisateur). Ce projet-ci reprend ce qui est utile de ce travail (voir §6).

## 2. Objectifs

### Première version
- Lister les modules KOTOR trouvés dans l'installation, puis en choisir un. La carte de référence est **Taris** (modules `tar_m02aa` … `tar_m11ab`).
- **Unité de conversion : un module KOTOR = une carte JA.**
- La carte contient tout ce qu'on voit dans le module :
  - toutes les pièces du module (géométrie, textures, éclairage) ;
  - le ciel et les éléments de décor lointain ;
  - les **objets placés** (placeables : bancs, caisses, consoles…), à leur position et orientation d'origine ;
  - les **portes**, qui **s'ouvrent** en jeu.
- Les collisions fonctionnent : on ne traverse ni les murs ni le sol, et les objets placés bloquent le passage comme dans KOTOR.
- Un point d'apparition permet d'entrer dans la carte.
- Une vue 3D intégrée, avec une caméra libre, montre **le résultat converti** pour vérifier qu'il ne manque rien.
- Un export produit un pk3 chargeable avec `devmap <nom>` dans SWGL en solo.

### Plus tard (hors première version)
- **Assembler plusieurs modules** en une seule carte (par exemple tout l'Upper City). On étudiera d'abord si c'est faisable avec les limites du moteur.
- Une vue de comparaison avec le module KOTOR d'origine.
- Les portes de transition entre modules, les PNJ, etc. Chaque type d'entité est une étape séparée.

## 3. Non-objectifs
- Pas de conversion des PNJ, des dialogues, des scripts NWScript, des quêtes, des sons ni de la musique dans la première version.
- Pas de multijoueur visé. Si la carte marche aussi en multi, tant mieux, mais ce n'est pas vérifié.
- Pas d'édition de la carte dans l'application. La vue 3D sert à vérifier.
- Pas de KOTOR II dans la première version. SWGL a déjà un pack KOTOR II, et ce n'est pas la demande.
- Pas de dépendance à GtkRadiant ni à un travail manuel dans un éditeur.

## 4. Anti-objectifs (échec même si « ça marche »)
- **Des morceaux qui manquent.** C'est le critère principal de l'utilisateur. Une pièce, un objet, une porte, un pan de ciel ou un décor lointain absent rend la carte ratée, même si le reste est beau. Tout ce qui n'a pas pu être converti doit être **signalé dans le journal**, jamais ignoré en silence.
- Une carte qui ne se charge pas, dans laquelle on tombe à travers le sol ou on traverse les murs.
- Une réinterprétation : textures remplacées, géométrie « nettoyée » ou simplifiée au point de ne plus ressembler à KOTOR.
- Modifier les fichiers des jeux installés. KOTOR est lu en lecture seule. On n'écrit dans le dossier de JA que le pk3 produit.

## 5. Ce que l'on sait (reconnaissance du 2026-10-14)

### KOTOR — `C:\Program Files (x86)\Steam\steamapps\common\swkotor\`
- `modules\` : un module = `<nom>.rim` + `<nom>_s.rim` (environ 70 Mo au total). Taris compte 23 modules : `tar_m02aa`–`af`, `tar_m03aa/ab/ad/ae/af/mg`, `tar_m04aa`, `tar_m05aa/ab`, `tar_m08aa`, `tar_m09aa/ab`, `tar_m10aa/ab/ac`, `tar_m11aa/ab`.
- `chitin.key` + `data\*.bif` : `models.bif`, `textures.bif`, `layouts.bif`, `lightmaps*.bif` (13 fichiers), `templates.bif`, `2da.bif`, etc. (1,3 Go).
- `TexturePacks\` : ERF de textures TPC (642 Mo). Il existe plusieurs qualités ; on prend la meilleure disponible.
- `Override\` est vide (pas de mods installés), `patch.erf` existe.
- Contrairement à TFU, ces formats sont **documentés publiquement** et implémentés par plusieurs projets ouverts (xoreos, PyKotor, KotorBlender, le wiki DeadlyStream). Formats en jeu :
  - archives KEY/BIF, RIM, ERF ;
  - GFF (IFO, ARE, GIT, UTP, UTD…), 2DA ;
  - LYT (disposition des pièces), VIS ;
  - MDL/MDX (modèles), WOK/PWK/DWK (maillages de marche) ;
  - TPC/TGA (textures), lightmaps.

### Jedi Academy SWGL — `C:\Program Files (x86)\Steam\steamapps\common\Jedi Academy - SWGL_DEV\GameData\`
- Exécutables OpenJK 64 bits (`SWGL_SP.x86_64.exe`), rendus rend2 et Vulkan. Le lancement passe par `START.ps1` (sans `fs_game`).
- Dossier `SWGL\` (278 pk3 du mod) et dossier `base\`. L'export TFU écrivait `base\tfu2ja_DAG_1.pk3`.
- Cartes KOTOR déjà présentes : `SWGL_Map_Taris.pk3` (`maps/Taris.bsp`, 2009) et `SWGL_Map_KOTORII.pk3`.

### Projet TFU — `c:\Users\lambe\source\repos\SWGLFU-Map-importer\`
- C# .NET 10. Il comprend :
  - `Tfu.Core` : lecture TFU et export JA ;
  - `Tfu.Compiler` : compilateur BSP maison, sous licence GPL-2.0 et inspiré de q3map2 ;
  - `Tfu.Pipeline` : déroulé en trois étapes et réglages ;
  - `Tfu2Ja.App` : application MonoGame Vulkan + ImGui ;
  - `Tfu2Ja.Cli`, avec ses tests.
- Ses documents (`docs/superpowers/specs`, `plans/ROADMAP-suite.md`, `plans/phase1|2/reports`) décrivent les choix et les problèmes rencontrés. Ils sont à lire avant de copier.

## 6. Décisions de l'utilisateur
- **Fidélité avant tout**, et surtout **rien ne doit manquer**.
- **Carte de référence : Taris.** Cible : **solo**.
- **Un module = une carte.** L'assemblage de modules sera étudié plus tard.
- **Vue 3D : le résultat converti** dans un premier temps.
- **Portes qui s'ouvrent** et **objets placés convertis et positionnés** dès la première version.
- **Réutilisation : on copie depuis le projet TFU ce qui est intéressant, et on abandonne le reste.** Aucun code n'est partagé entre les deux dépôts. Le code de lecture propre à TFU (paks, GTO, disposition XML, classifieurs TFU) n'est pas repris.

- **Échelle** : l'échec du projet TFU (« niveaux trop petits ») venait d'une **erreur d'échelle**. Pour ce projet, l'échelle est donc un point critique, à valider en jeu très tôt.
- **Liberté de mouvement** : on ne met pas de murs invisibles pour enfermer le joueur dans la zone jouable de KOTOR. La question sera reprise plus tard.
- **Éléments animés** (circulation de speeders, textures qui défilent, particules, brouillard) : présents mais **immobiles** dans la première version. Ce qui ne peut pas être représenté, même immobile, est listé dans le journal.
- **Rendu de référence** : Vulkan (`rdsp-vulkan`). L'utilisateur précise que le rendu ne doit pas poser de problème.

## 7. Choix faits par Claude (à valider en relisant)
- **Échelle** : on cale la taille d'un personnage KOTOR sur celle du joueur JA, plutôt que de convertir les mètres en unités (environ 1 m = 39 unités). Le facteur reste réglable dans la configuration. **La première étape d'implémentation qui produit une carte sert à valider l'échelle en jeu** avec l'utilisateur : passer une porte, traverser un couloir, comparer avec la taille des PNJ de SWGL. On ne continue pas tant que l'échelle n'est pas validée.
- **Collisions sans murs invisibles** : les surfaces visibles (pièces et objets) bloquent le joueur. Si un saut permet d'atteindre un trou ou un toit sans fond, c'est accepté pour l'instant, mais ces trous ne doivent pas apparaître dans la zone jouable de KOTOR.
- **Mêmes technologies que le projet TFU**, puisqu'on en copie du code : C# .NET 10, MonoGame (backend Vulkan, repli DesktopGL) + ImGui.NET, CLI avec System.CommandLine, compilateur BSP maison.
- **Même méthode de travail que le projet TFU** (sa `ROADMAP-suite.md` §3) :
  - conception, puis plan, puis exécution par des agents neufs ;
  - un fichier par étape et des rapports écrits dans des fichiers ;
  - communication en français ;
  - fusion locale dans `master`.
- **Dépôt git** : le dossier `SWGLKotor-Map` n'est pas encore un dépôt. On fait `git init` au début de l'implémentation, et ce document y est ajouté.
- **Copier, pas déplacer** : les fichiers repris gardent leur en-tête de licence (GPL-2.0 pour le compilateur). Les candidats probables sont :
  - `Tfu.Compiler` en entier ;
  - dans `Tfu.Core/Export` : `MapWriter`, `ShaderWriter`, `TgaWriter`/`TgaReader`, `PngWriter`, `Pk3Builder`, `MaterialTable` ;
  - le squelette de `Tfu.Pipeline` (déroulé, réglages, aperçu) et `Tfu2Ja.App` ;
  - `ImportLog`.

  La liste finale est fixée au moment du plan, après lecture du code.
- **Éclairage : reprendre les lightmaps de KOTOR** plutôt que recalculer la lumière. C'est le plus fidèle. Si le compilateur ou le moteur ne permet pas d'utiliser des lightmaps externes, il faut le signaler et revenir vers l'utilisateur, plutôt que de passer en silence à un éclairage recalculé.
- **Textures** : la meilleure qualité disponible (TexturePacks), et non la version basse résolution des RIM.
- **Portes** : chaque porte KOTOR (entrée GIT + modèle UTD) devient une porte JA qui s'ouvre à l'approche, avec la géométrie du modèle KOTOR. Les portes verrouillées et les portes de transition vers un autre module restent fermées et sont listées dans le journal.
- **Collisions** : on part du maillage de marche WOK pour le sol, et on ajoute ce qu'il faut pour que les murs visibles bloquent (voir plus haut : pas de murs invisibles). Les objets placés ont une collision tirée de leur maillage (PWK, sinon boîte englobante).
- **Point d'apparition** : à l'entrée du module définie par KOTOR (IFO), sinon au premier point de passage, sinon au centre du module.
- **Sortie** : un pk3 par module, au même endroit que l'export TFU (`base\`, qui a fonctionné), avec un préfixe propre comme `kotor_tar_m02aa`. Le jeu n'est pas modifié ailleurs.
- **Premier module testé** : `tar_m02aa`, le premier module de l'Upper City. On a vérifié l'identifiant, pas le nom de la zone (Nord ou Sud), qu'on lira dans le fichier ARE.
- **Limites du moteur** : le moteur SWGL est un OpenJK recompilé, dont les limites BSP ont peut-être été relevées. On les vérifie dans le dépôt `OpenJK-SWGL` avant de prévoir de la simplification. Comme la fidélité prime, on préfère signaler un dépassement plutôt que de simplifier en silence.

## 8. Critère de réussite de la première version
L'utilisateur charge `tar_m02aa` converti avec `devmap` dans SWGL (solo) et vérifie :
1. la carte se charge sans erreur bloquante, et **l'échelle est juste** (le joueur a la même taille relative que dans KOTOR) ;
2. il apparaît sur le sol, ne tombe pas à travers et ne traverse pas les murs ;
3. **rien ne manque** par rapport au module dans KOTOR : pièces, objets, portes, ciel ;
4. les textures sont les bonnes, à l'endroit, et l'éclairage ressemble à KOTOR ;
5. les portes s'ouvrent ;
6. la vue 3D de l'application montre la même chose que le jeu.

## 9. Laissé au constructeur
- La structure de la solution .NET et les noms des projets.
- La manière exacte de transformer les maillages MDL en géométrie BSP : modèles intégrés de type `misc_model` ou surfaces directes.
- La représentation des portes côté JA (entité, modèle, mouvement d'ouverture).
- La construction des collisions des murs.
- La manière d'intégrer les lightmaps KOTOR dans le BSP, sous réserve de la règle du §7.
- Le choix et l'ordre des modules de test après `tar_m02aa`.
- Le détail de l'interface ImGui.

## 10. Risques
- **Lightmaps KOTOR → BSP JA** : les UV de lightmap et le format diffèrent. Il peut falloir transformer les lightmaps en atlas, ou les appliquer comme seconde texture par shader.
- **Portes** : faire tourner ou glisser un modèle KOTOR dans JA demande une entité de type brush-model ou un modèle séparé. C'est à étudier dans le moteur SWGL.
- **Limites du moteur** : un module KOTOR est petit, mais avec les objets et le ciel, les limites doivent quand même être vérifiées.
- **Compilateur maison** : il a été écrit pour la géométrie TFU. Les cas propres à KOTOR (portes, lightmaps, transparences) peuvent demander de l'étendre.
