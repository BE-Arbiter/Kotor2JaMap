using System.Numerics;
using Kotor.Core;
using Kotor.Core.Export;
using Kotor.Pipeline;
using Kotor.Pipeline.Preview;
using Kotor.Pipeline.Workflow;

namespace Kotor.Pipeline.Tests;

public sealed class WorkflowControllerTests
{
    private static PreviewModel Model() =>
        new([], new Dictionary<string, PreviewTexture>(), [], [], null, 0f, Vector3.Zero, Vector3.Zero);

    private sealed class FakeSteps : IWorkflowSteps
    {
        public Func<PreviewModel> OnPreview = Model;
        public Action<KotorGame>? OnPreviewGame;
        public int Calls;
        public Func<PreviewModel> OnImport = Model;
        public Func<PreviewModel> OnExport = Model;
        public Func<string> OnPack = () => "x.pk3";
        public IReadOnlyList<ModuleListItem> Modules() => [new(KotorGame.Kotor1, "m1", "One"), new(KotorGame.Kotor1, "m2", "Two")];
        public PreviewModel Preview(KotorGame game, string module, IProgress<string> log) { OnPreviewGame?.Invoke(game); return OnPreview(); }
        public PreviewModel Import(IProgress<string> log) => OnImport();
        public PreviewModel Export(IProgress<string> log) => OnExport();
        public string CreatePk3(IProgress<string> log) => OnPack();
    }

    private static async Task<WorkflowController> ToStage(FakeSteps f, int steps)
    {
        var c = new WorkflowController(f);
        c.SelectModule(KotorGame.Kotor1, "m1");
        await c.PreviewAsync();
        for (int i = 1; i < steps; i++) await c.NextAsync();
        return c;
    }

    [Fact]
    public async Task Labels_follow_stages()
    {
        var c = new WorkflowController(new FakeSteps());
        c.SelectModule(KotorGame.Kotor1, "m1");
        Assert.Null(c.NextLabel);
        Assert.True(c.CanPreview);
        await c.PreviewAsync();
        Assert.Equal("Import", c.NextLabel);
        await c.NextAsync();
        Assert.Equal("Export", c.NextLabel);
        await c.NextAsync();
        Assert.Equal("Create PK3", c.NextLabel);
        PreviewModel? exported = c.Preview;
        await c.NextAsync();
        Assert.Null(c.NextLabel);
        Assert.Equal(WorkflowStage.Packed, c.Stage);
        Assert.Same(exported, c.Preview);
        Assert.False(c.CanNext);
    }

    [Fact]
    public async Task Busy_blocks_selection_and_buttons()
    {
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var f = new FakeSteps();
        var c = await ToStage(f, 1);
        f.OnImport = () => { started.SetResult(); gate.Task.Wait(); return Model(); };
        Task t = c.NextAsync();
        await started.Task;
        Assert.True(c.IsBusy);
        Assert.False(c.SelectModule(KotorGame.Kotor1, "m2"));
        Assert.False(c.CanNext);
        Assert.False(c.CanPreview);
        gate.SetResult();
        await t;
        Assert.False(c.IsBusy);
        Assert.Equal(WorkflowStage.Imported, c.Stage);
        Assert.True(c.CanNext);
    }

    [Fact]
    public async Task Failure_keeps_stage_and_logs()
    {
        var f = new FakeSteps { OnExport = () => throw new PipelineStepException("EXPORT_IO_ERROR", "disque plein") };
        var c = await ToStage(f, 2);
        await c.NextAsync();
        Assert.Equal(WorkflowStage.Imported, c.Stage);
        Assert.Equal("Export", c.NextLabel);
        Assert.NotNull(c.LastError);
        Assert.Contains(c.DrainLog(), l => l.Contains("EXPORT_IO_ERROR"));
        Assert.False(c.IsBusy);

        f.OnExport = () => throw new InvalidOperationException("boum");
        await c.NextAsync();
        Assert.Equal(WorkflowStage.Imported, c.Stage);
        Assert.Contains("boum", c.LastError);
    }

    [Fact]
    public async Task Stale_result_is_ignored()
    {
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var f = new FakeSteps { OnPreview = () => { started.SetResult(); gate.Task.Wait(); return Model(); } };
        var c = new WorkflowController(f);
        c.SelectModule(KotorGame.Kotor1, "m1");
        Task t = c.PreviewAsync();
        await started.Task;
        c.Reset(); // nouvelle generation : le resultat tardif est ecarte
        gate.SetResult();
        await t;
        Assert.Null(c.Preview);
        Assert.Equal(WorkflowStage.None, c.Stage);
        Assert.False(c.IsBusy);
    }

