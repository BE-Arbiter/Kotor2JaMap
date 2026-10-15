# Étape 06 — Re-relecture 1 (correction 1, b8cd429)

Plage : 36f400b..b8cd429. Relecture statique (lecture du diff et du code, grep). Dotnet non rejoué dans cette passe. Aucun fichier modifié hors de ce rapport.

## Verdicts

| # | Point relevé | Verdict | Preuve |
|---|---|---|---|
| 1 | `"Apparition"` devient `"Spawn"` (PreviewRenderer.cs) | ADDRESSED | `src/Kotor2Ja.App/Rendering/PreviewRenderer.cs:380` : `"Spawn"`. `grep Apparition` sur `src` et `tests` : aucun résultat dans le code (seulement dans les rapports `docs/`). |
| 2 | `…` devient `...` (MainScreen.cs) | ADDRESSED | `src/Kotor2Ja.App/Ui/MainScreen.cs:181` : `(busy ? "..." : label)`. Plus aucun caractère U+2026 dans `MainScreen.cs`. |
| 3 | README : `--config` ne vaut que pour `export-ja` | ADDRESSED | Code : `src/Kotor2Ja.Cli/Commands/ExportJaCommand.cs:22` seul enregistre `--config` (`cfgPath` ligne 27). `ListModulesCommand.cs` et `ExportObjCommand.cs` n'ont pas d'option `--config` et passent par `GameOption.ResolveDir` sans chemin (`GameOption.cs:44`, `DefaultConfigPath`). README : `README.md:102` (« Only `export-ja` accepts `--config` ... ») et `README.md:89` (option listée sous `export-ja` uniquement). Exact. |
| 4 | README : `export-obj` renvoie 1 s'il n'y a aucun triangle | ADDRESSED | README : `README.md:91` (« For `export-obj`, exit code 1 also means that the module produced no triangle »). Code : `src/Kotor2Ja.Cli/Commands/ExportObjCommand.cs:88` `return tris > 0 ? 0 : 1;`, `tris` calculé ligne 76. Exact. |
| 5 | `THIRD-PARTY-NOTICES.md` en anglais, sans perte de licence, nom ni URL | ADDRESSED | Licences conservées : GPL-2.0 (`THIRD-PARTY-NOTICES.md:6`), MS-PL (`:19`), MIT (`:26`). URL conservées : `:21` (https://www.monogame.net/), `:28` (https://github.com/ImGuiNET/ImGui.NET). Noms conservés : TFU, `SWGLFU-Map-importer`, `Tfu.Compiler`, Pfim, q3map2 / NetRadiant-custom, MonoGame Team, Eric Mellino, xUnit, Xunit.SkippableFact, Microsoft.NET.Test.Sdk, coverlet, `PngWriter.cs`, `TgaWriter` / `TgaReader`. Chaque ligne retirée du diff a son équivalent anglais. |

## Casse introduite par le diff

Aucune détectée. Le diff `src` ne touche que deux littéraux (`PreviewRenderer.cs:380`, `MainScreen.cs:181`). Aucun test ni aucune comparaison de chaîne ne dépend de `"Apparition"` ou de `…` (grep dans `src` et `tests`). Les autres fichiers modifiés sont de la documentation (README, notices, rapports).

## Remarque hors périmètre

Le diff ne corrige pas les points Minor de la relecture initiale qui n'étaient pas demandés (coloration `WARNING:` dans `AppGame.cs:66`, notamment). Ils ne sont pas traités ici.
