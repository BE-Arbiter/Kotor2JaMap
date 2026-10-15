using System.IO.Compression;
using Kotor.Core.Export;

namespace Kotor.Core.Tests;

public sealed class Pk3BuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pk3test_" + Guid.NewGuid().ToString("N"));

    private string _stage => Path.Combine(_root, "stage");
    private string _base => Path.Combine(_root, "base");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private void Seed(bool bsp = true)
    {
        Directory.CreateDirectory(Path.Combine(_stage, "maps"));
        Directory.CreateDirectory(Path.Combine(_stage, "shaders"));
        Directory.CreateDirectory(Path.Combine(_stage, "textures", "kotor"));
        if (bsp) File.WriteAllBytes(Path.Combine(_stage, "maps", "x.bsp"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(_stage, "shaders", "kotor_x.shader"), "textures/kotor/t\n{\n}\n");
        File.WriteAllBytes(Path.Combine(_stage, "textures", "kotor", "t.tga"), [4, 5]);
    }

    private static string[] Names(string pk3)
    {
        using var z = ZipFile.OpenRead(pk3);
        return z.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public void Pk3_contains_expected_entries()
    {
        Seed();
        Directory.CreateDirectory(_base);
        string pk3 = Path.Combine(_base, "out.pk3");
        Pk3Builder.Build(_stage, pk3);

        Assert.Equal(["maps/x.bsp", "shaders/kotor_x.shader", "textures/kotor/t.tga"], Names(pk3));
    }

    [Fact]
    public void Entry_names_are_lowercase_forward_slash()
    {
        Seed();
        Directory.CreateDirectory(Path.Combine(_stage, "textures", "kotor", "lm"));
        File.WriteAllBytes(Path.Combine(_stage, "textures", "kotor", "lm", "LM0.TGA"), [1]);
        Directory.CreateDirectory(_base);
        string pk3 = Path.Combine(_base, "out.pk3");
        Pk3Builder.Build(_stage, pk3);

        Assert.Contains("textures/kotor/lm/lm0.tga", Names(pk3));
        foreach (string n in Names(pk3))
        {
            Assert.DoesNotContain("\\", n);
            Assert.Equal(n.ToLowerInvariant(), n);
        }
    }

    [Fact]
    public void Pk3_writes_only_pk3()
    {
        Seed();
        Directory.CreateDirectory(_base);
        string pk3 = Path.Combine(_base, "kotor_x.pk3");
        Pk3Builder.Build(_stage, pk3);

        Assert.Equal([pk3], Directory.GetFileSystemEntries(_base));
        Assert.Equal(3, Names(pk3).Length);
    }

    [Fact]
    public void Same_staging_built_twice_gives_identical_bytes()
    {
        Seed();
        Directory.CreateDirectory(_base);
        string a = Path.Combine(_base, "a.pk3"), b = Path.Combine(_base, "b.pk3");
        Pk3Builder.Build(_stage, a);
        // Different modification times (staging recreated by a later export) must not change the pk3.
        foreach (string f in Directory.GetFiles(_stage, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(f, new DateTime(2031, 5, 6, 7, 8, 10, DateTimeKind.Utc));
        Pk3Builder.Build(_stage, b);

        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
        using var z = ZipFile.OpenRead(a);
        Assert.All(z.Entries, e => Assert.Equal(new DateTime(1980, 1, 1), e.LastWriteTime.DateTime));
    }

    [Fact]
    public void Missing_bsp_fails_without_output()
    {
        Seed(bsp: false);
        Directory.CreateDirectory(_base);
        string pk3 = Path.Combine(_base, "out.pk3");
        var ex = Assert.Throws<FileNotFoundException>(() => Pk3Builder.Build(_stage, pk3));
        Assert.Contains(".bsp", ex.Message);
        Assert.False(File.Exists(pk3));
    }

    [Fact]
    public void Existing_pk3_replaced_only_on_success()
    {
        Seed();
        Directory.CreateDirectory(_base);
        string pk3 = Path.Combine(_base, "out.pk3");
        File.WriteAllText(pk3, "old");
        Pk3Builder.Build(_stage, pk3);
        Assert.Equal(3, Names(pk3).Length);

        File.Delete(Path.Combine(_stage, "maps", "x.bsp"));
        File.WriteAllText(pk3, "old");
        Assert.Throws<FileNotFoundException>(() => Pk3Builder.Build(_stage, pk3));
        Assert.Equal("old", File.ReadAllText(pk3));
    }
}
