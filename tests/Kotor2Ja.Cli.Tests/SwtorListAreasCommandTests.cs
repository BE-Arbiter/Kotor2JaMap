using Kotor2Ja.Cli.Commands;
using Swtor.Core;
using static Swtor.Core.Tests.SyntheticArea;

namespace Kotor2Ja.Cli.Tests;

public class SwtorListAreasCommandTests : IDisposable
{
    private readonly string _root = WriteRoot(("4611686000000000001", "war_planworking", ["_everywhere_", "room_01"]), ("4611686000000000002", "tat_main", ["main"]));
    private readonly string _work = Path.Combine(Path.GetTempPath(), "swtor_work_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Directory.Delete(_root, true);
        if (Directory.Exists(_work)) Directory.Delete(_work, true);
    }

    [Fact]
    public void Lists_every_area_with_its_columns_and_reports_the_source_on_stderr()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int rc = SwtorListAreasCommand.Run(_root, null, false, false, _work, "fr-fr", output, error);
        Assert.Equal(0, rc);
        string[] lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Id\tInternalName\tCategory", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.Contains(lines, l => l.StartsWith("4611686000000000001\twar_planworking\tstory\t2\t"));
        Assert.Contains("2 area(s) of 2 (rebuilt", error.ToString());
    }

    [Fact]
    public void Filter_and_cache_are_applied()
    {
        SwtorListAreasCommand.Run(_root, null, false, false, _work, "fr-fr", new StringWriter(), new StringWriter());
        var output = new StringWriter();
        var error = new StringWriter();
        Assert.Equal(0, SwtorListAreasCommand.Run(_root, "TAT", false, false, _work, "fr-fr", output, error));
        Assert.Equal(2, output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("1 area(s) of 2 (cache", error.ToString());
    }

    [Fact]
    public void Models_option_adds_the_exact_instance_count()
    {
        var output = new StringWriter();
        Assert.Equal(0, SwtorListAreasCommand.Run(_root, "war_", false, true, _work, "fr-fr", output, new StringWriter()));
        Assert.Contains("Models", output.ToString());
        Assert.Contains("\t6\t", output.ToString());
    }

    [Fact]
    public void Missing_resources_return_2()
    {
        var error = new StringWriter();
        Assert.Equal(2, SwtorListAreasCommand.Run(Path.Combine(_root, "nothing"), null, false, false, _work, "fr-fr", new StringWriter(), error));
        Assert.Contains("not found", error.ToString());
    }

    [Fact]
    public void Dump_area_accepts_an_internal_name()
    {
        var output = new StringWriter();
        int rc = SwtorDumpAreaCommand.Run("war_planworking", _root, 2, output, new StringWriter(), _work);
        Assert.Equal(0, rc);
        Assert.Contains("Rooms (2): _everywhere_, room_01", output.ToString());
        Assert.Equal(2, SwtorDumpAreaCommand.Run("unknown_name", _root, 2, new StringWriter(), new StringWriter(), _work));
    }

    [Fact]
    public void Command_is_registered_and_parses_options()
    {
        var root = Program.BuildRoot();
        var parse = root.Parse(["swtor-list-areas", "--assets", "X", "--filter", "black", "--rebuild", "--models", "--work", "W"]);
        Assert.Empty(parse.Errors);
        Assert.Contains(root.Subcommands, c => c.Name == "swtor-list-areas");
    }

    [SkippableFact]
    public void Real_catalogue_lists_the_black_talon()
    {
        var assets = new SwtorAssets();
        Skip.If(!assets.Exists || !Directory.Exists(Path.Combine(assets.Root, "systemgenerated", "buckets")), "Ressources SWTOR absentes");
        var output = new StringWriter();
        Assert.Equal(0, SwtorListAreasCommand.Run(assets.Root, "serre noire", false, false, _work, "fr-fr", output, new StringWriter()));
        Assert.Contains("imp_transition_flashpoint", output.ToString());
    }
}
