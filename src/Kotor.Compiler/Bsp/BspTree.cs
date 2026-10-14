// Inspiré de la démarche de q3map2 / NetRadiant-custom (arbre BSP, feuilles référençant des brushes, GPL-2.0) : aucun code copié.
// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;

namespace Kotor.Compiler.Bsp;

/// <summary>Nœud interne : plan de séparation axial. Enfant &gt;= 0 = nœud, &lt; 0 = feuille d'index (-1 - enfant), comme Q3.</summary>
public sealed record BspNode(int PlaneIndex, int FrontChild, int BackChild);

/// <summary>Feuille : solide si au moins un brush la touche.</summary>
public sealed record BspLeaf(IReadOnlyList<int> BrushIndices, Vector3 Min, Vector3 Max)
{
    public bool IsSolid => BrushIndices.Count > 0;
}

public sealed class BspTree
{
    private const int MaxDepth = 40;
    private const int LeafBrushTarget = 4;

    public IReadOnlyList<BspNode> Nodes { get; private init; } = [];
    public IReadOnlyList<BspLeaf> Leaves { get; private init; } = [];
    public IReadOnlyList<BspPlane> Planes { get; private init; } = [];
    public IReadOnlyList<Brush> Brushes { get; private init; } = [];

    public static BspTree Build(IReadOnlyList<Brush> brushes, Limits? limits = null)
    {
        limits ??= Limits.Vanilla;
        limits.CheckBrushes(brushes.Count);

        var nodes = new List<BspNode>();
        var leaves = new List<BspLeaf>();
        var planes = new List<BspPlane>();
        var planeIndex = new Dictionary<(float, float, float, float), int>();

        foreach (var b in brushes)
            foreach (var p in b.Planes) AddPlane(planes, planeIndex, p);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var b in brushes) { min = Vector3.Min(min, b.Min); max = Vector3.Max(max, b.Max); }
        if (brushes.Count == 0) { min = Vector3.Zero; max = Vector3.Zero; }

        Recurse(Enumerable.Range(0, brushes.Count).ToList(), min, max, 0);
        limits.CheckPlanes(planes.Count);
        limits.CheckNodes(nodes.Count);
        limits.CheckLeaves(leaves.Count);
        return new BspTree { Nodes = nodes, Leaves = leaves, Planes = planes, Brushes = brushes };

        // Renvoie la référence enfant : >= 0 nœud, < 0 feuille.
        int Recurse(List<int> ids, Vector3 lo, Vector3 hi, int depth)
        {
            if (ids.Count > LeafBrushTarget && depth < MaxDepth
                && TrySplit(ids, lo, hi, out int axis, out float pos, out var front, out var back))
            {
                var normal = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
                int pi = AddPlane(planes, planeIndex, new BspPlane(normal, pos));
                int self = nodes.Count;
                nodes.Add(new BspNode(pi, 0, 0));
                var frontLo = lo; var backHi = hi;
                SetAxis(ref frontLo, axis, pos);
                SetAxis(ref backHi, axis, pos);
                int f = Recurse(front, frontLo, hi, depth + 1);
                int bk = Recurse(back, lo, backHi, depth + 1);
                nodes[self] = new BspNode(pi, f, bk);
                return self;
            }
            leaves.Add(new BspLeaf(ids.ToArray(), lo, hi));
            return -leaves.Count; // -1 - index
        }

        bool TrySplit(List<int> ids, Vector3 lo, Vector3 hi, out int axis, out float pos, out List<int> front, out List<int> back)
        {
            var ext = hi - lo;
            axis = ext.X >= ext.Y && ext.X >= ext.Z ? 0 : ext.Y >= ext.Z ? 1 : 2;
            int ax = axis;
            var centers = ids.Select(i => Get((brushes[i].Min + brushes[i].Max) * 0.5f, ax)).OrderBy(x => x).ToList();
            pos = centers[centers.Count / 2];
            front = []; back = [];
            foreach (int i in ids)
            {
                bool f = Get(brushes[i].Max, axis) > pos;
                bool bk = Get(brushes[i].Min, axis) < pos;
                if (f) front.Add(i);
                if (bk) back.Add(i);
                if (!f && !bk) front.Add(i);
            }
            return front.Count < ids.Count && back.Count < ids.Count;
        }
    }

    private static float Get(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    private static void SetAxis(ref Vector3 v, int axis, float val)
    {
        if (axis == 0) v.X = val; else if (axis == 1) v.Y = val; else v.Z = val;
    }

    private static int AddPlane(List<BspPlane> planes, Dictionary<(float, float, float, float), int> index, BspPlane p)
    {
        var key = (MathF.Round(p.Normal.X, 4), MathF.Round(p.Normal.Y, 4), MathF.Round(p.Normal.Z, 4), MathF.Round(p.Dist, 3));
        if (index.TryGetValue(key, out int i)) return i;
        planes.Add(p);
        index[key] = planes.Count - 1;
        return planes.Count - 1;
    }
}
