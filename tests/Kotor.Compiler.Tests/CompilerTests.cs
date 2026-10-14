using System.Numerics;
using System.Text;
using BspCompiler = Kotor.Compiler.Compiler;
using Kotor.Compiler.Bsp;

namespace Kotor.Compiler.Tests;

public sealed class CompilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tfu2ja-compiler-" + Guid.NewGuid().ToString("N"));
    private string MapsDir => Path.Combine(_root, "maps");
    private string AssetRoot => Path.Combine(_root, "tfu2ja");
    private string ModelsDir => Path.Combine(AssetRoot, "models", "Test");

    public CompilerTests()
    {
        Directory.CreateDirectory(MapsDir);
        Directory.CreateDirectory(ModelsDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string WriteMap(params string[] models)
    {
        var sb = new StringBuilder("{\n\"classname\" \"worldspawn\"\n\"_ambient\" \"15\"\n}\n");
        foreach (string m in models)
            sb.Append("{\n\"classname\" \"misc_model\"\n\"model\" \"models/Test/").Append(m).Append(".obj\"\n\"origin\" \"0 0 0\"\n}\n");
        sb.Append("{\n\"classname\" \"info_player_start\"\n\"origin\" \"5 5 70\"\n\"angle\" \"0\"\n}\n");
        sb.Append("{\n\"classname\" \"info_player_deathmatch\"\n\"origin\" \"5 5 70\"\n\"angle\" \"0\"\n}\n");
        string path = Path.Combine(MapsDir, "Test.map");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    // Un sol carre (2 triangles) en unites Quake.
    private void WriteFloor(string file, bool withUv)
    {
        string uv = withUv ? "vt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nvn 0 0 1\n" : "";
        string f = withUv ? "f 1/1/1 2/2/1 3/3/1\nf 1/1/1 3/3/1 4/4/1\n" : "f 1 2 3\nf 1 3 4\n";
        File.WriteAllText(Path.Combine(ModelsDir, file),
            "g floor\nv 0 0 0\nv 100 0 0\nv 100 100 0\nv 0 100 0\n" + uv + f);
    }

    [Fact]
    public void Limit_exceeded_writes_no_bsp()
    {
        string map = WriteMap("floor");
        WriteFloor("floor.obj", true);
        WriteFloor("Test_collision.obj", false);
        string bsp = Path.Combine(MapsDir, "Test.bsp");

        var r = BspCompiler.Compile(map, bsp, new Limits(0, 0x20000, 0x20000, 0x20000));

        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
        Assert.Contains("MAX_MAP_BRUSHES", r.Error);
        Assert.False(File.Exists(bsp));
    }

    [Fact]
    public void Compiles_small_map_end_to_end()
    {
        string map = WriteMap("floor");
        WriteFloor("floor.obj", true);
        WriteFloor("Test_collision.obj", false);
        string bsp = Path.Combine(MapsDir, "Test.bsp");

        var r = BspCompiler.Compile(map, bsp, Limits.Default);

        Assert.True(r.Ok, r.Error);
        Assert.True(File.Exists(bsp));
        byte[] b = File.ReadAllBytes(bsp);
        // Jedi Academy utilise l'identifiant RBSP (le plan disait IBSP).
        Assert.Equal("RBSP", Encoding.ASCII.GetString(b, 0, 4));
        Assert.True(BitConverter.ToInt32(b, 12 + BspWriter.LumpSurfaces * 8) > 0);
        Assert.True(BitConverter.ToInt32(b, 12 + BspWriter.LumpBrushes * 8) > 0);
        string ents = Encoding.ASCII.GetString(b, BitConverter.ToInt32(b, 8), BitConverter.ToInt32(b, 12));
        Assert.Contains("info_player_deathmatch", ents);
        Assert.DoesNotContain("misc_model", ents);
        Assert.True(File.Exists(Path.Combine(AssetRoot, "Test.journal.txt")));
    }

    [Fact]
    public void Missing_model_is_warning_not_error()
    {
        string map = WriteMap("absent");
        WriteFloor("Test_collision.obj", false);
        string bsp = Path.Combine(MapsDir, "Test.bsp");

        var r = BspCompiler.Compile(map, bsp, Limits.Default);

        Assert.True(r.Ok, r.Error);
        Assert.Contains(r.Warnings, w => w.Contains("absent"));
        Assert.True(File.Exists(bsp));
    }

    [Fact]
    public void Missing_collision_is_warning_not_error()
    {
        string map = WriteMap("floor");
        WriteFloor("floor.obj", true);
        var r = BspCompiler.Compile(map, Path.Combine(MapsDir, "Test.bsp"), Limits.Default);
        Assert.True(r.Ok, r.Error);
        Assert.Contains(r.Warnings, w => w.Contains("collision"));
    }

    [Fact]
    public void Render_surfaces_are_attached_to_leaves_and_shader_named_after_obj()
    {
        WriteFloor("floor.obj", true);
        var tree = BspTree.Build([]);
        var surfaces = RenderSurfaces.FromObj(Path.Combine(ModelsDir, "floor.obj"), Vector3.Zero, tree);
        var s = Assert.Single(surfaces);
        Assert.Equal("textures/tfu2ja/floor", s.Shader);
        Assert.Equal(6, s.Indices.Length);
        Assert.Equal(4, s.Positions.Length);
        Assert.Equal(0, s.Leaf);
    }
}
