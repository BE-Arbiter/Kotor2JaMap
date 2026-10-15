# SWGLKotor-Map

Converts modules (maps) from *Star Wars: Knights of the Old Republic* and *Knights of the Old Republic II* into maps for *Jedi Academy* (SWGL). The project is written in C# (.NET 10) and comes as a desktop application with a command line tool.

For each module it produces a `.pk3` containing the BSP (render geometry, lightmaps, collisions, sky), the shaders, the textures, and the entities (spawn points, placed objects as `misc_model_static` MD3 models, doors as `func_door`).

## Download

Prebuilt portable builds (Windows x64) are published on the GitHub releases page of this repository. Use them if you do not want to build the project yourself.

## Requirements

- A Windows PC with the .NET 10 SDK (only to build from source).
- KOTOR and/or KOTOR 2 installed (Steam). Both are only read, never modified.
- A Jedi Academy SWGL installation, where the generated `.pk3` files are written.

Default paths (Steam):

| Game | Default folder |
| --- | --- |
| KOTOR | `C:\Program Files (x86)\Steam\steamapps\common\swkotor` |
| KOTOR 2 | `C:\Program Files (x86)\Steam\steamapps\common\Knights of the Old Republic II` |
| Jedi Academy (SWGL) | `C:\Program Files (x86)\Steam\steamapps\common\Jedi Academy - SWGL_DEV\GameData\base` |

## Build and test

```
dotnet build Kotor2Ja.sln
dotnet test Kotor2Ja.sln
```

The integration tests that read an installed game are skipped when that game is missing. Tests never write to the Jedi Academy folder.

## Desktop application

```
dotnet run -c Release --project src/Kotor2Ja.App
```

The window is titled "KOTOR → Jedi Academy".

1. Pick a module in the `KOTOR` or `KOTOR 2` tree on the left (a game whose folder is not found is greyed out). Zone names come from the game's own `dialog.tlk`, so they appear in the language of your installation.
2. Click `Preview`. The module is read and shown in 3D: rooms, markers, placed objects and doors with their real meshes.
3. Use the button at the bottom right to go through the steps: `Import` (builds the shaders, textures, BSP inputs and entities), `Export` (compiles the BSP into a staging folder and shows the result, including the generated MD3 models), then `Create PK3`.
4. Only `Create PK3` writes into the Jedi Academy `base` folder, as `kotor_<map>.pk3` (KOTOR) or `kotor2_<map>.pk3` (KOTOR 2).

The `Log` panel lists progress, warnings and errors. Every log entry has a stable code (for example `TEXTURE_MISSING`), and a `<map>.journal.txt` file with a summary by code is written to the work folder. The view options `Collisions`, `Sky` and `Double-sided` toggle what the 3D view shows.

Camera: hold the right mouse button to look around, `W`/`Z` `A`/`Q` `S` `D` to move, `Space`/`E` to go up, `Ctrl`/`C` to go down, `Shift` to go faster, mouse wheel to change the speed.

### Scripted runs (no clicks)

The application accepts command line options to run the workflow and take a screenshot unattended:

```
Kotor2Ja.App [--game kotor|kotor2] --module <module> [--run-to preview|import|export|pk3]
             [--camera "x y z yaw pitch"] [--screenshot <png>] [--exit]
```

- `--module` selects the module (required with any other option).
- `--run-to` runs the workflow up to the given step (`pk3` writes into the Jedi Academy folder).
- `--camera` places the camera (position in Jedi Academy units, then yaw and pitch in degrees).
- `--screenshot` saves the 3D view to a PNG file.
- `--exit` closes the application when done; the exit code is 1 if a step failed.

## Command line

```
dotnet run --project src/Kotor2Ja.Cli -- <command> [options]
```

| Command | Purpose |
| --- | --- |
| `list-modules` | Lists the modules of the chosen game with the name of their zone. |
| `export-obj <module> --out <folder>` | Exports a module as OBJ/MTL (visual check in Blender) and writes a log by code. |
| `export-ja <module>` | Converts a module into a `.pk3` for Jedi Academy. |

Options common to all commands:

- `--game kotor|kotor2` (default `kotor`).
- `--kotor <folder>` installation folder of the chosen game.

Options of `export-ja`:

- `--name <map>` map name (default: the module name). Allowed characters: `a-z`, `0-9` and `_`, 48 characters at most.
- `--scale <n>` scale from KOTOR metres to Jedi Academy units.
- `--lightmap-brightness <n>` lightmap brightness multiplier.
- `--bsp-limits swgl|vanilla` BSP limits profile.
- `--config <file>` JSON configuration file.

Exit code 0 means success, 1 invalid arguments or a failed export, 2 game folder or module not found.

Examples:

```
dotnet run --project src/Kotor2Ja.Cli -- list-modules --game kotor2
dotnet run --project src/Kotor2Ja.Cli -- export-ja tar_m02aa --name taris_demo
```

## Configuration

Settings are read from `kotor2ja.config.json`, next to the executable (the application and the command line tool use the same file, `--config` overrides it for the command line). Missing keys use the defaults.

```json
{
  "kotorDir": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\swkotor",
  "kotor2Dir": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Knights of the Old Republic II",
  "jaBaseDir": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Jedi Academy - SWGL_DEV\\GameData\\base",
  "workDir": "C:\\jka_maps\\kotor_work",
  "scale": 40,
  "lightmapBrightness": 1.0,
  "bspLimits": "swgl"
}
```

| Key | Meaning |
| --- | --- |
| `kotorDir` | KOTOR installation folder. |
| `kotor2Dir` | KOTOR 2 installation folder. |
| `jaBaseDir` | The Jedi Academy `base` folder where the `.pk3` files are written. |
| `workDir` | Work folder: staging files, journals and caches, one sub-folder per map. |
| `scale` | Scale from KOTOR metres to Jedi Academy units (default 40). Must be a finite number greater than 0. |
| `lightmapBrightness` | Multiplier applied to the lightmaps (default 1.0). Must be a finite number greater than 0. |
| `bspLimits` | `swgl` (default) uses the larger limits of the SWGL engine; `vanilla` uses the original Jedi Academy limits. |

Environment variables take priority over the configuration file for the game folders:

- `KOTOR_PATH` overrides `kotorDir`.
- `KOTOR2_PATH` overrides `kotor2Dir`.

The command line option `--kotor` takes priority over both.

## What is converted

- **World geometry**: room meshes with their textures and lightmaps, vertex lighting from light nodes, sky, fog, walkmesh-based collisions and the spawn point.
- **Placed objects** (placeables): converted to MD3 models placed with `misc_model_static`, with their collision when a walkmesh exists.
- **Doors**: converted to `func_door` entities. Door models are BSP sub-models or MD3 models, and all doors slide up.
- **Not converted**: creatures and encounters (NPCs, counted in the log as `NPC_SKIPPED`), reflections, animations, animated textures (exported as still images) and module assembly.

## Notices

Third-party code and references are listed in `THIRD-PARTY-NOTICES.md`.
