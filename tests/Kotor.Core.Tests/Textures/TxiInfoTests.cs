using Kotor.Core.Textures;

namespace Kotor.Core.Tests.Textures;

public class TxiInfoTests
{
    [Fact]
    public void Txi_detects_animation()
    {
        var t = TxiInfo.Parse("proceduretype cycle\nnumx 4");
        Assert.True(t.IsAnimated);
        Assert.Equal("cycle", t.Raw["proceduretype"]);
    }

    [Fact]
    public void Txi_defaults_and_fields()
    {
        Assert.False(TxiInfo.Empty.IsAnimated);
        Assert.Equal("default", TxiInfo.Parse(null).Blending);
        var t = TxiInfo.Parse("Blending PunchThrough\r\ndecal 1\nenvmaptexture CM_Dark\n");
        Assert.Equal("punchthrough", t.Blending);
        Assert.True(t.Decal);
        Assert.Equal("cm_dark", t.EnvMapTexture);
        Assert.False(t.IsAnimated);
        Assert.True(TxiInfo.Parse("defaultwidth 8").IsAnimated);
    }
}
