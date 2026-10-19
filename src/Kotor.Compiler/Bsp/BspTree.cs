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

    /// <summary>Pieces (indices croissants) dont la boite contient toute la cellule de la feuille ; null sans partition en pieces, vide = hors de toute piece.</summary>
    public IReadOnlyList<int>? Rooms { get; init; }

    /// <summary>Vrai si la feuille est dans la boite de la piece <paramref name="room"/>.</summary>
    public bool InRoom(int room) => Rooms is not null && Rooms.Contains(room);
}

/// <summary>Boite englobante d'une piece (unites JA) pour la partition de l'arbre ; <see cref="Room"/> = indice de la piece.</summary>
public readonly record struct RoomBox(int Room, Vector3 Min, Vector3 Max);

public sealed class BspTree
{
    private const int MaxDepth = 40;
    private const int LeafBrushTarget = 4;

    public IReadOnlyList<BspNode> Nodes { get; private init; } = [];
    public IReadOnlyList<BspLeaf> Leaves { get; private init; } = [];
    public IReadOnlyList<BspPlane> Planes { get; private init; } = [];
    public IReadOnlyList<Brush> Brushes { get; private init; } = [];

    /// <summary>Clusters de visibilite : un par ensemble distinct de pieces contenant une cellule. Vide sans partition en pieces.</summary>
    public IReadOnlyList<int[]> Clusters { get; private init; } = [];

    /// <summary>Cluster de chaque feuille (meme ordre que <see cref="Leaves"/>) ; vide sans partition. Un cluster dont l'ensemble est vide = hors de toute piece.</summary>
    public IReadOnlyList<int> LeafClusters { get; private init; } = [];

    /// <summary>Boites de pieces de la partition (vide sans partition). Une piece absente de cette liste n a pas de cluster.</summary>
    public IReadOnlyList<RoomBox> RoomBoxes { get; private init; } = [];

