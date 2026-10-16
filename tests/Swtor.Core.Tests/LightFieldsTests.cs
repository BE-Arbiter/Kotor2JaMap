using System.Numerics;
using Swtor.Core.Areas;

namespace Swtor.Core.Tests;

public class LightFieldsTests
{
    private static RoomInstance Light(Vector3 scale, params RoomField[] fields) => new()
    {
        Id = 1, TypeId = 2, Kind = InstanceKind.Light, Scale = scale, Fields = fields,
    };

    [Fact]
    public void Decodes_type_brightness_gain_and_ramps()
    {
        var l = Light(new Vector3(2, 1, 0.5f),
            new RoomField(3, LightFields.LightType, 3),
            new RoomField(8, LightFields.BrightnessText, ".5"),
            new RoomField(4, LightFields.Gain, 0.8f),
            new RoomField(8, LightFields.FalloffRamp, "art\\lights\\falloff_spotlight"),
            new RoomField(8, LightFields.ShapeRamp, "art/lights/lum_round"),
            new RoomField(8, LightFields.ColorRamp, "art/lights/fill_235_210_207"));
        LightParams p = LightFields.Read(l);
        Assert.Equal(SwtorLightType.Spot, p.Type);
        Assert.True(p.IsSpot);
        Assert.Equal(0.5f, p.Brightness);
        Assert.Equal(0.8f, p.Gain);
        Assert.Equal(2f, p.MaxExtent);
        Assert.Equal("falloff_spotlight", p.FalloffRamp);
        Assert.Equal("lum_round", p.ShapeRamp);
        Assert.Equal("fill_235_210_207", p.ColorRamp);
    }

    [Fact]
    public void Missing_or_garbled_fields_give_neutral_defaults()
    {
        LightParams p = LightFields.Read(Light(Vector3.One, new RoomField(8, LightFields.BrightnessText, "abc"), new RoomField(3, LightFields.LightType, 9)));
        Assert.Equal(SwtorLightType.Unknown, p.Type);
        Assert.Equal(1f, p.Brightness);
        Assert.Equal(1f, p.Gain);
        Assert.False(p.IsSpot);
        Assert.Null(p.FalloffRamp);
    }

    [Theory]
    [InlineData("1.5", true, 1.5f)]
    [InlineData(".75", true, 0.75f)]
    [InlineData("1,5", false, 0f)]
    [InlineData("", false, 0f)]
    [InlineData("NaN", false, 0f)]
    public void Brightness_text_uses_invariant_decimals(string text, bool ok, float value)
    {
        Assert.Equal(ok, LightFields.TryParse(text, out float v));
        if (ok) Assert.Equal(value, v);
    }

    [Theory]
    [InlineData("art/lights/falloff_linear", "falloff_linear")]
    [InlineData("art\\lights\\falloff_linear.dds", "falloff_linear")]
    [InlineData("fill_1", "fill_1")]
    public void Ramp_names_drop_folder_and_extension(string path, string expected) => Assert.Equal(expected, LightFields.RampName(path));

    [Fact]
    public void Area_lighting_reads_sun_colours_and_fog()
    {
        var props = new AreaProperties(new Dictionary<string, string>
        {
            ["is_outdoor"] = "true", ["static_light_front"] = "#1,0.9,0.8,1", ["static_light_side"] = "#0.2,0.3,0.4,1", ["static_light_back"] = "#0,0,0,1",
            ["static_light_intensity"] = "1.5", ["light_azimuth"] = "90", ["light_polar"] = "90",
            ["static_fog_max"] = "0.7", ["static_fog_colors"] = "#0.8,0.9,1,1", ["static_fog_start"] = "5", ["static_fog_colors_end"] = "20",
        });
        AreaLighting a = AreaLighting.From(props);
        Assert.True(a.IsOutdoor);
        Assert.Equal(new Vector3(1, 0.9f, 0.8f), a.Front);
        Assert.Equal(new Vector3(0.2f, 0.3f, 0.4f), a.Side);
        Assert.Equal(1.5f, a.Intensity);
        Assert.True(Vector3.Distance(a.SunTravel, Vector3.UnitY) < 1e-4f);   // azimut 90, polaire 90 : horizontal vers +Y
        Assert.NotNull(a.Fog);
        Assert.Equal(0.7f, a.Fog!.Max);
        Assert.Equal(20f, a.Fog.End);
    }

    [Fact]
    public void Empty_properties_give_the_indoor_defaults_and_no_fog()
    {
        AreaLighting a = AreaLighting.From(AreaProperties.Empty);
        Assert.False(a.IsOutdoor);
        Assert.Null(a.Fog);
        Assert.Equal(Vector3.One, a.Front);
        Assert.True(a.SunTravel.Z < 0f);
    }
}
