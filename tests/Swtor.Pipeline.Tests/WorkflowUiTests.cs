using Kotor.Core;
using Kotor.Core.Export;
using Kotor.Pipeline;


using Kotor.Pipeline.Workflow;
using Swtor.Core;
using Swtor.Core.Tests;

namespace Swtor.Pipeline.Tests;

/// <summary>Logique non graphique de l'integration UI (T6) : selection de rooms, budget, etapes SWTOR, aiguillage KOTOR / SWTOR.</summary>
public class WorkflowUiTests
{
    private static SwtorRoomSelection Selection(params (string Name, int Tri, bool Space)[] rooms) =>
        new("123", "war_test", rooms.Select(r => new SwtorRoomInfo(r.Name, r.Tri, r.Space)));

    private sealed class Log : IProgress<string>
    {
        public List<string> Lines { get; } = [];
        public void Report(string value) => Lines.Add(value);
    }

    [Fact]
    public void Selection_defaults_exclude_space_and_name_the_whole_area()
    {
        var s = Selection(("a", 1000, false), ("b", 2000, false), ("SPACE", 500000, true));
        Assert.Equal(["a", "b"], s.SelectedRooms);
        Assert.Equal(3000, s.TotalTriangles);
        Assert.True(s.IsDefault);
        Assert.Equal("123", s.ModuleSpec);
        Assert.Equal("war_test", s.DefaultMapName);
        Assert.Equal(SwtorBudgetLevel.Ok, s.Level);
        Assert.Null(s.BudgetMessage);
    }

    [Fact]
    public void Selection_subset_gives_rooms_spec_and_suffixed_name()
    {
        var s = Selection(("a", 1000, false), ("b", 2000, false));
        s.Set("a", false);
        Assert.False(s.IsDefault);
        Assert.Equal("123:b", s.ModuleSpec);
        Assert.Equal("war_test_b", s.DefaultMapName);
        Assert.Equal(2000, s.TotalTriangles);
        var parsed = SwtorAreaSpec.Parse(s.ModuleSpec!);
        Assert.Equal(["b"], parsed.Rooms);
    }

    [Fact]
    public void Selection_none_has_no_spec_and_space_can_be_checked()
    {
        var s = Selection(("a", 10, false), ("SPACE", 20, true));
        s.SelectAll(false);
        Assert.Null(s.ModuleSpec);
        s.Set("SPACE", true);
        Assert.Equal("123:SPACE", s.ModuleSpec);
        s.SelectDefault();
        Assert.Equal("123", s.ModuleSpec);
        s.Set("unknown", true);   // ignore
        Assert.Equal(1, s.SelectedCount);
    }

    [Fact]
    public void Budget_levels_follow_the_draw_index_limit()
    {
        Assert.Equal(174750, SwtorBudget.MaxRenderTriangles);
        Assert.Equal(SwtorBudgetLevel.Ok, SwtorBudget.Level(100_000));
        Assert.Equal(SwtorBudgetLevel.Near, SwtorBudget.Level(160_000));
        Assert.Equal(SwtorBudgetLevel.Near, SwtorBudget.Level(SwtorBudget.MaxRenderTriangles));
        Assert.Equal(SwtorBudgetLevel.Exceeded, SwtorBudget.Level(SwtorBudget.MaxRenderTriangles + 1));
        // 149 047 triangles (war_planworking, T5) -> 447 177 index mesures.
        Assert.Equal(447_141 + 36, SwtorBudget.EstimateDrawIndexes(149_047));
        Assert.True(SwtorBudget.EstimateDrawIndexes(SwtorBudget.MaxRenderTriangles) <= SwtorBudget.MaxDrawIndexes);
        Assert.True(SwtorBudget.EstimateDrawIndexes(SwtorBudget.MaxRenderTriangles + 1) > SwtorBudget.MaxDrawIndexes);
    }