    /// <summary>
    /// Construit l'arbre. Avec <paramref name="roomBoxes"/>, l'arbre est d'abord partitionne par les faces des boites de pieces :
    /// chaque feuille se trouve ainsi entierement dans le meme ensemble de boites (<see cref="BspLeaf.Rooms"/>), puis les cellules
    /// sont subdivisees par les brushes comme d'habitude. Sans boites la sortie est identique a l'arbre sans piece.
    /// </summary>
    public static BspTree Build(IReadOnlyList<Brush> brushes, Limits? limits = null, IReadOnlyList<RoomBox>? roomBoxes = null)
    {

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
        bool partition = roomBoxes is { Count: > 0 };
        if (partition)
            foreach (RoomBox rb in roomBoxes!) { min = Vector3.Min(min, rb.Min); max = Vector3.Max(max, rb.Max); }

        // Cellule « infinie » (clo..chi) : classe une feuille par les boites qui la contiennent, y compris les feuilles de bord
        // qui s'etendent a l'infini ; lo..hi reste la region finie de l'arbre (bornes de feuille).
        var inf = new Vector3(1e9f);
        List<int> allBoxes = partition ? Enumerable.Range(0, roomBoxes!.Count).ToList() : [];
        Recurse(Enumerable.Range(0, brushes.Count).ToList(), min, max, -inf, inf, allBoxes, 0);

        var clusters = new List<int[]>();
        var leafClusters = new List<int>();
        if (partition)
        {
            var byKey = new Dictionary<string, int>();
            foreach (BspLeaf l in leaves)
            {
                int[] set = [.. l.Rooms!];
                string key = string.Join(',', set);
                if (!byKey.TryGetValue(key, out int c)) { byKey[key] = c = clusters.Count; clusters.Add(set); }
                leafClusters.Add(c);
            }
        }
        return new BspTree { Nodes = nodes, Leaves = leaves, Planes = planes, Brushes = brushes, Clusters = clusters, LeafClusters = leafClusters, RoomBoxes = partition ? roomBoxes! : [] };

        // Renvoie la référence enfant : >= 0 nœud, < 0 feuille.
        int Recurse(List<int> ids, Vector3 lo, Vector3 hi, Vector3 clo, Vector3 chi, List<int> boxIds, int depth)
        {
            int[]? roomSet = null;
            if (partition)
            {
                // Boites qui recoupent l'interieur de la cellule (seules ces boites comptent pour les descendants).
                var relevant = new List<int>(boxIds.Count);
                var contains = new List<int>();
                int bestAxis = -1; float bestPos = 0, bestScore = float.MaxValue;
                foreach (int b in boxIds)
                {
                    RoomBox rb = roomBoxes![b];
                    if (!(clo.X < rb.Max.X && chi.X > rb.Min.X && clo.Y < rb.Max.Y && chi.Y > rb.Min.Y && clo.Z < rb.Max.Z && chi.Z > rb.Min.Z)) continue;
                    relevant.Add(b);
                    if (rb.Min.X <= clo.X && chi.X <= rb.Max.X && rb.Min.Y <= clo.Y && chi.Y <= rb.Max.Y && rb.Min.Z <= clo.Z && chi.Z <= rb.Max.Z)
                    { contains.Add(b); continue; }
                    for (int ax = 0; ax < 3; ax++)
                        foreach (float pos in new[] { Get(rb.Min, ax), Get(rb.Max, ax) })
                        {
                            if (!(pos > Get(clo, ax) && pos < Get(chi, ax))) continue;
                            // Plan le plus proche du centre de la region finie : arbre equilibre.
                            float score = MathF.Abs(pos - (Get(lo, ax) + Get(hi, ax)) * 0.5f) / MathF.Max(1f, Get(hi, ax) - Get(lo, ax));
                            if (score < bestScore) { bestScore = score; bestAxis = ax; bestPos = pos; }
                        }
                }
                if (bestAxis >= 0)
                {
                    Partition(ids, bestAxis, bestPos, out var f0, out var b0);
                    return SplitWith(f0, b0, lo, hi, clo, chi, relevant, bestAxis, bestPos, depth);
                }
                boxIds = relevant;
                roomSet = contains.Select(b => roomBoxes![b].Room).OrderBy(r => r).ToArray();
            }

            if (ids.Count > LeafBrushTarget && depth < MaxDepth
                && TrySplit(ids, lo, hi, out int axis, out float p, out var front, out var back))
                return SplitWith(front, back, lo, hi, clo, chi, boxIds, axis, p, depth);
            leaves.Add(new BspLeaf(ids.ToArray(), lo, hi) { Rooms = roomSet });
            return -leaves.Count; // -1 - index
        }

        int SplitWith(List<int> front, List<int> back, Vector3 lo, Vector3 hi, Vector3 clo, Vector3 chi, List<int> boxIds, int axis, float pos, int depth)
        {
            var normal = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
            int pi = AddPlane(planes, planeIndex, new BspPlane(normal, pos));
            int self = nodes.Count;
            nodes.Add(new BspNode(pi, 0, 0));
            Vector3 frontLo = lo, backHi = hi, frontClo = clo, backChi = chi;
            SetAxis(ref frontLo, axis, pos);
            SetAxis(ref backHi, axis, pos);
            SetAxis(ref frontClo, axis, pos);
            SetAxis(ref backChi, axis, pos);
            int f = Recurse(front, frontLo, hi, frontClo, chi, boxIds, depth + 1);
            int bk = Recurse(back, lo, backHi, clo, backChi, boxIds, depth + 1);
            nodes[self] = new BspNode(pi, f, bk);
            return self;
        }

        void Partition(List<int> ids, int axis, float pos, out List<int> front, out List<int> back)
        {
            front = []; back = [];
            foreach (int i in ids)
            {
                bool f = Get(brushes[i].Max, axis) > pos;
                bool bk = Get(brushes[i].Min, axis) < pos;
                if (f) front.Add(i);
                if (bk) back.Add(i);
                if (!f && !bk) front.Add(i);
            }
        }

        bool TrySplit(List<int> ids, Vector3 lo, Vector3 hi, out int axis, out float pos, out List<int> front, out List<int> back)
        {
            var ext = hi - lo;
            axis = ext.X >= ext.Y && ext.X >= ext.Z ? 0 : ext.Y >= ext.Z ? 1 : 2;
            int ax = axis;
            var centers = ids.Select(i => Get((brushes[i].Min + brushes[i].Max) * 0.5f, ax)).OrderBy(x => x).ToList();
            pos = centers[centers.Count / 2];
            Partition(ids, axis, pos, out front, out back);
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
