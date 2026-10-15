using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Mdl;

namespace Kotor.Core.Models;

/// <summary>MDL KOTOR -> maillages par texture dans le repere du modele (objets places, portes).</summary>
public static class ModelMeshBuilder
{
    /// <summary>
    /// Parcourt <see cref="KotorModel.Walk"/>, applique les transformations de noeud, garde les maillages rendus (ni AABB, ni vides)
    /// et les regroupe par texture (minuscules) ; <paramref name="include"/> restreint le resultat a certains noeuds (pieces d'une porte). Les noeuds lumiere, emetteur et reference n'ont pas de maillage. Le resultat est trie par nom de texture.
    /// </summary>
    public static IReadOnlyList<ModelPart> Build(KotorModel model, ImportLog log, string where, Func<KotorNode, bool>? include = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(log);
        var groups = new SortedDictionary<string, Group>(StringComparer.Ordinal);
        int notRendered = 0, empty = 0, noTexture = 0;
        foreach (var (node, transform) in model.Walk())
        {
            KotorMesh? m = node.Mesh;
            if (m is null || (include is not null && !include(node))) continue;
            if (!m.Render || node.Kind.HasFlag(NodeKind.Aabb)) { notRendered++; continue; }
            if (m.Triangles.Length < 3 || m.Positions.Length == 0) { empty++; continue; }
            if (m.Triangles.Length % 3 != 0 || m.Triangles.Any(i => i < 0 || i >= m.Positions.Length))
            {
                log.Warn("MESH_BAD_INDEX", $"{where}/{node.Name}: triangle index out of range, mesh skipped.");
                continue;
            }
            if (m.Texture0 is null) { noTexture++; continue; }

            string tex = m.Texture0.ToLowerInvariant();
            if (!groups.TryGetValue(tex, out Group? g)) groups[tex] = g = new Group();
            int first = g.Positions.Count;
            bool hasNormals = m.Normals is { } nn && nn.Length == m.Positions.Length;
            bool hasUv = m.Uv0 is { } uu && uu.Length == m.Positions.Length;
            if (!hasUv) log.Warn("MDL_NO_UV", $"{where}/{node.Name}: no diffuse UV, UV set to zero.");
            for (int i = 0; i < m.Positions.Length; i++)
            {
                g.Positions.Add(Vector3.Transform(m.Positions[i], transform));
                g.Normals.Add(hasNormals ? SafeNormalize(Vector3.TransformNormal(m.Normals![i], transform)) : Vector3.UnitZ);
                g.Uv.Add(hasUv ? m.Uv0![i] : Vector2.Zero);
            }
            foreach (int idx in m.Triangles) g.Indices.Add(first + idx);
            if (!hasNormals) FaceNormals(g, first, m.Positions.Length, m.Triangles.Length);
        }
        if (notRendered > 0) log.Warn("MESH_NOT_RENDERED", $"{where}: {notRendered} non-rendered mesh(es) skipped.");
        if (empty > 0) log.Warn("MESH_EMPTY", $"{where}: {empty} empty mesh(es) skipped.");
        if (noTexture > 0) log.Warn("MESH_NO_TEXTURE", $"{where}: {noTexture} rendered mesh(es) without texture skipped.");
        return [.. groups.Select(kv => new ModelPart(kv.Key, [.. kv.Value.Positions], [.. kv.Value.Normals], [.. kv.Value.Uv], [.. kv.Value.Indices]))];
    }

    private sealed class Group
    {
        public readonly List<Vector3> Positions = [];
        public readonly List<Vector3> Normals = [];
        public readonly List<Vector2> Uv = [];
        public readonly List<int> Indices = [];
    }

    /// <summary>Normales lissees pour un maillage sans normales (ses sommets et indices sont les derniers ajoutes au groupe).</summary>
    private static void FaceNormals(Group g, int firstVertex, int vertexCount, int indexCount)
    {
        var acc = new Vector3[vertexCount];
        for (int i = g.Indices.Count - indexCount; i + 2 < g.Indices.Count; i += 3)
        {
            int a = g.Indices[i], b = g.Indices[i + 1], c = g.Indices[i + 2];
            Vector3 n = Vector3.Cross(g.Positions[b] - g.Positions[a], g.Positions[c] - g.Positions[a]);
            acc[a - firstVertex] += n; acc[b - firstVertex] += n; acc[c - firstVertex] += n;
        }
        for (int i = 0; i < vertexCount; i++) g.Normals[firstVertex + i] = SafeNormalize(acc[i]);
    }

    private static Vector3 SafeNormalize(Vector3 v) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.UnitZ;
}
