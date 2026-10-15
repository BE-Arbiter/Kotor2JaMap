// Inspiré de la démarche de q3map2 / NetRadiant-custom (surfaces triangle soup rattachées aux feuilles, GPL-2.0) : aucun code copié.
// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;
using Kotor.Core.Diagnostics;

namespace Kotor.Compiler.Bsp;

/// <summary>
/// Surface de type triangle soup : sommets (unités Quake), indices locaux, shader et feuilles qui la référencent (leafsurfaces, relation plusieurs à plusieurs).
/// <paramref name="LightmapUvs"/> : UV de lightmap par sommet (null = pas de lightmap, éclairage par sommet).
/// </summary>
public sealed record RenderSurface(string Shader, Vector3[] Positions, Vector2[] Uvs, Vector3[] Normals, int[] Indices, IReadOnlyList<int> Leaves,
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

    /// <summary>Surfaces de rendu d'une seule entrée (voir <see cref="FromInputs"/>).</summary>
    public static IReadOnlyList<RenderSurface> FromInput(SurfaceInput s, BspTree tree, ImportLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        return FromInputs([s], tree, log);
    }

    /// <summary>
    /// Construit les surfaces de rendu : toutes les entrées de même shader (même DoubleSided, même présence d'UV de lightmap)
    /// sont fusionnées puis découpées uniquement selon les limites du moteur, en gardant l'ordre des entrées (chunks compacts).
    /// Chaque surface est rattachée à toutes les feuilles non solides que sa boîte englobante recoupe (repli : feuilles solides
    /// si aucune autre). Les UV de lightmap suivent les sommets ; les UV0 sont recopiés tels quels (convention Quake).
    /// </summary>
    public static IReadOnlyList<RenderSurface> FromInputs(IReadOnlyList<SurfaceInput> inputs, BspTree tree, ImportLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(tree);
        var groups = new Dictionary<(string Shader, bool DoubleSided, bool Lm), List<Builder>>();
        var order = new List<(string Shader, bool DoubleSided, bool Lm)>();
        int uniqueKey = 0;
        for (int si = 0; si < inputs.Count; si++)
        {
            SurfaceInput s = inputs[si];
            int nv = s.Positions.Length;
            bool hasN = s.Normals.Length == nv;
            bool hasUv0 = s.Uv0.Length == nv;
            bool hasUv1 = s.Uv1 is not null && s.Uv1.Length == nv;
            if (log is not null)
            {
                if (!hasN) log.Warn("SURFACE_ATTRIBUTE_MISMATCH", $"Surface '{s.Shader}': {s.Normals.Length} normal(s) for {nv} vertex(es), face normals used.");
                if (!hasUv0) log.Warn("SURFACE_ATTRIBUTE_MISMATCH", $"Surface '{s.Shader}': {s.Uv0.Length} UV0 for {nv} vertex(es), zero UVs used.");
                if (s.Uv1 is not null && !hasUv1) log.Warn("SURFACE_ATTRIBUTE_MISMATCH", $"Surface '{s.Shader}': {s.Uv1.Length} UV1 for {nv} vertex(es), no lightmap (vertex lighting).");
            }

            var gk = (s.Shader, s.DoubleSided, hasUv1);
            if (!groups.TryGetValue(gk, out List<Builder>? chunks)) { groups[gk] = chunks = [new Builder()]; order.Add(gk); }
            for (int t = 0; t + 2 < s.Indices.Length; t += 3)
            {
                int i0 = s.Indices[t], i1 = s.Indices[t + 1], i2 = s.Indices[t + 2];
                if ((uint)i0 >= (uint)nv || (uint)i1 >= (uint)nv || (uint)i2 >= (uint)nv) continue; // indice invalide : triangle ignoré
                Vector3 a = s.Positions[i0], b = s.Positions[i1], c = s.Positions[i2];
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
                    long key = hasN ? ((long)si << 32) | (uint)i : -1 - uniqueKey++;
                    bl.Add(key, s.Positions[i], hasUv0 ? s.Uv0[i] : Vector2.Zero, hasN ? s.Normals[i] : faceN, hasUv1 ? s.Uv1![i] : null);
                }
            }
        }

        var result = new List<RenderSurface>();
        foreach (var gk in order)
            foreach (Builder b in groups[gk])
            {
                if (b.Indices.Count == 0) continue;
                Vector3[] pos = b.Positions.ToArray();
                result.Add(new RenderSurface(gk.Shader, pos, b.Uvs.ToArray(), b.Normals.ToArray(), b.Indices.ToArray(),
                    LeavesTouching(tree, pos), DoubleSided: gk.DoubleSided, LightmapUvs: gk.Lm ? b.Uv1.ToArray() : null));
            }
        return result;
    }

    /// <summary>
    /// Feuilles non solides dont la région recoupe la boîte englobante des points (descente des deux côtés quand la boîte
    /// chevauche un plan). Si toutes les feuilles touchées sont solides, on les garde : aucune surface ne reste sans feuille.
    /// </summary>
    public static IReadOnlyList<int> LeavesTouching(BspTree tree, IReadOnlyList<Vector3> points)
    {
        if (tree.Nodes.Count == 0) return [0];
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (Vector3 p in points) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
        var all = new List<int>();
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            BspNode node = tree.Nodes[stack.Pop()];
            BspPlane pl = tree.Planes[node.PlaneIndex];
            // Extrêmes de n.p sur la boîte.
            float dMin = 0, dMax = 0;
            dMin += pl.Normal.X >= 0 ? pl.Normal.X * lo.X : pl.Normal.X * hi.X; dMax += pl.Normal.X >= 0 ? pl.Normal.X * hi.X : pl.Normal.X * lo.X;
            dMin += pl.Normal.Y >= 0 ? pl.Normal.Y * lo.Y : pl.Normal.Y * hi.Y; dMax += pl.Normal.Y >= 0 ? pl.Normal.Y * hi.Y : pl.Normal.Y * lo.Y;
            dMin += pl.Normal.Z >= 0 ? pl.Normal.Z * lo.Z : pl.Normal.Z * hi.Z; dMax += pl.Normal.Z >= 0 ? pl.Normal.Z * hi.Z : pl.Normal.Z * lo.Z;
            bool front = dMax >= pl.Dist, back = dMin < pl.Dist; // front = n.p >= dist, comme FindLeaf
            if (back) Visit(node.BackChild);
            if (front) Visit(node.FrontChild);
        }
        void Visit(int child)
        {
            if (child < 0) all.Add(-1 - child); else stack.Push(child);
        }
        all.Sort();
        var open = all.Where(l => !tree.Leaves[l].IsSolid).ToList();
        return open.Count > 0 ? open : all;
    }

    private sealed class Builder
    {
        private readonly Dictionary<long, int> _map = [];
        public List<Vector3> Positions { get; } = [];
        public List<Vector2> Uvs { get; } = [];
        public List<Vector2> Uv1 { get; } = [];
        public List<Vector3> Normals { get; } = [];
        public List<int> Indices { get; } = [];

        public void Add(long key, Vector3 p, Vector2 st, Vector3 n, Vector2? lm)
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
