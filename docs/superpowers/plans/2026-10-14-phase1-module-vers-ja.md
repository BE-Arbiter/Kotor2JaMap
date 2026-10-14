# Phase 1 — Un module KOTOR (pièces) en carte Jedi Academy : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** `kotor2ja export-ja tar_m02aa` produit `base\kotor_tar_m02aa.pk3`, qui se charge avec `devmap tar_m02aa` dans SWGL. La carte contient toutes les pièces du module, avec leurs textures, leurs lightmaps KOTOR et des collisions, et l'**échelle est validée en jeu** par l'utilisateur.

**Architecture :** on crée une nouvelle solution C# `Kotor2Ja` et on y copie du projet TFU le compilateur BSP maison et les écritures JA (TGA, shaders, pk3, journal). On écrit à neuf la lecture KOTOR :
- archives KEY/BIF, RIM et ERF, puis GFF, TLK et 2DA ;
- LYT, MDL/MDX, WOK et TPC/TGA/TXI.

Un `ModuleSceneBuilder` assemble le module en mètres KOTOR. L'export le met à l'échelle, puis le compilateur reçoit la scène **en mémoire** (plus d'aller-retour par des fichiers OBJ), avec un deuxième jeu d'UV pour les lightmaps.

**Tech Stack :** .NET 10, C#, xUnit + `Xunit.SkippableFact`, System.CommandLine (comme le projet TFU). Aucune bibliothèque d'images : on écrit nos propres décodeurs DXT et TGA.

**Spec :** `docs/superpowers/specs/2026-10-14-kotor-vers-jedi-academy-design.md`

**Exécution :** fichiers par étape dans `docs/superpowers/plans/phase1/` : `context.md` (partagé) + `step-NN.md`. Chaque agent ne lit que `context.md` et son étape.

## Découpage du projet en phases

La spec couvre trop de choses pour un seul plan. Chaque phase produit un logiciel qui marche et passe par sa propre conception et son propre plan.

| Phase | Contenu | Pourquoi dans cet ordre |
|---|---|---|
| **1 (ce plan)** | Lecture KOTOR, pièces du module, textures, lightmaps, collisions, point d'apparition, export pk3, **validation de l'échelle en jeu** | L'échelle a fait échouer le projet TFU. On la fixe avant tout le reste. |
| 2 | Objets placés (GIT + UTP) et portes qui s'ouvrent (GIT + UTD, sous-modèles BSP dans le compilateur) | Ces éléments ont besoin de l'échelle et du compilateur de la phase 1. |
| 3 | Ciel, décor lointain et éléments animés rendus immobiles ; bilan « rien ne manque » sur les 23 modules de Taris | Complétude, critère principal de la spec. |
| 4 | Application MonoGame + ImGui (vue 3D du résultat converti), copiée de `Tfu2Ja.App` | La CLI suffit pour valider les phases 1 à 3. |

## Global Constraints

- KOTOR (`C:\Program Files (x86)\Steam\steamapps\common\swkotor\`) est lu **en lecture seule**.
- Dans `GameData\`, on n'écrit que `GameData\base\kotor_<module>.pk3`. Tout le reste va dans le dossier de travail (défaut `C:\jka_maps\kotor_work`).
- Un module KOTOR donne une carte JA, nommée comme le module (`tar_m02aa` → `maps/tar_m02aa.bsp`).
- **Fidélité** : aucune simplification de la géométrie visible. Tout élément non converti est écrit dans le journal avec un code. On n'ignore jamais rien en silence.
- Échelle réglable (`--scale`). Valeur par défaut `36` unités JA par mètre KOTOR (64 / 1,78 : la hauteur du joueur JA divisée par celle d'un humain KOTOR). L'étape 09 la valide.
- Textures : la meilleure qualité, c'est-à-dire `TexturePacks\swpc_tex_tpa.erf` avant les BIF.
- Le compilateur et les fichiers copiés du projet TFU gardent leur en-tête GPL-2.0. On tient `THIRD-PARTY-NOTICES.md` à jour.
- Rendu de référence : Vulkan (`rdsp-vulkan`), exécutable `GameData\SWGL_SP.x86_64.exe`.
- Code, rapports et messages sont en français. Les identifiants C# sont en anglais.

## Review Focus

1. **Ressource absente** (modèle de pièce, texture ou lightmap introuvable) : on continue l'export avec une texture de remplacement, et le journal indique le code et le nom. Testé aux étapes 06 et 08.
2. **Nœud ou contrôleur MDL non pris en charge** (émetteur, lumière, référence, quaternion compressé, skin, dangly) : rien ne plante. Les maillages skin et dangly sont exportés comme statiques, les autres nœuds sont comptés dans le journal. Testé aux étapes 05 et 06.
3. **Fichier présent dans `Override\`** : il prend le pas sur les RIM et les BIF. Testé à l'étape 02.
4. **Collision au-delà de la limite de brushes JA** (`0x8000`) : on fusionne les triangles coplanaires, puis on plafonne avec un message dans le journal. Pas de plantage et pas de BSP partiel. Testé à l'étape 07.
5. **Casse des noms** (`M02aa_01A` contre `m02aa_01a`) et **texture présente en TPC et en TGA** : la recherche ignore la casse, et le TPC de `tpa` passe en premier. Testé aux étapes 02 et 04.

---

## Étapes

| Étape | Contenu | Dépend de | Modèle |
|---|---|---|---|
| 01 | Solution, `git init`, copie du projet TFU (compilateur, écritures JA, journal) | — | haiku |
| 02 | Archives KEY/BIF, RIM, ERF et `ResourceResolver` | 01 | sonnet |
| 03 | GFF, TLK, catalogue des modules, commande `list-modules` | 02 | sonnet |
| 04 | LYT, textures TPC/TGA/TXI, décodeur DXT | 02 | sonnet |
| 05 | Lecteur MDL/MDX (géométrie, UV0/UV1, hiérarchie, contrôleurs) | 02 | sonnet |
| 06 | WOK, 2DA, `ModuleSceneBuilder`, commande `export-obj` + **contrôle visuel Blender par l'utilisateur** | 03, 04, 05 | sonnet |
| 07 | Compilateur : entrée en mémoire, UV de lightmap, fusion des collisions coplanaires | 01 | sonnet |
| 08 | Export JA : échelle, TGA, shaders lightmap, point d'apparition, pk3, commande `export-ja` | 06, 07 | sonnet |
| 09 | **Validation en jeu de l'échelle et du rendu** (utilisateur), choix de l'échelle par défaut | 08 | orchestrateur |

Après chaque étape, un nouvel agent relit le travail. L'orchestrateur commite une fois la relecture passée, puis une relecture finale de toute la branche a lieu avant la fusion dans `master`.
