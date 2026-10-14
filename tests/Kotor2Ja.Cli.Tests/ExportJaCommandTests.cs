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
}
