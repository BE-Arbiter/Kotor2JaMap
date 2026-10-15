# SWGLKotor-Map

Converts modules (maps) from *Star Wars: Knights of the Old Republic* and *Knights of the Old Republic II* into maps for *Jedi Academy* (SWGL). The project is written in C# (.NET 10) and comes as a desktop application with a command line tool.

For each module it produces a `.pk3` containing the BSP (render geometry, lightmaps, collisions, sky), the shaders, the textures, and the entities (spawn points, placed objects as `misc_model_static` MD3 models, doors as `func_door`).

## Portable build / Download

A prebuilt Windows x64 package (no .NET installation needed) is attached to the `latest` pre-release on the GitHub releases page of this repository. It is rebuilt automatically by the `build-latest` workflow on every push to `master`.

Unzip `Kotor2Ja-win-x64.zip` anywhere. It contains:

| File | Purpose |
| --- | --- |
| `Kotor2Ja.App.exe` | The desktop application. |
| `Kotor2Ja.Cli.exe` | The command line tool (`list-modules`, `export-obj`, `export-ja`). |
| `kotor2ja.config.example.json` | Example configuration. Copy it to `kotor2ja.config.json` to use it. |
| `README.md`, `THIRD-PARTY-NOTICES.md` | Documentation and notices. |

No configuration is needed in most cases: without a `kotor2ja.config.json` next to the executables, the KOTOR, KOTOR 2 and Jedi Academy folders are detected through Steam (the Steam path from the registry, then every library listed in `steamapps\libraryfolders.vdf`). The work folder defaults to `%LOCALAPPDATA%\Kotor2Ja\work`. In the application, the `Settings` panel (left column) shows the folders in use (invalid ones are in red), has a `Detect` button and a `Save` button that writes `kotor2ja.config.json` next to the executable and reloads the module list.

To build the same package locally: `powershell -File scripts/publish.ps1` (output: `artifacts/Kotor2Ja-win-x64.zip`). The executables are self-contained single files; the first start unpacks the native libraries to a temporary folder, so it can take a moment.

## Requirements

- A Windows PC with the .NET 10 SDK (only to build from source).
- KOTOR and/or KOTOR 2 installed (Steam). Both are only read, never modified.
- A Jedi Academy SWGL installation, where the generated `.pk3` files are written.

Folders are detected through Steam. Fallback paths when detection finds nothing:

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

Exit code 0 means success, 1 invalid arguments or a failed export, 2 game folder or module not found. For `export-obj`, exit code 1 also means that the module produced no triangle.

Examples:

```
dotnet run --project src/Kotor2Ja.Cli -- list-modules --game kotor2
dotnet run --project src/Kotor2Ja.Cli -- export-ja tar_m02aa --name taris_demo
```

## Configuration

Settings are read from `kotor2ja.config.json`, next to the executable (see also Portable build above). The application and the command line tool use the same file. Only `export-ja` accepts `--config` to read another file; `list-modules` and `export-obj` always read the file next to the executable. Missing keys use the defaults.

```json
{
  "kotorDir": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\swkotor",
  "kotor2Dir": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Knights of the Old Republic II",
  "jaBaseDir": "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Jedi Academy - SWGL_DEV\\GameData\\base",
  "workDir": "C:\\Users\\you\\AppData\\Local\\Kotor2Ja\\work",
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
| `workDir` | Work folder: staging files, journals and caches, one sub-folder per map (default `%LOCALAPPDATA%\Kotor2Ja\work`). |
| `scale` | Scale from KOTOR metres to Jedi Academy units (default 40). Must be a finite number greater than 0. |
| `lightmapBrightness` | Multiplier applied to the lightmaps (default 1.0). Must be a finite number greater than 0. |
| `bspLimits` | `swgl` (default) uses the larger limits of the SWGL engine; `vanilla` uses the original Jedi Academy limits. |

For the game folders the order is: environment variable, then configuration file, then the folders detected through Steam, then the fallback paths. Environment variables:

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
