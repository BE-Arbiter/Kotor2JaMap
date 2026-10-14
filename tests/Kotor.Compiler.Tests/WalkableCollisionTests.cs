using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core.Geometry;

namespace Kotor.Compiler.Tests;

public sealed class WalkableCollisionTests
{
    private static ModelMesh Grid(int quads)
    {
        int w = quads + 1;
        var pos = new Vector3[w * w];
        for (int y = 0; y < w; y++)
            for (int x = 0; x < w; x++)
                pos[y * w + x] = new Vector3(x * 16, y * 16, 3f * MathF.Sin(x * 0.3f) * MathF.Cos(y * 0.2f));
        var tris = new List<int>();
        for (int y = 0; y < quads; y++)
            for (int x = 0; x < quads; x++)
            {
                int i = y * w + x;
                tris.AddRange([i, i + 1, i + w, i + 1, i + w + 1, i + w]);
            }
        return new ModelMesh { Name = "t", SubMeshes = [new SubMesh { MaterialName = "m", Positions = pos, Triangles = tris.ToArray() }] };
    }

    [Fact]
    public void Walkable_collision_brush_count_is_under_limit()
    {
        var log = new List<string>();
        var mesh = Grid(141); // 141*141*2 = 39 762 triangles (~40 000)
        int tris = mesh.SubMeshes[0].Triangles.Length / 3;
        Assert.InRange(tris, 39_000, 41_000);

        var r = WalkableCollision.Build(mesh, 11_900, log: log.Add);

        Assert.True(r.Brushes.Count < 11_900);
        Assert.True(r.Brushes.Count > 0);
        Assert.True(r.Reduced);
        Assert.Equal(tris, r.WalkableTriangles);
        Assert.Contains(log, l => l.Contains("marchables") && l.Contains(r.Brushes.Count.ToString()));
    }

    [Fact]
    public void Small_set_is_not_reduced()
    {
        var log = new List<string>();
        var r = WalkableCollision.Build(Grid(10), 11_900, log: log.Add);
        Assert.False(r.Reduced);
        Assert.Equal(200, r.Brushes.Count);
    }

    [Fact]
    public void Empty_walkable_set_adds_no_brushes()
    {
        var log = new List<string>();
        var r = WalkableCollision.Build(new ModelMesh { Name = "e" }, 11_900, log: log.Add);
        Assert.Empty(r.Brushes);
        Assert.Equal(0, r.WalkableTriangles);
        Assert.Contains(log, l => l.Contains("marchables"));
    }
}
