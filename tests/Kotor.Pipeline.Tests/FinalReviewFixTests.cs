using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Pipeline.Workflow;

namespace Kotor.Pipeline.Tests;

public sealed class FinalReviewFixTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "k2j_fix_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private ExportOptions Opt(bool createBase = true, string limits = "swgl")
    {
        var o = new ExportOptions("k", Path.Combine(_root, "base"), Path.Combine(_root, "work"), BspLimits: limits);
        if (createBase) Directory.CreateDirectory(o.JaBaseDir);
        return o;
    }

    private static ModuleScene Scene(int walkTriangles = 2, bool walkable = true)
    {
        var mesh = new SceneMesh("r", "n", "t", null, TxiInfo.Empty, 0,
            [new(1, 2, 3), new(11, 2, 3), new(11, 12, 3), new(1, 12, 3)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [new(0, 0), new(1, 0), new(1, 1), new(0, 1)], null, [0, 1, 2, 0, 2, 3]);
        var pos = new Vector3[walkTriangles * 3];
        var idx = new int[walkTriangles * 3];
        for (int i = 0; i < walkTriangles; i++)
        {
            float x = (i % 200) * 5f, y = (i / 200) * 5f;
            pos[i * 3] = new(x, y, 0); pos[i * 3 + 1] = new(x + 1, y, 0); pos[i * 3 + 2] = new(x, y + 1, 0);
            idx[i * 3] = i * 3; idx[i * 3 + 1] = i * 3 + 1; idx[i * 3 + 2] = i * 3 + 2;
        }
        var floor = new SceneWalkmesh("r", pos, idx, Enumerable.Repeat(walkable, walkTriangles).ToArray());
        var tex = new Dictionary<string, KotorTexture?> { ["t"] = new KotorTexture(2, 2, new byte[16], false, TxiInfo.Empty) };
        return new ModuleScene(new ModuleInfo("tst", "tst", "Zone", new ModuleEntry(Vector3.Zero, Vector2.UnitX)), [mesh], [floor], tex);
    }

    [Theory]
    [InlineData("..")]
    [InlineData(@"..\..\x")]
    [InlineData("a/b")]
    [InlineData("two words")]
    [InlineData("café")]
    [InlineData("")]
    [InlineData("   ")]
    public void Import_rejects_invalid_map_names(string name)
    {
        var log = new ImportLog();
        var ex = Assert.Throws<PipelineStepException>(() => PipelineSteps.Import(Scene(), name, Opt(), log));
        Assert.Equal("MAP_NAME_INVALID", ex.Code);
        Assert.Contains(log.Warnings, w => w.Code == "MAP_NAME_INVALID");
    }

    [Fact]
    public void Import_rejects_too_long_name_and_accepts_the_limit()
    {
        Assert.Throws<PipelineStepException>(() => PipelineSteps.Import(Scene(), new string('a', 49), Opt(), new ImportLog()));
        Assert.Equal(new string('a', 48), PipelineSteps.Import(Scene(), new string('a', 48), Opt(), new ImportLog()).MapName);
    }

    [Fact]
    public void Import_normalises_case_then_validates()
    {
        Assert.Equal("my_map_2", PipelineSteps.Import(Scene(), "My_Map_2", Opt(), new ImportLog()).MapName);
    }

    [Fact]
    public void ModuleExporter_rejects_invalid_name_without_touching_disk()
    {
        var r = ModuleExporter.Export(Scene(), @"..\..\evil", Opt(), new ImportLog());
        Assert.False(r.Ok);
        Assert.Contains("Nom de carte invalide", r.Error);
        Assert.False(Directory.Exists(Path.Combine(_root, "work")));
    }

    [Fact]
    public void Export_and_CreatePk3_refuse_missing_ja_base_dir_and_create_nothing()
    {
        var opt = Opt(createBase: false);
        var log = new ImportLog();
        ImportStepResult imp = PipelineSteps.Import(Scene(), "demo", opt, log);

        var ex = Assert.Throws<PipelineStepException>(() => PipelineSteps.Export(imp, opt, log));
        Assert.Equal("JA_BASE_MISSING", ex.Code);
        Assert.Contains(log.Warnings, w => w.Code == "JA_BASE_MISSING");
        Assert.False(Directory.Exists(opt.JaBaseDir));
        Assert.False(Directory.Exists(opt.WorkDir));

        // CreatePk3 : staging fabrique avec un base existant, puis base supprime.
        var okOpt = Opt();
        ExportStepResult exp = PipelineSteps.Export(imp, okOpt, log);
        Directory.Delete(okOpt.JaBaseDir);
        ex = Assert.Throws<PipelineStepException>(() => PipelineSteps.CreatePk3(exp, imp, okOpt, log));
        Assert.Equal("JA_BASE_MISSING", ex.Code);
        Assert.False(Directory.Exists(okOpt.JaBaseDir));
    }

    [Fact]
    public void Export_view_warns_when_compiler_reduced_collision()
    {
        var opt = Opt(limits: "vanilla");
        var log = new ImportLog();
        ImportStepResult imp = PipelineSteps.Import(Scene(walkTriangles: 36000, walkable: false), "big", opt, log);
        PipelineSteps.Export(imp, opt, log);
        Assert.Contains(log.Warnings, w => w.Code is "COLLISION_CAPPED" or "WALKABLE_CAPPED");

        KotorWorkflowSteps.WarnIfCollisionReduced(log);

        Assert.Contains(log.Warnings, w => w.Code == "EXPORT_VIEW_COLLISION_STALE" && w.Message.Contains("collisions reduites"));
    }

    [Fact]
    public void Import_without_game_folder_logs_that_placeables_are_skipped()
    {
        var opt = Opt() with { KotorDir = Path.Combine(Path.GetTempPath(), "k2j_no_such_game_" + Guid.NewGuid().ToString("N")) };
        var log = new ImportLog();
        PipelineSteps.Import(Scene(), "nogame", opt, log);
        Assert.Contains(log.Warnings, w => w.Code == "PLACEABLES_NO_GAME_DIR" && w.Message.Contains("placeable conversion skipped"));
    }

    [Fact]
    public void Export_view_does_not_warn_without_reduction()
    {
        var log = new ImportLog();
        KotorWorkflowSteps.WarnIfCollisionReduced(log);
        Assert.Empty(log.Warnings);
    }
}
