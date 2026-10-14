using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core.Geometry;

namespace Kotor.Compiler.Tests;

public class VertexLightingTests
{
    private static readonly VertexLighting L = VertexLighting.Create(new Vector3(0, 0, -1), 0.5, 0.25, shift: 0);

    [Fact]
    public void Face_to_sun_is_ambient_plus_sun()
    {
        byte[] c = L.Evaluate(Vector3.UnitZ);
        Assert.Equal((byte)Math.Round(0.75 * 255), c[0]);
        Assert.Equal(c[0], c[2]);
        Assert.Equal(255, c[3]);
    }

    [Fact]
    public void Face_away_from_sun_is_ambient()
    {
        byte[] c = L.Evaluate(-Vector3.UnitZ);
        Assert.Equal((byte)Math.Round(0.25 * 255), c[0]);
    }

    [Fact]
    public void Double_sided_surface_is_lit_the_same_on_both_sides()
    {
        Assert.Equal(L.Evaluate(Vector3.UnitZ, true), L.Evaluate(-Vector3.UnitZ, true));
        Assert.True(L.Evaluate(-Vector3.UnitZ, true)[0] > L.Evaluate(-Vector3.UnitZ, false)[0]);
    }

    [Fact]
    public void Overbright_shift_divides_stored_colour_and_default_never_saturates()
    {
        var d = VertexLighting.Default;
        byte[] c = d.Evaluate(Vector3.UnitZ);
        Assert.InRange(c[1], 63, 64); // saturation (1.0) / 4 ; canal vert, la teinte cyan du soleil baisse le rouge
    }

    [Fact]
    public void Default_lighting_is_teal_tinted_like_TFU()
    {
        var d = VertexLighting.Default;
        Assert.True(d.Sun.X < d.Sun.Y && d.Sun.Y == d.Sun.Z);
        Assert.True(d.Ambient.X < d.Ambient.Y && d.Ambient.Y < d.Ambient.Z);
        byte[] c = d.Evaluate(Vector3.UnitZ);
        Assert.True(c[0] < c[1]);
    }

    [Fact]
    public void Worldspawn_keys_are_read()
    {
        var w = new Dictionary<string, string> { ["_kotor_sun_dir"] = "0 0 -2", ["_kotor_sun"] = "0.5", ["_kotor_ambient"] = "0.1 0.2 0.3" };
        var l = VertexLighting.FromWorldspawn(w);
        Assert.Equal(new Vector3(0, 0, -1), l.SunDir);
        Assert.Equal(new Vector3(0.5f), l.Sun);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), l.Ambient);
    }

    [Fact]
    public void Missing_or_bad_keys_fall_back_to_defaults()
    {
        Assert.Equal(VertexLighting.Default, VertexLighting.FromWorldspawn(new Dictionary<string, string>()));
        var bad = new Dictionary<string, string> { ["_kotor_sun_dir"] = "x", ["_kotor_sun"] = "-3" };
        Assert.Equal(VertexLighting.Default, VertexLighting.FromWorldspawn(bad));
    }

    [Fact]
    public void BspWriter_bakes_lighting_into_drawvert_colours_and_leaves_sky_white()
    {
        var tree = BspTree.Build([]);
        var up = new RenderSurface("a", [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [default, default, default],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], [0, 1, 2], [0]);
        var sky = up with { Shader = "sky", Sky = true };
        var ms = new MemoryStream();
        BspWriter.Write(tree, ms, null, null, [up, sky], L);
        byte[] b = ms.ToArray();
        int vo = BitConverter.ToInt32(b, 8 + 8 * BspWriter.LumpDrawVerts);
        Assert.Equal((byte)Math.Round(0.75 * 255), b[vo + 64]);
        Assert.Equal(255, b[vo + 67]);
        Assert.Equal(255, b[vo + 3 * 80 + 64]); // premier sommet du ciel
    }
}
