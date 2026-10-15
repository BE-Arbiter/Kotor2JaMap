using System.IO.Compression;
using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Pipeline;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Core.Tests.Export;

public sealed class ModuleExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "k2j_exp_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private ExportOptions Opt()
    {
        var o = new ExportOptions("k", Path.Combine(_root, "base"), Path.Combine(_root, "work"));
        Directory.CreateDirectory(o.JaBaseDir);
        return o;
    }

    private static ModuleInfo Info(Vector3 pos, Vector2 dir) => new("tst", "tst", "Zone", new ModuleEntry(pos, dir));

    private static SceneMesh Quad(string tex, string? lm, Vector2[]? uv0 = null, Vector2[]? uv1 = null) =>
        new("r", "n", tex, lm, TxiInfo.Empty, 0,
            [new(1, 2, 3), new(11, 2, 3), new(11, 12, 3), new(1, 12, 3)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            uv0 ?? [new(0, 0), new(1, 0), new(1, 1), new(0, 1)], uv1 ?? (lm is null ? null : [new(0, 0), new(1, 0), new(1, 1), new(0, 1)]),
            [0, 1, 2, 0, 2, 3]);

    private static KotorTexture Tex() => new(2, 2, new byte[16], false, TxiInfo.Empty);

    private static SceneWalkmesh Floor() =>
        new("r", [new(-5, -5, 0), new(5, -5, 0), new(5, 5, 0), new(-5, 5, 0)], [0, 1, 2, 0, 2, 3], [true, true]);

    private static ModuleScene Scene(IReadOnlyList<SceneMesh> meshes, string[] textures, IReadOnlyList<SceneWalkmesh>? walk = null,
        Vector3? entry = null, Vector2? dir = null)
    {
        var d = new Dictionary<string, KotorTexture?>();
        foreach (string t in textures) d[t] = Tex();
        return new ModuleScene(Info(entry ?? Vector3.Zero, dir ?? Vector2.UnitX), meshes, walk ?? [Floor()], d);
    }

    [Fact]
    public void Scale_is_applied_to_positions_not_normals()
    {
        var scene = Scene([Quad("t", null)], ["t"]);
        var plan = ModuleExporter.BuildPlan(scene, null, Opt() with { Scale = 36f }, new ImportLog());
        var s = Assert.Single(plan.Input.Surfaces);
        Assert.Equal(new Vector3(36, 72, 108), s.Positions[0]);
        Assert.Equal(Vector3.UnitZ, s.Normals[0]);
    }

    [Fact]
    public void Lights_position_and_radius_are_scaled_and_ambient_comes_from_the_area()
    {
        var scene = Scene([Quad("t", null)], ["t"]);
        scene = new ModuleScene(scene.Info, scene.Meshes, scene.Walkmeshes, scene.Textures)
        {
            Lights =
            [
                new SceneLight("a", new Vector3(1, 2, 3), new Vector3(0.6f, 0.9f, 1f), 2.5f, 2f),
                new SceneLight("amb", new Vector3(4, 5, 6), new Vector3(0.1f), 8f, 1.5f, AmbientOnly: true),
                new SceneLight("zero", Vector3.Zero, Vector3.One, 0f, 1f),                    // filtered out
                new SceneLight("nan", Vector3.Zero, Vector3.One, float.NaN, 1f),              // filtered out
                new SceneLight("neg", Vector3.Zero, Vector3.One, 3f, -1f),                    // filtered out
            ],
            AmbientColor = new Vector3(0.3f, 0.4f, 0.5f),
            SunDiffuseColor = new Vector3(0.9f),
            AreaLightingKnown = true,
        };
        var plan = ModuleExporter.BuildPlan(scene, null, Opt() with { Scale = 36f }, new ImportLog());
        Assert.Equal(2, plan.Input.Lights!.Count);
        var a = plan.Input.Lights[0];
        Assert.Equal(new Vector3(36, 72, 108), a.Position);
        Assert.Equal(90f, a.Radius);                         // 2.5 m x 36
        Assert.Equal(2f, a.Multiplier);                      // the multiplier is not a length
        Assert.Equal(new Vector3(0.6f, 0.9f, 1f), a.Color);
        Assert.False(a.AmbientOnly);
        var amb = plan.Input.Lights[1];
        Assert.Equal(new Vector3(144, 180, 216), amb.Position);
        Assert.Equal(288f, amb.Radius);
        Assert.True(amb.AmbientOnly);
        // Lights present: ambient of the area, no sun in the compiled lighting.
        Assert.Equal(new Vector3(0.3f, 0.4f, 0.5f), plan.Input.Lighting.Ambient);
        var eff = Kotor.Compiler.Compiler.EffectiveLighting(plan.Input);
        Assert.Equal(Vector3.Zero, eff.Sun);
        Assert.Equal(new Vector3(0.3f, 0.4f, 0.5f), eff.Ambient);
    }

    [Fact]
    public void Module_without_light_node_uses_area_ambient_and_sun_diffuse()
    {
        var s0 = Scene([Quad("t", null)], ["t"]);
        var scene = new ModuleScene(s0.Info, s0.Meshes, s0.Walkmeshes, s0.Textures)
        {
            AmbientColor = new Vector3(0.3f, 0.4f, 0.5f),
            SunDiffuseColor = new Vector3(0.9f, 0.8f, 0.7f),
            AreaLightingKnown = true,
        };
        var plan = ModuleExporter.BuildPlan(scene, null, Opt(), new ImportLog());
        Assert.Empty(plan.Input.Lights!);
        var l = Kotor.Compiler.Compiler.EffectiveLighting(plan.Input);
        Assert.Equal(new Vector3(0.3f, 0.4f, 0.5f), l.Ambient);
        Assert.Equal(new Vector3(0.9f, 0.8f, 0.7f), l.Sun);
        Assert.Equal(Kotor.Compiler.Bsp.VertexLighting.Default.SunDir, l.SunDir);   // default direction
        Assert.Equal(0, l.OverbrightShift);
    }

    [Fact]
    public void Module_without_light_node_and_unreadable_area_keeps_the_compiler_defaults()
    {
        var scene = Scene([Quad("t", null)], ["t"]);   // AreaLightingKnown = false
        var plan = ModuleExporter.BuildPlan(scene, null, Opt(), new ImportLog());
        Assert.Equal(Kotor.Compiler.Bsp.VertexLighting.Default, Kotor.Compiler.Compiler.EffectiveLighting(plan.Input));
    }

    [Fact]
    public void Uv_v_is_flipped_for_both_channels()
    {
        var scene = Scene([Quad("t", "l", [new(0.25f, 0.25f), new(1, 0), new(1, 1), new(0, 1)],
            [new(0.5f, 0.1f), new(1, 0), new(1, 1), new(0, 1)])], ["t", "l"]);
        var s = Assert.Single(ModuleExporter.BuildPlan(scene, null, Opt(), new ImportLog()).Input.Surfaces);
        Assert.Equal(new Vector2(0.25f, 0.75f), s.Uv0[0]);
        Assert.Equal(new Vector2(1, 1), s.Uv0[1]);
        Assert.Equal(new Vector2(0.5f, 0.9f), s.Uv1![0]);
    }

    [Fact]
    public void Double_sided_only_for_cull_none_kinds()
    {
        var add = Quad("t", null) with { Txi = TxiInfo.Parse("blending additive") };
        var plan = ModuleExporter.BuildPlan(Scene([add, Quad("u", null)], ["t", "u"]), null, Opt(), new ImportLog());
        Assert.True(plan.Input.Surfaces[0].DoubleSided);
        Assert.False(plan.Input.Surfaces[1].DoubleSided);
    }

    [Fact]
    public void Spawn_uses_entry_and_yaw()
    {
        var scene = Scene([Quad("t", null)], ["t"], entry: new Vector3(1, 0, 0), dir: new Vector2(0, 1));
        var log = new ImportLog();
        var plan = ModuleExporter.BuildPlan(scene, null, Opt() with { Scale = 36f }, log);
        var spawn = plan.Input.Entities.Single(e => e["classname"] == "info_player_start");
        Assert.Equal("36 0 25", spawn["origin"]);
        Assert.Equal("90", spawn["angle"]);
        Assert.DoesNotContain(log.Warnings, w => w.Code == "SPAWN_FALLBACK");
        var world = plan.Input.Entities[0];
        Assert.Equal("worldspawn", world["classname"]);
        Assert.Equal("Zone", world["message"]);
        Assert.Equal("tst", world["_kotor_module"]);
    }

    [Fact]
    public void Spawn_falls_back_above_nearest_walkmesh()
    {
        var scene = Scene([Quad("t", null)], ["t"], entry: new Vector3(100, 0, 0));
        var log = new ImportLog();
        var plan = ModuleExporter.BuildPlan(scene, null, Opt(), log);
        var spawn = plan.Input.Entities.Single(e => e["classname"] == "info_player_start");
        Assert.Equal("0 0 25", spawn["origin"]);
        Assert.Contains(log.Warnings, w => w.Code == "SPAWN_FALLBACK");
    }

    [Fact]
    public void Missing_texture_uses_missing_tga_and_missing_lightmap_goes_unlit()
    {
        var scene = Scene([Quad("gone", "lm_gone")], []);
        var log = new ImportLog();
        var plan = ModuleExporter.BuildPlan(scene, null, Opt(), log);
        Assert.Equal("textures/kotor/missing", plan.Input.Surfaces[0].Shader);
        Assert.Equal(1, plan.MissingTextures);
        Assert.Contains(log.Warnings, w => w.Code == "LIGHTMAP_MISSING");
    }

    [Fact]
    public void Too_many_shaders_fails_cleanly()
    {
        var meshes = Enumerable.Range(0, 1025).Select(i => Quad("t" + i, null)).ToList();
        var scene = Scene(meshes, Enumerable.Range(0, 1025).Select(i => "t" + i).ToArray());
        var log = new ImportLog();
        var r = ModuleExporter.Export(scene, "big", Opt(), log);
        Assert.False(r.Ok);
        Assert.Null(r.Pk3Path);
        Assert.Contains(log.Warnings, w => w.Code == "SHADER_LIMIT");
        Assert.False(File.Exists(Path.Combine(_root, "base", "kotor_big.pk3")));
    }

    [Fact]
    public void Synthetic_export_writes_pk3_with_expected_files_and_journal()
    {
        var scene = Scene([Quad("t", "l")], ["t", "l"]);
        var log = new ImportLog();
        var r = ModuleExporter.Export(scene, "MyMap", Opt(), log);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(Path.Combine(_root, "base", "kotor_mymap.pk3"), r.Pk3Path);
        using (var z = ZipFile.OpenRead(r.Pk3Path!))
        {
            var names = z.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("maps/mymap.bsp", names);
            Assert.Contains("shaders/kotor_mymap.shader", names);
            Assert.Contains("textures/kotor/t.tga", names);
            Assert.Contains("textures/kotor/lm/l.tga", names);
        }
        Assert.True(File.Exists(Path.Combine(_root, "work", "mymap", "mymap.journal.txt")));
        Assert.Equal(1, r.Meshes);
        Assert.Equal(2, r.Triangles);
    }

    [SkippableFact]
    public void Real_export_tar_m02aa()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        var opt = new ExportOptions(TestEnv.KotorDir!, Path.Combine(_root, "base"), Path.Combine(_root, "work"));
        Directory.CreateDirectory(opt.JaBaseDir);
        var log = new ImportLog();
        var r = ModuleExporter.Export("tar_m02aa", null, opt, log);
        Assert.True(r.Ok, r.Error);
        Assert.True(File.Exists(r.Pk3Path));
        using var z = ZipFile.OpenRead(r.Pk3Path!);
        Assert.Contains(z.Entries, e => e.FullName == "maps/tar_m02aa.bsp");
        int tgas = z.Entries.Count(e => e.FullName.StartsWith("textures/kotor/") && !e.FullName.Contains("/lm/") && e.FullName.EndsWith(".tga"));
        Assert.True(tgas >= 1);
        Assert.True(File.Exists(Path.Combine(_root, "work", "tar_m02aa", "tar_m02aa.journal.txt")));
    }

    [Fact]
    public void Invalid_scale_gives_error_result()
    {
        var scene = Scene([Quad("t", "l")], ["t", "l"]);
        var r = ModuleExporter.Export(scene, "bad", Opt() with { Scale = 0f }, new ImportLog());
        Assert.False(r.Ok);
        Assert.Contains("Invalid scale", r.Error);
        Assert.Throws<ArgumentException>(() => ModuleExporter.BuildPlan(scene, null, Opt() with { Scale = float.NaN }, new ImportLog()));
    }

    [Fact]
    public void Journal_failure_is_reported_but_export_succeeds()
    {
        var scene = Scene([Quad("t", "l")], ["t", "l"]);
        Directory.CreateDirectory(Path.Combine(_root, "work", "jf", "jf.journal.txt")); // bloque l'ecriture du journal
        var log = new ImportLog();
        var r = ModuleExporter.Export(scene, "jf", Opt(), log);
        Assert.True(r.Ok, r.Error);
        Assert.NotNull(r.JournalError);
        Assert.Contains(log.Warnings, w => w.Code == "JOURNAL_WRITE_FAILED");
    }

    [Fact]
    public void Vanilla_profile_is_accepted_by_exporter()
    {
        var scene = Scene([Quad("t", "l")], ["t", "l"]);
        var r = ModuleExporter.Export(scene, "van", Opt() with { BspLimits = "vanilla" }, new ImportLog());
        Assert.True(r.Ok, r.Error);
    }
}
