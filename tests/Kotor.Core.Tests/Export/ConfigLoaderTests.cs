using Kotor.Core.Export;
using Kotor.Core;

namespace Kotor.Core.Tests.Export;

public class ConfigLoaderTests
{
    [Fact]
    public void Config_defaults_when_missing()
    {
        var o = ConfigLoader.Load(Path.Combine(Path.GetTempPath(), "nope_" + Guid.NewGuid().ToString("N") + ".json"));
        Assert.Equal(40f, o.Scale);
        Assert.Equal(1.0f, o.LightmapBrightness);
        Assert.Equal(40f, ConfigLoader.Load(null).Scale);
    }

    [Fact]
    public void Config_file_overrides_defaults()
    {
        string p = Path.Combine(Path.GetTempPath(), "cfg_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(p, "{ \"kotorDir\": \"K\", \"jaBaseDir\": \"J\", \"workDir\": \"W\", \"scale\": 10, \"lightmapBrightness\": 1.5 }");
        try
        {
            var o = ConfigLoader.Load(p, NoEnv);
            Assert.Equal(new ExportOptions("K", "J", "W", 10f, 1.5f, Kotor2Dir: o.Kotor2Dir), o);
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

    private static string? NoEnv(string name) => null;

    private static string WriteConfig(string json)
    {
        string p = Path.Combine(Path.GetTempPath(), "cfg_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(p, json);
        return p;
    }

    [Fact]
    public void Shipped_config_does_not_override_the_environment()
    {
        string shipped = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Kotor2Ja.Cli", "kotor2ja.config.json"));
        Assert.True(File.Exists(shipped), shipped);
        var o = ConfigLoader.Load(shipped, n => n switch { "KOTOR_PATH" => @"C:\env\kotor", "KOTOR2_PATH" => @"D:\env\k2", _ => null });
        Assert.Equal(@"C:\env\kotor", o.KotorDir);
        Assert.Equal(@"D:\env\k2", o.Kotor2Dir);
    }

    [Fact]
    public void Game_folders_priority_is_environment_then_config_then_default()
    {
        string p = WriteConfig("{ \"kotorDir\": \"C:\\\\cfg\\\\k1\", \"kotor2Dir\": \"C:\\\\cfg\\\\k2\" }");
        string empty = WriteConfig("{ }");
        try
        {
            // environment wins over the config
            var env = ConfigLoader.Load(p, n => n switch { "KOTOR_PATH" => @"E:\k1", "KOTOR2_PATH" => @"E:\k2", _ => null });
            Assert.Equal(@"E:\k1", env.KotorDir);
            Assert.Equal(@"E:\k2", env.Kotor2Dir);
            // config wins over the default (variables unset or blank)
            var cfg = ConfigLoader.Load(p, n => "  ");
            Assert.Equal(@"C:\cfg\k1", cfg.KotorDir);
            Assert.Equal(@"C:\cfg\k2", cfg.Kotor2Dir);
            // default when neither is given
            var def = ConfigLoader.Load(empty, NoEnv);
            Assert.Equal(ConfigLoader.DefaultKotorDir, def.KotorDir);
            Assert.Equal(ConfigLoader.DefaultKotor2Dir, def.Kotor2Dir);
            // per-game selection follows the same values
            Assert.Equal(@"E:\k2", env.ForGame(KotorGame.Kotor2).KotorDir);
            Assert.Equal(@"E:\k1", env.ForGame(KotorGame.Kotor1).KotorDir);
        }
        finally { File.Delete(p); File.Delete(empty); }
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
