using System.Numerics;
using System.Text;
using Kotor.Compiler.Bsp;
using Kotor.Core.Geometry;

namespace Kotor.Compiler.Tests;

public class GlobalFogTests
{
    private static RenderSurface Tri(string shader, bool sky = false) =>
        new(shader, [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [default, default, default],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], [0, 1, 2], [0]) { Sky = sky };

    private static byte[] Write(GlobalFog? fog)
    {
        var ms = new MemoryStream();
        BspWriter.Write(BspTree.Build([]), ms, null, null, [Tri("a"), Tri("sky", sky: true)], null, fog);
        return ms.ToArray();
    }

    private static (int Ofs, int Len) Lump(byte[] b, int i) => (BitConverter.ToInt32(b, 8 + 8 * i), BitConverter.ToInt32(b, 12 + 8 * i));

    [Fact]
    public void Global_fog_is_one_FOGS_entry_with_brushNum_minus_one_and_all_surfaces_use_it()
    {
        byte[] b = Write(new GlobalFog("textures/kotor/Test_fog"));
        var (o, l) = Lump(b, BspWriter.LumpFogs);
        Assert.Equal(72, l); // dfog_t : shader[64] + brushNum + visibleSide
        Assert.Equal("textures/kotor/Test_fog", Encoding.ASCII.GetString(b, o, 64).TrimEnd('\0'));
        Assert.Equal(-1, BitConverter.ToInt32(b, o + 64)); // brushNum
        Assert.Equal(-1, BitConverter.ToInt32(b, o + 68)); // visibleSide
        var (so, sl) = Lump(b, BspWriter.LumpSurfaces);
        Assert.Equal(2 * 148, sl);
        for (int s = 0; s < 2; s++) Assert.Equal(0, BitConverter.ToInt32(b, so + s * 148 + 4)); // fogNum, ciel compris
    }

    [Fact]
    public void Without_fog_the_lump_is_empty_and_fogNum_is_minus_one()
    {
        byte[] b = Write(null);
        Assert.Equal(0, Lump(b, BspWriter.LumpFogs).Len);
        var (so, _) = Lump(b, BspWriter.LumpSurfaces);
        Assert.Equal(-1, BitConverter.ToInt32(b, so + 4));
    }

    [Fact]
    public void Worldspawn_key_is_parsed_and_bad_values_mean_no_fog()
    {
        Assert.Equal("textures/kotor/DAG_1_fog",
            GlobalFog.FromWorldspawn(new Dictionary<string, string> { ["_kotor_fog"] = "0.04 0.32 0.363 4000" }, "DAG_1")?.Shader);
        Assert.Null(GlobalFog.FromWorldspawn(new Dictionary<string, string>(), "L"));
        Assert.Null(GlobalFog.FromWorldspawn(null, "L"));
        Assert.Null(GlobalFog.FromWorldspawn(new Dictionary<string, string> { ["_kotor_fog"] = "0.1 0.1 0.1" }, "L"));
        Assert.Null(GlobalFog.FromWorldspawn(new Dictionary<string, string> { ["_kotor_fog"] = "0.1 0.1 0.1 0" }, "L"));
        Assert.Null(GlobalFog.FromWorldspawn(new Dictionary<string, string> { ["_kotor_fog"] = "-1 0.1 0.1 100" }, "L"));
        Assert.Null(GlobalFog.FromWorldspawn(new Dictionary<string, string> { ["_kotor_fog"] = "a b c d" }, "L"));
    }
}
