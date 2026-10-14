# Étape 04 — Revue (Kotor2Ja.App)

Base 7a16617, tête 781cf60. Lecture seule : je n'ai ni relancé l'application ni la suite de tests.

## Conformité

- ✅ Disposition : colonne gauche pleine hauteur avec le nœud « KOTOR » (ouvert par défaut, `module — zone`), puis « Prévisualiser » (actif si `CanPreview`), puis le journal (défilement automatique, `LastError` en rouge). Vue 3D à droite, bouton unique en bas à droite (`NextLabel`, masqué si null, « … » pendant le travail, inactif si `!CanNext`). Cases en haut à droite : Collisions, une par `EntityKind` présent.
- ✅ Repère Z haut : `CreateLookAt(..., UnitZ)`, `Forward` (cos y·cos p, sin y·cos p, sin p), `right = (fy, -fx, 0)` : droite et gauche corrects pour un repère direct Z haut. Souris : droite = tourner à droite, bas = regarder en bas, correct.
- ✅ Entrées caméra ignorées si ImGui capture souris ou clavier (voir toutefois I2).
- ✅ Téléversement GPU dans `Update` (fil de rendu) quand la référence `Preview` change ; `Clear()` libère lots, textures, tampons de repères, collisions et apparition à chaque changement (pas de fuite).
- ✅ Les étapes passent par `PreviewAsync`/`NextAsync`, le chargement des modules est en tâche de fond : l'interface ne gèle pas.
- ✅ Ciel : aplat bleu sombre derrière la case « Ciel » décochée (décision du contrôleur).
- ✅ Squelette, licence MIT conservée, `THIRD-PARTY-NOTICES.md`, README, `.sln` : corrects.
- ❌ Fidélité d'affichage : orientation des faces fausse à l'étape Exporter (C1) ; lot Cutout + lightmap dessiné sans lightmap (I1).
- ⚠️ Case « Double face » : écart assumé et documenté, utile comme diagnostic, décochée par défaut.

## Points forts

- Séparation nette `ViewOptions` / `PreviewRenderer` / `MainScreen` / `FreeCamera` ; `Clear()` complet ; états de rendu (blend, profondeur, culling) posés lot par lot.
- Mipmaps et lightmap en `LinearClamp`, diffuse en `LinearWrap`. Recadrage conservé quand le module et les bornes ne changent pas entre étapes.
- Repli propre (texture « missing », damier) ; repères et contours de déclencheurs ; étiquettes ImGui limitées en distance et en nombre.

## Problèmes

### Critique

