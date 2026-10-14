using System.Numerics;
using System.Text;
using Kotor.Compiler.Bsp;
using Kotor.Core.Diagnostics;
using BspCompiler = Kotor.Compiler.Compiler;

namespace Kotor.Compiler.Tests;

public sealed class CompilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kotor-compiler-" + Guid.NewGuid().ToString("N"));
    private string BspPath => Path.Combine(_root, "Test.bsp");

    public CompilerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static int LumpLen(byte[] b, int lump) => BitConverter.ToInt32(b, 12 + lump * 8);

    [Fact]
    public void Limit_exceeded_writes_no_bsp()
    {
        var log = new ImportLog();
        // Le depassement de brushes est desormais plafonne (COLLISION_CAPPED) : on force une vraie limite dure, les plans.
        var r = BspCompiler.Compile(TestScenes.Input(), BspPath, new Limits(0x8000, 1, 0x20000, 0x20000), log);

        Assert.False(r.Ok);
        Assert.Contains("MAX_MAP_PLANES", r.Error);
        Assert.False(File.Exists(BspPath));
    }

    [Fact]
    public void Compile_in_memory_writes_bsp()
    {
        var log = new ImportLog();
        var r = BspCompiler.Compile(TestScenes.Input(), BspPath, Limits.Default, log);

        Assert.True(r.Ok, r.Error);
        byte[] b = File.ReadAllBytes(BspPath);
        Assert.Equal("RBSP", Encoding.ASCII.GetString(b, 0, 4));
        string ents = Encoding.ASCII.GetString(b, BitConverter.ToInt32(b, 8), LumpLen(b, 0));
        Assert.Contains("info_player_start", ents);
        Assert.Contains("info_player_deathmatch", ents);
        Assert.True(LumpLen(b, BspWriter.LumpSurfaces) > 0);
        Assert.True(LumpLen(b, BspWriter.LumpBrushes) > 0);
    }

    [Fact]
    public void Missing_collision_is_warning_not_error()
    {
        var log = new ImportLog();
        var input = TestScenes.Input(solid: new Kotor.Core.Geometry.ModelMesh { Name = "empty" });
        var r = BspCompiler.Compile(input, BspPath, Limits.Default, log);
        Assert.True(r.Ok, r.Error);
        Assert.Contains(r.Warnings, w => w.Contains("collision"));
        Assert.Contains(log.Warnings, w => w.Code == "BSP_WARNING");
    }

    [Fact]
    public void Brush_cap_logs_and_does_not_throw()
    {
        var pos = new List<Vector3>();
        var tri = new List<int>();
        for (int i = 0; i < 100; i++)
        {
            int b = pos.Count;
            float x = i * 50f;
            pos.AddRange([new(x, 0, 0), new(x + 10, 0, 0), new(x, 10, 0)]);
            tri.AddRange([b, b + 1, b + 2]);
        }
        var log = new ImportLog();
        var input = TestScenes.Input(solid: TestScenes.Mesh(pos.ToArray(), tri.ToArray()));

        var r = BspCompiler.Compile(input, BspPath, new Limits(60, 0x20000, 0x20000, 0x20000), log);

        Assert.True(r.Ok, r.Error);
        byte[] b2 = File.ReadAllBytes(BspPath);
        Assert.True(LumpLen(b2, BspWriter.LumpBrushes) / 12 <= 60);
        Assert.Contains(log.Warnings, w => w.Code == "COLLISION_CAPPED");
    }

    [Fact]
    public void Double_sided_comes_from_surface_input()
    {
        var down = TestScenes.FloorSurface() with { Normals = [-Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ] };
        var tree = BspTree.Build([]);
        Assert.All(RenderSurfaces.FromInput(down with { DoubleSided = true }, tree), s => Assert.True(s.DoubleSided));
        Assert.All(RenderSurfaces.FromInput(down, tree), s => Assert.False(s.DoubleSided));
    }

    [Fact]
    public void Render_surfaces_are_attached_to_leaves_and_keep_input_shader()
    {
        var tree = BspTree.Build([]);
        var s = Assert.Single(RenderSurfaces.FromInput(TestScenes.FloorSurface("textures/kotor/floor"), tree));
        Assert.Equal("textures/kotor/floor", s.Shader);
        Assert.Equal(6, s.Indices.Length);
        Assert.Equal(4, s.Positions.Length);
        Assert.Equal(0, s.Leaf);
    }
}
