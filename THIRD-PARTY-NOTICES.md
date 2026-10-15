# Third-party notices

## BSP compiler (`src/Kotor.Compiler/`)

- Origin: code copied from the TFU project (`SWGLFU-Map-importer`, `src/Tfu.Compiler/`), namespaces renamed from `Tfu.*` to `Kotor.*`.
- License: GPL-2.0 (in the header of each concerned file).
- Approach: inspired by q3map2 / NetRadiant-custom (`.map` to `.bsp` chain). The file headers state that no code from these projects was copied.

## Image libraries

No image library is used. The PNG is written by a minimal internal encoder (`src/Kotor.Core/Export/PngWriter.cs`), and the TGA by `TgaWriter` / `TgaReader`. The original TFU project used Pfim for DDS; this dependency is not carried over.

## Test dependencies

The test packages (xUnit, Xunit.SkippableFact, Microsoft.NET.Test.Sdk, coverlet) are not distributed with the tool.

## MonoGame 3.8.5.1 (MonoGame.Framework.Native, MonoGame.Runtime.Windows.Vulkan)

- License: MS-PL.
- Author: MonoGame Team.
- Project: https://www.monogame.net/
- Usage: graphical application `src/Kotor2Ja.App`, Vulkan backend (same packages as the TFU project).

## ImGui.NET 1.91.6.1 (interface)

- License: MIT.
- Author: Eric Mellino and contributors.
- Project: https://github.com/ImGuiNET/ImGui.NET
- `src/Kotor2Ja.App/ImGuiRenderer.cs` is copied from the TFU project (`Tfu2Ja.App`), itself adapted from the MonoGame/XNA example of this repository (MIT license). The original header is kept.
