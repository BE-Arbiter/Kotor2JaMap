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
    private static GamePaths NoDetect() => new(null, null, null, @"W:\work");
    private static GamePaths Detected() => new(@"F:\steam\k1", @"F:\steam\k2", @"F:\steam\ja", @"W:\work");

    [Fact]
    public void Config_save_roundtrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cfgsave_" + Guid.NewGuid().ToString("N"));
        string p = Path.Combine(dir, "sub", ConfigLoader.ConfigFileName);
        try
        {
            var o = new ExportOptions(@"C:\k1", @"C:\ja\base", @"C:\work", 12f, 1.25f, "vanilla", Kotor2Dir: @"D:\k2");
            ConfigLoader.Save(p, o);
            Assert.Equal(o, ConfigLoader.Load(p, NoEnv, Detected));
            Assert.Equal(o, ConfigLoader.LoadOrDetect(Path.GetDirectoryName(p)!, NoEnv, Detected));
            // without a file next to the executable: detection
            Assert.Equal(@"F:\steam\k1", ConfigLoader.LoadOrDetect(dir, NoEnv, Detected).KotorDir);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Config_roundtrip_keeps_the_swtor_source_and_folders()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cfgswtor_" + Guid.NewGuid().ToString("N"));
        string p = Path.Combine(dir, ConfigLoader.ConfigFileName);
        try
        {
            var game = new ExportOptions(@"C:\k1", @"C:\ja\base", @"C:\work", Kotor2Dir: @"D:\k2", SwtorDir: @"D:\tor\resources", SwtorGameDir: @"E:\SWTOR", SwtorFromGame: true);
            ConfigLoader.Save(p, game);
            Assert.Equal(game, ConfigLoader.Load(p, NoEnv, Detected));
            ConfigLoader.Save(p, game with { SwtorFromGame = false });
            ExportOptions loaded = ConfigLoader.Load(p, NoEnv, Detected);
            Assert.False(loaded.SwtorFromGame);
            Assert.Equal(@"E:\SWTOR", loaded.SwtorGameDir);
            Assert.Equal(@"D:\tor\resources", loaded.SwtorDir);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

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
            var def = ConfigLoader.Load(empty, NoEnv, NoDetect);
            Assert.Equal(ConfigLoader.DefaultKotorDir, def.KotorDir);
            Assert.Equal(ConfigLoader.DefaultKotor2Dir, def.Kotor2Dir);
            // detection wins over the default, loses to the config and to the environment
            var det = ConfigLoader.Load(empty, NoEnv, Detected);
            Assert.Equal(@"F:\steam\k1", det.KotorDir);
            Assert.Equal(@"F:\steam\k2", det.Kotor2Dir);
            Assert.Equal(@"F:\steam\ja", det.JaBaseDir);
            Assert.Equal(@"C:\cfg\k1", ConfigLoader.Load(p, NoEnv, Detected).KotorDir);
            Assert.Equal(@"E:\k1", ConfigLoader.Load(p, n => n == "KOTOR_PATH" ? @"E:\k1" : null, Detected).KotorDir);
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

    [Fact]
    public void Save_falls_back_to_appdata_when_exe_folder_is_not_writable_and_load_reads_it()
    {
        string root = Path.Combine(Path.GetTempPath(), "cfgfb_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string blocker = Path.Combine(root, "blocker");
            File.WriteAllText(blocker, "x");                       // a file where the executable folder should be: not writable
            string exeDir = Path.Combine(blocker, "bin"), appData = Path.Combine(root, "appdata");
            var o = new ExportOptions(@"C:\k1", @"C:\ja\base", @"C:\work", Kotor2Dir: @"D:\k2");

            string written = ConfigLoader.SaveWithFallback(exeDir, o, appData);
            Assert.Equal(Path.Combine(appData, ConfigLoader.ConfigFileName), written);
            Assert.Equal(written, ConfigLoader.FindConfig(exeDir, appData));
            Assert.Equal(o, ConfigLoader.LoadOrDetect(exeDir, _ => null, () => new GamePaths(null, null, null, @"C:\other"), appData));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Config_next_to_exe_wins_over_appdata()
    {
        string root = Path.Combine(Path.GetTempPath(), "cfgord_" + Guid.NewGuid().ToString("N"));
        try
        {
            string exeDir = Path.Combine(root, "exe"), appData = Path.Combine(root, "appdata");
            ConfigLoader.Save(Path.Combine(appData, ConfigLoader.ConfigFileName), new ExportOptions("A", "J", "W"));
            Assert.Equal(Path.Combine(appData, ConfigLoader.ConfigFileName), ConfigLoader.FindConfig(exeDir, appData));
            string written = ConfigLoader.SaveWithFallback(exeDir, new ExportOptions("B", "J", "W"), appData);
            Assert.Equal(Path.Combine(exeDir, ConfigLoader.ConfigFileName), written);
            Assert.Equal(written, ConfigLoader.FindConfig(exeDir, appData));
            Assert.Null(ConfigLoader.FindConfig(Path.Combine(root, "none"), Path.Combine(root, "none2")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
