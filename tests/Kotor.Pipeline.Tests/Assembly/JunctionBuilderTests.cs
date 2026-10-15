using System.Numerics;
using Kotor.Core.Scene;
using Kotor.Pipeline.Assembly;

namespace Kotor.Pipeline.Tests.Assembly;

public class JunctionBuilderTests
{
    private static DoorFrame Frame(float x, float y, float z, float w, float h, float yaw = 270f) => new(new Vector3(x, y, z), yaw, w, h);

    [Fact]
    public void ZeroGap_ReturnsNothing()
    {
        var (mesh, walk) = JunctionBuilder.Build(Frame(0, 0, 0, 2, 3), Frame(0.005f, 0, 0, 2, 3), "tex");
        Assert.Null(mesh);
        Assert.Null(walk);
    }

    [Fact]
    public void FourMetreGap_BuildsTheExactPrism()
    {
        var (mesh, walk) = JunctionBuilder.Build(Frame(0, 0, 5, 2, 3), Frame(4, 0, 5, 2, 3), "tex");
        Assert.NotNull(mesh);
        Assert.NotNull(walk);
        Assert.Equal("tex", mesh!.Texture);
        Assert.Null(mesh.Lightmap);
        Assert.Equal(0f, mesh.Positions.Min(p => p.X)); Assert.Equal(4f, mesh.Positions.Max(p => p.X));
        Assert.Equal(-1f, mesh.Positions.Min(p => p.Y)); Assert.Equal(1f, mesh.Positions.Max(p => p.Y));
        Assert.Equal(5f, mesh.Positions.Min(p => p.Z)); Assert.Equal(8f, mesh.Positions.Max(p => p.Z));
        Assert.Equal(mesh.Positions.Length, mesh.Normals.Length);
        Assert.Equal(mesh.Positions.Length, mesh.Uv0.Length);

        // Chaque triangle : normale geometrique et normale des sommets orientees vers l'axe central (y=0, z=6.5).
        var centre = new Vector3(2, 0, 6.5f);
        for (int t = 0; t < mesh.Triangles.Length; t += 3)
        {
            Vector3 a = mesh.Positions[mesh.Triangles[t]], b = mesh.Positions[mesh.Triangles[t + 1]], c = mesh.Positions[mesh.Triangles[t + 2]];
            Vector3 n = Vector3.Cross(b - a, c - a);
            Vector3 mid = (a + b + c) / 3f;
            Assert.True(Vector3.Dot(n, centre - mid) > 0, "geometric normal points outward");
            Assert.True(Vector3.Dot(mesh.Normals[mesh.Triangles[t]], centre - mid) > 0, "vertex normal points outward");
        }

        Assert.Equal(mesh.Triangles.Length / 3, walk!.Walkable.Length);
        float walkable = 0, solid = 0;
        for (int t = 0; t < walk.Triangles.Length; t += 3)
        {
            Vector3 a = walk.Positions[walk.Triangles[t]], b = walk.Positions[walk.Triangles[t + 1]], c = walk.Positions[walk.Triangles[t + 2]];
            float area = Vector3.Cross(b - a, c - a).Length() / 2f;
            if (walk.Walkable[t / 3]) { walkable += area; Assert.Equal(5f, a.Z); Assert.Equal(5f, b.Z); Assert.Equal(5f, c.Z); }
            else solid += area;
        }
        Assert.Equal(8f, walkable, 3);
        Assert.Equal(4 * 3 * 2 + 8, solid, 3); // deux murs 4x3 et un plafond 4x2
        Assert.Equal(8f + 4 * 3 * 2 + 8, mesh.Triangles.Length > 0 ? walkable + solid : 0, 3);
    }

    [Fact]
    public void DifferentWidths_UseTheMinimum()
    {
        var (mesh, _) = JunctionBuilder.Build(Frame(0, 0, 0, 2, 3), Frame(4, 0, 0, 3, 3), "tex");
        Assert.Equal(-1f, mesh!.Positions.Min(p => p.Y));
        Assert.Equal(1f, mesh.Positions.Max(p => p.Y));
    }

    [Fact]
    public void FacingAlongY_WidthIsAlongX()
    {
        var (mesh, _) = JunctionBuilder.Build(Frame(0, 0, 0, 2, 3, 0), Frame(0, 4, 0, 2, 3, 180), "tex");
        Assert.Equal(-1f, mesh!.Positions.Min(p => p.X)); Assert.Equal(1f, mesh.Positions.Max(p => p.X));
        Assert.Equal(4f, mesh.Positions.Max(p => p.Y));
    }
}
