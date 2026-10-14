using System.IO.Compression;
using Kotor.Core.Export;

namespace Kotor.Core.Tests;

public sealed class Pk3BuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pk3test_" + Guid.NewGuid().ToString("N"));

    private string _work => Path.Combine(_root, "work");
    private string _base => Path.Combine(_root, "base");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private void Seed(bool bsp = true)
    {
        Directory.CreateDirectory(Path.Combine(_work, "maps"));
        Directory.CreateDirectory(Path.Combine(_work, "shaders"));
        Directory.CreateDirectory(Path.Combine(_work, "textures"));
        if (bsp) File.WriteAllBytes(Path.Combine(_work, "maps", "DAG_1.bsp"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(_work, "shaders", "DAG_1.shader"), "textures/tfu2ja/x\n{\n\t{\n\t\tmap textures/tfu2ja/X.tga\n\t}\n}\n");
        File.WriteAllBytes(Path.Combine(_work, "textures", "X.tga"), [4, 5]);
        Directory.CreateDirectory(Path.Combine(_work, "models"));
        File.WriteAllText(Path.Combine(_work, "models", "a.obj"), "o");
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
        Pk3Builder.Build(_work, "DAG_1", pk3);

        Assert.Equal(["maps/dag_1.bsp", "shaders/dag_1.shader", "textures/tfu2ja/x.tga"], Names(pk3));
    }

    [Fact]
    public void Pk3_skips_tga_not_referenced_by_shader()
    {
        Seed();
        File.WriteAllBytes(Path.Combine(_work, "textures", "Stale.tga"), [9]);
        Directory.CreateDirectory(_base);
        string pk3 = Path.Combine(_base, "out.pk3");
        Pk3Builder.Build(_work, "DAG_1", pk3);

        Assert.Equal(["maps/dag_1.bsp", "shaders/dag_1.shader", "textures/tfu2ja/x.tga"], Names(pk3));
    }

    [Fact]
    public void Entry_names_are_lowercase_forward_slash()
    {
        Seed();
        Directory.CreateDirectory(_base);
        string pk3 = Path.Combine(_base, "out.pk3");
        Pk3Builder.Build(_work, "DAG_1", pk3);

        foreach (string n in Names(pk3))
        {
            Assert.DoesNotContain("\\", n);
            Assert.Equal(n.ToLowerInvariant(), n);
        }
    }

    [Fact]
    public void Pk3_reads_from_work_dir_writes_only_pk3()
    {
        Seed();
        Directory.CreateDirectory(_base);
        string pk3 = Path.Combine(_base, "tfu2ja_DAG_1.pk3");
        Pk3Builder.Build(_work, "DAG_1", pk3);

        Assert.Equal([pk3], Directory.GetFileSystemEntries(_base));
        Assert.Equal(3, Names(pk3).Length);
    }

    [Fact]
    public void Missing_bsp_fails_without_output()
    {
        Seed(bsp: false);
        Directory.CreateDirectory(_base);
        string pk3 = Path.Combine(_base, "out.pk3");
        var ex = Assert.Throws<FileNotFoundException>(() => Pk3Builder.Build(_work, "DAG_1", pk3));
        Assert.Contains("DAG_1.bsp", ex.Message);
        Assert.False(File.Exists(pk3));
    }

    [Fact]
    public void Existing_pk3_replaced_only_on_success()
    {
        Seed();
        Directory.CreateDirectory(_base);
        string pk3 = Path.Combine(_base, "out.pk3");
        File.WriteAllText(pk3, "old");
        Pk3Builder.Build(_work, "DAG_1", pk3);
        Assert.Equal(3, Names(pk3).Length);

        File.Delete(Path.Combine(_work, "maps", "DAG_1.bsp"));
        File.WriteAllText(pk3, "old");
        Assert.Throws<FileNotFoundException>(() => Pk3Builder.Build(_work, "DAG_1", pk3));
        Assert.Equal("old", File.ReadAllText(pk3));
    }
}