    [Fact]
    public void Selection_over_budget_warns_and_unchecking_rooms_clears_it()
    {
        var s = Selection(("big", 232_000, false), ("small", 1000, false));
        Assert.Equal(SwtorBudgetLevel.Exceeded, s.Level);
        Assert.Contains("uncheck rooms", s.BudgetMessage);
        s.Set("big", false);
        Assert.Equal(SwtorBudgetLevel.Ok, s.Level);
    }

    [Fact]
    public void Steps_are_hidden_without_assets_and_report_unknown_areas()
    {
        string root = Path.Combine(Path.GetTempPath(), "swtor_t6_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var opt = new ExportOptions("", Path.Combine(root, "base"), Path.Combine(root, "work"));
            var steps = new SwtorWorkflowSteps(opt, new SwtorAssets(root));
            Assert.False(steps.IsInstalled(KotorGame.Swtor));
            Assert.False(steps.IsInstalled(KotorGame.Kotor1));
            Assert.Empty(steps.Modules());
            Assert.Equal("GAME_NOT_FOUND", Assert.Throws<PipelineStepException>(() => steps.Preview(KotorGame.Swtor, "1", new Log())).Code);

            Directory.CreateDirectory(Path.Combine(root, "world", "areas"));
            Assert.True(steps.IsInstalled(KotorGame.Swtor));
            Assert.Equal("SWTOR_AREA_NOT_FOUND", Assert.Throws<PipelineStepException>(() => steps.Preview(KotorGame.Swtor, "nope:r1", new Log())).Code);
            Assert.Equal("SWTOR_ASSEMBLY_UNSUPPORTED", Assert.Throws<PipelineStepException>(() => steps.Preview(KotorGame.Swtor, ["1", "2"], new Log())).Code);
            Assert.Null(steps.Assembly);
            Assert.Throws<InvalidOperationException>(() => steps.Import(new Log()));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Steps_reject_an_invalid_map_name()
    {
        string root = Path.Combine(Path.GetTempPath(), "swtor_t6_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "world", "areas", "1"));
        File.WriteAllBytes(Path.Combine(root, "world", "areas", "1", "area.dat"), [0]);
        try
        {
            var steps = new SwtorWorkflowSteps(new ExportOptions("", root, root), new SwtorAssets(root)) { MapNameOverride = new string('x', 80) };
            Assert.Equal("MAP_NAME_INVALID", Assert.Throws<PipelineStepException>(() => steps.Preview(KotorGame.Swtor, "1", new Log())).Code);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Config_reads_and_saves_swtorAssetsDir_with_env_priority()
    {
        string dir = Path.Combine(Path.GetTempPath(), "swtor_cfg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string p = Path.Combine(dir, "c.json");
            File.WriteAllText(p, "{ \"swtorAssetsDir\": \"D:\\\\tor\" }");
            Func<GamePaths> none = () => new GamePaths(null, null, null, Path.Combine(dir, "w"));
            Assert.Equal("D:\\tor", ConfigLoader.Load(p, _ => null, none).SwtorDir);
            Assert.Equal("E:\\env", ConfigLoader.Load(p, n => n == "SWTOR_ASSETS" ? "E:\\env" : null, none).SwtorDir);
            Assert.Null(ConfigLoader.Load(null, _ => null, none).SwtorDir);   // defaut : SwtorAssets (C:\jka_tor_assets\resources)
            ConfigLoader.Save(p, ConfigLoader.Load(p, _ => null, none));
            Assert.Equal("D:\\tor", ConfigLoader.Load(p, _ => null, none).SwtorDir);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- zone reelle (ignore sans ressources) ----

    private const string CouncilAreaId = "4611686142106770000";
    private const string CouncilRoom = "station_dark_council_chamber";

    [SkippableFact]
    public void DarkCouncil_analysis_lists_rooms_and_the_chamber_fits_the_budget()
    {
        string? root = TestEnv.SwtorAssetsDir;
        Skip.If(root is null || !Directory.Exists(Path.Combine(root, "world", "areas", CouncilAreaId)), "SWTOR assets not available");
        var steps = new SwtorWorkflowSteps(new ExportOptions("", root!, Path.GetTempPath()), new SwtorAssets(root));
        SwtorAreaAnalysis a = steps.Analyze(CouncilAreaId);
        Assert.Equal(CouncilAreaId, a.AreaId);
        SwtorRoomInfo chamber = Assert.Single(a.Rooms, r => r.Name == CouncilRoom);
        Assert.InRange(chamber.Triangles, 50_000, SwtorBudget.MaxRenderTriangles);
        Assert.DoesNotContain(a.Rooms, r => r.Name.Equals("skyscene", StringComparison.OrdinalIgnoreCase));
        SwtorRoomSelection sel = a.NewSelection();
        sel.SelectAll(false);
        sel.Set(CouncilRoom, true);
        Assert.Equal(chamber.Triangles, sel.TotalTriangles);
        Assert.NotEqual(SwtorBudgetLevel.Exceeded, sel.Level);
    }

    [SkippableFact]
    public void DarkCouncil_runs_the_four_steps_to_a_pk3_and_reports_a_locked_pk3()
    {
        string? root = TestEnv.SwtorAssetsDir;
        Skip.If(root is null || !Directory.Exists(Path.Combine(root, "world", "areas", CouncilAreaId)), "SWTOR assets not available");
        string tmp = Path.Combine(Path.GetTempPath(), "swtor_t6_" + Guid.NewGuid().ToString("N"));
        try
        {
            var opt = new ExportOptions("", Path.Combine(tmp, "base"), Path.Combine(tmp, "work"));
            Directory.CreateDirectory(opt.JaBaseDir);
            var swtor = new SwtorWorkflowSteps(opt, new SwtorAssets(root));
            var steps = new GameWorkflowSteps(new KotorWorkflowSteps(opt), swtor);
            var ctl = new WorkflowController(steps);
            Assert.True(ctl.SelectModules(KotorGame.Swtor, [$"{CouncilAreaId}:{CouncilRoom}"]));
            ctl.PreviewAsync().Wait();
            Assert.Null(ctl.LastError);
            Assert.Equal(WorkflowStage.Previewed, ctl.Stage);
            Assert.True(ctl.Preview!.TriangleCount > 10_000);
            Assert.Equal("imperial_fleet_station_dark_council_chamber", swtor.MapName);
            ctl.NextAsync().Wait();
            Assert.Null(ctl.LastError);
            ctl.NextAsync().Wait();
            Assert.Null(ctl.LastError);
            Assert.Equal(WorkflowStage.Exported, ctl.Stage);

            // pk3 verrouille (jeu ouvert) : erreur claire, pas de plantage ; une fois libere, l'etape reussit.
            string pk3 = Path.Combine(opt.JaBaseDir, "swtor_imperial_fleet_station_dark_council_chamber.pk3");
            using (var held = new FileStream(pk3, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                ctl.NextAsync().Wait();
                Assert.NotNull(ctl.LastError);
                Assert.Contains("PK3_LOCKED", ctl.LastError);
                Assert.Equal(WorkflowStage.Exported, ctl.Stage);
            }
            ctl.NextAsync().Wait();
            Assert.Null(ctl.LastError);
            Assert.Equal(WorkflowStage.Packed, ctl.Stage);
            Assert.True(new FileInfo(pk3).Length > 100_000);
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }

    [Fact]
    public void Composite_routes_each_game_to_its_steps_and_keeps_the_kotor_list()
    {

        string root = Path.Combine(Path.GetTempPath(), "swtor_t6_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var swtor = new SwtorWorkflowSteps(new ExportOptions("", root, root), new SwtorAssets(root));
            var composite = new GameWorkflowSteps(new KotorWorkflowSteps(new ExportOptions("", "", "")), swtor);
            Assert.False(composite.IsInstalled(KotorGame.Swtor));
            // Un apercu SWTOR qui echoue ne change pas l'implementation active.
            Assert.Throws<PipelineStepException>(() => composite.Preview(KotorGame.Swtor, "x", new Log()));
            Assert.Null(composite.Assembly);
        }
        finally { Directory.Delete(root, true); }
    }

}
