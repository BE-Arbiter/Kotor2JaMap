namespace Swtor.Core.Tests;

public class SwtorAssetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"swtor_assets_{Guid.NewGuid():N}");

    public SwtorAssetsTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "art", "Static"));
        File.WriteAllText(Path.Combine(_root, "art", "Static", "Thing.GR2"), "x");
    }

    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData("/resources/art/static/thing.gr2")]
    [InlineData("art/static/thing.gr2")]
    [InlineData(@"\resources\art\STATIC\Thing.gr2")]
    [InlineData(@"art\static/thing.GR2")]
    public void Resolve_FindsFileIgnoringCaseAndSeparators(string path)
    {
        var assets = new SwtorAssets(_root);
        string? found = assets.Resolve(path);
        Assert.NotNull(found);
        Assert.True(File.Exists(found));
    }

    [Fact]
    public void Resolve_MissingOrEscapingPath_ReturnsNull()
    {
        var assets = new SwtorAssets(_root);
        Assert.Null(assets.Resolve("art/static/nothing.gr2"));
        Assert.Null(assets.Resolve("art/../../x"));
        Assert.Null(assets.ReadBytes("nope"));
    }

    [Fact]
    public void Normalize_StripsResourcesPrefix() =>
        Assert.Equal("art/a/b.dds", SwtorAssets.Normalize(@"/resources\art\a/b.dds"));
}
