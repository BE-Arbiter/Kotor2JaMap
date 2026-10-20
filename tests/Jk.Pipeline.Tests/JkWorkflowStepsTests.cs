using System.Text;
using Jk.Core;
using Kotor.Core;
using Kotor.Core.Export;
using Kotor.Pipeline;
using Kotor.Pipeline.Preview;
using Kotor.Pipeline.Workflow;
using Swtor.Core;
using Swtor.Pipeline;

namespace Jk.Pipeline.Tests;

public class JkWorkflowStepsTests
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";

    private sealed class Log : IProgress<string>
    {
        public List<string> Lines { get; } = [];
        public void Report(string value) { lock (Lines) Lines.Add(value); }
    }

    private static string? RealDir() => File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB")) ? JkDir : null;

    private static string Temp()
    {
        string d = Path.Combine(Path.GetTempPath(), "jkwf_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static ExportOptions Options(string? jkDir, string work) =>
        new("", Path.Combine(work, "base"), Path.Combine(work, "work")) { JediKnightDir = jkDir };

    private static void WriteGob(string dir, string sub, string name, params (string Path, string Content)[] files) =>
        Jk.Core.Tests.GobBuilder.WriteGob(dir, sub, name, files);   // source partagee avec Jk.Core.Tests (voir le csproj)

    private static string FakeInstall(string root)
    {
        string jk = Path.Combine(root, "jk");
        WriteGob(jk, "Episode", "JK1.GOB", (@"jkl\01narshadda.jkl", "x"), (@"jkl\02narshadda.jkl", "x"),
            ("episode.jk", "\"S\"\nTYPE 1\n10: 1 1 LEVEL 01narshadda.jkl 0 0 20 -1\n20: 1 2 LEVEL 02narshadda.jkl 0 0 -1 -1\n"),
            (@"misc\cogstrings.uni", "MSGS 1\n\"01NARSHADDA\" 0 \"1 - Betrayal\"\n"));
        WriteGob(jk, "Episode", "JK1MP.GOB", (@"jkl\m2.jkl", "SECTION: GEORESOURCE\nWorld vertices abc\n"));
        WriteGob(jk, "Episode", "JK1CTF.GOB", (@"jkl\c1.jkl", "x"));
        return jk;
    }

    [Fact]
    public void IsInstalled_IsCachedPerInstance_SoTheRenderThreadDoesNoDiskAccess()
    {
        string root = Temp();
        try
        {
            string jk = FakeInstall(root);
            var steps = new JkWorkflowSteps(Options(jk, root));
            Assert.True(steps.IsInstalled(KotorGame.JediKnight));
            File.Delete(Path.Combine(jk, "Episode", "JK1.GOB"));
            Assert.True(steps.IsInstalled(KotorGame.JediKnight));                            // memorise : pas de nouvel acces disque
            Assert.False(new JkWorkflowSteps(Options(jk, root)).IsInstalled(KotorGame.JediKnight));   // une nouvelle instance (Settings) relit
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Modules_SyntheticInstall_NamedAndGroupedByKind()
    {
        string root = Temp();
        try
        {
            var steps = new JkWorkflowSteps(Options(FakeInstall(root), root));
            Assert.True(steps.IsInstalled(KotorGame.JediKnight));
            var modules = steps.Modules();
            Assert.Equal(["01narshadda", "02narshadda", "m2", "c1"], modules.Select(m => m.Module));
            Assert.All(modules, m => Assert.Equal(KotorGame.JediKnight, m.Game));
            Assert.Equal("1 - Betrayal", modules[0].AreaName);
            Assert.Equal("02narshadda", modules[1].AreaName);   // sans chaine .uni : nom du fichier
            Assert.Equal(JkLevelKind.SinglePlayer, steps.KindOf("01narshadda"));
            Assert.Equal(JkLevelKind.Multiplayer, steps.KindOf("M2"));
            Assert.Equal(JkLevelKind.Ctf, steps.KindOf("c1"));
            Assert.Null(steps.KindOf("nope"));
            Assert.Equal("Single player", JkWorkflowSteps.GroupLabel(JkLevelKind.SinglePlayer));
            Assert.Equal("Multiplayer", JkWorkflowSteps.GroupLabel(JkLevelKind.Multiplayer));
            Assert.Equal("CTF", JkWorkflowSteps.GroupLabel(JkLevelKind.Ctf));
        }
        finally { Directory.Delete(root, true); }
    }

    [SkippableFact]
    public void Modules_34Levels_GroupedByKind()
    {
        Skip.If(RealDir() is null, "Jedi Knight not installed");
        string root = Temp();
        try
        {
            var steps = new JkWorkflowSteps(Options(JkDir, root));
            var modules = steps.Modules();
            Assert.Equal(34, modules.Count);
            Assert.Equal(25, modules.Count(m => steps.KindOf(m.Module) == JkLevelKind.SinglePlayer));
            Assert.Equal(6, modules.Count(m => steps.KindOf(m.Module) == JkLevelKind.Multiplayer));
            Assert.Equal(3, modules.Count(m => steps.KindOf(m.Module) == JkLevelKind.Ctf));
            Assert.Equal("01narshadda", modules[0].Module);   // ordre de episode.jk
            Assert.NotEqual("01narshadda", modules[0].AreaName);   // nom lisible du .uni
            Assert.Equal(34, modules.Select(m => m.Module).Distinct().Count());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void NotInstalled_NodeGreyed()
    {
        string root = Temp();
        try
        {
            foreach (string? dir in new string?[] { null, "", Path.Combine(root, "absent"), root })
            {
                var steps = new JkWorkflowSteps(Options(dir, root));
                Assert.False(steps.IsInstalled(KotorGame.JediKnight));
                Assert.Empty(steps.Modules());
                var composite = new GameWorkflowSteps(new KotorWorkflowSteps(Options(dir, root)), new SwtorWorkflowSteps(Options(dir, root), new SwtorAssets(Path.Combine(root, "x"))), steps);
                Assert.False(composite.IsInstalled(KotorGame.JediKnight));   // le nœud de l'arbre est grise
                Assert.False(composite.IsInstalled(KotorGame.Kotor1));       // les autres jeux gardent leur propre test
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static GameWorkflowSteps Composite(ExportOptions opt, JkWorkflowSteps jk, string root) =>
        new(new KotorWorkflowSteps(opt), new SwtorWorkflowSteps(opt, new SwtorAssets(Path.Combine(root, "swtor"))), jk);

    [Fact]
    public void Composer_JkPreviewRoutesToJk_NotInstalledMessageComesFromJk()
    {
        string root = Temp();
        try
        {
            ExportOptions opt = Options(null, root);
            var composite = Composite(opt, new JkWorkflowSteps(opt), root);
            var ex = Assert.Throws<PipelineStepException>(() => composite.Preview(KotorGame.JediKnight, "01narshadda", new Log()));
            Assert.Equal("GAME_NOT_FOUND", ex.Code);
            Assert.Contains("Jedi Knight", ex.Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Composer_Modules_ConcatenatesKotorAndJk()
    {
        string root = Temp();
        try
        {
            ExportOptions opt = Options(FakeInstall(root), root);
            var composite = Composite(opt, new JkWorkflowSteps(opt), root);
            var modules = composite.Modules();
            Assert.Equal(4, modules.Count(m => m.Game == KotorGame.JediKnight));
            Assert.True(composite.IsInstalled(KotorGame.JediKnight));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Preview_UnknownLevel_And_Assembly_AreClearErrors()
    {
        string root = Temp();
        try
        {
            var steps = new JkWorkflowSteps(Options(FakeInstall(root), root));
            Assert.Equal("JK_LEVEL_NOT_FOUND", Assert.Throws<PipelineStepException>(() => steps.Preview(KotorGame.JediKnight, "nope", new Log())).Code);
            Assert.Equal("JK_ASSEMBLY_UNSUPPORTED", Assert.Throws<PipelineStepException>(() =>
                steps.Preview(KotorGame.JediKnight, ["01narshadda", "02narshadda"], new Log())).Code);
            Assert.Null(steps.Assembly);
            Assert.False(steps.AssemblyUpToDate);
            Assert.Throws<InvalidOperationException>(() => steps.ApplyAssembly(new Log()));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Preview_UnreadableLevel_IsAPipelineStepException_NotACrash()
    {
        string root = Temp();
        try
        {
            using var steps = new JkWorkflowSteps(Options(FakeInstall(root), root));   // le .jkl de m2 est mal forme
            var ex = Assert.Throws<PipelineStepException>(() => steps.Preview(KotorGame.JediKnight, "m2", new Log()));
            Assert.Equal("JK_LEVEL_UNREADABLE", ex.Code);
            Assert.Throws<InvalidOperationException>(() => steps.Import(new Log()));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MapName_DefaultIsJkPrefixed_AndOverrideIsNormalized()
    {
        Assert.Equal("jk_01narshadda", JkWorkflowSteps.DefaultMapName("01NarShadda"));
        Assert.Equal("jk_m_boss15", JkWorkflowSteps.DefaultMapName("m_boss15"));
    }

    [SkippableFact]
    public void RealLevel_Composer_PreviewImportExportPk3_ToTempFolder()
    {
        Skip.If(RealDir() is null, "Jedi Knight not installed");
        string root = Temp();
        try
        {
            ExportOptions opt = Options(JkDir, root);
            Directory.CreateDirectory(opt.JaBaseDir);
            var jk = new JkWorkflowSteps(opt);
            var composite = Composite(opt, jk, root);
            var ctl = new WorkflowController(composite);
            Assert.True(ctl.SelectModules(KotorGame.JediKnight, ["m2"]));
            ctl.PreviewAsync().Wait();
            Assert.Null(ctl.LastError);
            Assert.Equal(WorkflowStage.Previewed, ctl.Stage);
            Assert.Equal("jk_m2", jk.MapName);
            PreviewModel preview = ctl.Preview!;
            Assert.True(preview.TriangleCount > 1000);
            Assert.NotEmpty(preview.Instances);   // objets et mobiles avec leurs vrais modeles des l'apercu
            Assert.Contains(preview.Instances, i => i.Kind == Kotor.Core.Entities.EntityKind.Door);   // un mobile (func_door a model2)
            Assert.Null(composite.Assembly);

            ctl.NextAsync().Wait();
            Assert.Null(ctl.LastError);
            Assert.Equal(WorkflowStage.Imported, ctl.Stage);
            Assert.NotEmpty(ctl.Preview!.Instances);
            ctl.NextAsync().Wait();
            Assert.Null(ctl.LastError);
            Assert.Equal(WorkflowStage.Exported, ctl.Stage);
            ctl.NextAsync().Wait();
            Assert.Null(ctl.LastError);
            Assert.Equal(WorkflowStage.Packed, ctl.Stage);
            Assert.True(File.Exists(Path.Combine(opt.JaBaseDir, "jk_m2.pk3")));
            Assert.Equal(["jk_m2.pk3"], Directory.GetFiles(opt.JaBaseDir).Select(Path.GetFileName));
        }
        finally { Directory.Delete(root, true); }
    }
}
