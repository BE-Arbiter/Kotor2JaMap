// Inspiré de la démarche de q3map2 / NetRadiant-custom (construction de brushes, GPL-2.0) : aucun code copié.
// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;
using Kotor.Core.Geometry;

namespace Kotor.Compiler.Bsp;

/// <summary>Plan orienté : n.p = Dist, la normale pointe vers l'extérieur du brush.</summary>
public readonly record struct BspPlane(Vector3 Normal, float Dist);

public sealed record Brush(IReadOnlyList<BspPlane> Planes, Vector3 Min, Vector3 Max);

public static class BrushBuilder
{
    /// <summary>Épaisseur du prisme (unités de la scène). Journalisée à chaque appel.</summary>
    public const float DefaultThickness = 0.5f;

    /// <summary>
    /// Un brush prismatique fin (5 plans : 2 faces + 3 bords) par triangle de collision.
    /// Les positions sont supposées déjà en unités Quake. Les triangles dégénérés sont ignorés.
    /// </summary>
    public static IReadOnlyList<Brush> FromCollision(ModelMesh collision, float thickness = DefaultThickness, Action<string>? log = null)
    {
        var brushes = new List<Brush>();
        int skipped = 0;
        foreach (var sub in collision.SubMeshes)
        {
            for (int i = 0; i + 2 < sub.Triangles.Length; i += 3)
            {
                var brush = FromTriangle(sub.Positions[sub.Triangles[i]], sub.Positions[sub.Triangles[i + 1]],
                                         sub.Positions[sub.Triangles[i + 2]], thickness);
                if (brush is null) skipped++; else brushes.Add(brush);
            }
        }
        if (brushes.Count == 0)
            log?.Invoke("Warning: empty collision mesh, no brush generated.");
        else
            log?.Invoke($"Collision: {brushes.Count} prismatic brushes, thickness {thickness}, {skipped} degenerate triangle(s) skipped.");
        return brushes;
    }

    /// <summary>
    /// Un brush prismatique par polygone convexe (2 faces + 1 plan par arête), épaisseur centrée sur le plan du polygone.
    /// Les sommets sont en sens direct autour de la normale (règle de la main droite). Polygones dégénérés ignorés.
    /// </summary>
    public static IReadOnlyList<Brush> FromPolygons(IReadOnlyList<Vector3[]> polygons, float thickness = DefaultThickness, Action<string>? log = null)
    {
        var brushes = new List<Brush>();
        int skipped = 0;
        foreach (Vector3[] poly in polygons)
        {
            Brush? b = FromPolygon(poly, thickness);
            if (b is null) skipped++; else brushes.Add(b);
        }
        if (brushes.Count == 0)
            log?.Invoke("Warning: empty collision mesh, no brush generated.");
        else
            log?.Invoke($"Collision: {brushes.Count} prismatic brushes, thickness {thickness}, {skipped} degenerate polygon(s) skipped.");
        return brushes;
    }

    private static Brush? FromPolygon(Vector3[] v, float thickness)
    {
        if (v.Length < 3) return null;
        Vector3 n = Vector3.Zero;
        for (int i = 0; i < v.Length; i++) n += Vector3.Cross(v[i], v[(i + 1) % v.Length]);
        float len = n.Length();
        if (len < 1e-6f) return null;
        n /= len;
        float d = Vector3.Dot(n, v[0]);
        float h = thickness / 2f;
        var planes = new List<BspPlane>(2 + v.Length) { new(n, d + h), new(-n, -(d - h)) };
        Vector3 lo = v[0], hi = v[0];
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 p = v[i], q = v[(i + 1) % v.Length];
            Vector3 e = Vector3.Cross(q - p, n);
            if (e.LengthSquared() < 1e-12f) return null;
            Vector3 en = Vector3.Normalize(e);
            planes.Add(new BspPlane(en, Vector3.Dot(en, p)));
            lo = Vector3.Min(lo, q); hi = Vector3.Max(hi, q);
        }
        Vector3 off = Vector3.Abs(n * h);
        return new Brush(planes, lo - off, hi + off);
    }

    private static Brush? FromTriangle(Vector3 a, Vector3 b, Vector3 c, float thickness)
    {
        var n = Vector3.Cross(b - a, c - a);
        float len = n.Length();
        if (len < 1e-6f) return null;
        n /= len;
        float d = Vector3.Dot(n, a);
        float h = thickness / 2f;
        var planes = new List<BspPlane>(5)
        {
            new(n, d + h),
            new(-n, -(d - h)),
        };
        Vector3[] v = [a, b, c];
        for (int i = 0; i < 3; i++)
        {
            var p = v[i];
            var q = v[(i + 1) % 3];
            var e = Vector3.Cross(q - p, n);
            if (e.LengthSquared() < 1e-12f) return null;
            var en = Vector3.Normalize(e);
            planes.Add(new BspPlane(en, Vector3.Dot(en, p)));
        }
        var off = Vector3.Abs(n * h);
        return new Brush(planes, Vector3.Min(Vector3.Min(a, b), c) - off, Vector3.Max(Vector3.Max(a, b), c) + off);
    }
}
