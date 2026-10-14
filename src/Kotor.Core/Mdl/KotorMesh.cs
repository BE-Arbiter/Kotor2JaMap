using System.Numerics;

namespace Kotor.Core.Mdl;

/// <summary>Maillage d'un noeud. Texture* en minuscules, null si "NULL" ou vide. Uv0 = diffuse, Uv1 = lightmap.</summary>
public sealed record KotorMesh(string? Texture0, string? Texture1, bool Render, bool HasLightmap, int TransparencyHint,
    Vector3[] Positions, Vector3[]? Normals, Vector2[]? Uv0, Vector2[]? Uv1, int[] Triangles);
