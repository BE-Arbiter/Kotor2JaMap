# Notices tierces

## Compilateur BSP (`src/Kotor.Compiler/`)

- Origine : code copié du projet TFU (`SWGLFU-Map-importer`, `src/Tfu.Compiler/`), namespaces renommés `Tfu.*` en `Kotor.*`.
- Licence : GPL-2.0 (en-tête de chaque fichier concerné).
- Démarche : inspirée de q3map2 / NetRadiant-custom (chaîne `.map` vers `.bsp`). Les en-têtes des fichiers indiquent qu'aucun code de ces projets n'a été copié.

## Bibliothèques d'images

Aucune bibliothèque d'images n'est utilisée. Le PNG est écrit par un encodeur interne minimal (`src/Kotor.Core/Export/PngWriter.cs`) et le TGA par `TgaWriter` / `TgaReader`. Le projet TFU d'origine utilisait Pfim pour le DDS ; cette dépendance n'est pas reprise.

## Dépendances de test

Les paquets de test (xUnit, Xunit.SkippableFact, Microsoft.NET.Test.Sdk, coverlet) ne sont pas distribués avec l'outil.

## MonoGame 3.8.5.1 (MonoGame.Framework.Native, MonoGame.Runtime.Windows.Vulkan)

- Licence : MS-PL.
- Auteur : MonoGame Team.
- Projet : https://www.monogame.net/
- Usage : application graphique `src/Kotor2Ja.App`, backend Vulkan (mêmes paquets que le projet TFU).

## ImGui.NET 1.91.6.1 (interface)

- Licence : MIT.
- Auteur : Eric Mellino et contributeurs.
- Projet : https://github.com/ImGuiNET/ImGui.NET
- `src/Kotor2Ja.App/ImGuiRenderer.cs` est copié du projet TFU (`Tfu2Ja.App`), lui-même adapté de l'exemple MonoGame/XNA de ce dépôt (licence MIT). L'en-tête d'origine est conservé.
