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

    /// <summary>Modules d'une carte assemblee avec leur transformation ; null pour la scene d'un seul module.</summary>
    public IReadOnlyList<AssemblyPartRef>? Parts { get; init; } = null;

    /// <summary>Copie de la scene transformee : positions, normales (rotation seule), lumieres et entree ; textures partagees.</summary>
    public ModuleScene Transformed(RigidTransform t)
    {
        var meshes = Meshes.Select(m => m with
        {
            Positions = m.Positions.Select(t.Apply).ToArray(),
            Normals = m.Normals.Select(t.ApplyDirection).ToArray(),
        }).ToList();
        var walkmeshes = Walkmeshes.Select(w => w with { Positions = w.Positions.Select(t.Apply).ToArray() }).ToList();
        var dir = Info.Entry.Direction;
        var d3 = t.ApplyDirection(new Vector3(dir.X, dir.Y, 0f));
        var info = Info with { Entry = new ModuleEntry(t.Apply(Info.Entry.Position), new Vector2(d3.X, d3.Y)) };
        return this with
        {
            Info = info,
            Meshes = meshes,
            Walkmeshes = walkmeshes,
            Lights = Lights.Select(l => l with { Position = t.Apply(l.Position) }).ToList(),
        };
    }
}

/// <summary>Module d'une carte assemblee et transformation qui lui a ete appliquee.</summary>
public sealed record AssemblyPartRef(string Module, RigidTransform Transform);
