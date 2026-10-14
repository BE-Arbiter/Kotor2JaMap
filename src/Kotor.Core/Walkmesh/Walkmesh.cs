using System.Numerics;

namespace Kotor.Core.Walkmesh;

/// <summary>Maillage de marche : un materiau (ligne de surfacemat.2da) par triangle.</summary>
public sealed record Walkmesh(Vector3[] Vertices, int[] Triangles, int[] Materials);
