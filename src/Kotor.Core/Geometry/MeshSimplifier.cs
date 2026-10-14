using System.Numerics;

namespace Kotor.Core.Geometry;

public sealed record SimplifyReport(int Before, int After, bool Simplified);

/// <summary>
/// Simplification par effondrement d'aretes (edge collapse) avec erreur quadrique (Garland-Heckbert),
/// appliquee separement a chaque sous-maillage (un materiau = un sous-maillage, jamais fusionnes).
/// Deterministe : departage des couts egaux par indices de sommets, aucune source d'aleatoire.
/// </summary>
public static class MeshSimplifier
{
    private const double BoundaryWeight = 1000.0;

    public static ModelMesh Reduce(ModelMesh input, int targetTriangles) =>
        ReduceWithReport(input, targetTriangles).Mesh;

    public static (ModelMesh Mesh, SimplifyReport Report) ReduceWithReport(ModelMesh input, int targetTriangles)
    {
        var counts = input.SubMeshes.Select(s => s.Triangles.Length / 3).ToList();
        int before = counts.Sum();
        if (before <= targetTriangles)
            return (input, new SimplifyReport(before, before, false));

        var targets = TriangleBudget.Allocate(counts, Math.Max(targetTriangles, 0));
        var subs = new List<SubMesh>(input.SubMeshes.Count);
        for (int i = 0; i < input.SubMeshes.Count; i++)
            subs.Add(counts[i] <= targets[i] ? input.SubMeshes[i] : ReduceSub(input.SubMeshes[i], targets[i]));

        var mesh = new ModelMesh
        {
            Name = input.Name,
            SubMeshes = subs,
            BBoxCenter = input.BBoxCenter,
            BBoxExtents = input.BBoxExtents,
        };
        int after = subs.Sum(s => s.Triangles.Length / 3);
        return (mesh, new SimplifyReport(before, after, true));
    }

    // Quadrique symetrique 4x4 stockee en 10 coefficients.
    private struct Quadric
    {
        public double A, B, C, D, E, F, G, H, I, J;

        public static Quadric FromPlane(double a, double b, double c, double d, double w) => new()
        {
            A = w * a * a, B = w * a * b, C = w * a * c, D = w * a * d,
            E = w * b * b, F = w * b * c, G = w * b * d,
            H = w * c * c, I = w * c * d, J = w * d * d,
        };

        public static Quadric operator +(Quadric p, Quadric q) => new()
        {
            A = p.A + q.A, B = p.B + q.B, C = p.C + q.C, D = p.D + q.D, E = p.E + q.E,
            F = p.F + q.F, G = p.G + q.G, H = p.H + q.H, I = p.I + q.I, J = p.J + q.J,
        };

        public readonly double Error(Vector3 v)
        {
            double x = v.X, y = v.Y, z = v.Z;
            return A * x * x + 2 * B * x * y + 2 * C * x * z + 2 * D * x
                 + E * y * y + 2 * F * y * z + 2 * G * y
                 + H * z * z + 2 * I * z + J;
        }
    }

