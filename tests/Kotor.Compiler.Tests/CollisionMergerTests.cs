using System.Numerics;
using Kotor.Compiler.Bsp;

namespace Kotor.Compiler.Tests;

public class CollisionMergerTests
{
    [Fact]
    public void Two_coplanar_triangles_merge_into_one_polygon()
    {
        var polys = CollisionMerger.MergeCoplanar(TestScenes.FloorMesh());
        var p = Assert.Single(polys);
        Assert.Equal(4, p.Length);
    }

    [Fact]
    public void Non_coplanar_triangles_stay_separate()
    {
        // Deux triangles partageant l'arete (0,0,0)-(10,0,0), a 90 degres.
        var mesh = TestScenes.Mesh([new(0, 0, 0), new(10, 0, 0), new(0, 10, 0), new(0, 0, 10)], [0, 1, 2, 1, 0, 3]);
        Assert.Equal(2, CollisionMerger.MergeCoplanar(mesh).Count);
    }

    [Fact]
    public void Merge_keeps_convexity()
    {
        // L de 3 carres coplanaires : (0,0) (1,0) (0,1), 6 triangles.
        var pos = new List<Vector3>();
        var tri = new List<int>();
        void Square(float x, float y)
        {
            int b = pos.Count;
            pos.AddRange([new(x, y, 0), new(x + 10, y, 0), new(x + 10, y + 10, 0), new(x, y + 10, 0)]);
            tri.AddRange([b, b + 1, b + 2, b, b + 2, b + 3]);
        }
        Square(0, 0); Square(10, 0); Square(0, 10);
        var polys = CollisionMerger.MergeCoplanar(TestScenes.Mesh(pos.ToArray(), tri.ToArray()));
        Assert.True(polys.Count >= 2);
        foreach (var p in polys) Assert.True(IsConvex(p), "polygone non convexe");
    }

    [Fact]
    public void Degenerate_triangle_is_ignored()
    {
        var mesh = TestScenes.Mesh([new(0, 0, 0), new(1, 0, 0), new(2, 0, 0)], [0, 1, 2]);
        Assert.Empty(CollisionMerger.MergeCoplanar(mesh));
    }

    private static bool IsConvex(Vector3[] p)
    {
        Vector3 n = Vector3.Zero;
        for (int i = 0; i < p.Length; i++) n += Vector3.Cross(p[i], p[(i + 1) % p.Length]);
        n = Vector3.Normalize(n);
        for (int i = 0; i < p.Length; i++)
        {
            Vector3 e1 = p[(i + 1) % p.Length] - p[i], e2 = p[(i + 2) % p.Length] - p[(i + 1) % p.Length];
            if (Vector3.Dot(Vector3.Cross(e1, e2), n) < -1e-3f) return false;
        }
        return true;
    }
}
