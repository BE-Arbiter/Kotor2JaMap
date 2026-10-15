# Re-relecture 1 — etape 07, phase 3 (60ea1e5..9b4c3b8)

Lecture seule du diff et des fichiers ; rien n'a ete execute ni modifie hors ce rapport.

## Verdicts

| Point | Verdict | Preuve |
|---|---|---|
| I1 `--target ${{ github.sha }}` | ADDRESSED | build-latest.yml:42 |
| I1 `concurrency` | ADDRESSED | build-latest.yml:11-13 (groupe unique, sans annulation : les releases s'enchainent) |
| I1 push master et main | ADDRESSED | build-latest.yml:5 |
| I2 controles Settings hors fil de rendu | ADDRESSED | MainScreen.cs:151-161 (lecture de champs ; `Check` dans `_checkJob`), SettingsProbe.cs:12-30 |
| I2 `Detect` hors fil de rendu, pas de relance concurrente | ADDRESSED | MainScreen.cs:167-176 (`BeginDisabled(_detectJob.IsRunning)` + `TryStart` refuse si en cours), AppGame.cs:77 |
| M1 repli `%APPDATA%\Kotor2Ja` pour Save, lu au demarrage | ADDRESSED (une reserve, voir N1) | ConfigLoader.cs:27-63, AppGame.cs:60 et 98, GameOption.cs:44 |
| M6 `Detect` n'ecrase pas un champ valide | ADDRESSED | SettingsProbe.cs:44-55 (`Pick` ne remplace que si invalide et trouve) |
| M3 bibliotheque Steam en echec sautee seule | ADDRESSED | GameLocator.cs (try/catch par bibliotheque + VDF a part) |
| M11 `publish.ps1` verifie les deux `.exe` | ADDRESSED | publish.ps1:39-41 (`throw` avant `Compress-Archive`) |

## Sûreté tache de fond / fil de rendu

- `BackgroundJob<T>` : un seul producteur a la fois (`CompareExchange` sur `_running`), `_result` ecrit avant `_hasResult` (`Volatile.Write`), consommateur unique (fil de rendu) via `CompareExchange`. Pas de lecture dechiree. Une exception met fin a la tache sans resultat et libere `_running` (finally) : pas de blocage.
- Les taches ne capturent que des valeurs immuables (tuples de chaines, `snapshot` record). `_settings` n'est lu que dans le fil de rendu (MainScreen.cs:169). Pas d'etat partage mutable.
- Resultat de controle perime (textes modifies pendant le calcul) : `_checkedFor != texts` relance un controle ; au pire un cadre avec un statut ancien. Acceptable.
- `SetSettings` remet `_checkedFor = null` sans toucher `_status` : meme comportement, sans souci.

## Priorite de lecture

env > fichier a cote de l'exe > %APPDATA% > detection > defauts : respectee. `FindConfig` (exe puis APPDATA) ; `Load` applique `GameDir` (env d'abord, puis fichier) sur `Defaults` (detection puis defauts) — ConfigLoader.cs:82-98. Le fichier APPDATA n'est jamais fusionne avec celui de l'exe (le premier trouve gagne) : conforme.

## Nouvelles casses

Aucune Critical ni Important.

Minors nouveaux :
- N1 `SaveWithFallback` (ConfigLoader.cs:50-62) : si un `kotor2ja.config.json` existe a cote de l'exe mais n'est pas inscriptible (exe dans Program Files), la sauvegarde part dans APPDATA, mais `FindConfig` prefere toujours le fichier a cote de l'exe : les reglages sauvegardes sont ignores au demarrage suivant, avec un message « Saved » trompeur. Cas rare (le zip ne livre qu'un `.example.json`). Le commentaire XML de la methode suppose le contraire. Correction possible : dans ce cas, avertir ou preferer APPDATA quand le fichier a cote est en lecture seule.
- N2 Application de `Detect` (MainScreen.cs:152-157) : remplace les quatre champs par le resultat calcule sur un instantane ; ce que l'utilisateur a tape pendant la detection est perdu. Cosmetique.
- N3 MainScreen.cs:126-129 : deux blocs `<summary>` empiles devant `SavedInfo`/`DetectSettings` (le doc de `DetectSettings` est maintenant attache a `SavedInfo`). Cosmetique.
- `Save` reste synchrone dans le fil de rendu (assume dans le rapport, action explicite ; hors I2).
