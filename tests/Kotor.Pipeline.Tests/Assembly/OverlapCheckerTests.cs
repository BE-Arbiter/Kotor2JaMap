using System.Numerics;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Pipeline.Assembly;

namespace Kotor.Pipeline.Tests.Assembly;

public class OverlapCheckerTests
{
    /// <summary>Scene avec un sol carre marchable x0..x0+size, y0..y0+size, a la hauteur z.</summary>
    private static ModuleScene Square(float x0, float y0, float size, float z = 0f, bool walkable = true)
    {
        var walk = new SceneWalkmesh("room", [new(x0, y0, z), new(x0 + size, y0, z), new(x0 + size, y0 + size, z), new(x0, y0 + size, z)],
            [0, 1, 2, 0, 2, 3], [walkable, walkable]);
        var info = new ModuleInfo("m", "m", "m", new ModuleEntry(Vector3.Zero, Vector2.UnitY));
        return new ModuleScene(info, [], [walk], new Dictionary<string, KotorTexture?>());
    }

    [Fact]
    public void SuperposedSquares_AreReported()
    {
        var o = Assert.Single(OverlapChecker.Check([Square(0, 0, 10), Square(2, 2, 10)]));
        Assert.Equal(0, o.A);
        Assert.Equal(1, o.B);
        Assert.False(string.IsNullOrWhiteSpace(o.Detail));
    }

    [Fact]
    public void AdjacentSquares_TouchingOverPointThreeMetres_AreNotReported()
    {
        Assert.Empty(OverlapChecker.Check([Square(0, 0, 10), Square(9.7f, 0, 10)]));
    }

    [Fact]
    public void StackedFloors_AtDifferentHeights_AreNotReported()
    {
        Assert.Empty(OverlapChecker.Check([Square(0, 0, 10, 0f), Square(0, 0, 10, 4f)]));
    }

    [Fact]
    public void NonWalkableTriangles_AreIgnored()
    {
        Assert.Empty(OverlapChecker.Check([Square(0, 0, 10), Square(0, 0, 10, 0f, walkable: false)]));
    }

    [Fact]
    public void ThreeScenes_OneLinePerOverlappingPair()
    {
        var pairs = OverlapChecker.Check([Square(0, 0, 10), Square(50, 0, 10), Square(5, 5, 10)]);
        var o = Assert.Single(pairs);
        Assert.Equal((0, 2), (o.A, o.B));
    }
}
