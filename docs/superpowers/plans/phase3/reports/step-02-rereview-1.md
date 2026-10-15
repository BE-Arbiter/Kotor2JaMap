# Étape 02 — Re-relecture ciblée de la correction 1

Diff relu : `ad43c18..47fa4e7` (source et tests). Aucun code modifié, aucun test relancé.

## Verdicts

| Constat | Verdict | Preuve |
|---|---|---|
| Important 1 : règle de résolution unique (env > config > défaut) | ADDRESSED | `ConfigLoader.cs:17-25` (Defaults : env sinon défaut), `:40-44` (`GameDir` : env, puis `Str` config, puis défaut) ; `GameOption.cs:32-41` (`ResolveDir` passe par `ConfigLoader.Load`, repli `Defaults` si config illisible) ; `ListModulesCommand.cs:25` et `ExportObjCommand.cs:31` utilisent `ResolveDir` ; `ExportJaCommand.cs:27-34` utilise `Load` + `ForGame` ; `AppGame.cs:56` utilise `Load`. Les trois commandes et l'application résolvent donc avec la même fonction, et `--kotor` reste prioritaire (`GameOption.cs:34`, `ExportJaCommand.cs:36`). Anciens doublons supprimés (`ResolveKotorDir`). |
| Important 2 : tests manquants | ADDRESSED | CLI : `GameOptionTests.cs:20,30,37,48` (analyse `--game`, valeur invalide code 1 pour les trois commandes), `:68` (priorité option/env/config/défaut), `:85` (config cassée), `:105` (`SkippableFact` réel K2). Config : `ConfigLoaderTests.cs:50,60`. Contrôleur : `WorkflowControllerTests.cs:194` (même nom `m1` dans les deux jeux, jeu transmis à `Preview`), `:214` (résultat tardif écarté). Le test `:214` passe par `Reset()` et non par un vrai changement de jeu : il couvre le jeton de génération, pas la sélection ; acceptable car `SelectModule` est refusé pendant une étape (`WorkflowController.cs:62`). |
| Minor 5 : messages CLI | ADDRESSED | `GameOption.cs:46-47` (`NotFound`, anglais, nom du jeu), utilisé en `ListModulesCommand.cs:34`, `ExportObjCommand.cs:42`, `ExportJaCommand.cs:60` ; aide de `--kotor` en anglais et mise à jour (`Program.cs`) ; test `GameOptionTests.cs:93`. Les autres messages français (`Module inconnu`, `Options invalides`) sont hors du constat. |
| Minor 3 : capture du jeu hors du verrou | ADDRESSED | `WorkflowController.cs:122-129` : `gen`, `module` et `game` lus dans le même `lock`, `canRun()` évalué sous ce verrou ; `PreviewAsync` (`:91-95`) n'a plus de verrou propre ; le jeu est passé à `run` (`:138`). |

## Nouvelles casses Critical / Important

Aucune.

Vérifications faites : le comportement de `Defaults()` sans argument est inchangé (env sinon défaut) ; une variable vide ou blanche est ignorée comme avant ; `Load` sans fichier retourne les défauts avec l'env ; les lambdas `(_, _, p)` de `NextAsync` conservent l'ordre des étapes ; `Program.BuildRoot()` ne change pas le comportement de `Main`.

## Hors périmètre (non traité par ce diff, non bloquant)

- `ExportJaCommand.cs:28` appelle `ConfigLoader.Load` sans `try/catch` : une config JSON illisible fait planter `export-ja`, alors que `ResolveDir` (list-modules, export-obj) et l'application se replient sur les défauts. Comportement antérieur à la correction, mais la règle « unique » ne couvre pas la gestion d'erreur.
- Réserve du rapport : la config livrée garde `kotor2Dir` ; sans variable d'environnement, le chemin Steam s'applique. Cohérent avec la décision de l'orchestrateur.
- Minor 4 (noms `textures/kotor/` en dur pour brouillard et collision), Minor 6 (`IsInstalled` par image), Minor 7 (même nom de carte dans deux jeux) : non traités, inchangés.
- Messages CLI restants en français : prévus pour l'étape anglais.
