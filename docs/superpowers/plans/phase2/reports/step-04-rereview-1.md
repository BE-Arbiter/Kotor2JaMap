# Étape 04 — Re-revue de la correction 1

Portée : diff 568bd76..7902ec2 uniquement (lecture seule, rien exécuté ; les résultats de build/tests sont ceux du rapport de correction).

## Verdict par constat

1. **C1 (enroulement BSP) : ADDRESSED.**
   `src/Kotor.Pipeline/Preview/PreviewBuilder.cs` (bloc FromBsp, `var idx = (int[])s.Indices.Clone()` puis échange `idx[i+1]`/`idx[i+2]`) : copie du tableau, le BSP n'est pas modifié, `idx` est passé au lot. Les trois vues partagent la convention face avant antihoraire, donc `CullBack` fonctionne partout. Test `FromBsp_triangles_are_counter_clockwise_like_import_view` (`tests/Kotor.Pipeline.Tests/PreviewBuilderTests.cs`, fin de fichier) : compare le produit vectoriel à la somme des normales de sommets, avec la vue Importer en référence (égalité stricte sur tous les triangles non dégénérés hors ciel). Le test est significatif : il échouerait avec l'ancien ordre v0,v2,v1.

2. **I1 (cutout + lightmap) : ADDRESSED.**
   `PreviewRenderer.cs` : `dual = lm is not null && !b.IsSky` (la condition Cutout est retirée) ; dans `DrawBatch`, nouvelle branche `b.Dual && s.Blend == Cutout` : passe 1 `AlphaTestEffect` avec `DepthOnly` (`ColorWriteChannels.None`) et profondeur par défaut, passe 2 `DualTextureEffect` avec `DepthEqual` (écriture désactivée), `BlendState.Opaque`. Le rasterizer (cull) est identique dans les deux passes. La passe 2 est dessinée par le code commun après la sélection de l'effet. Risque déjà signalé par l'auteur : égalité de profondeur entre deux effets non garantie bit à bit (même multiplication de position, probable mais non vérifié ici). À contrôler visuellement ; repli stencil prévu.

3. **I2 (clavier) : ADDRESSED.**
   `ImGuiRenderer.cs` : `NavEnableKeyboard` et `NavEnableGamepad` retirés (`io.ConfigFlags &= ~...`), Espace et Entrée retirés de `KeyMap`. Sans navigation, aucune touche n'active un bouton `###next`/Exporter/Créer PK3. `AppGame.cs` : la caméra reçoit `io.WantTextInput` au lieu de `WantCaptureKeyboard`, donc ZQSD/Espace/E fonctionnent sans activation préalable de l'interface. Les caractères de saisie (`AddInputCharacter`) restent transmis, utiles seulement pour un champ de texte.

4. **Mineurs : ADDRESSED.**
   - Damier partagé : `Clear()` saute `_fallback` (`ReferenceEquals`), libéré par `Dispose()` (hors diff, d'après le rapport).
   - Échec d'Upload : `_failedPreview` dans `AppGame.Update` (condition et affectation dans le `catch`) : plus de nouvelle tentative ni de journal à chaque image.
   - `imgui.ini` : supprimé du dépôt, `io.NativePtr->IniFilename = null` dans le constructeur.
   - Additif : `AddBlend` One/One (couleur et alpha) utilisé pour `PreviewBlend.Additive`.
   - ConfigLoader : `try/catch` ciblé (JsonException, IOException, UnauthorizedAccessException, InvalidOperationException, FormatException), absent = avertissement, invalide = erreur + `ConfigLoader.Defaults()`, message dans le journal.

## Nouvelle casse

Aucune.

## Observations hors périmètre

- Plusieurs fichiers modifiés ont gagné un BOM UTF-8 en première ligne (`PreviewBuilder.cs`, `AppGame.cs`, `ImGuiRenderer.cs`, `PreviewRenderer.cs`, `PreviewBuilderTests.cs`) : bruit de diff, sans effet fonctionnel.
- Cosmétique : espace de fin de ligne dans `KeyMap`, `}}` en fin de fichier de test, `unsafe` redondant (constructeur + bloc) dans `ImGuiRenderer`.
- Sans navigation, la touche Tab reste mappée mais inutile ; sans effet.
- Le comportement visuel (cutout + lightmap sans scintillement, additif, orientation des murs de la vue Exporter) reste à confirmer à l'œil, comme indiqué dans la liste de contrôle manuelle.

## Verdict

**APPROVED** — tous les constats corrigés, aucune casse détectée dans le diff.
