using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Pipeline;
using Kotor.Pipeline.Preview;

namespace Kotor.Pipeline.Tests;

public sealed class PreviewBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "k2j_prev_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static ModuleScene Scene(bool withTexture = true)
    {
        var mesh = new SceneMesh("r", "n", "t", null, TxiInfo.Empty, 0,
            [new(1, 2, 3), new(11, 2, 3), new(11, 12, 3), new(1, 12, 3)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [new(0, 0), new(1, 0), new(1, 1), new(0, 1)], null, [0, 1, 2, 0, 2, 3]);
        var floor = new SceneWalkmesh("r", [new(-5, -5, 0), new(5, -5, 0), new(5, 5, 0), new(-5, 5, 0)], [0, 1, 2, 0, 2, 3], [true, true]);
        var tex = new Dictionary<string, KotorTexture?>
        {
            ["t"] = withTexture ? new KotorTexture(2, 2, new byte[16], false, TxiInfo.Empty) : null,
        };
        return new ModuleScene(new ModuleInfo("tst", "tst", "Zone", new ModuleEntry(Vector3.Zero, Vector2.UnitX)), [mesh], [floor], tex);
    }

    private ExportOptions Opt() => new("k", Path.Combine(_root, "base"), Path.Combine(_root, "work"));

    [Fact]
    public void FromScene_flips_v_and_scales()
    {
        PreviewModel m = PreviewBuilder.FromScene(Scene(), 40f);
        PreviewBatch b = Assert.Single(m.Batches);
        Assert.Equal(new Vector2(0, 1), b.Vertices[0].Uv0);
        Assert.Equal(new Vector2(1, 0), b.Vertices[2].Uv0);
        Assert.Equal(new Vector3(40, 80, 120), b.Vertices[0].Position);
        Assert.Equal(2, m.TriangleCount);
        Assert.Equal(new Vector3(40, 80, 120), m.BoundsMin);
        Assert.NotNull(m.Spawn);
    }

    [Fact]
    public void Missing_texture_maps_to_checker()
    {
        PreviewModel m = PreviewBuilder.FromScene(Scene(withTexture: false), 40f);
        Assert.Equal("missing", Assert.Single(m.Batches).DiffuseKey);
        Assert.Equal("missing", PreviewModel.MissingTextureKey);
        PreviewTexture t = m.Textures["missing"];
        Assert.Equal((8, 8), (t.Width, t.Height));
        Assert.Equal(8 * 8 * 4, t.Rgba.Length);
    }

    [Fact]
    public void FromImport_contains_markers_and_spawn()
    {
        ImportStepResult imp = PipelineSteps.Import(Scene(), "demo", Opt(), new ImportLog());
        ModuleEntity[] ents =
        [
            new(EntityKind.Placeable, "crate", "p_crate", new Vector3(1, 2, 3), 90f, null),
            new(EntityKind.Trigger, "trg", "", new Vector3(0, 0, 0), 0f, [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)]),
        ];
        PreviewModel m = PreviewBuilder.FromImport(imp, ents, 40f);
        Assert.Equal(2, m.Markers.Count);
        Assert.Equal(new Vector3(40, 80, 120), m.Markers[0].Position);
        Assert.Equal("crate", m.Markers[0].Label);
        Assert.Equal(new Vector3(40, 40, 0), m.Markers[1].Polygon![2]);
        Assert.NotNull(m.Spawn);
        Assert.Equal(2, m.TriangleCount);
        Assert.Equal(PreviewBlend.Opaque, m.Batches[0].Blend);
        // UV deja en convention Q3 (t = 1 - v de la scene).
        Assert.Equal(new Vector2(0, 1), m.Batches[0].Vertices[0].Uv0);
    }

    [Fact]
    public void FromBsp_one_batch_per_surface()
    {
        var opt = Opt();
        var log = new ImportLog();
        ImportStepResult imp = PipelineSteps.Import(Scene(), "demo", opt, log);
        ExportStepResult exp = PipelineSteps.Export(imp, opt, log);
        var textures = new Dictionary<string, PreviewTexture> { ["t"] = new(2, 2, new byte[16]) };
        PreviewModel m = PreviewBuilder.FromBsp(exp.Bsp, textures);
        var nonSky = exp.Bsp.Surfaces.Where(s => s.Shader != JaDefaults.SkyShader).ToList();
        Assert.Equal(nonSky.Count, m.Batches.Count);
        Assert.Equal(nonSky.Sum(s => s.Indices.Length / 3), m.TriangleCount);
        Assert.Equal("t", m.Batches[0].DiffuseKey);
        Assert.Contains("missing", m.Textures.Keys);
        Assert.NotNull(m.Spawn);
    }
}
