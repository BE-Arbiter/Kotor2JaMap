using Kotor.Core.Modules;
using Kotor.Core.Textures;

namespace Kotor.Core.Scene;

/// <summary>Scene assemblee d'un module. Textures : cle = nom de texture ou de lightmap ; null = introuvable.</summary>
public sealed record ModuleScene(ModuleInfo Info, IReadOnlyList<SceneMesh> Meshes, IReadOnlyList<SceneWalkmesh> Walkmeshes,
    IReadOnlyDictionary<string, KotorTexture?> Textures);
