using Kotor.Pipeline;
using Kotor.Core.Export;
using Kotor2Ja.Cli.Commands;

namespace Kotor2Ja.Cli.Tests;

public class ExportJaCommandTests
{
    [Fact]
    public void Unknown_module_returns_2()
    {
        string dir = Path.Combine(Path.GetTempPath(), "k2j_cli_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "modules"));
        try
        {
            var err = new StringWriter();
            int code = ExportJaCommand.Run(new ExportOptions(dir, Path.Combine(dir, "b"), Path.Combine(dir, "w")),
                "nope", null, new StringWriter(), err);
            Assert.Equal(2, code);
            Assert.Contains("nope", err.ToString());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Missing_install_returns_2()
    {
        string absent = Path.Combine(Path.GetTempPath(), "absent_" + Guid.NewGuid().ToString("N"));
        int code = ExportJaCommand.Run(new ExportOptions(absent, "b", "w"), "x", null, new StringWriter(), new StringWriter());
        Assert.Equal(2, code);
    }

    [Fact]
    public void Invalid_scale_returns_1()
    {
        var err = new StringWriter();
        int code = ExportJaCommand.Run(new ExportOptions("k", "b", "w", Scale: -2f), "x", null, new StringWriter(), err);
        Assert.Equal(1, code);
        Assert.Contains("Echelle", err.ToString());
    }

    [Fact]
    public void Unknown_bsp_limits_returns_1()
    {
        int code = ExportJaCommand.Run(new ExportOptions("k", "b", "w", BspLimits: "x"), "x", null, new StringWriter(), new StringWriter());
        Assert.Equal(1, code);
    }

    [Fact]
    public void Bsp_limits_option_is_parsed()
    {
        var kotor = new System.CommandLine.Option<string?>("--kotor");
        var cmd = ExportJaCommand.Create(kotor);
        var pr = cmd.Parse(["--bsp-limits", "vanilla", "m"]);
        Assert.Empty(pr.Errors);
        Assert.Contains(cmd.Options, o => o.Name == "--bsp-limits");
    }
}
