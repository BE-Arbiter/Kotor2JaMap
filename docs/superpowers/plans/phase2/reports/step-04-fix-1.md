# Étape 04 — Correction 1

## Changements

- **C1 (enroulement du BSP)** : `PreviewBuilder.FromBsp` (src/Kotor.Pipeline/Preview) échange deux indices par triangle (copie du tableau, `Indices` du BSP inchangé). Les trois vues suivent désormais la convention KOTOR, face avant antihoraire, et `CullClockwiseFace` du rendu reste correct partout. Test `FromBsp_triangles_are_counter_clockwise_like_import_view` : pour chaque triangle hors ciel, le produit vectoriel `(b-a)x(c-a)` est du même côté que la somme des normales de sommets, pour la vue Importer (référence) comme pour la vue BSP.
- **I1 (Cutout + lightmap)** : tout lot avec lightmap (hors ciel) a maintenant un tampon `VertexDual`. Cutout + lightmap : passe 1 `AlphaTestEffect` avec `ColorWriteChannels.None` (profondeur seule), passe 2 `DualTextureEffect` avec `DepthBufferFunction.Equal`, sans écriture de profondeur. Risque : l'égalité de profondeur entre deux effets n'est pas garantie bit à bit par la spécification ; les deux utilisent la même multiplication position x WorldViewProjection, donc probable. À vérifier à l'œil sur une grille/un feuillage (si scintillement : passer par un stencil).
- **I2 (clavier ImGui)** : `NavEnableKeyboard` (et gamepad) retirés ; Espace et Entrée retirés de la table de touches envoyée à ImGui. Plus aucune touche ne peut activer un bouton d'étape. La caméra n'ignore le clavier que si `io.WantTextInput` (saisie de texte), plus `WantCaptureKeyboard`.
- **Mineurs** : le damier de repli partagé n'est plus libéré par `Clear()` (seulement par `Dispose()`) ; `AppGame` mémorise la vue dont l'envoi GPU a échoué (`_failedPreview`) et ne réessaie ni ne journalise plus tant que la référence ne change pas ; `IniFilename` à null (via `NativePtr`, la propriété est en lecture seule) et `imgui.ini` supprimé du dépôt ; blend additif `One/One` (`AddBlend`) comme le `add` de Q3 ; `ConfigLoader.Load` gardé : fichier absent = avertissement, JSON illisible ou options invalides = erreur dans le journal et valeurs par défaut, sans plantage.

## Commandes et résultats

- `dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur.
- `dotnet test Kotor2Ja.sln` : Cli 5, Compiler 68, Core 108, Pipeline 21 ; 0 échec.
- Démarrage : lancement en arrière-plan de `Kotor2Ja.App.exe` (sans clic) ; après 10 s le processus tourne toujours (Vulkan initialisé via volk), arrêté avec taskkill. Aucun `imgui.ini` recréé.

## Liste de contrôle manuelle (mise à jour)

- Cliquer « Importer », attendre la fin, appuyer sur Espace : rien ne se lance ; la caméra monte (Espace/E) et ZQSD fonctionne tout de suite après un clic dans l'interface.
- À l'étape Exporter, les murs proches et lointains sont vus de l'intérieur de la pièce, sans « Double face » (même sens qu'à l'étape Importer).
- Grille/feuillage avec lightmap : trou découpé et éclairage par la lightmap, sans scintillement.
- Texture additive : fondu One/One (plus clair que l'ancien SourceAlpha/One).
- Renommer ou casser `kotor2ja.config.json` : l'application démarre, le journal affiche le message.
- Les touches de l'interface texte (si une saisie existe) restent utilisables ; la caméra est suspendue seulement pendant la saisie.