    private static SubMesh ReduceSub(SubMesh sub, int target)
    {
        int vCount = sub.Positions.Length;
        int fCount = sub.Triangles.Length / 3;

        var pos = (Vector3[])sub.Positions.Clone();
        var nrm = sub.Normals is { } n && n.Length == vCount ? (Vector3[])n.Clone() : null;
        var uv = sub.Uv0 is { } u && u.Length == vCount ? (Vector2[])u.Clone() : null;
        var tri = (int[])sub.Triangles.Clone();
        var faceAlive = new bool[fCount];
        Array.Fill(faceAlive, true);

        var vertFaces = new List<int>[vCount];
        for (int i = 0; i < vCount; i++) vertFaces[i] = [];
        for (int f = 0; f < fCount; f++)
            for (int k = 0; k < 3; k++) vertFaces[tri[f * 3 + k]].Add(f);

        var quad = new Quadric[vCount];
        var version = new int[vCount];
        var vertAlive = new bool[vCount];
        Array.Fill(vertAlive, true);

        // Quadriques des plans de faces + plans de bord (penalite forte : bords et coutures preserves).
        var edgeUse = new Dictionary<(int, int), int>();
        for (int f = 0; f < fCount; f++)
        {
            int a = tri[f * 3], b = tri[f * 3 + 1], c = tri[f * 3 + 2];
            var nn = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
            double area2 = nn.Length();
            if (area2 > 1e-20)
            {
                var un = nn / (float)area2;
                double d = -Vector3.Dot(un, pos[a]);
                var q = Quadric.FromPlane(un.X, un.Y, un.Z, d, area2 * 0.5);
                quad[a] += q; quad[b] += q; quad[c] += q;
            }
            foreach (var e in Edges(a, b, c))
                edgeUse[e] = edgeUse.GetValueOrDefault(e) + 1;
        }
        for (int f = 0; f < fCount; f++)
        {
            int a = tri[f * 3], b = tri[f * 3 + 1], c = tri[f * 3 + 2];
            var fn = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
            if (fn.LengthSquared() < 1e-20f) continue;
            fn = Vector3.Normalize(fn);
            foreach (var (p, q) in new[] { (a, b), (b, c), (c, a) })
            {
                var key = p < q ? (p, q) : (q, p);
                if (edgeUse[key] != 1) continue;
                var side = Vector3.Cross(pos[q] - pos[p], fn);
                if (side.LengthSquared() < 1e-20f) continue;
                side = Vector3.Normalize(side);
                double d = -Vector3.Dot(side, pos[p]);
                var bq = Quadric.FromPlane(side.X, side.Y, side.Z, d, BoundaryWeight);
                quad[p] += bq; quad[q] += bq;
            }
        }

        var heap = new PriorityQueue<(int A, int B, int VerA, int VerB, int T), (double Cost, int A, int B)>();

        void Push(int a, int b)
        {
            if (a > b) (a, b) = (b, a);
            var q = quad[a] + quad[b];
            double bestErr = double.MaxValue;
            int bestT = 0;
            for (int t = 0; t < 3; t++)
            {
                var p = t == 0 ? pos[a] : t == 1 ? pos[b] : (pos[a] + pos[b]) * 0.5f;
                double err = q.Error(p);
                if (err < bestErr) { bestErr = err; bestT = t; }
            }
            double len2 = (pos[a] - pos[b]).LengthSquared();
            heap.Enqueue((a, b, version[a], version[b], bestT), (Math.Max(bestErr, 0) + len2 * 1e-9, a, b));
        }

        var seen = new HashSet<(int, int)>();
        for (int f = 0; f < fCount; f++)
            foreach (var e in Edges(tri[f * 3], tri[f * 3 + 1], tri[f * 3 + 2]))
                if (seen.Add(e)) Push(e.Item1, e.Item2);

        int faces = fCount;
        while (faces > target && heap.TryDequeue(out var item, out _))
        {
            var (a, b, va, vb, t) = item;
            if (!vertAlive[a] || !vertAlive[b] || version[a] != va || version[b] != vb) continue;

            var newPos = t == 0 ? pos[a] : t == 1 ? pos[b] : (pos[a] + pos[b]) * 0.5f;
            if (!CanCollapse(a, b, newPos, pos, tri, faceAlive, vertFaces)) continue;

            // Effondrement de b dans a.
            float tw = t == 0 ? 0f : t == 1 ? 1f : 0.5f;
            if (nrm != null)
            {
                var nv = Vector3.Lerp(nrm[a], nrm[b], tw);
                nrm[a] = nv.LengthSquared() > 1e-12f ? Vector3.Normalize(nv) : nrm[a];
            }
            if (uv != null) uv[a] = Vector2.Lerp(uv[a], uv[b], tw);
            pos[a] = newPos;
            quad[a] += quad[b];
            vertAlive[b] = false;

            foreach (int f in vertFaces[b].ToArray())
            {
                if (!faceAlive[f]) continue;
                bool hasA = tri[f * 3] == a || tri[f * 3 + 1] == a || tri[f * 3 + 2] == a;
                if (hasA)
                {
                    faceAlive[f] = false;
                    faces--;
                }
                else
                {
                    for (int k = 0; k < 3; k++)
                        if (tri[f * 3 + k] == b) tri[f * 3 + k] = a;
                    vertFaces[a].Add(f);
                }
            }
            vertFaces[b].Clear();
            vertFaces[a].RemoveAll(f => !faceAlive[f]);

            version[a]++;
            var neighbours = new SortedSet<int>();
            foreach (int f in vertFaces[a])
                for (int k = 0; k < 3; k++)
                    if (tri[f * 3 + k] != a) neighbours.Add(tri[f * 3 + k]);
            foreach (int nb in neighbours) Push(a, nb);
        }

        // Compactage : seuls les sommets encore references sont conserves, ordre d'indice croissant.
        var remap = new int[vCount];
        Array.Fill(remap, -1);
        var outTris = new List<int>(faces * 3);
        var order = new List<int>();
        for (int f = 0; f < fCount; f++)
        {
            if (!faceAlive[f]) continue;
            for (int k = 0; k < 3; k++)
            {
                int v = tri[f * 3 + k];
                if (remap[v] < 0) { remap[v] = 0; order.Add(v); }
            }
        }
        order.Sort();
        for (int i = 0; i < order.Count; i++) remap[order[i]] = i;
        for (int f = 0; f < fCount; f++)
        {
            if (!faceAlive[f]) continue;
            for (int k = 0; k < 3; k++) outTris.Add(remap[tri[f * 3 + k]]);
        }

        return new SubMesh
        {
            MaterialName = sub.MaterialName,
            Positions = order.Select(v => pos[v]).ToArray(),
            Normals = nrm == null ? null : order.Select(v => nrm[v]).ToArray(),
            Uv0 = uv == null ? null : order.Select(v => uv[v]).ToArray(),
            Triangles = outTris.ToArray(),
        };
    }

    private static IEnumerable<(int, int)> Edges(int a, int b, int c)
    {
        yield return a < b ? (a, b) : (b, a);
        yield return b < c ? (b, c) : (c, b);
        yield return c < a ? (c, a) : (a, c);
    }

    /// <summary>Refuse un effondrement qui retourne ou degenere un triangle voisin.</summary>
    private static bool CanCollapse(int a, int b, Vector3 newPos, Vector3[] pos, int[] tri,
        bool[] faceAlive, List<int>[] vertFaces)
    {
        foreach (var (self, other) in new[] { (a, b), (b, a) })
        {
            foreach (int f in vertFaces[self])
            {
                if (!faceAlive[f]) continue;
                int i0 = tri[f * 3], i1 = tri[f * 3 + 1], i2 = tri[f * 3 + 2];
                if (i0 == other || i1 == other || i2 == other) continue; // disparait
                var p0 = pos[i0]; var p1 = pos[i1]; var p2 = pos[i2];
                var before = Vector3.Cross(p1 - p0, p2 - p0);
                if (i0 == self) p0 = newPos; else if (i1 == self) p1 = newPos; else p2 = newPos;
                var after = Vector3.Cross(p1 - p0, p2 - p0);
                if (after.LengthSquared() < 1e-14f) return false;
                if (before.LengthSquared() > 1e-14f && Vector3.Dot(before, after) <= 0f) return false;
            }
        }
        return true;
    }
}
