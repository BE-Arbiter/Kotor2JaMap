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
    public void Truncated_config_gives_a_clear_message_and_code_1()
    {
        string dir = Path.Combine(Path.GetTempPath(), "k2j_cfg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string cfg = Path.Combine(dir, "bad.json");
            File.WriteAllText(cfg, "{ \"workDir\": \"x\", ");

            var err = new StringWriter();
            Assert.False(ExportJaCommand.TryLoadConfig(cfg, err, out _));
            string text = err.ToString();
            Assert.Contains("Cannot read the configuration file", text);
            Assert.Contains(cfg, text);
            Assert.DoesNotContain("   at ", text);

            // Through the real command: exit code 1, no exception (the module is never reached).
            string temp = Path.Combine(dir, "w");
            Assert.Equal(1, Program.BuildRoot().Parse(["export-ja", "tar_m02aa", "--config", cfg]).Invoke());
            Assert.False(Directory.Exists(temp));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Valid_config_loads()
    {
        string dir = Path.Combine(Path.GetTempPath(), "k2j_cfg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string cfg = Path.Combine(dir, "ok.json");
            File.WriteAllText(cfg, "{ \"workDir\": \"" + Path.Combine(dir, "w").Replace("\\", "\\\\") + "\" }");
            Assert.True(ExportJaCommand.TryLoadConfig(cfg, new StringWriter(), out var o));
            Assert.Equal(Path.Combine(dir, "w"), o.WorkDir);
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
        Assert.Contains("Invalid scale", err.ToString());
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a b")]
    [InlineData("x/y")]
    public void Invalid_map_name_returns_1(string name)
    {
        var err = new StringWriter();
        int code = ExportJaCommand.Run(new ExportOptions("k", "b", "w"), "x", name, new StringWriter(), err);
        Assert.Equal(1, code);
        Assert.Contains("Invalid map name", err.ToString());
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
