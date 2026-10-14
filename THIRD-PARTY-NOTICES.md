# Notices tierces

## Compilateur BSP (`src/Kotor.Compiler/`)

- Origine : code copié du projet TFU (`SWGLFU-Map-importer`, `src/Tfu.Compiler/`), namespaces renommés `Tfu.*` en `Kotor.*`.
- Licence : GPL-2.0 (en-tête de chaque fichier concerné).
- Démarche : inspirée de q3map2 / NetRadiant-custom (chaîne `.map` vers `.bsp`). Les en-têtes des fichiers indiquent qu'aucun code de ces projets n'a été copié.

## Bibliothèques d'images

Aucune bibliothèque d'images n'est utilisée. Le PNG est écrit par un encodeur interne minimal (`src/Kotor.Core/Export/PngWriter.cs`) et le TGA par `TgaWriter` / `TgaReader`. Le projet TFU d'origine utilisait Pfim pour le DDS ; cette dépendance n'est pas reprise.

## Dépendances de test

Les paquets de test (xUnit, Xunit.SkippableFact, Microsoft.NET.Test.Sdk, coverlet) ne sont pas distribués avec l'outil.
