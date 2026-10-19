using System.Numerics;
using Kotor.Core.Textures;

namespace Kotor.Core.Scene;

/// <summary>Maillage en positions monde du module (metres KOTOR). Uv0 : convention GL (v=0 en bas), non retournee.</summary>
public sealed record SceneMesh(string Room, string Node, string Texture, string? Lightmap, TxiInfo Txi, int TransparencyHint,
    Vector3[] Positions, Vector3[] Normals, Vector2[] Uv0, Vector2[]? Uv1, int[] Triangles)
{
    /// <summary>Opacity chosen by the source game. <see cref="SurfaceAlpha.Auto"/> (KOTOR): deduced from the texture and the TXI.</summary>
    public SurfaceAlpha Alpha { get; init; } = SurfaceAlpha.Auto;

    /// <summary>
    /// Seconde texture fondue sur <see cref="Texture"/> (sol SWTOR : deux couches de la carte de melange), opacite par sommet <see cref="VertexAlpha"/>.
    /// Null : une seule texture.
    /// </summary>
    public string? OverlayTexture { get; init; }

    /// <summary>Repetition de <see cref="OverlayTexture"/> relative a Uv0 (1 = memes UV).</summary>
    public Vector2 OverlayUvScale { get; init; } = Vector2.One;

    /// <summary>Opacite de <see cref="OverlayTexture"/> par sommet (0 a 255, meme indice que <see cref="Positions"/>) ; null sans seconde texture.</summary>
    public byte[]? VertexAlpha { get; init; }
}

/// <summary>Opacity of a mesh declared by its source (SWTOR material alpha mode) instead of deduced from the texture alpha.</summary>
public enum SurfaceAlpha { Auto, Opaque, Cutout, Blend, Additive, Multiply }
