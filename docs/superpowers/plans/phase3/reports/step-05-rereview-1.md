# Étape 05 (phase 3) — re-relecture ciblée de la correction 1

Diff relu : `b1d5035..bd9b718`. Aucun code modifié.

## Verdicts

### 1. Enroulement MD3 — ADDRESSED
- Écriture : `src/Kotor.Core/Export/Md3Writer.cs:126-127` émet (i0, i2, i1) par triangle.
- BSP : `src/Kotor.Compiler/Bsp/RenderSurfaces.cs:85-86` émet `{ i0, i2, i1 }` à partir de l'entrée KOTOR. Le MD3 suit donc exactement la même permutation que le BSP.
- Moteur : `OpenJK-SWGL/code/rd-vulkan/vk_pipelines.cpp:1458` (et 1778, 2032, 2900, 3114) : `frontFace = VK_FRONT_FACE_CLOCKWISE`, même état pour les surfaces BSP et MD3, sans réordonnancement des triangles MD3 : la permutation entrée KOTOR (antihoraire) → JA (horaire) est la bonne dans les deux cas. Cohérent avec le test en jeu rapporté (`step-05-ingame-md3-winding.png`, présente dans le diff ; non rouverte ici).
- Lecture : `src/Kotor.Pipeline/Preview/Md3Reader.cs:45-46` rétablit l'ordre KOTOR (échange des deux derniers indices, involutif), comme `PreviewBuilder.cs:158-159` pour le BSP. Seuls consommateurs de `Md3Reader.Read` : `PreviewBuilder.Models.cs:106` (vue Importer, octets de l'écrivain) et la vue Exporter (fichiers du staging, enroulement JA) : les deux sont donc corrects.
- `WriteSplit` (`Md3Writer.cs:163-181`) conserve l'ordre des triangles ; la permutation à l'écriture s'applique après découpage : correct. Surfaces Cutout (`cull none`, `Md3ShaderWriter.cs:31`) non affectées.
- Tests : `Md3WriterTests.cs:63-70` (ordre 0,2,1,1,2,3) et `PreviewBuilderTests.cs:248-255` (aller-retour).

### 2. Textes en anglais — ADDRESSED
`KotorWorkflowSteps.cs:89` et `:126` ("placed model(s)"), `Compiler.cs:81` (message anglais). Réserve cosmétique : le reste des deux phrases (« Importer : », « triangle(s) », « repere(s) », « relus dans ») reste en français ; la demande ne visait que les trois points cités.

### 3. Mode automatisé — ADDRESSED
- Liste des modules : `AppGame.cs:82` (`_modulesTask` lancé sans condition sur `_auto`), attente `AppGame.cs:154` avant `SelectModule`.
- Caméra par défaut : `AppGame.cs:179-187`. Vérifié par `FreeCamera.cs:29` (forward = cos y·cos p, sin y·cos p, sin p) : caméra à spawn − 64·(cos,sin) + 48 en Z, regard vers le point (pitch = −atan2(48,64)), donc hors du cube d'apparition et dirigée vers lui. Captures `step-05-fix-default.png` / `step-05-fix-shot.png` présentes.

### 4. Minors — ADDRESSED (3/3)
- `modelscale` ignoré pour les portes : `PreviewBuilder.Models.cs:74` (`kind == EntityKind.Placeable && ...`).
- Journal : `PREVIEW_MODEL_MISSING` (`Models.cs:64`, uniquement vue Importer via `logMissing: true`, pas de doublon avec `PREVIEW_MD3_MISSING` de la vue Exporter), `PREVIEW_MODEL_UNREADABLE` (`Models.cs:107`), `PREVIEW_MODEL_BAD_ORIGIN` (`Models.cs:69`). `log` transmis par `PreviewBuilder.cs:69,112` et `KotorWorkflowSteps.cs:86`.
- Code 2 : `AutomationOptions.cs:89-93` (message si `args.Length > 0` sans `--module`) ; `Program.cs:9-13` retourne 2. Test `AutomationOptionsTests.cs:66-76`.

## Nouvelles casses Critical/Important introduites par le diff
Aucune.

Points mineurs (non bloquants) :
- `AppGame.cs:154` : si `_workflow.Modules()` ne revenait jamais, le mode automatisé attendrait indéfiniment (pas de délai maximal). Le `try/catch` de la tâche rend l'échec bruyant improbable ; risque faible.
- Pas de test dédié aux codes `PREVIEW_MODEL_*` ni à `modelscale` des portes (déjà signalé dans le rapport).
- Faux positif possible du journal `PREVIEW_MODEL_MISSING` : tout `model`/`model2` absent de `models` (par exemple un MD3 déjà journalisé comme illisible) est signalé deux fois (UNREADABLE puis MISSING) ; simple redondance.

## Hors périmètre (non modifié par ce diff)
- Les messages de journal des étapes précédentes restent en français (par ex. `RenderSurfaces.cs:64-66`).
- Les rotations des modèles n'utilisent que le lacet ; orientation visuelle de chaque objet non comparée au jeu (déjà notée dans `step-05-impl.md`).
- Le fichier de relecture initiale et les captures ont été ajoutés au dépôt par le commit : aucune relecture du contenu des PNG au-delà de leur présence.
