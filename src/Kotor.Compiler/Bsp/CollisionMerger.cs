// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md. Fusion maison de triangles coplanaires (aucun code de q3map2).
using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Geometry;

namespace Kotor.Compiler.Bsp;

/// <summary>
/// Fusionne les triangles coplanaires voisins d'un maillage de collision en polygones convexes, pour réduire le nombre de brushes.
/// Deux triangles qui partagent une arête (parcourue en sens inverse), dont les normales diffèrent de moins de
/// <c>normalEpsilonDeg</c> et dont les plans sont à moins de <c>planeEpsilon</c>, sont fusionnés si le résultat reste convexe.
/// </summary>
public static class CollisionMerger
{
    /// <summary>Aire (unités²) sous laquelle un triangle est considéré dégénéré.</summary>
    public const float DegenerateArea = 1e-4f;

    /// <summary>Nombre maximal de sommets d'un polygone fusionné (borne le nombre de côtés par brush).</summary>
    public const int MaxPolygonVertices = 8;

    private const float Quantum = 1000f; // soudure des sommets confondus (1/1000 d'unité)

    private sealed class Poly
    {
        public List<(long, long, long)> Keys = [];
        public List<Vector3> Pts = [];
        public Vector3 Normal;
        public float Dist;
    }

    public static IReadOnlyList<Vector3[]> MergeCoplanar(ModelMesh mesh, float normalEpsilonDeg = 0.5f, float planeEpsilon = 0.05f,
        ImportLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        float cosEps = MathF.Cos(normalEpsilonDeg * MathF.PI / 180f);

        var polys = new Dictionary<int, Poly>();
        var edges = new Dictionary<((long, long, long), (long, long, long)), int>();
        int next = 0;
        int degenerate = 0;

        void Register(int id, Poly p)
        {
            polys[id] = p;
            for (int i = 0; i < p.Keys.Count; i++) edges[(p.Keys[i], p.Keys[(i + 1) % p.Keys.Count])] = id;
        }
        void Unregister(int id)
        {
            Poly p = polys[id];
            for (int i = 0; i < p.Keys.Count; i++)
            {
                var k = (p.Keys[i], p.Keys[(i + 1) % p.Keys.Count]);
                if (edges.TryGetValue(k, out int owner) && owner == id) edges.Remove(k);
            }
            polys.Remove(id);
        }

        foreach (SubMesh sm in mesh.SubMeshes)
            for (int t = 0; t + 2 < sm.Triangles.Length; t += 3)
            {
                Vector3 a = sm.Positions[sm.Triangles[t]], b = sm.Positions[sm.Triangles[t + 1]], c = sm.Positions[sm.Triangles[t + 2]];
                if (Vector3.Cross(b - a, c - a).Length() * 0.5f < DegenerateArea) { degenerate++; continue; }
                Poly? p = Make([a, b, c]);
                if (p is null || HasDuplicateKey(p)) { degenerate++; continue; }
                Register(next++, p);
            }

        if (degenerate > 0)
            log?.Warn("COLLISION_DEGENERATE", $"Collision: {degenerate} degenerate or vertex-welded triangle(s) skipped (area < {DegenerateArea}).");

        // Fusion gloutonne jusqu'à stabilité.
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (int id in polys.Keys.ToList())
            {
                if (!polys.TryGetValue(id, out Poly? p)) continue;
                for (int i = 0; i < p.Keys.Count; i++)
                {
                    if (!edges.TryGetValue((p.Keys[(i + 1) % p.Keys.Count], p.Keys[i]), out int qid) || qid == id) continue;
                    Poly q = polys[qid];
                    if (Vector3.Dot(p.Normal, q.Normal) < cosEps) continue;
                    if (q.Pts.Any(v => MathF.Abs(Vector3.Dot(p.Normal, v) - p.Dist) > planeEpsilon)) continue;
                    Poly? merged = TryMerge(p, i, q);
                    if (merged is null) continue;
                    Unregister(id); Unregister(qid);
                    Register(next++, merged);
                    changed = true;
                    break;
                }
            }
        }
        return polys.OrderBy(kv => kv.Key).Select(kv => kv.Value.Pts.ToArray()).ToList();
    }

    private static Poly? TryMerge(Poly p, int edge, Poly q)
    {
        int n = p.Keys.Count;
        int j = q.Keys.IndexOf(p.Keys[(edge + 1) % n]); // q parcourt b -> a
        int m = q.Keys.Count;
        var pts = new List<Vector3>();
        // Tous les sommets de P, de b jusqu'à a.
        for (int k = 1; k <= n; k++) pts.Add(p.Pts[(edge + k) % n]);
        // Sommets de Q strictement entre a et b.
        for (int k = 2; k < m; k++) pts.Add(q.Pts[(j + k) % m]);
        pts = RemoveCollinear(pts);
        if (pts.Count < 3 || pts.Count > MaxPolygonVertices) return null;
        Poly? r = Make(pts);
        if (r is null || HasDuplicateKey(r) || !IsConvex(r)) return null;
        return r;
    }

    private static Poly? Make(List<Vector3> pts)
    {
        Vector3 n = Vector3.Zero;
        for (int i = 0; i < pts.Count; i++) n += Vector3.Cross(pts[i], pts[(i + 1) % pts.Count]);
        float len = n.Length();
        if (len < 1e-6f) return null;
        n /= len;
        float d = pts.Sum(p => Vector3.Dot(n, p)) / pts.Count;
        return new Poly { Pts = pts, Keys = pts.Select(Key).ToList(), Normal = n, Dist = d };
    }

    private static (long, long, long) Key(Vector3 p) =>
        ((long)MathF.Round(p.X * Quantum), (long)MathF.Round(p.Y * Quantum), (long)MathF.Round(p.Z * Quantum));

    private static bool HasDuplicateKey(Poly p) => p.Keys.Distinct().Count() != p.Keys.Count;

    private static bool IsConvex(Poly p)
    {
        int n = p.Pts.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 e1 = p.Pts[(i + 1) % n] - p.Pts[i], e2 = p.Pts[(i + 2) % n] - p.Pts[(i + 1) % n];
            if (Vector3.Dot(Vector3.Cross(e1, e2), p.Normal) < -1e-4f * e1.Length() * e2.Length()) return false;
        }
        return true;
    }

    /// <summary>Retire les sommets alignés avec leurs voisins (jonctions en T issues de la fusion).</summary>
    private static List<Vector3> RemoveCollinear(List<Vector3> pts)
    {
        var r = new List<Vector3>(pts);
        bool removed = true;
        while (removed && r.Count > 3)
        {
            removed = false;
            for (int i = 0; i < r.Count; i++)
            {
                Vector3 prev = r[(i + r.Count - 1) % r.Count], cur = r[i], nxt = r[(i + 1) % r.Count];
                Vector3 e1 = cur - prev, e2 = nxt - cur;
                if (Vector3.Cross(e1, e2).Length() <= 1e-4f * e1.Length() * e2.Length())
                {
                    r.RemoveAt(i);
                    removed = true;
                    break;
                }
            }
        }
        return r;
    }
}
