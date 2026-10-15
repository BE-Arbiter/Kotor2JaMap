using System.Numerics;
using Kotor.Core.Modules;
using Kotor.Core.Textures;

namespace Kotor.Core.Scene;

/// <summary>
/// Scene assemblee d'un module. Textures : cle = nom de texture ou de lightmap ; null = introuvable.
/// <see cref="Lights"/> : noeuds lumiere des pieces ; <see cref="AmbientColor"/> : ambiante de la zone (ARE), 0..1.
/// </summary>
public sealed record ModuleScene(ModuleInfo Info, IReadOnlyList<SceneMesh> Meshes, IReadOnlyList<SceneWalkmesh> Walkmeshes,
    IReadOnlyDictionary<string, KotorTexture?> Textures)
{
    /// <summary>Ambiante par defaut quand l'ARE n'en donne pas.</summary>
    public static readonly Vector3 DefaultAmbient = new(0.2f, 0.2f, 0.2f);

    public IReadOnlyList<SceneLight> Lights { get; init; } = Array.Empty<SceneLight>();
    public Vector3 AmbientColor { get; init; } = DefaultAmbient;

    /// <summary>ARE <c>SunDiffuseColor</c> (0..1); only meaningful when <see cref="AreaLightingKnown"/>.</summary>
    public Vector3 SunDiffuseColor { get; init; }

    /// <summary>True when the ARE was found and readable: the area ambient and sun colour then replace the faux sun of a module without light node.</summary>
    public bool AreaLightingKnown { get; init; }
}
