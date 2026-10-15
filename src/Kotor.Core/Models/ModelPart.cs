using System.Numerics;

namespace Kotor.Core.Models;

/// <summary>Partie d'un modele : tous les triangles d'une meme texture, dans le repere du modele. UV en convention GL (v = 0 en bas).</summary>
public sealed record ModelPart(string Texture, Vector3[] Positions, Vector3[] Normals, Vector2[] Uv, int[] Indices);
