// Inspiré de la démarche de q3map2 / NetRadiant-custom (surfaces triangle soup rattachées aux feuilles, GPL-2.0) : aucun code copié.
// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;

namespace Kotor.Compiler.Bsp;

/// <summary>
/// Surface de type triangle soup : sommets (unités Quake), indices locaux, shader et feuille d'attache.
/// <paramref name="LightmapUvs"/> : UV de lightmap par sommet (null = pas de lightmap, éclairage par sommet).
/// </summary>
public sealed record RenderSurface(string Shader, Vector3[] Positions, Vector2[] Uvs, Vector3[] Normals, int[] Indices, int Leaf,
    bool Sky = false, bool DoubleSided = false, Vector2[]? LightmapUvs = null);

public static class RenderSurfaces
{
    // Moteur (OpenJK ParseTriSurf, qfiles.h) : SHADER_MAX_VERTEXES / SHADER_MAX_INDEXES (6x), refus si le compte est >= a la limite.
    // L'erreur observee en jeu cite 1200 : on reste strictement sous 1200 sommets et 7200 indices (multiple de 3).
    public const int MaxSurfaceVerts = 1199;
    public const int MaxSurfaceIndexes = 7197;

    /// <summary>Feuille contenant le point (descente de l'arbre ; front = côté positif du plan).</summary>
    public static int FindLeaf(BspTree tree, Vector3 p)
    {
        if (tree.Nodes.Count == 0) return 0;
        int n = 0;
        while (true)
        {
            BspNode node = tree.Nodes[n];
            BspPlane pl = tree.Planes[node.PlaneIndex];
            int child = Vector3.Dot(pl.Normal, p) >= pl.Dist ? node.FrontChild : node.BackChild;
            if (child < 0) return -1 - child;
            n = child;
        }
    }

    /// <summary>
    /// Découpe une surface en entrée en surfaces de rendu : une par feuille touchée (triangle rattaché à la feuille
    /// de son centroïde), chacune sous les limites du moteur. Les UV de lightmap suivent les sommets.
    /// Les UV0 sont recopiés tels quels (convention Quake : t = 0 en haut, à la charge de l'appelant).
    /// </summary>
    public static IReadOnlyList<RenderSurface> FromInput(SurfaceInput s, BspTree tree)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(tree);
        int nv = s.Positions.Length;
        bool hasN = s.Normals.Length == nv;
        bool hasUv0 = s.Uv0.Length == nv;
        bool hasUv1 = s.Uv1 is not null && s.Uv1.Length == nv;

        var byLeaf = new SortedDictionary<int, List<Builder>>();
        for (int t = 0; t + 2 < s.Indices.Length; t += 3)
        {
            int i0 = s.Indices[t], i1 = s.Indices[t + 1], i2 = s.Indices[t + 2];
            if ((uint)i0 >= (uint)nv || (uint)i1 >= (uint)nv || (uint)i2 >= (uint)nv) continue; // indice invalide : triangle ignoré
            Vector3 a = s.Positions[i0], b = s.Positions[i1], c = s.Positions[i2];
            int leaf = FindLeaf(tree, (a + b + c) / 3f);
            if (!byLeaf.TryGetValue(leaf, out List<Builder>? chunks)) byLeaf[leaf] = chunks = [new Builder()];
            Builder bl = chunks[^1];
            // Découpage par triangles entiers : au plus 3 nouveaux sommets par triangle.
            if (bl.Positions.Count + 3 > MaxSurfaceVerts || bl.Indices.Count + 3 > MaxSurfaceIndexes)
                chunks.Add(bl = new Builder());
            Vector3 faceN = Vector3.Cross(b - a, c - a);
            faceN = faceN.LengthSquared() > 0 ? Vector3.Normalize(faceN) : Vector3.UnitZ;
            // Enroulement JA (mesure etape 15) : ordre inverse de l'entree, on emet v0, v2, v1.
            foreach (int i in new[] { i0, i2, i1 })
            {
                // Sans normale en entrée la normale dépend de la face : pas de partage de sommet.
                bl.Add(hasN ? i : -1 - bl.Positions.Count, s.Positions[i], hasUv0 ? s.Uv0[i] : Vector2.Zero,
                    hasN ? s.Normals[i] : faceN, hasUv1 ? s.Uv1![i] : null);
            }
        }

        return byLeaf.SelectMany(kv => kv.Value.Select(b => new RenderSurface(s.Shader, b.Positions.ToArray(), b.Uvs.ToArray(),
            b.Normals.ToArray(), b.Indices.ToArray(), kv.Key, DoubleSided: s.DoubleSided,
            LightmapUvs: hasUv1 ? b.Uv1.ToArray() : null))).ToList();
    }

    private sealed class Builder
    {
        private readonly Dictionary<int, int> _map = [];
        public List<Vector3> Positions { get; } = [];
        public List<Vector2> Uvs { get; } = [];
        public List<Vector2> Uv1 { get; } = [];
        public List<Vector3> Normals { get; } = [];
        public List<int> Indices { get; } = [];

        public void Add(int key, Vector3 p, Vector2 st, Vector3 n, Vector2? lm)
        {
            if (!_map.TryGetValue(key, out int i))
            {
                i = Positions.Count;
                _map[key] = i;
                Positions.Add(p); Uvs.Add(st); Normals.Add(n);
                if (lm is { } v) Uv1.Add(v);
            }
            Indices.Add(i);
        }
    }
}
