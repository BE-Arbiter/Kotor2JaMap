using System.IO.Compression;
using System.Numerics;
using Kotor.Core.Diagnostics;
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

    private ExportOptions Opt() => new("k", Path.Combine(_root, "base"), Path.Combine(_root, "work"));

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
        var plan = ModuleExporter.BuildPlan(scene, null, Opt(), new ImportLog());
        var s = Assert.Single(plan.Input.Surfaces);
        Assert.Equal(new Vector3(36, 72, 108), s.Positions[0]);
        Assert.Equal(Vector3.UnitZ, s.Normals[0]);
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
        var plan = ModuleExporter.BuildPlan(scene, null, Opt(), log);
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
            Assert.Contains("scripts/kotor_mymap.shader", names);
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
}
