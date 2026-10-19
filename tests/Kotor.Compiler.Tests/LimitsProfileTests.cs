using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core.Diagnostics;
using Kotor.Core.Geometry;
using BspCompiler = Kotor.Compiler.Compiler;

namespace Kotor.Compiler.Tests;

public class LimitsProfileTests : IDisposable
{
    private readonly string _bsp = Path.Combine(Path.GetTempPath(), "k2j_lim_" + Guid.NewGuid().ToString("N") + ".bsp");

    public void Dispose() { try { File.Delete(_bsp); } catch (IOException) { } }

    [Fact]
    public void Profiles_have_expected_values()
    {
        Assert.Equal(new Limits(0x8000, 0x20000, 0x20000, 0x20000, 0x20000, 0x40000), Limits.Vanilla);
        Assert.Equal(int.MaxValue, Limits.Swgl.MaxBrushes);
        Assert.Equal(int.MaxValue, Limits.Swgl.MaxPlanes);
        Assert.Equal(int.MaxValue, Limits.Swgl.MaxNodes);
        Assert.Equal(int.MaxValue, Limits.Swgl.MaxLeaves);
        Assert.Equal(int.MaxValue, Limits.Swgl.MaxBrushSides);
        Assert.Equal(int.MaxValue, Limits.Swgl.MaxLeafBrushes);
        // Relevee pour l assemblage end_m01aa + end_m01ab (1 242 104 leaf faces) : les chargeurs SWGL allouent d apres le lump.
        Assert.Equal(int.MaxValue / 4, Limits.Swgl.MaxLeafSurfaces);
        Assert.Equal(0x20000, Limits.Vanilla.MaxLeafSurfaces);
        Assert.Same(Limits.Swgl, Limits.FromName(null));
        Assert.Same(Limits.Swgl, Limits.FromName("SWGL"));
        Assert.Same(Limits.Vanilla, Limits.FromName("vanilla"));
        Assert.Throws<ArgumentException>(() => Limits.FromName("x"));
    }

    private static CompileInput Scene(int triangles)
    {
        var pos = new Vector3[triangles * 3];
        var tri = new int[triangles * 3];
        for (int i = 0; i < triangles; i++)
        {
            float x = (i % 200) * 50f, y = (i / 200) * 50f;
            pos[i * 3] = new(x, y, 0); pos[i * 3 + 1] = new(x + 10, y, 0); pos[i * 3 + 2] = new(x, y + 10, 0);
            tri[i * 3] = i * 3; tri[i * 3 + 1] = i * 3 + 1; tri[i * 3 + 2] = i * 3 + 2;
        }
        return TestScenes.Input(solid: TestScenes.Mesh(pos, tri));
    }

    [Fact]
    public void Swgl_profile_keeps_collision_beyond_vanilla_limits_and_vanilla_caps_it()
    {
        const int Count = 40000; // > MAX_MAP_BRUSHES vanilla (0x8000)
        var input = Scene(Count);

        var logSwgl = new ImportLog();
        var r = BspCompiler.Compile(input, _bsp, Limits.Swgl, logSwgl);
        Assert.True(r.Ok, r.Error);
        Assert.DoesNotContain(logSwgl.Warnings, w => w.Code == "COLLISION_CAPPED");
        Assert.True(BitConverter.ToInt32(File.ReadAllBytes(_bsp), 8 + BspWriter.LumpBrushes * 8 + 4) / 12 >= Count);

        var logVan = new ImportLog();
        r = BspCompiler.Compile(input, _bsp, Limits.Vanilla, logVan);
        Assert.True(r.Ok, r.Error);
        Assert.Contains(logVan.Warnings, w => w.Code == "COLLISION_CAPPED");
    }
}
