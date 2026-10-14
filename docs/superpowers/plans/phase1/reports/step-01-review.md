# Étape 01 : relecture

Base d5d500d, tête 14e56f2. Relecture du diff complet, plus comparaison fichier par fichier avec les sources TFU (après `s/Tfu./Kotor./`). Les tests n'ont pas été relancés (chiffres du rapport non revérifiés).

## Conformité

- ✅ Solution `Kotor2Ja.sln` et six projets (net10.0, `Nullable`, `ImplicitUsings`). `TreatWarningsAsErrors` absent, comme dans TFU (vérifié par grep : absent des csproj TFU).
- ✅ `.gitignore` (`bin/`, `obj/`, `out/`) déjà présent, `README.md` court en français, `THIRD-PARTY-NOTICES.md` : origine TFU, GPL-2.0, démarche q3map2, aucune bibliothèque d'images.
- ✅ Tous les fichiers du tableau sont copiés. Comparaison avec TFU : seuls écarts, `Tfu.` vers `Kotor.`, `JaExportOptions` vers `JaDefaults` (VertexLighting, Compiler) et le retrait de `MaterialAlpha.From`/`BlendName`. Rien d'autre n'a été modifié.
- ✅ En-têtes GPL-2.0 conservés sur les fichiers du compilateur (10 en-têtes « Licence : GPL-2.0 » présents). Les fichiers `Kotor.Core` copiés n'en avaient pas à l'origine : rien à conserver.
- ✅ Namespaces `Tfu.*` renommés, plus aucune occurrence de `Tfu.` dans le diff.
- ✅ `JaDefaults.SkyShader = "textures/skies/hevil"`. Les valeurs sun/ambient reprennent exactement `JaExportOptions` TFU.
- ✅ `MaterialAlpha.From` retiré, record, `AlphaKind` et `DefaultBlend` conservés (conforme à l'étape).
- ✅ `TestEnv.KotorDir` (`KOTOR_PATH`, sinon chemin Steam s'il existe, sinon null) et `JaBaseDir` conformes à l'interface.
- ✅ Trois projets de tests, paquets repris de TFU (xUnit 2.9.3, SkippableFact...). `Kotor2Ja.Cli.Tests` ne référence pas SkippableFact, acceptable (vide).
- ✅ Tests supprimés listés avec leur raison dans le rapport. `SpawnPlacer` et `SpawnPlacerTests` exclus à bon droit (dépendent de `Scene`/`MapWriter`).
- ✅ Les ajouts hors liste (`MeshSimplifier`, `TriangleBudget`, cinq constantes de `JaDefaults`) sont signalés dans le rapport et nécessaires : `WalkableCollision` appelle `MeshSimplifier.Reduce`, `VertexLighting.Default` lit les constantes. Copies identiques à TFU.
- ⚠️ Non vérifiable ici : build 0 avertissement et tests 20/20 et 39/39 (chiffres du rapport, non relancés).
- ✅ Aucun commit réalisé par l'implémenteur, conforme à la décision du contrôleur.

## Points forts

- Copie fidèle : la comparaison automatique montre que les écarts sont minimaux et tous justifiés.
- Rapport honnête et précis : écarts à la liste, tests retirés, `PngTestReader` devenu orphelin, mojibake de TFU signalé.
- `THIRD-PARTY-NOTICES.md` propre (UTF-8), cohérent avec les en-têtes des fichiers.
- Doc de `MaterialAlpha` mise à jour pour expliquer l'absence de `From`.

## Problèmes

### Critique

Aucun.

### Important

Aucun.

### Mineur

1. `src/Kotor.Core/Geometry/MeshSimplifier.cs` et `TriangleBudget.cs` (258 et 79 lignes) sont copiés sans leurs tests TFU (`MeshSimplifierTests`). Le code n'est couvert qu'indirectement par `WalkableCollisionTests`. Fix : copier `MeshSimplifierTests` si autonome, ou le noter pour une étape suivante.
2. `src/Kotor.Core/Geometry/TriangleBudget.cs` : la doc cite `InstancePruner` (non copié) dans un `<see cref>` ; `src/Kotor.Core/Diagnostics/ImportLog.cs:3` : la doc liste encore des codes TFU (`marker-no-actor`, `actor-no-gto`...) qui ne correspondent pas à la convention MAJUSCULES du contexte partagé. Fix : adapter les commentaires (sans toucher à la logique).
3. `tests/Kotor.Core.Tests/Support/PngTestReader.cs` copié mais plus utilisé par aucun test (déjà signalé). Fix : le garder pour un test `PngWriter` à venir, ou écrire un `PngWriterTests` minimal qui l'emploie (PngWriter n'a donc aucun test).
4. Noms TFU conservés dans les sorties (`textures/tfu2ja/`, `_tfu_*`, dossier `..\tfu2ja`, `Pk3Builder.cs:23` préfixe `map textures/tfu2ja/`). Non demandé par l'étape et signalé par l'implémenteur : à trancher avant l'étape 08, avec une constante partagée plutôt que des littéraux dupliqués dans `ShaderNames`, `Pk3Builder`, `RenderSurfaces`.
5. `tests/Kotor.Core.Tests/TestEnv.cs:20` : `JaBaseDir` en dur (pas de variable d'environnement, contrairement à `KotorDir`). Fix : lire par exemple `JA_BASE`, avec ce chemin par défaut.
6. `tests/Kotor2Ja.Cli.Tests` ne contient aucun test (avertissement VSTest). Fix : un test minimal sur `Program.Main` ou le laisser tel quel jusqu'à l'étape CLI.

## Évaluation

**Approuvé.** Conforme à l'étape, copie fidèle, écarts minimaux et documentés. Six remarques mineures, aucune bloquante.
