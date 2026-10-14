using System.Numerics;
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
        public Func<PreviewModel> OnImport = Model;
        public Func<PreviewModel> OnExport = Model;
        public Func<string> OnPack = () => "x.pk3";
        public IReadOnlyList<ModuleListItem> Modules() => [new("m1", "One"), new("m2", "Two")];
        public PreviewModel Preview(string module, IProgress<string> log) => OnPreview();
        public PreviewModel Import(IProgress<string> log) => OnImport();
        public PreviewModel Export(IProgress<string> log) => OnExport();
        public string CreatePk3(IProgress<string> log) => OnPack();
    }

    private static async Task<WorkflowController> ToStage(FakeSteps f, int steps)
    {
        var c = new WorkflowController(f);
        c.SelectModule("m1");
        await c.PreviewAsync();
        for (int i = 1; i < steps; i++) await c.NextAsync();
        return c;
    }

    [Fact]
    public async Task Labels_follow_stages()
    {
        var c = new WorkflowController(new FakeSteps());
        c.SelectModule("m1");
        Assert.Null(c.NextLabel);
        Assert.True(c.CanPreview);
        await c.PreviewAsync();
        Assert.Equal("Importer", c.NextLabel);
        await c.NextAsync();
        Assert.Equal("Exporter", c.NextLabel);
        await c.NextAsync();
        Assert.Equal("Créer PK3", c.NextLabel);
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
        Assert.False(c.SelectModule("m2"));
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
        Assert.Equal("Exporter", c.NextLabel);
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
        c.SelectModule("m1");
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
    public async Task Select_resets()
    {
        var c = await ToStage(new FakeSteps(), 2);
        Assert.Equal(WorkflowStage.Imported, c.Stage);
        Assert.True(c.SelectModule("m2"));
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
            var c = new WorkflowController(new KotorWorkflowSteps(opt));
            Skip.If(!c.Modules().Any(m => m.Module == "tar_m02aa"), "tar_m02aa absent");
            c.SelectModule("tar_m02aa");
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
            Assert.Equal(imported.TriangleCount, exported.TriangleCount);
            Assert.True(File.Exists(Path.Combine(opt.JaBaseDir, "kotor_tar_m02aa.pk3")));
            Assert.NotEmpty(imported.Markers);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
