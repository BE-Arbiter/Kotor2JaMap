using System.Numerics;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
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
    public void FromBsp_one_batch_per_surface_sky_included()
    {
        var opt = Opt();
        var log = new ImportLog();
        ImportStepResult imp = PipelineSteps.Import(Scene(), "demo", opt, log);
        ExportStepResult exp = PipelineSteps.Export(imp, opt, log);
        var textures = new Dictionary<string, PreviewTexture> { ["t"] = new(2, 2, new byte[16]) };
        PreviewModel m = PreviewBuilder.FromBsp(exp.Bsp, textures);
        Assert.Equal(exp.Bsp.Surfaces.Count, m.Batches.Count);
        Assert.Equal(exp.Bsp.Surfaces.Sum(s => s.Indices.Length / 3), m.TriangleCount);
        var sky = m.Batches.Where(b => b.IsSky).ToList();
        Assert.NotEmpty(sky);
        Assert.All(m.Batches, b => Assert.Equal(b.Name == JaDefaults.SkyShader, b.IsSky));
        long skyTris = sky.Sum(b => (long)b.Indices.Length / 3);
        Assert.Equal(SkyBox.IndexCount / 3, skyTris);

        // Hors ciel : identique a la vue Importer ; total BSP = Importer + ciel.
        PreviewModel imported = PreviewBuilder.FromImport(imp, [], 40f);
        Assert.Equal(imported.TriangleCount, m.Batches.Where(b => !b.IsSky).Sum(b => (long)b.Indices.Length / 3));
        Assert.Equal(imported.TriangleCount + skyTris, m.TriangleCount);

        Assert.Equal("t", m.Batches.First(b => !b.IsSky).DiffuseKey);
        Assert.Contains("missing", m.Textures.Keys);
        Assert.NotNull(m.Spawn);
    }

    [Fact]
    public void Duplicate_shader_names_do_not_throw()
    {
        var opt = Opt();
        var log = new ImportLog();
        ImportStepResult imp = PipelineSteps.Import(Scene(), "demo", opt, log);
        PlannedShader first = imp.Plan.Shaders[0];
        var dup = imp.Plan with { Shaders = [first, first with { Texture = "autre" }] };
        PreviewModel m = PreviewBuilder.FromImport(imp with { Plan = dup }, [], 40f);
        Assert.NotEmpty(m.Batches);

        ExportStepResult exp = PipelineSteps.Export(imp, opt, log);
        PreviewModel b = PreviewBuilder.FromBsp(exp.Bsp, new Dictionary<string, PreviewTexture>(), [first, first with { Texture = "autre" }]);
        Assert.NotEmpty(b.Batches);
    }

    [Fact]
    public void FromBsp_short_vertex_arrays_fall_back()
    {
        var s = new BspSurface("textures/kotor/x", 1, [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [], [new(0.5f, 0.5f)], [],
            [], [0, 1, 2]);
        var bsp = new BspFile([], [s], "", new BspBrushInfo(0, 0, 0));
        PreviewModel m = PreviewBuilder.FromBsp(bsp, new Dictionary<string, PreviewTexture>());
        PreviewVertex[] v = Assert.Single(m.Batches).Vertices;
        Assert.Equal(3, v.Length);
        Assert.Equal(Vector3.UnitZ, v[1].Normal);
        Assert.Equal(new Vector2(0.5f, 0.5f), v[0].Uv0);
        Assert.Equal(Vector2.Zero, v[2].Uv0);
    }

    // Enroulement : le compilateur ecrit v0,v2,v1 (JA) ; la vue doit retrouver la convention KOTOR (face avant = antihoraire,
    // normale geometrique (b-a)x(c-a) dans le sens de la normale du sommet), comme pour la vue Importer.
    [Fact]
    public void FromBsp_triangles_are_counter_clockwise_like_import_view()
    {
        var opt = Opt();
        var log = new ImportLog();
        ImportStepResult imp = PipelineSteps.Import(Scene(), "demo", opt, log);
        ExportStepResult exp = PipelineSteps.Export(imp, opt, log);
        PreviewModel bsp = PreviewBuilder.FromBsp(exp.Bsp, new Dictionary<string, PreviewTexture>());
        PreviewModel imported = PreviewBuilder.FromImport(imp, [], 40f);

        static (int agree, int total) Agreement(PreviewModel m)
        {
            int agree = 0, total = 0;
            foreach (PreviewBatch b in m.Batches.Where(b => !b.IsSky))
                for (int i = 0; i + 2 < b.Indices.Length; i += 3)
                {
                    PreviewVertex a = b.Vertices[b.Indices[i]], v1 = b.Vertices[b.Indices[i + 1]], c = b.Vertices[b.Indices[i + 2]];
                    Vector3 n = Vector3.Cross(v1.Position - a.Position, c.Position - a.Position);
                    if (n.LengthSquared() < 1e-6f) continue;
                    total++;
                    if (Vector3.Dot(n, a.Normal + v1.Normal + c.Normal) > 0) agree++;
                }
            return (agree, total);
        }
        (int ba, int bt) = Agreement(bsp);
        (int ia, int it) = Agreement(imported);
        Assert.True(bt > 0 && it > 0);
        Assert.Equal(it, ia);   // la vue Importer est la reference
        Assert.Equal(bt, ba);   // la vue BSP suit la meme convention
    }}
