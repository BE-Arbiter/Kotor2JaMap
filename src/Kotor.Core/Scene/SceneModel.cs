using System.Numerics;

namespace Kotor.Core.Scene;

/// <summary>
/// Modele place plusieurs fois (SWTOR) : maillages dans le repere du modele (unites et axes de la scene). L'export en fait un MD3
/// pose par des <c>misc_model_static</c>, ou le remet dans le BSP (<see cref="Bake"/>) quand le plafond d'entites du moteur est atteint.
/// </summary>
public sealed record SceneModel(string Name, IReadOnlyList<SceneMesh> Meshes)
{
    public long Triangles { get; } = Meshes.Sum(m => (long)m.Triangles.Length / 3);

    /// <summary>Maillages de l'instance en repere de scene (piece <paramref name="room"/>).</summary>
    public IEnumerable<SceneMesh> Bake(Matrix4x4 transform, string room)
    {
        Matrix4x4 normalM = Matrix4x4.Invert(transform, out Matrix4x4 inv) ? Matrix4x4.Transpose(inv) : transform;
        bool flip = transform.GetDeterminant() < 0f;
        foreach (SceneMesh m in Meshes)
        {
            var pos = new Vector3[m.Positions.Length];
            var nrm = new Vector3[m.Normals.Length];
            for (int i = 0; i < pos.Length; i++) pos[i] = Vector3.Transform(m.Positions[i], transform);
            for (int i = 0; i < nrm.Length; i++)
            {
                Vector3 n = Vector3.TransformNormal(m.Normals[i], normalM);
                nrm[i] = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitZ;
            }
            int[] tri = m.Triangles;
            if (flip)
            {
                tri = (int[])tri.Clone();
                for (int t = 0; t + 2 < tri.Length; t += 3) (tri[t + 1], tri[t + 2]) = (tri[t + 2], tri[t + 1]);
            }
            yield return m with { Room = room, Positions = pos, Normals = nrm, Triangles = tri };
        }
    }
}

/// <summary>Instance de <see cref="ModuleScene.Models"/>[<paramref name="Model"/>] : p_scene = p_modele * <paramref name="Transform"/>.</summary>
public sealed record SceneModelInstance(int Model, string Room, Matrix4x4 Transform);
