using System.Numerics;
using Kotor.Core.Entities;

namespace Kotor.Pipeline.Preview;

/// <summary>Sommet de la vue, en unites JA. UV « haut-gauche » (t = 0 en haut), comme les images.</summary>
public readonly record struct PreviewVertex(Vector3 Position, Vector3 Normal, Vector2 Uv0, Vector2 Uv1);

public enum PreviewBlend { Opaque, Cutout, Additive, Alpha }

/// <summary>Texture RGBA 8 bits, ligne 0 = haut.</summary>
public sealed record PreviewTexture(int Width, int Height, byte[] Rgba);

public sealed record PreviewBatch(string Name, string DiffuseKey, string? LightmapKey, PreviewBlend Blend, bool DoubleSided,
    PreviewVertex[] Vertices, int[] Indices);

/// <summary>Repere d'entite. Position et Polygon en unites JA ; Yaw en degres autour de Z.</summary>
public sealed record PreviewMarker(EntityKind Kind, string Label, Vector3 Position, float Yaw, Vector3[]? Polygon);

/// <summary>Ce que l'application affiche. <c>CollisionTriangles</c> : trois points par triangle.</summary>
public sealed record PreviewModel(IReadOnlyList<PreviewBatch> Batches, IReadOnlyDictionary<string, PreviewTexture> Textures,
    Vector3[] CollisionTriangles, IReadOnlyList<PreviewMarker> Markers, Vector3? Spawn, float SpawnYaw, Vector3 BoundsMin, Vector3 BoundsMax)
{
    public const string MissingTextureKey = "missing";

    public long TriangleCount { get; } = Batches.Sum(b => (long)b.Indices.Length / 3);
}
