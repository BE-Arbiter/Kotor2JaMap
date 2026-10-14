# Étape 08 — Correctifs 1 (relecture + décisions du contrôleur)

## Changements par point

1. **Limites de collision SWGL** (`Kotor.Compiler/Bsp/Limits.cs`, `BspWriter.cs`, `WalkableCollision.cs`, `Compiler.cs`, `ModuleExporter.cs`, `ExportOptions.cs`, `ConfigLoader.cs`, `ExportJaCommand.cs`).
   - `Limits` porte maintenant `MaxBrushes, MaxPlanes, MaxNodes, MaxLeaves, MaxBrushSides, MaxLeafBrushes` (les deux derniers ont des valeurs par défaut vanilla, donc les anciens appels à 4 arguments compilent toujours).
   - `Limits.Default` devient `Limits.Vanilla` (0x8000 / 0x20000 / 0x20000 / 0x20000 / 0x20000 / 0x40000). Nouveau `Limits.Swgl` : les six valeurs à 0x100000. Shaders, draw verts/indexes, surfaces et modèles ne bougent pas (`LimitCheck`, `MaxDrawIndexes`). `Limits.FromName("swgl"|"vanilla")`.
   - `MaxBrushSides` n'est plus une constante, et `BspWriter.MaxLeafBrushes` (0x40000, codé en dur) est supprimé : `BspWriter`, `WalkableCollision.MaxBrushesFor` et le budget de `Compiler.cs` passent par le profil. Aucune autre limite de collision codée en dur n'a été trouvée (`BspTree` utilise déjà le profil).
   - `ExportOptions.BspLimits = "swgl"`, clé `bspLimits` du JSON, option CLI `--bsp-limits`. L'exporteur utilise `Limits.FromName(opt.BspLimits)`. Profil inconnu : erreur (voir point 4).
2. **Namespace** : `ModuleExporter` (et `ExportResult`, `ExportPlan`, `ExportTexture`) sont dans `Kotor.Compiler.Export`. `using` ajoutés dans le CLI et les tests ; `Compiler.Compiler.Compile` écrit `global::Kotor.Compiler.Compiler.Compile`.
3. **Config livrée** : `kotorDir` retiré de `kotor2ja.config.json` (ajout de `"bspLimits": "swgl"`). La résolution option > `KOTOR_PATH` > Steam s'applique.
4. **Validation** : `ExportOptions.Validate()` refuse `Scale` et `LightmapBrightness` non finis ou <= 0, ainsi qu'un profil inconnu. CLI : message sur stderr, code de sortie 1. API : `ModuleExporter.Export` renvoie `ExportResult(Ok=false, Error=...)` ; `BuildPlan` lève `ArgumentException`.
5. **Journal** : `WriteJournal` renvoie le message d'échec, qui est mis dans `ExportResult.JournalError` (nouveau champ optionnel) et dans le journal (`JOURNAL_WRITE_FAILED`). Le CLI l'affiche sur stderr (« Avertissement : ... »). L'export ne échoue pas pour autant.
6. **Compiler.cs** : BOM retiré, mojibake corrigé par aller-retour cp1252/UTF-8 (5 lignes d'en-tête et commentaires ; aucune ligne de code modifiée). Vérifié avec `git diff`.

## Tests couvrants (ajoutés)

- `LimitsProfileTests` : valeurs des profils, `FromName`, et 40 000 polygones de collision (> 0x8000 brushes) : avec `Swgl` aucun `COLLISION_CAPPED` et au moins 40 000 brushes écrits ; avec `Vanilla` `COLLISION_CAPPED`. Les tests de plafonnement existants (maintenant `Limits.Vanilla` ou limites explicites) passent.
- `ConfigLoaderTests` : `bspLimits` (défaut et lecture), config livrée n'écrase pas `KOTOR_PATH`, validation (0, négatif, NaN, infini ; profil inconnu).
- `ModuleExporterTests` : échelle invalide (résultat d'erreur et `ArgumentException`), échec du journal signalé sans échec de l'export, profil vanilla accepté.
- `ExportJaCommandTests` : échelle invalide = 1, profil inconnu = 1, option `--bsp-limits` reconnue.

## Commandes

- `dotnet test Kotor2Ja.sln -c Release` : Cli.Tests 5/5, Compiler.Tests 55/55, Core.Tests 99/99, aucun avertissement, aucune erreur.
- `dotnet run -c Release --project src/Kotor2Ja.Cli -- export-ja tar_m02aa`.

## Export réel de tar_m02aa

Premier lancement (cible `GameData\base`) : **échec propre `EXPORT_IO_ERROR` « Access to the path is denied »**. Le BSP est compilé, mais le remplacement du `kotor_tar_m02aa.pk3` est refusé : le processus `SWGL_SP.x86_64.exe` est en cours d'exécution et verrouille le pk3 (un `mv` manuel donne « Device or resource busy »). Le pk3 existant (5 269 580 octets, 21:31) n'a pas été modifié, ce qui confirme l'écriture atomique. Je n'ai pas fermé le jeu. **Le pk3 de GameData n'est donc pas à jour : fermer le jeu puis relancer la commande.**

Pour valider quand même, deuxième lancement avec `--config` pointant `jaBaseDir` vers le dossier temporaire du scratchpad (rien écrit dans GameData) :

```
Module tar_m02aa -> carte tar_m02aa
Maillages : 574 / Triangles : 41216 / Textures manquantes : 1
Journal : BSP_WARNING 2, LIGHTMAP_MISSING 3, MDL_NO_UV 5, MESH_NOT_RENDERED 15, MESH_NO_TEXTURE 4,
          NODE_LIGHT 9, TEXTURE_ENVMAP 8, TEXTURE_MISSING 3
pk3 : 7 246 003 octets
```

- **`COLLISION_CAPPED` : absent** (avant : 12 724 polygones solides sur 23 485 retirés).
- Brushes : 23 485 solides + 231 de terrain = 23 716 (lump 284 592 octets / 12), aucune réduction.
- Autres lumps : plans 214 786, nœuds 26 573, côtés de brush 408 031, BSP de 22,4 Mo.
