# Phase 2 — Application graphique : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** une application `Kotor2Ja.App` (MonoGame Vulkan + ImGui). On y choisit un module sous le nœud « KOTOR » et on le fait passer par Prévisualiser → Importer → Exporter → Créer PK3, avec une vue 3D à chaque étape.

**Architecture :** une bibliothèque `Kotor.Pipeline` contient :
- les quatre étapes, découpées dans l'actuel `ModuleExporter` ;
- un lecteur de GIT pour les entités ;
- l'automate (`WorkflowController`, copié du projet TFU) ;
- les modèles de vue sans GPU (`PreviewModel`).

`Kotor.Compiler` gagne un `BspReader`. L'application `Kotor2Ja.App` (copiée de `Tfu2Ja.App`) affiche le `PreviewModel` de l'étape en cours. La CLI `export-ja` enchaîne les mêmes étapes.

**Tech Stack :** .NET 10, MonoGame.Framework.Native + MonoGame.Runtime.Windows.Vulkan 3.8.5.1, ImGui.NET 1.91.6.1 (mêmes versions que le projet TFU), xUnit.

**Spec :** `docs/superpowers/specs/2026-10-14-phase2-application-design.md` (et la spec de base `docs/superpowers/specs/2026-10-14-kotor-vers-jedi-academy-design.md`).

## Global Constraints

- Disposition imposée par l'utilisateur :
  - colonne de gauche = nœud « KOTOR » avec la liste des modules, puis le bouton « Prévisualiser », puis le journal ;
  - fenêtre principale = vue 3D, avec en bas à droite un bouton unique dont le libellé suit l'étape : « Importer », « Exporter », « Créer PK3 ».
- Vues : Prévisualiser = carte KOTOR ; Importer = carte au format map + entités (lumières, objets, portes…) ; Exporter = carte au format BSP (relue) ; Créer PK3 = aucun changement de vue.
- Les fichiers produits par l'application et par la CLI `export-ja` sont identiques.
- KOTOR est lu en lecture seule. Dans `GameData`, on n'écrit que `base\kotor_<carte>.pk3`.
- La vue montre exactement ce qui part dans la carte, sans simplification d'affichage.
- L'interface ne gèle pas : les étapes tournent en arrière-plan.
- Interface, rapports et messages en français.

## Review Focus

1. **Changement de module pendant qu'une étape tourne** : le choix est refusé tant que l'étape est en cours, et aucun résultat d'une étape ancienne ne vient écraser la vue. Testé à l'étape 03.
2. **Étape en échec** (texture introuvable, limite de shaders, pk3 verrouillé par le jeu lancé) : le bouton reste sur la même étape, l'erreur est dans le journal, et l'application ne plante pas. Testé aux étapes 01 et 03.
3. **Module sans GIT ou avec un GIT incomplet** (listes absentes) : aucune entité n'est affichée, et le journal le signale. Testé à l'étape 02.
4. **BSP relu différent de celui écrit** (lump vide, surface sans index) : la lecture échoue proprement avec un message. Testé à l'étape 01.
5. **Texture absente dans la vue** : un damier magenta s'affiche, comme dans la carte. Testé à l'étape 03 (modèle de vue).

---

| Étape | Contenu | Dépend de | Modèle |
|---|---|---|---|
| 01 | `Kotor.Pipeline` : découpage de `ModuleExporter` en étapes, `BspReader`, CLI branchée sur les étapes | — | sonnet |
| 02 | Lecteur GIT et lumières des pièces → `ModuleEntities` | 01 | sonnet |
| 03 | `PreviewModel` des trois vues, `WorkflowController` à quatre étapes (tests) | 01, 02 | sonnet |
| 04 | `Kotor2Ja.App` : MonoGame Vulkan + ImGui, disposition imposée, vue 3D, caméra | 03 | sonnet |
| 05 | Vérification manuelle par l'utilisateur | 04 | orchestrateur |
