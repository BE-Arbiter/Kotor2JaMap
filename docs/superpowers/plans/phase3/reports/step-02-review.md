# Étape 02 — KOTOR 2 : relecture

Diff relu : `8342229..ad43c18`. Aucun test relancé (lecture seule).

## Verdict 1 — Conformité à la spec : ✅

Tout ce que fixe l'étape est présent :
- `KotorGame`/`KotorGames` conformes à l'interface ; `ForModule(dir, module, game)` ; `ModuleListItem(Game, ...)` ; `SelectModule(game, module)`.
- Résolution : `Override`, `.mod` (qui remplace `.rim` et `_s.rim`), sinon `.rim` + `_s.rim`, puis `_dlg.erf` (K2 seulement), pack de textures, `chitin.key`. K1 prend aussi un `.mod`.
- MDL : détection par le premier pointeur (4273776 / 4285200), pointeur inconnu = `NotSupportedException` en anglais. `ModuleSceneBuilder.cs:125,242` l'attrape et le journalise : pas d'abandon silencieux. +8 octets avant les décalages MDX (324) et sommets (328), mesuré sur 3 modules réels (001ebo, 101per, 202tel), avec contrôle inverse sur K1.
- Noms produits : `kotor2_<carte>.pk3`, `shaders/kotor2_<carte>.shader`, `textures/kotor2/` ; nom de carte BSP inchangé. K1 identique (`MaxMaterialLength` K1 = 63-15-4 = 44, comme avant).
- Nœud K2 grisé `KOTOR 2 (not found)` (MainScreen, `DrawGameNode`) ; `GAME_NOT_FOUND` côté pas-à-pas ; CLI `--game kotor|kotor2`.
- Tests demandés présents (dont les trois `SkippableFact` réels).

Écart mineur par rapport au plan : la partie « test sur au moins trois modèles de pièce » est bien couverte, mais le test ne vérifie pas explicitement le `.mod` réel (aucun installé, assumé dans le rapport).

## Verdict 2 — Qualité : pas de Critical, 2 Important, 5 Minor

### Important

1. **La config livrée masque `KOTOR2_PATH`, et la CLI ne lit pas la config pour K2 selon la commande.**
   `ConfigLoader.cs:34` : `Str("kotor2Dir", o.Kotor2Dir ?? "")` fait primer la config sur la variable d'environnement. `kotor2ja.config.json` livre `kotor2Dir` (chemin Steam par défaut), alors que `kotorDir` est volontairement absent (le rapport le note en réserve). Scénario : KOTOR 2 installé hors Steam, `KOTOR2_PATH=D:\K2`. `export-ja --game kotor2` et l'application chargent la config livrée, prennent le chemin Steam, et affichent `KOTOR 2 (not found)` / « Installation KOTOR 2 introuvable ». Par contre `list-modules --game kotor2` et `export-obj` passent par `GameOption.ResolveDir` (`GameOption.cs`, `ConfigLoader.Defaults()`), ne lisent jamais la config et utilisent bien `D:\K2`. Les trois commandes de la CLI résolvent donc le dossier K2 différemment. Correction : retirer `kotor2Dir` du fichier livré (comme `kotorDir`) ou faire primer l'environnement sur la valeur par défaut seulement, et faire résoudre les trois commandes par le même code.

2. **Cas demandés par l'étape non couverts par des tests.** Aucun test de la CLI `--game` (le plan exige `list-modules --game kotor2` ; aucun test de la CLI n'est touché par le diff). Aucun test de la priorité config / `KOTOR2_PATH` / défaut. Aucun test d'un même nom de module dans les deux jeux côté `WorkflowController` (`Select_takes_game` n'utilise que `m1`), ni de `PreviewAsync` après changement de jeu pendant un aperçu. Aucun test `.mod` avec K1. Scénario : une régression sur le point 1 ou sur la sélection `(jeu, module)` passerait l'intégration.

### Minor

3. **Capture du jeu hors du verrou de `Run`.** `WorkflowController.cs:93-96` lit `_game` sous un premier verrou, puis `Run` capture `_module` sous un second. Un `SelectModule(K2, "x")` entre les deux (autre fil, ou test) donne `Preview(K1, "x")`. Dans l'application tout se passe sur le fil d'interface, donc le risque est théorique. Correction : capturer `(game, module)` dans le même verrou que `gen`.

4. **Noms durs de K1 pour le brouillard et la collision.** `ShaderNames.cs:56` (`FogName` = `textures/kotor/<niveau>_fog`) et `CompilerNames.cs:8` (`textures/kotor/collision`) ne dépendent pas du jeu. Aujourd'hui `_kotor_fog` n'est écrit nulle part, donc sans effet ; le jour où le brouillard sera émis pour K2, le BSP référencera un shader absent de `kotor2_<carte>.shader`.

5. **Messages CLI.** `ListModulesCommand.cs:42` et `ExportObjCommand.cs:42` disent « Installation KOTOR introuvable » (en français) même pour K2. `ExportJaCommand.cs:60` utilise le bon nom de jeu mais reste en français alors que la ligne a été retouchée. L'aide de `--kotor` (`Program.cs`) ne mentionne pas qu'elle vise le jeu choisi.

6. **`IsInstalled` appelé à chaque image.** `MainScreen.DrawGameNode` appelle `Directory.Exists` deux fois par image sur le fil d'interface. Sur un lecteur réseau lent, l'interface saccade. De plus, il suffit que le dossier existe : un dossier K2 sans `modules` donne un nœud actif et vide. Correction : mettre en cache ou tester `modules`.

7. **Même nom de carte dans les deux jeux.** `maps/<carte>.bsp` n'est pas préfixé : deux modules de même nom dans K1 et K2 produiraient deux pk3 qui se masquent. Aucun recouvrement dans les installations actuelles (vérifié), et la spec fixe ce nommage.

## Points d'attention demandés

- **Non-régression K1** : noms de pk3, de shader et de textures identiques ; résolution inchangée sauf `.mod` ; détection MDL K1 inchangée (`k2 = 0`). Seul effet visible : un `.mod` dans `modules` de K1 apparaît dans la liste (voulu par le plan).
- **Priorité `.mod`/`.rim`** : correcte et testée (`Resolver_prefers_mod_over_rim`). Le `_s.rim` est lui aussi ignoré quand le `.mod` existe, ce qui est cohérent avec le jeu.
- **Dossier K2 absent** : application sans plantage ; `Modules()` saute le jeu ; `Preview` lève `GAME_NOT_FOUND` ; CLI code 2. Voir le point 5 pour le message.
- **Contrôleur** : `SelectModule` est refusé pendant une étape ; le jeton de génération écarte les résultats périmés ; `_opt` et `_scene` ne changent qu'après un aperçu réussi ; l'interface distingue bien `(jeu, module)` (surbrillance et identifiants ImGui). Voir le point 3.
- **Pointeur inconnu** : exception anglaise attrapée et journalisée, le modèle est sauté avec un code.
- **Priorité config / environnement** : voir le point 1.
