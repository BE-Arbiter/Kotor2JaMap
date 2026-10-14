using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core.Geometry;

namespace Kotor.Compiler.Tests;

public class BspTreeTests
{
    private static ModelMesh Mesh(params Vector3[] tri) => new()
    {
        Name = "c",
        SubMeshes = [new SubMesh { MaterialName = "m", Positions = tri, Triangles = Enumerable.Range(0, tri.Length).ToArray() }],
    };

    [Fact]
    public void Empty_collision_yields_no_brushes()
    {
        var warnings = new List<string>();
        var brushes = BrushBuilder.FromCollision(new ModelMesh { Name = "e" }, log: warnings.Add);
        Assert.Empty(brushes);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void One_triangle_yields_one_brush()
    {
        var brushes = BrushBuilder.FromCollision(Mesh(new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)));
        Assert.Single(brushes);
        Assert.Equal(5, brushes[0].Planes.Count);
    }

    [Fact]
    public void Tree_leaves_contain_brushes()
    {
        // 6 triangles distincts formant les faces d'un cube de 100.
        Vector3[] tris =
        [
            new(0,0,0), new(100,0,0), new(0,100,0),
            new(0,0,100), new(100,0,100), new(0,100,100),
            new(0,0,0), new(100,0,0), new(0,0,100),
            new(0,100,0), new(100,100,0), new(0,100,100),
            new(0,0,0), new(0,100,0), new(0,0,100),
            new(100,0,0), new(100,100,0), new(100,0,100),
        ];
        var brushes = BrushBuilder.FromCollision(Mesh(tris));
        Assert.Equal(6, brushes.Count);
        var tree = BspTree.Build(brushes);
        Assert.NotEmpty(tree.Leaves);
        var solid = tree.Leaves.Where(l => l.IsSolid).ToList();
        Assert.NotEmpty(solid);
        Assert.All(solid, l => Assert.NotEmpty(l.BrushIndices));
        Assert.Equal(6, solid.SelectMany(l => l.BrushIndices).Distinct().Count());
    }

    [Fact]
    public void Limit_exceeded_throws_clear_error()
    {
        var limits = new Limits(1, 1000, 1000, 1000);
        var ex = Assert.Throws<BspLimitException>(() => limits.CheckBrushes(2));
        Assert.Contains("MAX_MAP_BRUSHES", ex.Message);
    }
}
