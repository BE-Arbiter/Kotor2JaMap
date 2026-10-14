using Kotor.Compiler.Bsp;

namespace Kotor.Compiler.Tests;

public class LimitCheckTests
{
    [Fact]
    public void Within_limit_does_not_throw()
    {
        LimitCheck.Check("MAX_MAP_TEST", 10, 10);
        LimitCheck.CheckRenderLimits(shaders: 2, surfaces: 3, verts: 100, indexes: 300, leafSurfaces: 3);
    }

    [Fact]
    public void Exceeded_limit_names_lump_and_values()
    {
        var ex = Assert.Throws<BspLimitException>(() => LimitCheck.Check("MAX_MAP_TEST", 11, 10));
        Assert.Contains("MAX_MAP_TEST", ex.Message);
        Assert.Contains("10", ex.Message);
        Assert.Contains("11", ex.Message);
    }

    [Fact]
    public void Render_limits_name_the_offending_lump()
    {
        var ex = Assert.Throws<BspLimitException>(() =>
            LimitCheck.CheckRenderLimits(1, 1, 10, Limits.MaxDrawIndexes + 1, 1));
        Assert.Contains("MAX_MAP_DRAW_INDEXES", ex.Message);
    }
}
