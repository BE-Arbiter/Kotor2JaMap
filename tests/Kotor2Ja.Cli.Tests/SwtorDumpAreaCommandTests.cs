using System.Numerics;
using Kotor2Ja.Cli.Commands;
using Swtor.Core;
using static Swtor.Core.Tests.SyntheticArea;

namespace Kotor2Ja.Cli.Tests;

[Collection("ConsoleRedirect")]
public class SwtorDumpAreaCommandTests
{
    [Fact]
    public void Dumps_rooms_counts_and_first_instances_of_a_synthetic_area()
    {
        string dir = WriteFolder(["room_a"], StandardAssets(), _ =>
        [
            new Obj(1, GroundTile, Position: new Vector3(6.4f, 0, -3.2f), Euler: new Vector3(0, 90, 0)),
            new Obj(2, GroundTile, Parent: 1, Position: new Vector3(1, 0, 0)),
            new Obj(3, UnlistedType),
        ]);
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();
            int rc = SwtorDumpAreaCommand.Run(dir, null, 5, output, error);
            string text = output.ToString();
            Assert.Equal(0, rc);
            Assert.Equal("", error.ToString());
            Assert.Contains("Rooms (1): room_a", text);
            Assert.Contains("Model", text);
            Assert.Contains("Unknown", text);
            Assert.Contains("pos (6.40, 0.00, -3.20)", text);
            Assert.Contains("rot (0, 90, 0)", text);
            Assert.Contains("parent 0x1 world (6.40, 0.00, -4.20)", text);
            Assert.Contains("Markers: arrival_test", text);
            Assert.Contains("(unknown type 0x40000000000000FF)", text, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Max_limits_the_detailed_instances()
    {
        string dir = WriteFolder(["r"], StandardAssets(), _ => Enumerable.Range(1, 5).Select(n => new Obj((ulong)n, GroundTile)).ToArray());
        try
        {
            var output = new StringWriter();
            Assert.Equal(0, SwtorDumpAreaCommand.Run(dir, null, 3, output, new StringWriter()));
            Assert.Equal(3, output.ToString().Split('\n').Count(l => l.StartsWith("  Model") && l.Contains("floor_tile.gr2")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Unknown_area_returns_2_with_a_message()
    {
        var error = new StringWriter();
        int rc = SwtorDumpAreaCommand.Run("does-not-exist-1234", Path.GetTempPath(), 5, new StringWriter(), error);
        Assert.Equal(2, rc);
        Assert.Contains("not found", error.ToString());
    }

    [Fact]
    public void Command_is_registered_and_parses_arguments()
    {
        var root = Program.BuildRoot();
        var parse = root.Parse(["swtor-dump-area", "123", "--assets", "X", "--max", "7"]);
        Assert.Empty(parse.Errors);
        Assert.Contains(root.Subcommands, c => c.Name == "swtor-dump-area");
    }

    [SkippableFact]
    public void Real_ship_interior_reports_all_models_found()
    {
        Skip.If(!Directory.Exists(SwtorAssets.DefaultRoot) && Environment.GetEnvironmentVariable("SWTOR_ASSETS") is null, "Ressources SWTOR absentes");
        var assets = new SwtorAssets();
        Skip.If(!assets.Exists, "Ressources SWTOR absentes");
        var output = new StringWriter();
        int rc = SwtorDumpAreaCommand.Run("4611686045100172646", assets.Root, 3, output, new StringWriter());
        Skip.If(rc == 2, "Zone absente");
        Assert.Equal(0, rc);
        Assert.Contains("found on disk: 75 (100", output.ToString());
        Assert.Contains("== room_01: 294 instances", output.ToString());
    }
}
