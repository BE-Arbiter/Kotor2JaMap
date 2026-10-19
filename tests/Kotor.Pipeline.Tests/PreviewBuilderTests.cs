using System.Numerics;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Core;
using Kotor.Core.Models;
using Kotor.Pipeline;
using Kotor.Pipeline.Entities;
using Kotor.Pipeline.Workflow;
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

    private ExportOptions Opt()
    {
        var o = new ExportOptions("k", Path.Combine(_root, "base"), Path.Combine(_root, "work"));
        Directory.CreateDirectory(o.JaBaseDir);
        return o;
    }

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

    [Theory]
    [InlineData(KotorGame.Kotor1, false, "wall")]
    [InlineData(KotorGame.Kotor2, false, "wall")]
    [InlineData(KotorGame.Swtor, false, "wall")]
    [InlineData(KotorGame.Swtor, true, "wall")]
    public void KeyOf_strips_the_game_prefix(KotorGame game, bool lightmap, string name)
    {
        string rel = (lightmap ? ShaderNames.LightmapPrefixFor(game) : ShaderNames.PrefixFor(game)) + name + ".tga";
        Assert.Equal(name, PreviewBuilder.KeyOf(new ExportTexture(rel, null, lightmap)));
    }

    [Fact]
    public void SetSpawn_rewrites_the_player_starts_and_the_view()
    {
        var steps = new KotorWorkflowSteps(Opt());
        steps.UseScene(Scene(), Opt(), "demo");
        steps.Import(new Progress<string>());

        PreviewModel view = steps.SetSpawn(new Vector3(100, 200, 300), 45f);
        Assert.Equal(new Vector3(100, 200, 300), view.Spawn);
        Assert.Equal(45f, view.SpawnYaw);

        // Un deuxieme import n'est pas necessaire : l'export lit le plan modifie.
        var again = steps.SetSpawn(new Vector3(1.5f, 2, 3), 10f);
        Assert.Equal(new Vector3(1.5f, 2, 3), again.Spawn);
    }

    [Fact]
    public void SetSpawn_requires_an_import()
    {
        var steps = new KotorWorkflowSteps(Opt());
        steps.UseScene(Scene(), Opt(), "demo");
        Assert.Throws<InvalidOperationException>(() => steps.SetSpawn(Vector3.Zero, 0f));
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
    }
    private static Md3Asset TriangleMd3(string path)
    {
        var part = new ModelPart("tex_a", [new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [new(0, 0), new(1, 0), new(0, 1)], [0, 1, 2]);
        byte[] data = Md3Writer.Write("tri", [part], "models/kotor/");
        return new Md3Asset(path, data, ["tex_a"]);
    }

    private static Dictionary<string, string> Ent(string cls, string key, string model, string angles, string origin, string anglesKey) => new()
    {
        ["classname"] = cls, [key] = model, ["origin"] = origin, [anglesKey] = angles,
    };

    [Fact]
    public void Import_view_has_model_instances()
    {
        ImportStepResult imp = PipelineSteps.Import(Scene(), "demo", Opt(), new ImportLog());
        var tex = new KotorTexture(2, 2, new byte[16], false, TxiInfo.Empty);
        var converted = new ConvertedEntities(
            [Ent("misc_model_static", "model", "models/kotor/tri.md3", "0 90 0", "100 200 300", "angles")],
            [TriangleMd3("models/kotor/tri.md3")], [])
        {
            TextureData = new Dictionary<string, KotorTexture?> { ["tex_a"] = tex },
        };
        ModuleEntity[] ents = [new(EntityKind.Placeable, "crate", "p_crate", new Vector3(100f / 40, 200f / 40, 300f / 40), 90f, null),
            new(EntityKind.Waypoint, "wp", "", Vector3.Zero, 0f, null)];
        PreviewModel m = PreviewBuilder.FromImport(imp with { Converted = converted }, ents, 40f);

        Assert.Single(m.Models);
        PreviewBatch b = Assert.Single(m.Models["models/kotor/tri.md3"]);
        Assert.Equal("md3:tex_a", b.DiffuseKey);
        Assert.Contains("md3:tex_a", m.Textures.Keys);
        PreviewModelInstance inst = Assert.Single(m.Instances);
        Assert.Equal(EntityKind.Placeable, inst.Kind);
        Assert.Equal(new Vector3(100, 200, 300), inst.Transform.Translation);
        // Lacet 90 degres : +X du modele devient +Y.
        Vector3 x = Vector3.TransformNormal(Vector3.UnitX, inst.Transform);
        Assert.Equal(0f, x.X, 3);
        Assert.Equal(1f, x.Y, 3);
        // Le cube de l'objet est remplace par le maillage ; les autres reperes restent.
        PreviewMarker marker = Assert.Single(m.Markers);
        Assert.Equal(EntityKind.Waypoint, marker.Kind);
    }

    [Fact]
    public void Import_view_without_converted_has_no_instances()
    {
        ImportStepResult imp = PipelineSteps.Import(Scene(), "demo", Opt(), new ImportLog());
        PreviewModel m = PreviewBuilder.FromImport(imp, [], 40f);
        Assert.Empty(m.Instances);
        Assert.Empty(m.Models);
    }

    [Fact]
    public void Md3Reader_round_trips_writer_output()
    {
        Md3Asset a = TriangleMd3("x.md3");
        Md3Surface s = Assert.Single(Md3Reader.Read(a.Data));
        Assert.Equal("models/kotor/tex_a", s.Shader);
        Assert.Equal(new Vector3(10, 0, 0), s.Positions[1]);
        Assert.Equal(1f, s.Normals[0].Z, 2);
        Assert.Equal(new Vector2(0, 1), s.Uv[0]);   // t = 1 - v dans le fichier
        Assert.Equal(new[] { 0, 1, 2 }, s.Indices);
        Assert.Throws<InvalidDataException>(() => Md3Reader.Read(new byte[200]));
    }

    [Fact]
    public void Md3Reader_restores_KOTOR_winding_after_write()
    {
        var part = new ModelPart("tex_a", [new(0, 0, 0), new(10, 0, 0), new(0, 10, 0), new(10, 10, 0)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [new(0, 0), new(1, 0), new(0, 1), new(1, 1)], [0, 1, 2, 1, 3, 2]);
        Md3Surface s = Assert.Single(Md3Reader.Read(Md3Writer.Write("q", [part], "models/kotor/")));
        Assert.Equal(new[] { 0, 1, 2, 1, 3, 2 }, s.Indices);
    }

    [SkippableFact]
    public void Real_tar_m02aa_export_view_has_md3()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR n'est pas installe.");
        var opt = new ExportOptions(TestEnv.KotorDir!, Path.Combine(_root, "base"), Path.Combine(_root, "work"));
        Directory.CreateDirectory(opt.JaBaseDir);
        var steps = new KotorWorkflowSteps(opt);
        var progress = new Progress<string>();
        steps.Preview(KotorGame.Kotor1, "tar_m02aa", progress);
        PreviewModel imported = steps.Import(progress);
        PreviewModel exported = steps.Export(progress);

        Assert.True(exported.Instances.Count >= 10, $"{exported.Instances.Count} instance(s)");
        Assert.Contains(exported.Instances, i => i.Kind == EntityKind.Door);
        Assert.All(exported.Instances, i => Assert.Contains(i.ModelKey, exported.Models.Keys));
        Assert.True(imported.Instances.Count >= 10);
        Assert.DoesNotContain(imported.Markers, mk => mk.Kind is EntityKind.Creature or EntityKind.Encounter);
    }
}
