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

    /// <summary>Mode compact : triangles x feuilles recoupees par la boite d'une surface au-dela desquels elle est scindee. Le moteur (estimation du lancer de rayons de rdsp-vulkan) alloue 208 octets par triangle et par feuille listant la surface.</summary>
    public const int MaxLeafTriangleCost = 1000;

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
    /// si aucune autre). <paramref name="compact"/> trie d'abord les triangles par Morton (surfaces compactes, voir plus bas). Les UV de lightmap suivent les sommets ; les UV0 sont recopiés tels quels (convention Quake).
    /// </summary>
    public static IReadOnlyList<RenderSurface> FromInputs(IReadOnlyList<SurfaceInput> inputs, BspTree tree, ImportLog? log = null, bool compact = false)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(tree);
        var groups = new Dictionary<(string Shader, bool DoubleSided, bool Lm, int Room), List<Builder>>();
        var order = new List<(string Shader, bool DoubleSided, bool Lm, int Room)>();
        var pending = new Dictionary<(string Shader, bool DoubleSided, bool Lm, int Room), List<Tri>>();
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

            var gk = (s.Shader, s.DoubleSided, hasUv1, tree.Clusters.Count > 0 ? s.Room : -1);
            if (!groups.TryGetValue(gk, out List<Builder>? chunks)) { groups[gk] = chunks = [new Builder()]; order.Add(gk); }
            if (!pending.TryGetValue(gk, out List<Tri>? tris)) pending[gk] = tris = [];
            for (int t = 0; t + 2 < s.Indices.Length; t += 3)
            {
                int i0 = s.Indices[t], i1 = s.Indices[t + 1], i2 = s.Indices[t + 2];
                if ((uint)i0 >= (uint)nv || (uint)i1 >= (uint)nv || (uint)i2 >= (uint)nv) continue; // indice invalide : triangle ignoré
                tris.Add(new Tri(si, i0, i1, i2, hasN, hasUv0, hasUv1));
            }
        }

        // Mode compact : triangles d'un même shader triés selon une courbe de Morton avant le découpage, de sorte que chaque surface
        // occupe une région compacte. Sans cela une surface mélange des instances éloignées, sa boîte englobante recoupe presque
        // toutes les feuilles et le moteur (estimation RTX : somme sur les feuilles de triangles par surface) surestime énormément.
        if (compact && pending.Count > 0)
        {
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            foreach (SurfaceInput s in inputs)
                foreach (Vector3 p in s.Positions) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            Vector3 ext = Vector3.Max(hi - lo, new Vector3(1e-3f));
            foreach (var kv in pending)
            {
                List<Tri> list = kv.Value;
                var keys = new ulong[list.Count];
                for (int k = 0; k < list.Count; k++)
                {
                    Tri tr = list[k];
                    SurfaceInput s = inputs[tr.Input];
                    Vector3 c = (s.Positions[tr.I0] + s.Positions[tr.I1] + s.Positions[tr.I2]) / 3f;
                    keys[k] = Morton((c - lo) / ext);
                }
                var idx = Enumerable.Range(0, list.Count).ToArray();
                Array.Sort(idx, (x, y) => keys[x] != keys[y] ? keys[x].CompareTo(keys[y]) : x.CompareTo(y));
                pending[kv.Key] = idx.Select(k => list[k]).ToList();
            }
        }

        void AddTri(Builder bl, Tri tr)
        {
            SurfaceInput s = inputs[tr.Input];
            Vector3 a = s.Positions[tr.I0], b = s.Positions[tr.I1], c = s.Positions[tr.I2];
            Vector3 faceN = Vector3.Cross(b - a, c - a);
            faceN = faceN.LengthSquared() > 0 ? Vector3.Normalize(faceN) : Vector3.UnitZ;
            // Enroulement JA (mesure etape 15) : ordre inverse de l'entree, on emet v0, v2, v1.
            foreach (int i in new[] { tr.I0, tr.I2, tr.I1 })
            {
                // Sans normale en entrée la normale dépend de la face : pas de partage de sommet.
                long key = tr.HasN ? ((long)tr.Input << 32) | (uint)i : -1 - uniqueKey++;
                bl.Add(key, s.Positions[i], tr.HasUv0 ? s.Uv0[i] : Vector2.Zero, tr.HasN ? s.Normals[i] : faceN, tr.HasUv1 ? s.Uv1![i] : null);
            }
        }

        foreach (var gk in order)
        {
            List<Builder> chunks = groups[gk];
            List<Tri> list = pending[gk];
            if (!compact)
            {
                foreach (Tri tr in list)
                {
                    Builder bl = chunks[^1];
                    // Découpage par triangles entiers : au plus 3 nouveaux sommets par triangle.
                    if (bl.Positions.Count + 3 > MaxSurfaceVerts || bl.Indices.Count + 3 > MaxSurfaceIndexes)
                        chunks.Add(bl = new Builder());
                    AddTri(bl, tr);
                }
                continue;
            }

            // Mode compact : bissection de la liste triee tant qu'une surface depasse les limites du moteur ou que
            // triangles x feuilles recoupees par sa boite depasse MaxLeafTriangleCost.
            chunks.Clear();
            void Emit(int l, int r)
            {
                var bl = new Builder();
                for (int k = l; k < r; k++) AddTri(bl, list[k]);
                bool fits = bl.Positions.Count <= MaxSurfaceVerts && bl.Indices.Count <= MaxSurfaceIndexes;
                if (r - l > 1 && (!fits || (long)(r - l) * LeavesTouching(tree, bl.Positions, gk.Room).Count > MaxLeafTriangleCost))
                {
                    int mid = (l + r) / 2;
                    Emit(l, mid);
                    Emit(mid, r);
                    return;
                }
                chunks.Add(bl);
            }
            if (list.Count > 0) Emit(0, list.Count);
            else chunks.Add(new Builder());
        }

        var result = new List<RenderSurface>();
        foreach (var gk in order)
            foreach (Builder b in groups[gk])
            {
                if (b.Indices.Count == 0) continue;
                Vector3[] pos = b.Positions.ToArray();
                result.Add(new RenderSurface(gk.Shader, pos, b.Uvs.ToArray(), b.Normals.ToArray(), b.Indices.ToArray(),
                    LeavesTouching(tree, pos, gk.Room), DoubleSided: gk.DoubleSided, LightmapUvs: gk.Lm ? b.Uv1.ToArray() : null));
            }
        return result;
    }

    /// <summary>
    /// Feuilles non solides dont la région recoupe la boîte englobante des points (descente des deux côtés quand la boîte
    /// chevauche un plan). Si toutes les feuilles touchées sont solides, on les garde : aucune surface ne reste sans feuille.
    /// </summary>
    public static IReadOnlyList<int> LeavesTouching(BspTree tree, IReadOnlyList<Vector3> points, int room = -1)
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
        // Surface d une piece : seulement les feuilles de la boite de cette piece (repli : toutes celles touchees).
        if (room >= 0 && tree.Clusters.Count > 0)
        {
            var own = all.Where(l => tree.Leaves[l].InRoom(room)).ToList();
            if (own.Count > 0) all = own;
        }
        var open = all.Where(l => !tree.Leaves[l].IsSolid).ToList();
        return open.Count > 0 ? open : all;
    }

    private readonly record struct Tri(int Input, int I0, int I1, int I2, bool HasN, bool HasUv0, bool HasUv1);

    /// <summary>Code de Morton 3 x 21 bits d'un point de [0,1]^3.</summary>
    private static ulong Morton(Vector3 p)
    {
        static ulong Spread(uint v)
        {
            ulong x = v & 0x1FFFFFUL;
            x = (x | (x << 32)) & 0x1F00000000FFFFUL;
            x = (x | (x << 16)) & 0x1F0000FF0000FFUL;
            x = (x | (x << 8)) & 0x100F00F00F00F00FUL;
            x = (x | (x << 4)) & 0x10C30C30C30C30C3UL;
            x = (x | (x << 2)) & 0x1249249249249249UL;
            return x;
        }
        static uint Q(float f) => (uint)Math.Clamp(f * 2097151f, 0f, 2097151f);
        return Spread(Q(p.X)) | (Spread(Q(p.Y)) << 1) | (Spread(Q(p.Z)) << 2);
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
