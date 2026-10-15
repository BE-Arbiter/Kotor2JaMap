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
    public void Overbright_shift_divides_stored_colour()
    {
        var d = VertexLighting.Default with { OverbrightShift = 2 };
        byte[] c = d.Evaluate(Vector3.UnitZ);
        Assert.InRange(c[1], 63, 64); // saturation (1.0) / 4 ; canal vert, la teinte cyan du soleil baisse le rouge
    }

    [Fact]
    public void Default_lighting_is_teal_tinted_like_TFU()
    {
        var d = VertexLighting.Default with { OverbrightShift = 2 };
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


    [Fact]
    public void Point_light_attenuates()
    {
        // Light at the origin, radius 100. Vertices away from the light so the n.l product is really evaluated (no zero-distance shortcut).
        var lit = VertexLighting.ForKotor(Vector3.Zero, [new PointLight(Vector3.Zero, Vector3.One, 100f, 1f)], shift: 0);
        byte At(Vector3 p, Vector3 n) => lit.Evaluate(p, n)[0];
        Assert.Equal((byte)Math.Round(0.75 * 255), At(new Vector3(0, 0, 25), -Vector3.UnitZ));
        Assert.Equal((byte)Math.Round(0.5 * 255), At(new Vector3(0, 0, 50), -Vector3.UnitZ));
        Assert.Equal((byte)Math.Round(0.5 * 255), At(new Vector3(30, 40, 0), new Vector3(-0.6f, -0.8f, 0)));   // distance 50, off axis
        Assert.Equal(0, At(new Vector3(0, 0, 100), -Vector3.UnitZ));
        Assert.Equal(0, At(new Vector3(0, 0, 150), -Vector3.UnitZ));
        // n.l: a normal tilted 60 degrees away from the light halves the contribution.
        var tilted = new Vector3(MathF.Sin(MathF.PI / 3), 0, -MathF.Cos(MathF.PI / 3));
        Assert.InRange((int)At(new Vector3(0, 0, 50), tilted), 63, 65);
    }

    [Fact]
    public void Point_light_sum_is_clamped_per_channel()
    {
        var lights = new[]
        {
            new PointLight(Vector3.Zero, new Vector3(1, 0.1f, 0), 100f, 3f),
            new PointLight(Vector3.Zero, new Vector3(1, 0.1f, 0), 100f, 3f),
        };
        var lit = VertexLighting.ForKotor(new Vector3(0.5f), lights, shift: 0);
        byte[] c = lit.Evaluate(new Vector3(0, 0, 10), -Vector3.UnitZ);
        Assert.Equal(255, c[0]);
        Assert.Equal(255, c[1]);   // 0.5 + 2 * 0.1 * 3 * 0.9 = 1.04, clamped
        Assert.Equal((byte)Math.Round(0.5 * 255), c[2]);
    }

    [Fact]
    public void Ambient_only_light_adds_to_ambient_without_direction_or_attenuation()
    {
        var amb = new PointLight(new Vector3(0, 0, 0), new Vector3(0.2f), 10f, 2f, AmbientOnly: true);
        var lit = VertexLighting.ForKotor(new Vector3(0.1f), [amb], shift: 0);
        byte expected = (byte)Math.Round(0.5 * 255);                                       // 0.1 + 0.2 * 2
        Assert.Equal(expected, lit.Evaluate(new Vector3(5000, 0, 0), Vector3.UnitX)[0]);   // far beyond the radius, facing away
        Assert.Equal(expected, lit.Evaluate(Vector3.Zero, -Vector3.UnitX)[0]);
        Assert.Equal(expected, lit.Evaluate(new Vector3(3, 4, 0), Vector3.UnitZ, true)[0]);
    }

    [Fact]
    public void Invalid_lights_are_ignored_and_ToByte_is_safe_for_nan()
    {
        var bad = new[]
        {
            new PointLight(Vector3.Zero, Vector3.One, float.NaN, 1f),
            new PointLight(Vector3.Zero, Vector3.One, float.PositiveInfinity, 1f),
            new PointLight(Vector3.Zero, Vector3.One, 100f, float.NaN),
            new PointLight(Vector3.Zero, Vector3.One, 100f, -1f),
            new PointLight(Vector3.Zero, new Vector3(float.NaN, 1, 1), 100f, 1f),
            new PointLight(Vector3.Zero, Vector3.One, 100f, float.PositiveInfinity, AmbientOnly: true),
        };
        var lit = VertexLighting.ForKotor(new Vector3(0.25f), bad, shift: 0);
        byte[] c = lit.Evaluate(new Vector3(0, 0, 10), -Vector3.UnitZ);
        Assert.Equal(new byte[] { 64, 64, 64, 255 }, c);
        // A NaN ambient must not produce an undefined byte.
        byte[] n = VertexLighting.ForKotor(new Vector3(float.NaN), [], shift: 0).Evaluate(Vector3.Zero, Vector3.UnitZ);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, n);
    }

    [Fact]
    public void Default_shift_is_zero_like_swgl_and_can_be_overridden()
    {
        Assert.Equal(0, VertexLighting.DefaultOverbrightShift);
        Assert.Equal(0, VertexLighting.Default.OverbrightShift);
        var lit = VertexLighting.ForKotor(new Vector3(0.8f), []);
        Assert.Equal((byte)Math.Round(0.8 * 255), lit.Evaluate(Vector3.Zero, Vector3.UnitZ)[0]);
        Assert.Equal((byte)Math.Round(0.2 * 255), (lit with { OverbrightShift = 2 }).Evaluate(Vector3.Zero, Vector3.UnitZ)[0]);
    }
    [Fact]
    public void Point_light_respects_facing_and_double_sided()
    {
        var lit = VertexLighting.ForKotor(new Vector3(0.1f), [new PointLight(Vector3.Zero, Vector3.One, 100f, 1f)], shift: 0);
        var p = new Vector3(0, 0, 50);
        byte ambient = (byte)Math.Round(0.1 * 255);
        Assert.Equal(ambient, lit.Evaluate(p, Vector3.UnitZ)[0]);                 // facing away: ambient only
        Assert.True(lit.Evaluate(p, Vector3.UnitZ, true)[0] > ambient);          // double sided: |n.l|
        Assert.Equal(lit.Evaluate(p, Vector3.UnitZ, true), lit.Evaluate(p, -Vector3.UnitZ, true));
    }

    [Fact]
    public void Compile_input_lights_switch_to_kotor_mode()
    {
        var input = TestScenes.Input();
        Assert.Equal(VertexLighting.Default, Compiler.EffectiveLighting(input));
        var withLights = input with { Lights = [new PointLight(Vector3.Zero, Vector3.One, 10f, 1f)] };
        var eff = Compiler.EffectiveLighting(withLights);
        Assert.Equal(Vector3.Zero, eff.Sun);
        Assert.Single(eff.Lights);
    }
}
