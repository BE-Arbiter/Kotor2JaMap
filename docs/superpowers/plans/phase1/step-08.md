# Étape 08 — Export Jedi Academy : échelle, textures, shaders lightmap, point d'apparition, pk3

**But :** `kotor2ja export-ja tar_m02aa` produit `GameData\base\kotor_tar_m02aa.pk3`, prêt pour `devmap tar_m02aa`.

**Fichiers :**
- Créer : `src/Kotor.Core/Export/{ExportOptions,ConfigLoader,ShaderPlanner,ModuleExporter}.cs`
- Modifier : `src/Kotor.Core/Export/Pk3Builder.cs`. La signature devient `Build(string stagingDir, string pk3Path)`, et les tests copiés sont adaptés.
- Créer : `src/Kotor2Ja.Cli/Commands/ExportJaCommand.cs`. Créer `src/Kotor2Ja.Cli/kotor2ja.config.json` (valeurs par défaut, copié à la sortie).
- Tester : `tests/Kotor.Core.Tests/Export/{ShaderPlannerTests,ModuleExporterTests,ConfigLoaderTests}.cs`, `tests/Kotor2Ja.Cli.Tests/ExportJaCommandTests.cs`

**Interfaces :**
- Consomme : `ModuleScene`, `SceneMesh`, `SceneWalkmesh`, `ModuleSceneBuilder` (06), `KotorTexture`, `TxiInfo` (04), `CompileInput`, `SurfaceInput`, `Compiler.Compile` (07), `TgaWriter`, `ImportLog`, `ShaderNames`, `JaDefaults` (01).
- Produit :
```csharp
public sealed record ExportOptions(string KotorDir, string JaBaseDir, string WorkDir, float Scale = ExportOptions.DefaultScale,
    float LightmapBrightness = 1.0f)
{ public const float DefaultScale = 36f; }
public static class ConfigLoader { public static ExportOptions Load(string? configPath); } // défauts si fichier absent
public enum ShaderKind { Lightmapped, LightmappedCutout, Additive, Blend, Unlit }
public sealed record PlannedShader(string Name, ShaderKind Kind, string Texture, string? Lightmap);
public static class ShaderPlanner
{
    public static PlannedShader Plan(SceneMesh mesh, KotorTexture? texture);
    public static string Write(PlannedShader s);   // texte du bloc shader
}
public sealed record ExportResult(bool Ok, string? Pk3Path, string? Error, int Meshes, int Triangles, int MissingTextures);
public static class ModuleExporter { public static ExportResult Export(string module, string? mapName, ExportOptions opt, ImportLog log); }
```
**Ce qui est fixé :**
- **Échelle** : toutes les positions sont multipliées par `Scale`, sur les mêmes axes (KOTOR et JA ont tous deux Z vers le haut). Les normales sont inchangées.
- **Nom de la carte** : `mapName ?? module`, en minuscules. L'option `--name` sert aux variantes de l'étape 09.
- **Fichiers dans le pk3** (préparés dans `<WorkDir>\<mapName>\pk3\`) :
  - `maps/<mapName>.bsp` ;
  - `scripts/kotor_<mapName>.shader` ;
  - `textures/kotor/<texture>.tga` ;
  - `textures/kotor/lm/<lightmap>.tga`.

  Le pk3 va dans `<JaBaseDir>\kotor_<mapName>.pk3`. Il est remplacé seulement si toute la chaîne réussit (fichier temporaire, puis renommage, comme dans le projet TFU).
- **Texture manquante** : `textures/kotor/missing.tga` (damier magenta 8×8) avec `TEXTURE_MISSING`, déjà journalisé à l'étape 06. L'export continue. **Lightmap manquante** : le shader devient `Unlit`, avec le code `LIGHTMAP_MISSING`.
- La luminosité des lightmaps est appliquée aux pixels de la TGA (`× LightmapBrightness`, borné à 255).
- **Choix du shader** (`ShaderPlanner.Plan`) :

| Condition | Kind | Nom |
|---|---|---|
| `Txi.Blending == "additive"` | `Additive` | `textures/kotor/<tex>` |
| `TransparencyHint != 0` et texture avec alpha, non `punchthrough` | `Blend` | `textures/kotor/<tex>` |
| lightmap présente et (`punchthrough` ou texture avec alpha, `TransparencyHint == 0`) | `LightmappedCutout` | `textures/kotor/<tex>__<lm>` |
| lightmap présente | `Lightmapped` | `textures/kotor/<tex>__<lm>` |
| sinon | `Unlit` | `textures/kotor/<tex>` |

- **Texte des shaders** (exact) :
```
// Lightmapped
textures/kotor/<tex>__<lm>
{
	{
		map textures/kotor/lm/<lm>.tga
		tcGen lightmap
		rgbGen identity
	}
	{
		map textures/kotor/<tex>.tga
		blendFunc GL_DST_COLOR GL_ZERO
		rgbGen identity
	}
}
// LightmappedCutout
textures/kotor/<tex>__<lm>
{
	cull none
	{
		map textures/kotor/<tex>.tga
		alphaFunc GE128
		depthWrite
		rgbGen identity
	}
	{
		map textures/kotor/lm/<lm>.tga
		tcGen lightmap
		blendFunc GL_DST_COLOR GL_ZERO
		depthFunc equal
		rgbGen identity
	}
}
// Additive
textures/kotor/<tex>
{
	surfaceparm nonsolid
	surfaceparm trans
	cull none
	{
		map textures/kotor/<tex>.tga
		blendFunc GL_ONE GL_ONE
		rgbGen identity
	}
}
// Blend
textures/kotor/<tex>
{
	surfaceparm trans
	cull none
	{
		map textures/kotor/<tex>.tga
		blendFunc GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA
		rgbGen vertex
	}
}
// Unlit
textures/kotor/<tex>
{
	{
		map textures/kotor/<tex>.tga
		rgbGen vertex
	}
}
```
  Les shaders sont dédoublonnés par nom. **Plus de 1024 shaders** (`MAX_MAP_SHADERS`) : échec propre avec `SHADER_LIMIT`, sans pk3.
- **Collisions** :
  - `Solid` = tous les `SceneMesh` dont le shader n'est ni `Additive` ni `Blend`, plus les triangles non marchables des walkmeshes ;
  - `Walkable` = les triangles marchables des walkmeshes.

  Tout est mis à l'échelle.
- **Entités** :
  - `worldspawn` avec `message` = `AreaName` et `_kotor_module` = module ;
  - `info_player_start` à `Entry.Position × Scale + (0, 0, 25)`, avec `angle` = `atan2(Dir.Y, Dir.X)` en degrés.
  - Si l'entrée n'est sur aucun walkmesh (projection verticale), le point est placé au-dessus du centre du walkmesh le plus proche, avec le code `SPAWN_FALLBACK`.
- **Journal** : `<WorkDir>\<mapName>\<mapName>.journal.txt`. Il contient toutes les entrées de l'`ImportLog` (codes et messages) et un résumé par code.
- **CLI** : `kotor2ja export-ja <module> [--name <carte>] [--scale <f>] [--lightmap-brightness <f>] [--config <fichier>] [--kotor <dir>]`.
  - Code de sortie 0 si OK, 1 en cas d'échec de compilation ou de limite, 2 si le module est inconnu.
  - Le résumé affiché : maillages, triangles, textures manquantes, entrées par code, chemin du pk3.

- [ ] **Étape 1 : tests qui échouent**
  - `Planner_picks_kind_per_table` : un cas par ligne du tableau.
  - `Lightmapped_shader_text_is_exact` : le texte produit est égal au bloc ci-dessus (nom `textures/kotor/a__b`).
  - `Scale_is_applied_to_positions_not_normals` : sur une scène synthétique, un sommet (1,2,3) devient (36,72,108) et une normale (0,0,1) reste (0,0,1).
  - `Spawn_uses_entry_and_yaw` : sur une scène synthétique avec un walkmesh marchable sous l'entrée, une entrée (1,0,0) de direction (0,1) donne un `info_player_start` à `origin "36 0 25"` et `angle "90"`.
  - `Too_many_shaders_fails_cleanly` : 1025 textures distinctes donnent `Ok == false`, `SHADER_LIMIT` et aucun pk3.
  - `Pk3_contains_expected_entries` (adapté du test TFU) : `maps/x.bsp`, `scripts/kotor_x.shader` et `textures/kotor/t.tga`, en minuscules avec des `/`.
  - `Config_defaults_when_missing` : `Scale == 36`.
  - `[SkippableFact] Real_export_tar_m02aa` (vers un `JaBaseDir` temporaire) :
    - `Ok` ;
    - le pk3 existe et contient `maps/tar_m02aa.bsp` ;
    - le nombre de `textures/kotor/*.tga` est au moins égal au nombre de textures non manquantes ;
    - le journal existe.
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter.**
- [ ] **Étape 4 : `dotnet test Kotor2Ja.sln`** tout vert, puis `dotnet run -c Release --project src/Kotor2Ja.Cli -- export-ja tar_m02aa`. Le pk3 est écrit dans le vrai `GameData\base\`. Copier le résumé dans le rapport. **Ne pas tester en jeu** (étape 09).
- [ ] **Étape 5 : rapport** `reports/step-08-impl.md`.
