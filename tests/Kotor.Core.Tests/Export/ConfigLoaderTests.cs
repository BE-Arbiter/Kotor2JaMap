using Kotor.Core.Export;

namespace Kotor.Core.Tests.Export;

public class ConfigLoaderTests
{
    [Fact]
    public void Config_defaults_when_missing()
    {
        var o = ConfigLoader.Load(Path.Combine(Path.GetTempPath(), "nope_" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.Equal(36f, o.Scale);
        Assert.Equal(1.0f, o.LightmapBrightness);
        Assert.Equal(36f, ConfigLoader.Load(null).Scale);
    }

    [Fact]
    public void Config_file_overrides_defaults()
    {
        string p = Path.Combine(Path.GetTempPath(), "cfg_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(p, "{ \"kotorDir\": \"K\", \"jaBaseDir\": \"J\", \"workDir\": \"W\", \"scale\": 10, \"lightmapBrightness\": 1.5 }");
        try
        {
            var o = ConfigLoader.Load(p);
            Assert.Equal(new ExportOptions("K", "J", "W", 10f, 1.5f), o);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void BspLimits_defaults_to_swgl_and_is_read_from_config()
    {
        Assert.Equal("swgl", ConfigLoader.Load(null).BspLimits);
        string p = Path.Combine(Path.GetTempPath(), "cfg_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(p, "{ \"bspLimits\": \"vanilla\" }");
        try { Assert.Equal("vanilla", ConfigLoader.Load(p).BspLimits); }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Shipped_config_does_not_override_KOTOR_PATH()
    {
        string shipped = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Kotor2Ja.Cli", "kotor2ja.config.json"));
        Assert.True(File.Exists(shipped), shipped);
        string? old = Environment.GetEnvironmentVariable("KOTOR_PATH");
        Environment.SetEnvironmentVariable("KOTOR_PATH", @"C:\env\kotor");
        try { Assert.Equal(@"C:\env\kotor", ConfigLoader.Load(shipped).KotorDir); }
        finally { Environment.SetEnvironmentVariable("KOTOR_PATH", old); }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Validate_rejects_bad_scale_and_brightness(float v)
    {
        Assert.NotNull(new ExportOptions("k", "j", "w", Scale: v).Validate());
        Assert.NotNull(new ExportOptions("k", "j", "w", LightmapBrightness: v).Validate());
    }

    [Fact]
    public void Validate_accepts_defaults_and_rejects_unknown_profile()
    {
        Assert.Null(new ExportOptions("k", "j", "w").Validate());
        Assert.Null(new ExportOptions("k", "j", "w", BspLimits: "Vanilla").Validate());
        Assert.NotNull(new ExportOptions("k", "j", "w", BspLimits: "x").Validate());
    }
}
