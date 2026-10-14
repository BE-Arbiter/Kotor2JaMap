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
}
