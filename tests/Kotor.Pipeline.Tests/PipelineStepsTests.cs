using System.IO.Compression;
using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Pipeline;

namespace Kotor.Pipeline.Tests;

public sealed class PipelineStepsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "k2j_pipe_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private ExportOptions Opt(string? kotor = null) => new(kotor ?? "k", Path.Combine(_root, "base"), Path.Combine(_root, "work"));

    private static ModuleScene SmallScene()
    {
        var mesh = new SceneMesh("r", "n", "t", null, TxiInfo.Empty, 0,
            [new(1, 2, 3), new(11, 2, 3), new(11, 12, 3), new(1, 12, 3)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [new(0, 0), new(1, 0), new(1, 1), new(0, 1)], null, [0, 1, 2, 0, 2, 3]);
        var floor = new SceneWalkmesh("r", [new(-5, -5, 0), new(5, -5, 0), new(5, 5, 0), new(-5, 5, 0)], [0, 1, 2, 0, 2, 3], [true, true]);
        var tex = new Dictionary<string, KotorTexture?> { ["t"] = new KotorTexture(2, 2, new byte[16], false, TxiInfo.Empty) };
        return new ModuleScene(new ModuleInfo("tst", "tst", "Zone", new ModuleEntry(Vector3.Zero, Vector2.UnitX)), [mesh], [floor], tex);
    }

    [Fact]
    public void Steps_produce_pk3_with_readable_bsp()
    {
        var opt = Opt();
        var log = new ImportLog();
        ImportStepResult imp = PipelineSteps.Import(SmallScene(), "Demo", opt, log);
        Assert.Equal("demo", imp.MapName);
        Assert.False(Directory.Exists(opt.WorkDir)); // Import n'ecrit rien

        ExportStepResult exp = PipelineSteps.Export(imp, opt, log);
        Assert.True(File.Exists(exp.BspPath));
        Assert.Contains("info_player_start", exp.Bsp.Entities);
        Assert.Contains(exp.Bsp.Surfaces, s => s.Shader == "textures/kotor/t");
        Assert.True(File.Exists(Path.Combine(exp.StagingDir, "shaders", "kotor_demo.shader")));
        Assert.False(File.Exists(Path.Combine(opt.JaBaseDir, "kotor_demo.pk3"))); // pas encore de pk3

        Pk3StepResult pk3 = PipelineSteps.CreatePk3(exp, imp, opt, log);
        Assert.Equal(Path.Combine(opt.JaBaseDir, "kotor_demo.pk3"), pk3.Pk3Path);
        using var z = ZipFile.OpenRead(pk3.Pk3Path);
        Assert.Contains(z.Entries, e => e.FullName == "maps/demo.bsp");
    }

    [Fact]
    public void ReadBack_of_real_compile_matches_plan()
    {
        var opt = Opt();
        var log = new ImportLog();
        ImportStepResult imp = PipelineSteps.Import(SmallScene(), "rb", opt, log);
        ExportStepResult exp = PipelineSteps.Export(imp, opt, log);
        BspFile again = PipelineSteps.ReadBack(File.ReadAllBytes(exp.BspPath), imp.Plan, log);
        Assert.Equal(imp.Plan.Input.Surfaces.Sum(s => s.Indices.Length) + SkyBox.IndexCount, again.Surfaces.Sum(s => s.Indices.Length));
    }

    [Fact]
    public void ReadBack_fails_when_bsp_is_empty_or_inconsistent_with_plan()
    {
        var opt = Opt();
        var log = new ImportLog();
        ImportStepResult imp = PipelineSteps.Import(SmallScene(), "rb2", opt, log);
        ExportStepResult exp = PipelineSteps.Export(imp, opt, log);
        byte[] good = File.ReadAllBytes(exp.BspPath);

        // Lump surfaces vide : lisible par BspReader, mais incoherent avec le plan.
        byte[] empty = (byte[])good.Clone();
        BitConverter.GetBytes(0).CopyTo(empty, 12 + BspWriter.LumpSurfaces * 8);
        var ex = Assert.Throws<PipelineStepException>(() => PipelineSteps.ReadBack(empty, imp.Plan, log));
        Assert.Equal("BSP_READBACK_FAILED", ex.Code);

        // Surface sans index : refusee par BspReader.
        byte[] noIdx = (byte[])good.Clone();
        BitConverter.GetBytes(0).CopyTo(noIdx, BitConverter.ToInt32(noIdx, 8 + BspWriter.LumpSurfaces * 8) + 24);
        ex = Assert.Throws<PipelineStepException>(() => PipelineSteps.ReadBack(noIdx, imp.Plan, log));
        Assert.Equal("BSP_READBACK_FAILED", ex.Code);

        // Nombre total d'index different du plan.
        ImportStepResult tooMany = imp with
        {
            Plan = imp.Plan with
            {
                Input = imp.Plan.Input with
                {
                    Surfaces = [.. imp.Plan.Input.Surfaces, .. imp.Plan.Input.Surfaces]
                }
            }
        };
        ex = Assert.Throws<PipelineStepException>(() => PipelineSteps.ReadBack(good, tooMany.Plan, log));
        Assert.Equal("BSP_READBACK_FAILED", ex.Code);
        Assert.Contains(log.Warnings, w => w.Code == "BSP_READBACK_FAILED");
    }

    [Fact]
    public void CreatePk3_locked_target_fails_cleanly()
    {
        var opt = Opt();
        var log = new ImportLog();
        ImportStepResult imp = PipelineSteps.Import(SmallScene(), "locked", opt, log);
        ExportStepResult exp = PipelineSteps.Export(imp, opt, log);
        Directory.CreateDirectory(opt.JaBaseDir);
        string pk3 = Path.Combine(opt.JaBaseDir, "kotor_locked.pk3");
        byte[] original = [1, 2, 3, 4, 5];
        File.WriteAllBytes(pk3, original);

        using (var hold = new FileStream(pk3, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var ex = Assert.Throws<PipelineStepException>(() => PipelineSteps.CreatePk3(exp, imp, opt, log));
            Assert.Equal("EXPORT_IO_ERROR", ex.Code);
        }
        Assert.Contains(log.Warnings, w => w.Code == "EXPORT_IO_ERROR");
        Assert.Equal(original, File.ReadAllBytes(pk3));
    }

    [SkippableFact]
    public void Steps_chain_equals_ModuleExporter()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        var optA = new ExportOptions(TestEnv.KotorDir!, Path.Combine(_root, "baseA"), Path.Combine(_root, "workA"));
        var optB = new ExportOptions(TestEnv.KotorDir!, Path.Combine(_root, "baseB"), Path.Combine(_root, "workB"));

        ExportResult r = ModuleExporter.Export("tar_m02aa", null, optA, new ImportLog());
        Assert.True(r.Ok, r.Error);

        var log = new ImportLog();
        ModuleScene scene = PipelineSteps.Preview("tar_m02aa", optB, log);
        ImportStepResult imp = PipelineSteps.Import(scene, null, optB, log);
        ExportStepResult exp = PipelineSteps.Export(imp, optB, log);
        Pk3StepResult pk3 = PipelineSteps.CreatePk3(exp, imp, optB, log);

        using var za = ZipFile.OpenRead(r.Pk3Path!);
        using var zb = ZipFile.OpenRead(pk3.Pk3Path);
        Assert.Equal(za.Entries.Select(e => e.FullName).OrderBy(x => x, StringComparer.Ordinal),
            zb.Entries.Select(e => e.FullName).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(Bytes(za, "maps/tar_m02aa.bsp"), Bytes(zb, "maps/tar_m02aa.bsp"));
        Assert.Equal(Bytes(za, "maps/tar_m02aa.bsp"), File.ReadAllBytes(exp.BspPath));
        Assert.NotEmpty(exp.Bsp.Surfaces);
    }

    private static byte[] Bytes(ZipArchive z, string name)
    {
        using var s = z.GetEntry(name)!.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
