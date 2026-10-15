using System.CommandLine;
using Kotor.Core;
using Kotor2Ja.Cli.Commands;

namespace Kotor2Ja.Cli.Tests;

public class GameOptionTests
{
    private static string Config(string json)
    {
        string p = Path.Combine(Path.GetTempPath(), "k2j_go_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(p, json);
        return p;
    }

    [Theory]
    [InlineData("kotor", KotorGame.Kotor1)]
    [InlineData("KOTOR2", KotorGame.Kotor2)]
    [InlineData("2", KotorGame.Kotor2)]
    public void TryParse_accepts_known_values(string text, KotorGame expected)
    {
        Assert.True(GameOption.TryParse(text, out KotorGame g));
        Assert.Equal(expected, g);
    }

    [Theory]
    [InlineData("tfu")]
    [InlineData("kotor3")]
    [InlineData("")]
    public void TryParse_rejects_invalid_values(string text)
    {
        Assert.False(GameOption.TryParse(text, out _));
        Assert.Contains("kotor2", GameOption.BadValue(text));
    }

    [Fact]
    public void Arguments_of_list_modules_carry_the_game()
    {
        RootCommand root = Program.BuildRoot();
        ParseResult ok = root.Parse(["list-modules", "--game", "kotor2", "--kotor", "X"]);
        Assert.Empty(ok.Errors);
        Option<string> game = (Option<string>)root.Subcommands.First(c => c.Name == "list-modules").Options.First(o => o.Name == "--game");
        Assert.Equal("kotor2", ok.GetValue(game));
        Assert.Equal("kotor", root.Parse(["list-modules"]).GetValue(game));
    }

    [Fact]
    public void Invalid_game_value_returns_1_for_the_three_commands()
    {
        TextWriter oldErr = Console.Error;
        var err = new StringWriter();
        Console.SetError(err);
        try
        {
            foreach (string[] args in new[]
            {
                new[] { "list-modules", "--game", "bogus" },
                new[] { "export-obj", "m", "--out", "o", "--game", "bogus" },
                new[] { "export-ja", "m", "--game", "bogus" },
            })
                Assert.Equal(1, Program.BuildRoot().Parse(args).Invoke());
            Assert.Contains("Unknown game: bogus", err.ToString());
        }
        finally { Console.SetError(oldErr); }
    }

    [Fact]
    public void ResolveDir_priority_is_option_then_environment_then_config_then_default()
    {
        string cfg = Config("{ \"kotorDir\": \"C:\\\\cfg\\\\k1\", \"kotor2Dir\": \"C:\\\\cfg\\\\k2\" }");
        try
        {
            Func<string, string?> env = n => n == "KOTOR2_PATH" ? @"E:\k2" : null;
            Assert.Equal(@"O:\x", GameOption.ResolveDir(@"O:\x", KotorGame.Kotor2, cfg, env));
            Assert.Equal(@"E:\k2", GameOption.ResolveDir(null, KotorGame.Kotor2, cfg, env));
            Assert.Equal(@"C:\cfg\k1", GameOption.ResolveDir(null, KotorGame.Kotor1, cfg, env));
            Assert.Equal(@"C:\cfg\k2", GameOption.ResolveDir(null, KotorGame.Kotor2, cfg, _ => null));
            Assert.Equal(Kotor.Core.Export.ConfigLoader.DefaultKotor2Dir,
                GameOption.ResolveDir(null, KotorGame.Kotor2, Path.Combine(Path.GetTempPath(), "absent_" + Guid.NewGuid().ToString("N")), _ => null));
        }
        finally { File.Delete(cfg); }
    }

    [Fact]
    public void ResolveDir_falls_back_to_defaults_on_a_broken_config()
    {
        string cfg = Config("{ not json");
        try { Assert.Equal(Kotor.Core.Export.ConfigLoader.DefaultKotor2Dir, GameOption.ResolveDir(null, KotorGame.Kotor2, cfg, _ => null)); }
        finally { File.Delete(cfg); }
    }

    [Fact]
    public void Missing_install_message_is_english_and_names_the_game()
    {
        string absent = Path.Combine(Path.GetTempPath(), "absent_" + Guid.NewGuid().ToString("N"));
        var err = new StringWriter();
        Assert.Equal(2, ListModulesCommand.Run(absent, new StringWriter(), err, KotorGame.Kotor2));
        Assert.Contains("KOTOR 2 installation not found", err.ToString());
        err = new StringWriter();
        Assert.Equal(2, ExportObjCommand.Run(absent, "m", absent, new StringWriter(), err, KotorGame.Kotor1));
        Assert.Contains("KOTOR installation not found", err.ToString());
    }

    [SkippableFact]
    public void Real_list_modules_kotor2_lists_modules()
    {
        Skip.If(TestEnv.Kotor2Dir is null, "KOTOR 2 absent");
        var output = new StringWriter();
        Assert.Equal(0, ListModulesCommand.Run(TestEnv.Kotor2Dir, output, new StringWriter(), KotorGame.Kotor2));
        string[] lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 50);
        Assert.Contains(lines, l => l.StartsWith("001ebo\t"));
    }
}
