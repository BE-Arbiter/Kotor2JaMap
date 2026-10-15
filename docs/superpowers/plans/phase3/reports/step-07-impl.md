# Étape 07 — Rapport d'implémentation

Statut : FAIT_AVEC_RESERVES (voir les réserves en fin de rapport).

## Réalisé
- `src/Kotor.Core/Export/GameLocator.cs` : `GamePaths`, `Detect(string? steamRootOverride)`, `Detect(Func<string?> steamPath)` (abstraction testable : le registre `HKCU\Software\Valve\Steam\SteamPath` n'est lu que si aucune racine n'est fournie, repli `C:\Program Files (x86)\Steam`), `ParseLibraryFolders`, `DefaultWorkDir()` = `%LOCALAPPDATA%\Kotor2Ja\work`. Lecture seule. Pour Jedi Academy, le dossier SWGL est préféré s'il y en a plusieurs.
- `ConfigLoader` : règle unique conservée, étendue : variable d'environnement > fichier > détection Steam > défauts codés en dur. `Defaults`/`Load` reçoivent un paramètre optionnel `detect` (tests sans registre). Ajouts : `LoadOrDetect(exeDir)`, `Save(path, options)`, `ConfigFileName`. `DefaultWorkDir` devient `%LOCALAPPDATA%\Kotor2Ja\work`. `GameOption.ResolveDir` reçoit aussi le paramètre `detect`.
- Application : `AppGame` lit `kotor2ja.config.json` à côté de l'exécutable, sinon détection (message « No configuration file ... detected through Steam »). `MainScreen` : panneau repliable `Settings` (4 champs, boutons `Detect` et `Save`, chemin invalide en rouge avec info-bulle, message d'état). `Save` écrit le fichier à côté de l'exécutable, recrée le workflow et recharge la liste des modules (refusé pendant une étape en cours).
- `scripts/publish.ps1` (paramètre `-SingleFile`, vrai par défaut), `.github/workflows/build-latest.yml`, `src/Kotor2Ja.Cli/kotor2ja.config.example.json` (chemins vides), `.gitignore` (`publish/`, `artifacts/`), section « Portable build / Download » du README.
- Les propriétés de publication sont passées en ligne de commande par le script (comme fixé par l'étape) ; les `.csproj` n'ont pas été modifiés (inutile).
- Noms réels des exécutables : `Kotor2Ja.App.exe` et `Kotor2Ja.Cli.exe` (pas `kotor2ja.exe` : l'assembly de la CLI s'appelle `Kotor2Ja.Cli`). Noms gardés, README et zip en conséquence.

## Tests
- Nouveaux : `GameLocatorTests` (3 : `ParseLibraryFolders_reads_paths`, `Detect_finds_games_in_fake_library`, `Detect_missing_games_null`), `Config_save_roundtrip`, et un ajout à `Game_folders_priority...` (détection entre config et défaut). Tests existants adaptés (détection factice) pour ne pas dépendre de la machine.
- `dotnet build Kotor2Ja.sln` : 0 avertissement. `dotnet test` : Compiler 80, Cli 20, Core 156, Pipeline 74, tous verts.
- Le cycle « test rouge d'abord » n'a pas été fait à la lettre (tests écrits avec l'implémentation).

## Publication
- Mode : **fichier unique** pour les deux exécutables (MonoGame Vulkan, SDL2, cimgui fonctionnent ; extraction native au premier lancement). Pas de mode dossier nécessaire.
- Tailles : `Kotor2Ja.App.exe` 75,5 Mo ; `Kotor2Ja.Cli.exe` 70,9 Mo ; zip `artifacts/Kotor2Ja-win-x64.zip` 63,4 Mo (66 472 870 octets).
- Contenu du zip : `Kotor2Ja.App.exe`, `Kotor2Ja.Cli.exe`, `kotor2ja.config.example.json`, `README.md`, `THIRD-PARTY-NOTICES.md`. Le `kotor2ja.config.json` de développement (chemins de la machine) est volontairement exclu. Pas de dossier `Content/` ni de polices externes dans le projet.

## Essai portable
- Zip extrait dans `%TEMP%\k2j_portable` (sans `kotor2ja.config.json`).
- `Kotor2Ja.App.exe --game kotor --module tar_m02aa --run-to preview --screenshot ... --exit` : code 0, détection Steam utilisée (`KOTOR: c:\program files (x86)\steam\steamapps\common\swkotor`), 199 modules, aperçu 574 meshes / 41216 triangles / 166 entités, capture 1600x900 non noire (carte, repère Spawn visibles).
- Capture : `C:\Users\lambe\AppData\Local\Temp\k2j_portable\portable.png`.
- `Kotor2Ja.Cli.exe list-modules` : 117 lignes pour KOTOR ; `--game kotor2` fonctionne aussi.

## Workflow GitHub
- `build-latest.yml` : push sur `master` + `workflow_dispatch`, `windows-latest`, `contents: write`, checkout@v4, setup-dotnet@v4 (`10.0.x`), build Release, test Release (`--no-build`), `pwsh scripts/publish.ps1`, remplacement de la release `latest` (`gh release delete latest --yes --cleanup-tag` sans échec si absente, puis `gh release create ... --prerelease`), `GH_TOKEN: ${{ github.token }}`.
- Syntaxe : pas de module `yaml` Python (Python absent) : relecture manuelle de l'indentation et des clés. Non exécuté (pas de remote).

## Réserves
- L'interface du panneau `Settings` n'a pas été essayée à la souris (consigne : pas de clic) : seul son affichage replié est visible sur la capture. L'orchestrateur doit l'essayer (Detect, Save, champ rouge).
- Le registre renvoie le chemin Steam en minuscules et avec `/` : les chemins détectés s'affichent en minuscules (`c:\program files (x86)\...`). Sans conséquence sur Windows.
- Le panneau `Settings` est placé sous le bouton `Preview` (au-dessus du journal), pas tout en bas.
- Branche du workflow : `master` (spec), alors que la branche principale annoncée est `main` : dépôt local sans remote, à ajuster si besoin.
- Le dossier `publish/` et `artifacts/` existent localement (ignorés par git). Rien n'a été commité ni poussé.

## Correction 1

Relecture : `reports/step-07-review.md`. Rien n'est commité ni poussé ; le dossier Jedi Academy n'est pas touché, le jeu n'est pas lancé.

### I1 — Release `latest` et commit du zip
- `build-latest.yml` : `gh release create ... --target ${{ github.sha }}` ; bloc `concurrency: { group: build-latest, cancel-in-progress: false }` (pas de croisement de deux pushes) ; déclencheur `push` sur `[master, main]`.
- Test : aucun (workflow non exécutable en local, pas de remote). Relecture manuelle de l'indentation et des clés (pas d'analyseur YAML disponible).

### I2 — Plus d'E/S dans le fil de rendu pour le panneau Settings
- Nouvelle classe testable `Kotor.Core.Export.SettingsProbe` : `Check` (contrôle d'existence des quatre chemins, `PathStatus`), `ApplyDetected` (règle de fusion de `Detect`), et `BackgroundJob<T>` (une seule tâche à la fois ; `TryStart` ne relance pas si elle tourne ; le résultat est repris par `TryTake` à une image ultérieure).
- `MainScreen.DrawSettings` : ne fait plus que lire des champs. Les contrôles sont lancés en tâche de fond quand le texte a changé depuis plus de 300 ms et qu'aucun contrôle ne tourne ; le résultat est appliqué à l'image suivante. `Detect` (registre + disque) tourne aussi en tâche de fond, le bouton est désactivé pendant ce temps, le résultat est appliqué à l'image suivante. `Save` reste synchrone (écriture courte et protégée, action explicite).
- Tests : `SettingsProbeTests.Check_reports_each_path`, `BackgroundJob_does_not_restart_while_running_and_delivers_result`, `BackgroundJob_survives_an_exception`.

### Minors
- **M1 (Save sans repli)** : `ConfigLoader.SaveWithFallback(exeDir, options)` écrit à côté de l'exécutable, sinon dans `%APPDATA%\Kotor2Ja\kotor2ja.config.json` ; `FindConfig` / `LoadOrDetect` lisent l'exécutable d'abord, puis `%APPDATA%`. L'application (au démarrage) et la CLI (`GameOption.DefaultConfigPath`) utilisent cet ordre. Le panneau et le journal indiquent, en anglais, où le fichier a été écrit (« Saved to <path> (the executable folder is not writable). »). README mis à jour. Tests : `Save_falls_back_to_appdata_when_exe_folder_is_not_writable_and_load_reads_it`, `Config_next_to_exe_wins_over_appdata`.
- **M6 (Detect écrase)** : `Detect` appelle `GameLocator.Detect()` et `SettingsProbe.ApplyDetected` : un champ déjà valide est gardé, un champ non trouvé n'est pas remplacé (plus de chemin codé en dur injecté). Tests : `ApplyDetected_keeps_valid_fields_and_unfound_games`, `ApplyDetected_fills_empty_fields`.
- **M3 (exception sur une bibliothèque)** : dans `GameLocator.Detect`, chaque bibliothèque a son `try/catch` (E/S, accès, argument) : elle est sautée seule ; la lecture du VDF a le sien (racine Steam seule en cas d'échec). Test : `Detect_skips_an_unusable_library_and_keeps_the_others` (bibliothèque au chemin inutilisable avant une valide ; on ne sait pas provoquer une vraie exception d'E/S en test, la voie `catch` n'est donc pas exercée directement).
- **M11 (publish.ps1)** : vérification de la présence de `Kotor2Ja.App.exe` et `Kotor2Ja.Cli.exe` avant `Compress-Archive` (échec avec `throw`). Pas de test automatisé ; le cas nominal est exercé par l'exécution du script. Une première version avait une erreur de syntaxe PowerShell (`$staging:` lu comme un lecteur), corrigée avec `${}`.
- Non traités (hors périmètre demandé) : M2, M4, M5, M7, M8, M9, M10 (partiellement couvert par `concurrency`), M12.

### Commandes et résultats
- `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur.
- `dotnet test Kotor2Ja.sln` : Compiler 80, Cli 20, Core 164, Pipeline 74, tous verts (Core +8 tests).
- `powershell -File scripts/publish.ps1` : OK (relancé après la correction du script et après la mise à jour du README) ; `Kotor2Ja.App.exe` 75,5 Mo, `Kotor2Ja.Cli.exe` 70,9 Mo, zip 63,4 Mo.
- Essai portable : zip extrait dans `%TEMP%\k2j_portable` sans config ; `Kotor2Ja.App.exe --game kotor --module tar_m02aa --run-to preview --screenshot ...\portable.png --exit` : code 0, message « No configuration file (next to the executable or in %APPDATA%\Kotor2Ja) ... », KOTOR détecté, 199 modules, aperçu 574 meshes / 41216 triangles / 166 entités, capture 1600x900 de 1,07 Mo (même taille qu'avant la correction). `Kotor2Ja.Cli.exe list-modules` : 117 lignes.
- Réserve : le panneau Settings (Detect, champs rouges, Save avec repli) n'a pas été essayé à la souris.
