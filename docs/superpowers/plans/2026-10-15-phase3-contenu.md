# Phase 3 — Contenu, KOTOR 2, anglais, build portable : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** des cartes sans vert, à la lumière cohérente, avec objets (MD3) et portes qui s'ouvrent, pour KOTOR et KOTOR 2, une application en anglais et un exécutable portable produit par GitHub Actions.

**Architecture :**
- Corrections du rendu dans `Kotor.Core` (textures, shaders) et `Kotor.Compiler` (éclairage par sommet).
- Un paramètre `KotorGame` qui traverse le résolveur, le catalogue, la configuration et l'application.
- Un `Md3Writer` et un convertisseur d'entités (`Kotor.Pipeline/Entities`) qui produisent les MD3, les entités JA et la collision ajoutée.
- Des sous-modèles BSP dans le compilateur pour les portes.
- Une passe de langue, puis le packaging.

**Tech Stack :** .NET 10, xUnit + SkippableFact, MonoGame 3.8.5.1 Vulkan, ImGui.NET 1.91.6.1, GitHub Actions.

**Spec :** `docs/superpowers/specs/2026-10-15-phase3-contenu-design.md`

## Global Constraints

- KOTOR et KOTOR 2 sont lus en lecture seule. Dans `GameData\base`, on n'écrit que `kotor_<carte>.pk3` et `kotor2_<carte>.pk3`. Les tests écrivent dans des dossiers temporaires.
- Aucun abandon silencieux : tout élément sauté a un code au journal.
- Les fichiers produits par l'application et par la CLI restent identiques. La compilation reste déterministe.
- Tout nouveau texte visible par l'utilisateur est en anglais. Les commentaires, les rapports et les plans sont en français.
- `dotnet build Kotor2Ja.sln` sans avertissement et `dotnet test Kotor2Ja.sln` vert à la fin de chaque étape.

## Review Focus

1. **Lightmap entièrement invalide** (4×4 de bruit) : gris neutre et journal, sans plantage. Testé à l'étape 01.
2. **Module KOTOR 2 avec `.mod`** à la place des `.rim` : il est résolu. Un dossier de jeu absent ne plante pas l'application. Testé aux étapes 02 et 07.
3. **Objet dont le modèle dépasse ±512 unités** une fois à l'échelle : `modelscale`, sans débordement int16. Testé à l'étape 03.
4. **Porte vers un autre module** : verrouillée, elle ne s'ouvre pas sur le vide. Testé à l'étape 04.
5. **Deux objets ou deux portes qui partagent un modèle** : un seul MD3 dans le pk3. Testé aux étapes 03 et 04.

---

| Étape | Contenu | Dépend de | Modèle |
|---|---|---|---|
| 01 | Rendu : dilatation des lightmaps, nopicmip, alpha de reflet, éclairage par sommet KOTOR, cause de la zone blanche | — | sonnet |
| 02 | KOTOR 2 : `KotorGame`, résolveur, MDL K2, catalogue, configuration, deux nœuds dans l'application | 01 | sonnet |
| 03 | MD3 et objets : `Md3Writer`, conversion des placeables, `misc_model_static`, collision PWK | 02 | sonnet |
| 04 | Portes : quaternions des doorhooks, sous-modèles BSP, `func_door` et `model2` | 03 | sonnet |
| 05 | PNJ retirés, vues Importer et Exporter avec les modèles, options d'essai sans clic | 04 | sonnet |
| 06 | Passe anglais | 05 | sonnet |
| 07 | Build portable, détection Steam, panneau Settings, workflow GitHub | 06 | sonnet |
| 08 | Essais visuels de l'application puis du jeu, par l'orchestrateur | 07 | orchestrateur |
