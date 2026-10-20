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

No configuration is needed in most cases: without a `kotor2ja.config.json` next to the executables, the KOTOR, KOTOR 2 and Jedi Academy folders are detected through Steam (the Steam path from the registry, then every library listed in `steamapps\libraryfolders.vdf`). The work folder defaults to `%LOCALAPPDATA%\Kotor2Ja\work`. In the application, the `Settings` panel (left column) shows the folders in use (invalid ones are in red), has a `Detect` button and a `Save` button that writes `kotor2ja.config.json` next to the executable (or, if that folder is not writable, to `%APPDATA%Kotor2Ja`; both locations are read at startup, the executable folder first) and reloads the module list.

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
Kotor2Ja.App [--game kotor|kotor2|swtor|jk] --module <module>[,<module>...] [--run-to preview|import|export|pk3]
             [--camera "x y z yaw pitch"] [--screenshot <png>] [--exit]
```

- `--module` selects the module, or several comma-separated modules to assemble (required with any other option).
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
| `export-ja <module>[,<module>...]` | Converts a module (or several assembled modules, see Assembling modules) into a `.pk3` for Jedi Academy. |

Options common to all commands:

- `--game kotor|kotor2|jk` (default `kotor`; `export-ja` also accepts `swtor`).
- `--kotor <folder>` installation folder of the chosen game.

Options of `export-ja`:

- `--name <map>` map name (default: the module name; several modules: `<reference>_<count>`). Allowed characters: `a-z`, `0-9` and `_`, 48 characters at most.
- `--reference <module>` reference module of an assembly (default: the first module).
- `--scale <n>` scale from KOTOR metres to Jedi Academy units.
- `--lightmap-brightness <n>` lightmap brightness multiplier.
- `--bsp-limits swgl|vanilla` BSP limits profile.
- `--config <file>` JSON configuration file.
- `--map-visuals on|off` (default `on`) writes the textured editing map described in Editing the map; `off` skips it.

Exit code 0 means success, 1 invalid arguments or a failed export, 2 game folder or module not found. For `export-obj`, exit code 1 also means that the module produced no triangle.

Examples:

```
dotnet run --project src/Kotor2Ja.Cli -- list-modules --game kotor2
dotnet run --project src/Kotor2Ja.Cli -- export-ja tar_m02aa --name taris_demo
```

## Assembling modules

KOTOR splits a zone into several modules linked by transition doors (for example `end_m01aa` and `end_m01ab` on the Endar Spire). Several modules of the same game can be assembled into one Jedi Academy map, written as a single `.pk3`.

In the application, Ctrl+click selects several modules of one game. One of them is the reference module (the first one by default, selectable in the list): it stays in place and gives the map its lighting and spawn point. Preview places the other modules automatically, door against door, using the transition doors of the modules (`LinkedToModule` / `LinkedTo` in the area GIT). A KOTOR door does not tell on which side its rooms are, so each linked module is tried back to back and turned by 180 degrees around the door, and the pose that overlaps the modules already placed the least is kept (`ASSEMBLY_FLIPPED` in the log when the turned pose wins). The transition door is kept, unlocked, and a short junction corridor fills any gap between the two door frames. Modules that no transition links to the reference must be placed by hand in Edit placement (move, rotate, choose the two doors to join); Import stays disabled until every module is linked. Overlapping rooms are reported in the log (`ASSEMBLY_OVERLAP`) and in the placement editor.

The command line tool only uses the automatic placement:

```
dotnet run --project src/Kotor2Ja.Cli -- export-ja end_m01aa,end_m01ab
dotnet run --project src/Kotor2Ja.Cli -- export-ja end_m01aa,end_m01ab --reference end_m01ab --name endar_spire
```

- The modules are separated by commas, without spaces (or quote the list).
- `--reference <module>` chooses the reference module (default: the first one).
- `--name` defaults to `<reference>_<number of modules>`, for example `end_m01aa_2` (pk3 `kotor_end_m01aa_2.pk3`).
- If some modules cannot be linked automatically, nothing is written and the exit code is 1 with the message `Modules not linked: <modules>. Use the application to place them manually.`

The scripted runs of the application accept the same list: `--module end_m01aa,end_m01ab` behaves like the multiple selection, and `--run-to import|export|pk3` fails with the same message when a module is not linked.

The first example, `end_m01aa,end_m01ab`, gives the map `end_m01aa_2` (about 1.24 million leaf surfaces, loaded by the SWGL engine). The junction door opens, but a static, locked door of `end_m01aa` stands just behind it, so the second module cannot be reached on foot yet.

Limits: transitions made by triggers (not doors) are not used to place modules. Some modules contain their own copy of the rooms around the transition door (an airlock, a decorative locked door); these copies are kept and can overlap the other module or block the way. The assembled map must still fit the BSP limits: the `swgl` profile allows 2,097,152 leaf surfaces (the engine allocates them from the file), `vanilla` keeps 131,072. Larger assemblies fail with a `BSP limit exceeded` message and nothing is written.

## Editing the map

Every export also writes a `.map` that shows the whole map, textured, in a map editor (NetRadiant, GtkRadiant). It is written under the work folder, never into the `.pk3`:

```
<workDir>\<map>\edit\base\maps\<map>.map      the map to open
<workDir>\<map>\edit\base\textures\...        copy of the textures (plus a grey textures/common/caulk.tga)
<workDir>\<map>\edit\base\shaders\<map>.shader copy of the map shaders
```

The `.map` inside the `.pk3` (`maps/<map>.map`) is unchanged: collision brushes (`caulk` / `clip`) and entities only, so it stays small. The editing map is that file plus the visible geometry.

How to open it (NetRadiant, game `Jedi Academy`):

1. Set the engine path to `<workDir>\<map>\edit` (the folder that contains `base`), so the editor finds `base\textures` and `base\shaders`. To keep your usual engine path instead, add `<workDir>\<map>\edit\base` as an extra resource path.
2. Open `<workDir>\<map>\edit\base\maps\<map>.map`.

What it contains:

- The `worldspawn` and the entities of the `.pk3` map, with the collision brushes as before (`common/caulk`, doors as sub-model entities).
- One `func_group` with the key `_kotor_visual` `1`: one thin prism per rendered triangle (2 units thick behind the surface). The face on the surface itself carries the real texture of the surface (for example `jk/01wbolts`: the base texture, not the lightmap shader, which an editor cannot show), the other faces are `caulk`. Double sided surfaces (foliage, grates, glass) are textured on both big faces. Texture names are written without the `textures/` prefix, as Radiant and `q3map2` expect.
- Texture mapping uses the classic Quake 3 `.map` format (the only one that every Jedi Academy editor setup reads). It is an axial projection with rotation, scale and shift worked out from the UV of each triangle, so planar mapping (walls, floors, ceilings, signs) is reproduced exactly. Curved meshes (rocks, trees, river beds) have skewed UV that this format cannot hold: the mapping is then a best fit (on `danm14aa`, 29,348 of 38,785 triangles are off by more than 0.2% of a texture, 7,700 of them by more than 5%; on `jk_01narshadda` 108 of 7,852), and the log reports the count as `MAP_VISUAL_UV_APPROX`. Surfaces at exactly 45 degrees may choose the other projection axis in the editor.
- Models (`misc_model_static` and so on) stay entities with their `model` key; their geometry is not drawn in the editor.

Sizes (examples): `jk_01narshadda` 7.5 MB, about 97,000 lines, 7,852 visual brushes using 103 textures (the `.pk3` map is 2.9 MB); KOTOR `danm14aa` 49.5 MB, 565,000 lines, 38,785 visual brushes using 20 textures (the `.pk3` map is 25.7 MB). The editing folder also holds a copy of the textures (16 MB and 66 MB). At most 300,000 visual brushes are written; beyond that the smallest triangles are left out (`MAP_VISUAL_CAPPED`).

Compiling the editing map with `q3map2` gives textured visible geometry plus the collision brushes; it is meant for editing and looking, not as a replacement for the converter (lightmaps, vertex lighting, doors, sky box and visibility are not rebuilt). The BSP and the `.pk3` do not depend on this file: with `--map-visuals off` they are byte-for-byte the same.

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
| `jediKnightDir` | Jedi Knight installation folder (optional). |
| `jaBaseDir` | The Jedi Academy `base` folder where the `.pk3` files are written. |
| `workDir` | Work folder: staging files, journals and caches, one sub-folder per map (default `%LOCALAPPDATA%\Kotor2Ja\work`). |
| `scale` | Scale from KOTOR metres to Jedi Academy units (default 40). Must be a finite number greater than 0. |
| `lightmapBrightness` | Multiplier applied to the lightmaps (default 1.0). Must be a finite number greater than 0. |
| `bspLimits` | `swgl` (default) uses the larger limits of the SWGL engine; `vanilla` uses the original Jedi Academy limits. |

For the game folders the order is: environment variable, then configuration file, then the folders detected through Steam, then the fallback paths. Environment variables:

- `KOTOR_PATH` overrides `kotorDir`.
- `KOTOR2_PATH` overrides `kotor2Dir`.
- `JK_DIR` overrides `jediKnightDir`.

The command line option `--kotor` takes priority over both.

## Jedi Knight: Dark Forces II

The application and the command line tool also convert the levels of *Star Wars Jedi Knight: Dark Forces II* (Steam). The game is only read, never modified.

- **Folder**: detected through Steam (`steamapps\common\Star Wars Jedi Knight`, which must contain `Episode\JK1.GOB`); fallback `C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight`. Configuration key `jediKnightDir`, environment variable `JK_DIR`, `Settings` field `Jedi Knight` (red when invalid; `Detect` fills it).
- **Levels**: the 34 levels, grouped in the tree under `Jedi Knight` as `Single player` (25), `Multiplayer` (6) and `CTF` (3). Names come from the game (`episode.jk` and its string table), else the file name. Levels are converted one at a time (no assembly).
- **Output**: `jk_<level>.pk3` (for example `jk_01narshadda.pk3`), with assets under `textures/jk/`, `models/jk/`, `sound/jk/` and `music/jk/`.
- **Converted**: geometry with its textures, animated and scrolling textures, sky, water, lava and killing zones, JK lighting, placed objects with their models (spinning fans turn and can be walked through), secret areas (counted by the Jedi Academy "secrets found" counter, `target_secret`), continuous sector thrust (fans, drafts, ducts: `00_sectorthrust` and the level file `THRUST`, as non-solid `trigger_push` zones, `JK_SECTOR_THRUST`), pick-ups (weapons, ammunition, health, shields) as Jedi Academy items, start points, sector sounds, the level music (below), and moving things (doors, lifts, bridges, traps, switches) as moving Jedi Academy entities, with their opening and stopping sounds (sound class `startmove` / `stopmove` played by `target_speaker` fired from the door, `JK_MOVER_SOUND`).
- **Music**: JK plays CD tracks chosen by the level's `PlaySong` COG call (the Steam edition keeps them as `MUSIC\TrackNN.ogg`, tracks 12-18 and 22-32). Jedi Academy cannot play Ogg, so each level with music gets one `music/jk/<level>.wav` (22 050 Hz 16-bit stereo, the tracks of the level's cycle one after the other in the order JK plays them, decoded with NVorbis), played in a loop by the worldspawn `music` key (no `.mus` file needed). 23 of the 25 single player levels have music (`JK_MUSIC` gives the tracks, length and size); a level pk3 with music is 10 to 38 MB (without music: 0.5 to 8 MB). Levels 06abarons and 09fuelstation use CD tracks that the Steam edition does not ship, and the multiplayer and CTF levels have no music script: `JK_MUSIC_NONE`. Volume scripts (fades, lowering during dialogue, level 01's music that only plays in the bar) are not converted: constant volume (`JK_MUSIC_VOLUME_SCRIPT`).
- **Not converted / known limits**: enemies and NPCs (no actors: the log has `JK_ACTOR_SKIPPED` with their number); sector thrust that events change at run time (switched fans, lift thrust, puzzles: `JK_SECTOR_THRUST_SKIPPED`); a converted push sets the player's velocity instead of adding to it, so it cannot be walked against; mover sounds are limited to the start and stop sounds (no looping movement sound, no train sounds, no sounds played by the COG itself: `JK_MOVER_SOUND_SKIPPED`); water is visible and swimmable as a volume but has no underwater fog; door and lift rotations are not animated (a rotating part is rebuilt as a translation or stays put, `JK_COG_APPROX`); particles and sprites (`JK_PARTICLE_SKIPPED`, `JK_SPRITE_SKIPPED`; `JK_PARTICLE_APPROX` and `JK_COG_REVEAL` from the design are not implemented); things created by scripts while the game runs (`JK_RUNTIME_THING`). The force star given for finding every secret is not converted (`JK_SECRET_AREAS`). Barriers that scripts can open without moving anything (force fields, grates, breakable glass) are walkable from the start (`JK_ADJOIN_COG_OPENED`). The converted levels have not been verified in game yet.
- **Log codes**: approximations and skipped details are listed by code (`JK_*`) in the log and in `<map>.journal.txt`; every code, its level and its meaning are in [`docs/superpowers/specs/jk-log-codes.md`](docs/superpowers/specs/jk-log-codes.md).
- **Command line**: `list-modules --game jk` and `export-ja <level> --game jk` (`--kotor <folder>` gives the Jedi Knight folder). Application: `Kotor2Ja.App --game jk --module 01narshadda --run-to export --screenshot out.png`.

## What is converted (KOTOR)

- **World geometry**: room meshes with their textures and lightmaps, vertex lighting from light nodes, sky, fog, walkmesh-based collisions and the spawn point.
- **Placed objects** (placeables): converted to MD3 models placed with `misc_model_static`, with their collision when a walkmesh exists.
- **Doors**: converted to `func_door` entities. Door models are BSP sub-models or MD3 models, and all doors slide up.
- **Not converted**: creatures and encounters (NPCs, counted in the log as `NPC_SKIPPED`), reflections, animations, animated textures (exported as still images) and module assembly.

## Notices

Third-party code and references are listed in `THIRD-PARTY-NOTICES.md`.