    [Fact]
    public async Task Stale_pk3_result_does_not_log()
    {
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var f = new FakeSteps { OnPack = () => { started.SetResult(); gate.Task.Wait(); return "stale.pk3"; } };
        var c = await ToStage(f, 3);
        c.DrainLog();
        Task t = c.NextAsync();
        await started.Task;
        c.Reset();
        gate.SetResult();
        await t;
        Assert.DoesNotContain(c.DrainLog(), l => l.Contains("stale.pk3"));
        Assert.Equal(WorkflowStage.None, c.Stage);
    }

    [Fact]
    public async Task Pk3_success_is_logged()
    {
        var c = await ToStage(new FakeSteps(), 3);
        c.DrainLog();
        await c.NextAsync();
        Assert.Contains(c.DrainLog(), l => l.Contains("PK3: x.pk3"));
    }

    [SkippableFact]
    public async Task Failed_repreview_keeps_previous_state_usable()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR introuvable");
        string root = Path.Combine(Path.GetTempPath(), "k2j_wf2_" + Guid.NewGuid().ToString("N"));
        try
        {
            var opt = new ExportOptions(TestEnv.KotorDir!, Path.Combine(root, "base"), Path.Combine(root, "work"));
            Directory.CreateDirectory(opt.JaBaseDir);
            var c = new WorkflowController(new KotorWorkflowSteps(opt));
            Skip.If(!c.Modules().Any(m => m.Module == "tar_m02aa"), "tar_m02aa absent");
            c.SelectModule(KotorGame.Kotor1, "tar_m02aa");
            await c.PreviewAsync();
            await c.NextAsync();
            Assert.Equal(WorkflowStage.Imported, c.Stage);

            // Re-previsualisation en echec (module inexistant) : Stage et etat interne restent coherents.
            c.SelectModule(KotorGame.Kotor1, "zzz_inexistant");
            Assert.Equal(WorkflowStage.None, c.Stage); // SelectModule remet a zero le controleur
            var steps = new KotorWorkflowSteps(opt);
            var progress = new Progress<string>();
            steps.Preview(KotorGame.Kotor1, "tar_m02aa", progress);
            steps.Import(progress);
            Assert.ThrowsAny<Exception>(() => steps.Preview(KotorGame.Kotor1, "zzz_inexistant", progress));
            PreviewModel exported = steps.Export(progress); // l'etat Imported a survecu a l'echec
            Assert.NotEmpty(exported.Batches);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Select_takes_game()
    {
        var seen = new List<KotorGame>();
        var f = new FakeSteps { OnPreviewGame = g => seen.Add(g) };
        var c = new WorkflowController(f);
        Assert.True(c.SelectModule(KotorGame.Kotor2, "m1"));
        Assert.Equal(KotorGame.Kotor2, c.SelectedGame);
        Assert.Equal("m1", c.SelectedModule);
        await c.PreviewAsync();
        Assert.Equal([KotorGame.Kotor2], seen);
        Assert.True(c.SelectModule(KotorGame.Kotor1, "m1"));
        Assert.Equal(KotorGame.Kotor1, c.SelectedGame);
        Assert.Equal(WorkflowStage.None, c.Stage);
    }


    [Fact]
    public async Task Same_module_name_in_both_games_gives_distinct_selections()
    {
        var seen = new List<KotorGame>();
        var c = new WorkflowController(new FakeSteps { OnPreviewGame = g => seen.Add(g) });
        Assert.NotEqual(new ModuleListItem(KotorGame.Kotor1, "m1", "A"), new ModuleListItem(KotorGame.Kotor2, "m1", "A"));

        Assert.True(c.SelectModule(KotorGame.Kotor1, "m1"));
        (KotorGame, string?) first = (c.SelectedGame, c.SelectedModule);
        await c.PreviewAsync();
        Assert.True(c.SelectModule(KotorGame.Kotor2, "m1"));
        (KotorGame, string?) second = (c.SelectedGame, c.SelectedModule);
        await c.PreviewAsync();

        Assert.NotEqual(first, second);
        Assert.Equal("m1", c.SelectedModule);
        Assert.Equal(KotorGame.Kotor2, c.SelectedGame);
        Assert.Equal([KotorGame.Kotor1, KotorGame.Kotor2], seen);
    }

    [Fact]
    public async Task Late_result_of_the_first_game_is_ignored_after_switching_game()
    {
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        PreviewModel lateK1 = Model(), k2 = Model();
        var f = new FakeSteps();
        f.OnPreviewGame = g =>
        {
            if (g != KotorGame.Kotor1) return;
            started.SetResult();
            gate.Task.Wait();
        };
        f.OnPreview = () => f.Calls++ == 0 ? lateK1 : k2;
        var c = new WorkflowController(f);
        c.SelectModule(KotorGame.Kotor1, "m1");
        Task late = c.PreviewAsync();
        await started.Task;
        c.Reset(); // new generation: the K1 result must be dropped
        gate.SetResult();
        await late;
        Assert.Null(c.Preview);
        Assert.Equal(WorkflowStage.None, c.Stage);

        Assert.True(c.SelectModule(KotorGame.Kotor2, "m1"));
        await c.PreviewAsync();
        Assert.Same(k2, c.Preview);
        Assert.Equal(KotorGame.Kotor2, c.SelectedGame);
        Assert.Equal(WorkflowStage.Previewed, c.Stage);
    }
    [Fact]
    public void Missing_game_folder_is_reported_not_installed()
    {
        string root = Path.Combine(Path.GetTempPath(), "k2j_wf3_" + Guid.NewGuid().ToString("N"));
        var steps = new KotorWorkflowSteps(new ExportOptions(root, "b", "w", Kotor2Dir: null));
        Assert.False(steps.IsInstalled(KotorGame.Kotor1));
        Assert.False(steps.IsInstalled(KotorGame.Kotor2));
        Assert.Empty(steps.Modules());
        var ex = Assert.Throws<PipelineStepException>(() => steps.Preview(KotorGame.Kotor2, "001ebo", new Progress<string>()));
        Assert.Equal("GAME_NOT_FOUND", ex.Code);
    }

    [SkippableFact]
    public void Real_modules_list_both_games()
    {
        Skip.If(TestEnv.KotorDir is null || TestEnv.Kotor2Dir is null, "KOTOR or KOTOR 2 absent");
        var steps = new KotorWorkflowSteps(new ExportOptions(TestEnv.KotorDir!, "b", "w", Kotor2Dir: TestEnv.Kotor2Dir));
        var all = steps.Modules();
        Assert.Contains(all, m => m.Game == KotorGame.Kotor1 && m.Module == "tar_m02aa");
        Assert.Contains(all, m => m.Game == KotorGame.Kotor2 && m.Module == "001ebo" && m.AreaName.Length > 0);
    }

    [Fact]
    public async Task Select_resets()
    {
        var c = await ToStage(new FakeSteps(), 2);
        Assert.Equal(WorkflowStage.Imported, c.Stage);
        Assert.True(c.SelectModule(KotorGame.Kotor1, "m2"));
        Assert.Equal(WorkflowStage.None, c.Stage);
        Assert.Null(c.Preview);
        Assert.Null(c.NextLabel);
        Assert.Equal("m2", c.SelectedModule);
    }

    [SkippableFact]
    public async Task Real_tar_m02aa_full_workflow()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR introuvable");
        string root = Path.Combine(Path.GetTempPath(), "k2j_wf_" + Guid.NewGuid().ToString("N"));
        try
        {
            var opt = new ExportOptions(TestEnv.KotorDir!, Path.Combine(root, "base"), Path.Combine(root, "work"));
            Directory.CreateDirectory(opt.JaBaseDir);
            var c = new WorkflowController(new KotorWorkflowSteps(opt));
            Skip.If(!c.Modules().Any(m => m.Module == "tar_m02aa"), "tar_m02aa absent");
            c.SelectModule(KotorGame.Kotor1, "tar_m02aa");
            await c.PreviewAsync();
            Assert.Null(c.LastError);
            long tScene = c.Preview!.TriangleCount;
            await c.NextAsync();
            Assert.Null(c.LastError);
            PreviewModel imported = c.Preview!;
            await c.NextAsync();
            Assert.Null(c.LastError);
            PreviewModel exported = c.Preview!;
            await c.NextAsync();
            Assert.Null(c.LastError);
            Assert.Equal(WorkflowStage.Packed, c.Stage);
            long skyTris = exported.Batches.Where(b => b.IsSky).Sum(b => (long)b.Indices.Length / 3);
            Assert.True(skyTris > 0);
            Assert.Equal(imported.TriangleCount, exported.Batches.Where(b => !b.IsSky).Sum(b => (long)b.Indices.Length / 3));
            Assert.Equal(imported.TriangleCount + skyTris, exported.TriangleCount);
            Assert.True(File.Exists(Path.Combine(opt.JaBaseDir, "kotor_tar_m02aa.pk3")));
            Assert.NotEmpty(imported.Markers);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
