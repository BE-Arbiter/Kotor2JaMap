# Étape 08 — Rapport d'implémentation

Statut : OK (aucun commit, comme demandé).

## Ce qui a été construit

- `ExportOptions`, `ConfigLoader` (défauts si fichier absent ; clés `kotorDir`, `jaBaseDir`, `workDir`, `scale`, `lightmapBrightness`), `ShaderPlanner` (`Plan` et `Write`, texte exact de l'étape), `PlannedShader`, `ShaderKind` : dans `src/Kotor.Core/Export/`.
- `ModuleExporter` (`Export(module, mapName, opt, log)`, surcharge `Export(ModuleScene, ...)`, `BuildPlan(...)` public pour tester la scène convertie) : dans `src/Kotor.Compiler/Export/ModuleExporter.cs`, namespace `Kotor.Core.Export`.
  - Échelle des positions seules, normales inchangées. UV0 et UV1 : `t = 1 - v`.
  - `SurfaceInput.DoubleSided = true` pour `LightmappedCutout`, `Additive`, `Blend`.
  - Texture manquante (null, y compris `notexture`) : `textures/kotor/missing.tga` (damier magenta/noir 8×8). Lightmap manquante (ou sans UV1) : shader `Unlit` et `LIGHTMAP_MISSING`.
  - Luminosité des lightmaps appliquée aux pixels RGB de la TGA (borné à 255).
  - Collision : `Solid` = maillages non Additive/Blend + triangles non marchables des walkmeshes ; `Walkable` = triangles marchables. Tout mis à l'échelle.
  - Entités : `worldspawn` (`message`, `_kotor_module`), `info_player_start` (origine entrée × échelle + (0,0,25), `angle` = atan2). Projection verticale sur les triangles marchables ; sinon `SPAWN_FALLBACK` au-dessus du centre (boîte englobante) du walkmesh le plus proche.
  - `SHADER_LIMIT` si plus de 1024 shaders : échec propre, aucun pk3. Le pk3 est construit via un dossier de préparation `<WorkDir>\<carte>\pk3\` (nettoyé à chaque export), puis fichier temporaire et renommage.
  - Journal `<WorkDir>\<carte>\<carte>.journal.txt` (toutes les entrées + résumé par code), écrit même en cas d'échec.
- `Pk3Builder.Build(stagingDir, pk3Path)` : zippe tout le dossier de préparation (noms en minuscules, `/`), exige un `maps/*.bsp`.
- CLI `export-ja` (`--name`, `--scale`, `--lightmap-brightness`, `--config`, `--kotor`) ; `kotor2ja.config.json` copié à la sortie ; code de sortie 0 / 1 / 2.
- Littéraux TFU remplacés : `ShaderNames.Prefix = "textures/kotor/"` est la constante unique ; ajout de `LightmapPrefix` et `MissingTexture`. `MaxMaterialLength` devient `static readonly` (calculé depuis `Prefix.Length`, soit 44). Les tests existants (ShaderWriter, MaterialAlpha, GlobalFog) ont été adaptés.

## Preuve TDD

- RED : après écriture des tests (`ShaderPlannerTests`, `ConfigLoaderTests`, `ModuleExporterTests`, `Pk3BuilderTests` adapté, `ExportJaCommandTests`) et avant l'implémentation, `dotnet build tests/Kotor.Core.Tests` échoue : `CS0246 ExportOptions introuvable` (puis `CS0103 ModuleExporter introuvable`).
- GREEN : `dotnet test Kotor2Ja.sln` : Cli.Tests 2/2, Compiler.Tests 53/53, Core.Tests 89/89 (dont `Real_export_tar_m02aa`, exécuté vers un JaBaseDir temporaire), 0 échec, aucun avertissement.
- Tests ajoutés en plus de l'étape : `Uv_v_is_flipped_for_both_channels`, `Double_sided_only_for_cull_none_kinds`, `Spawn_falls_back_above_nearest_walkmesh`, `Missing_texture_uses_missing_tga_and_missing_lightmap_goes_unlit`, `Synthetic_export_writes_pk3_with_expected_files_and_journal`, `Other_shader_texts_are_exact`, `Config_file_overrides_defaults`, tests CLI (module inconnu, installation absente).

## Fichiers modifiés

Créés : `src/Kotor.Core/Export/{ExportOptions,ConfigLoader,ShaderPlanner}.cs`, `src/Kotor.Compiler/Export/ModuleExporter.cs`, `src/Kotor2Ja.Cli/Commands/ExportJaCommand.cs`, `src/Kotor2Ja.Cli/kotor2ja.config.json`, `tests/Kotor.Core.Tests/Export/{ShaderPlannerTests,ModuleExporterTests,ConfigLoaderTests}.cs`, `tests/Kotor2Ja.Cli.Tests/ExportJaCommandTests.cs`.
Modifiés : `Pk3Builder.cs`, `ShaderNames.cs`, `ShaderWriter.cs` (commentaire), `Program.cs`, `Kotor2Ja.Cli.csproj`, `tests/Kotor.Core.Tests/Kotor.Core.Tests.csproj` (référence vers Kotor.Compiler), `Pk3BuilderTests.cs`, `ShaderWriterTests.cs`, `MaterialAlphaTests.cs`, `tests/Kotor.Compiler.Tests/GlobalFogTests.cs`.

## Export réel `export-ja tar_m02aa` (code de sortie 0)

```
Maillages : 574
Triangles : 41216
Textures manquantes : 1
Journal par code :
  BSP_WARNING : 2
  COLLISION_CAPPED : 1
  LIGHTMAP_MISSING : 3
  MDL_NO_UV : 5
  MESH_NOT_RENDERED : 15
  MESH_NO_TEXTURE : 4
  NODE_LIGHT : 9
  TEXTURE_ENVMAP : 8
  TEXTURE_MISSING : 3
pk3 : C:\Program Files (x86)\Steam\steamapps\common\Jedi Academy - SWGL_DEV\GameData\base\kotor_tar_m02aa.pk3
```

- pk3 : 5 269 580 octets, 99 fichiers (BSP 15,7 Mo, shader 66 Ko avec 262 blocs, 72 lightmaps `textures/kotor/lm/`, `missing.tga` présent).
- BSP : terrain 231 brushes marchables ; collision 10 761 brushes prismatiques ; `COLLISION_CAPPED` : 12 724 polygones solides retirés sur 23 485 (plus petites aires d'abord, limites BSP). Nombre de surfaces non exposé par le compilateur.
- Seul `kotor_tar_m02aa.pk3` a été écrit dans `GameData\base\`. Jeu non lancé.

## Concerns

1. **Écart de structure** : `ModuleExporter` est dans `Kotor.Compiler` et non `Kotor.Core` (l'étape le place dans Core), car Kotor.Compiler référence déjà Kotor.Core (dépendance circulaire sinon). Namespace conservé (`Kotor.Core.Export`). `Kotor.Core.Tests` référence donc aussi Kotor.Compiler.
2. **Ajout** : une entité `info_player_deathmatch` identique à `info_player_start` (non demandée) pour que `devmap` fonctionne en FFA ; à retirer si non souhaité.
3. **Collision plafonnée** sur tar_m02aa (plus de la moitié des polygones solides retirés) : risque de trous de collision en jeu, à vérifier à l'étape 09.
4. Conflit de nom de shader (même texture en Unlit et en Blend par exemple) : le second reçoit un suffixe `_<kind>` et `SHADER_NAME_CONFLICT` est journalisé (non prévu par l'étape).
5. Les noms de shader lightmappés (`<tex>__<lm>`) ne sont pas tronqués ; un nom dépassant 63 caractères n'est pas détecté.
6. Pas de test CLI de bout en bout (le projet de tests CLI n'a pas SkippableFact) ; couvert par l'export réel exécuté à la main.
