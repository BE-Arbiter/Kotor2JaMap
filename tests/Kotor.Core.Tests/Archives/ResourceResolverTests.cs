using Kotor.Core.Archives;

namespace Kotor.Core.Tests.Archives;

public class ResourceResolverTests
{
    private static string MakeFakeKotor()
    {
        string dir = Path.Combine(Path.GetTempPath(), "k2j_kotor_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Override"));
        Directory.CreateDirectory(Path.Combine(dir, "modules"));
        return dir;
    }

    [Fact]
    public void Resolver_prefers_override()
    {
        string dir = MakeFakeKotor();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "Override", "m02aa.lyt"), new byte[] { 0xAA });
            File.WriteAllBytes(Path.Combine(dir, "modules", "tar_m02aa.rim"),
                ArchiveBuilders.Rim(("m02aa", (uint)ResourceType.Lyt, new byte[] { 0xBB })));
            using var r = ResourceResolver.ForModule(dir, "tar_m02aa");
            Assert.Equal(new byte[] { 0xAA }, r.Find("m02aa", ResourceType.Lyt));
            Assert.Contains("Override", r.FindSource("m02aa", ResourceType.Lyt));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Resolver_falls_back_to_rim()
    {
        string dir = MakeFakeKotor();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "modules", "tar_m02aa.rim"),
                ArchiveBuilders.Rim(("m02aa", (uint)ResourceType.Lyt, new byte[] { 0xBB })));
            using var r = ResourceResolver.ForModule(dir, "tar_m02aa");
            Assert.Equal(new byte[] { 0xBB }, r.Find("M02AA", ResourceType.Lyt));
            Assert.Contains("tar_m02aa.rim", r.FindSource("m02aa", ResourceType.Lyt));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Resolver_missing_returns_null()
    {
        string dir = MakeFakeKotor();
        try
        {
            using var r = ResourceResolver.ForModule(dir, "tar_m02aa");
            Assert.Null(r.Find("nothing", ResourceType.Mdl));
            Assert.Null(r.FindSource("nothing", ResourceType.Mdl));
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public void Real_install_finds_tar_m02aa_resources()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        using var r = ResourceResolver.ForModule(TestEnv.KotorDir!, "tar_m02aa");
        Assert.NotNull(r.Find("module", ResourceType.Ifo));
        Assert.NotNull(r.Find("m02aa", ResourceType.Are));
        Assert.NotNull(r.Find("m02aa", ResourceType.Lyt));
        Assert.NotNull(r.Find("m02aa_01a", ResourceType.Mdl));
        Assert.NotNull(r.Find("m02aa_01a", ResourceType.Mdx));
        Assert.NotNull(r.Find("m02aa_01a", ResourceType.Wok));
        Assert.NotNull(r.Find("m02aa_01a_lm0", ResourceType.Tga));
        Assert.Contains("models.bif", r.FindSource("m02aa_01a", ResourceType.Mdl));
    }
}
