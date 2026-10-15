using Kotor.Core.Textures;

namespace Kotor.Core.Tests.Textures;

public class LightmapRepairTests
{
    [Fact]
    public void Repair_fills_invalid_from_nearest()
    {
        byte[] a = [10, 20, 30], b = [200, 100, 50];
        byte[] rgba =
        [
            a[0], a[1], a[2], 255,
            1, 2, 3, 0,
            4, 5, 6, 0,
            b[0], b[1], b[2], 255,
        ];
        var (repaired, none) = LightmapRepair.Repair(rgba, 4, 1);
        Assert.Equal(2, repaired);
        Assert.False(none);
        byte[] expected =
        [
            10, 20, 30, 255, 10, 20, 30, 255, 200, 100, 50, 255, 200, 100, 50, 255,
        ];
        Assert.Equal(expected, rgba);
    }

    [Fact]
    public void Repair_all_invalid_gives_gray()
    {
        byte[] rgba = new byte[4 * 4 * 4];
        for (int i = 0; i < 16; i++) { rgba[i * 4] = 0; rgba[i * 4 + 1] = 255; rgba[i * 4 + 2] = 0; rgba[i * 4 + 3] = 1; }
        var (repaired, none) = LightmapRepair.Repair(rgba, 4, 4);
        Assert.True(none);
        Assert.Equal(16, repaired);
        for (int i = 0; i < 16; i++)
            Assert.Equal(new byte[] { 128, 128, 128, 255 }, rgba[(i * 4)..(i * 4 + 4)]);
    }

    [Fact]
    public void Repair_leaves_a_valid_image_untouched()
    {
        byte[] rgba = [1, 2, 3, 255, 4, 5, 6, 255];
        byte[] copy = (byte[])rgba.Clone();
        Assert.Equal((0, false), LightmapRepair.Repair(rgba, 2, 1));
        Assert.Equal(copy, rgba);
    }

    [Fact]
    public void Repair_propagates_over_several_texels_and_rows()
    {
        // 5x3 with a single valid texel in the corner: everything gets its colour.
        byte[] rgba = new byte[5 * 3 * 4];
        for (int i = 0; i < 15; i++) rgba[i * 4 + 3] = 0;
        rgba[0] = 7; rgba[1] = 8; rgba[2] = 9; rgba[3] = 255;
        var (repaired, none) = LightmapRepair.Repair(rgba, 5, 3);
        Assert.Equal(14, repaired);
        Assert.False(none);
        for (int i = 0; i < 15; i++)
            Assert.Equal(new byte[] { 7, 8, 9, 255 }, rgba[(i * 4)..(i * 4 + 4)]);
    }
}