**C1 — Mauvais sens de culling à l'étape Exporter (vue du BSP relu) : les murs seront vus à l'envers.**
`PreviewRenderer.cs:50` (`CullBack = CullClockwiseFace`), `:1002` (`DrawBatch`), cause dans `Kotor.Compiler/Bsp/RenderSurfaces.cs:84-85`.
Raisonnement : MonoGame, `CullClockwiseFace` supprime les triangles horaires à l'écran, donc face avant = antihoraire (normale droite de `(b-a)×(c-a)` vers l'œil). Les étapes Prévisualiser (`FromScene`) et Importer (`FromImport`) reprennent les indices KOTOR tels quels (aucune inversion dans `Kotor.Core`) : antihoraires, le choix est donc bon pour elles. Mais le compilateur émet `v0, v2, v1` (« enroulement JA mesuré à l'étape 15 », test `RenderSurfacesTests.cs:66` : `cross·normale < 0`, horaire vu de la face visible), et `FromBsp` repasse `s.Indices` tels quels (`PreviewBuilder.cs:147`). À l'étape Exporter, les faces visibles de chaque mur sont donc horaires et sont éliminées : on voit l'arrière des murs lointains, la pièce est « retournée ». Un seul mode de culling pour trois modèles d'enroulement différents ne peut pas être juste partout, et ce serait précisément la vue qui doit montrer ce que le jeu verra (et qui servirait à détecter une erreur d'enroulement du compilateur : la bonne orientation n'est donc pas un détail).
Correction : porter l'enroulement dans le modèle, pas dans une constante. Soit `PreviewBuilder.FromBsp` inverse chaque triangle (échanger deux indices) pour ramener tous les modèles à la convention « antihoraire = face avant », soit ajouter un booléen `PreviewModel.ClockwiseFront` (vrai pour `FromBsp`) et choisir `CullCounterClockwiseFace`/`CullClockwiseFace` selon lui. Ajouter un test sur `FromBsp` (le produit mixte des arêtes avec la normale a le même signe qu'à l'étape Importer). Garder « Double face » comme repli.

### Important

**I1 — Cutout + lightmap dessiné sans lightmap (`PreviewRenderer.cs:859`, `:1028-1033`).**
Contrainte utilisateur : « la vue montre exactement ce qui part dans la carte, sans simplification d'affichage ». Un lot `LightmappedCutout` (grilles, feuillages) est affiché plein éclairage alors que le jeu le multiplie par la lightmap. Le rapport l'annonce, mais cela reste un écart à la contrainte, pas une préférence.
Correction sans shader personnalisé : passe 1 `AlphaTestEffect` avec `ColorWriteChannels.None` (profondeur seule, seuls les texels opaques écrivent), passe 2 `DualTextureEffect` avec `DepthStencilState` à `DepthBufferFunction = Equal` et sans écriture de profondeur. Si l'égalité de profondeur est instable entre effets, passer par un stencil. À défaut, noter explicitement l'écart dans l'interface (étiquette) tant qu'il existe.

**I2 — `NavEnableKeyboard` : le clavier reste « capturé » et Espace peut réactiver un bouton (`ImGuiRenderer.cs:34`, `MainScreen.cs:1351`).** (À confirmer à la main, plausible.)
Avec la navigation clavier activée, après un clic sur un widget de la fenêtre ImGui, `WantCaptureKeyboard` reste vrai (navigation active) : ZQSD/Espace/Ctrl sont ignorés par la caméra jusqu'à un clic dans le vide. Pire, le bouton d'étape garde le même identifiant (`"###next"`) à toutes les étapes ; la touche Espace, mappée dans `KeyMap` et utilisée pour « monter », peut alors réactiver le bouton focalisé : un Espace pour monter peut lancer Importer, Exporter, voire « Créer PK3 » (écriture dans le dossier du jeu).
Correction : retirer `ImGuiConfigFlags.NavEnableKeyboard` (inutile ici), ou ne retenir que `io.WantTextInput` comme « clavier capturé » pour la caméra et retirer Espace/Entrée de `KeyMap`. Test manuel : cliquer « Importer », attendre la fin, appuyer sur Espace.

### Mineur

- `PreviewRenderer.cs:93`/`:1139` : `MakeTexture` peut renvoyer `Fallback()` (données trop courtes) qui est stocké dans `_textures` ; `Clear()` (`:1169`) le dispose, puis `_fallback` (non nul, déjà libéré) est réutilisé à l'envoi suivant. Ne pas stocker le damier dans `_textures`, ou remettre `_fallback = null` après disposition.
- `AppGame.cs:289-298` : si `Upload` échoue, `Upload(null)` remet `_model` à null alors que `Preview` ne l'est pas : la condition `!ReferenceEquals` reste vraie, l'envoi est retenté et l'erreur journalisée à chaque image (journal inondé, interface ralentie). Mémoriser la référence en échec et ne retenter que si elle change.
- `imgui.ini` est versionné (fenêtre « Debug » comprise) : mettre `io.IniFilename = null` dans `ImGuiRenderer`, supprimer le fichier du dépôt.
- `BlendState.Additive` de MonoGame est `SourceAlpha, One` ; Q3 `add` est `One, One`. Différence visible sur les textures à alpha < 1 : utiliser un `BlendState` explicite (One/One).
- Lots transparents non triés (ordre du fichier) ; étiquettes sans test d'occlusion (visibles à travers les murs) ; `MainScreen` n'utilise pas de `ImGuiListClipper` pour 5000 lignes de journal et `OrderBy` alloue à chaque image. Acceptable pour l'instant.
- `ConfigLoader.Load` dans `Initialize` sans garde : configuration absente = plantage au démarrage sans message.

## Évaluation

**Corrections nécessaires.** C1 doit être corrigé : à l'étape Exporter la vue sera faussée, et l'enroulement du BSP est exactement ce que cette vue doit permettre de vérifier. I1 (contrainte de fidélité) et I2 (risque d'action involontaire, dont l'écriture du PK3) aussi. Le reste (disposition, GPU, fil de rendu, matrices Z haut, capture ImGui hors I2) est conforme.
