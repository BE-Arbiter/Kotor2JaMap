using System.Numerics;

namespace Kotor.Core.Geometry;

/// <summary>Un sous-maillage : un materiau, des sommets, des triangles (toujours en liste).</summary>
public sealed class SubMesh
{
    public string MaterialName { get; init; } = "";
    public Vector3[] Positions { get; init; } = [];
    public Vector3[]? Normals { get; init; }
    public Vector2[]? Uv0 { get; init; }
    /// <summary>Triplets d'indices dans <see cref="Positions"/>.</summary>
    public int[] Triangles { get; init; } = [];
}

public sealed class ModelMesh
{
    public string Name { get; init; } = "";
    public IReadOnlyList<SubMesh> SubMeshes { get; init; } = [];
    public Vector3 BBoxCenter { get; init; }
    public Vector3 BBoxExtents { get; init; }
}
