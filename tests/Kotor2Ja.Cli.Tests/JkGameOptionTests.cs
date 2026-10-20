using System.CommandLine;
using System.Text;
using Kotor.Core;
using Kotor.Core.Export;
using Kotor2Ja.Cli.Commands;

namespace Kotor2Ja.Cli.Tests;

public class JkGameOptionTests
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";
    private static bool JkInstalled => File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB"));

    private static string Temp()
    {
        string d = Path.Combine(Path.GetTempPath(), "k2j_jk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void WriteGob(string dir, string name, params (string Path, string Content)[] files)
    {
        Directory.CreateDirectory(Path.Combine(dir, "Episode"));
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("GOB "));
        w.Write(0x14);
        w.Write(0);
        var offsets = new List<(int Off, int Size)>();
        foreach (var f in files)
        {
            byte[] data = Encoding.ASCII.GetBytes(f.Content);
            offsets.Add(((int)ms.Position, data.Length));
            w.Write(data);
        }
        int indexOffset = (int)ms.Position;
        w.Write(files.Length);
        for (int i = 0; i < files.Length; i++)
        {
            w.Write(offsets[i].Off);
            w.Write(offsets[i].Size);
            byte[] n = new byte[128];
            Encoding.ASCII.GetBytes(files[i].Path).CopyTo(n, 0);
            w.Write(n);
        }
        w.Flush();
        byte[] bytes = ms.ToArray();
        BitConverter.GetBytes(indexOffset).CopyTo(bytes, 8);
        File.WriteAllBytes(Path.Combine(dir, "Episode", name), bytes);
    }

    [Theory]
    [InlineData("jk")]
    [InlineData("JK")]
    [InlineData("jediknight")]
    public void GameOption_ParsesJk(string text)
    {
        Assert.True(GameOption.TryParse(text, out KotorGame game));
        Assert.Equal(KotorGame.JediKnight, game);
        Assert.Contains("jk", GameOption.BadValue("bogus"));
    }

    [Fact]
    public void GameOption_JkArgumentsParse_OnListModulesAndExportJa()
    {
        RootCommand root = Program.BuildRoot();
        foreach (string[] args in new[] { new[] { "list-modules", "--game", "jk" }, new[] { "export-ja", "01narshadda", "--game", "jk", "--kotor", "X" } })
            Assert.Empty(root.Parse(args).Errors);
        Assert.Contains("jk", GameOption.Create().Description);
    }

    [Fact]
    public void ResolveDir_Jk_IsOptionThenEnvironmentThenConfigThenDefault()
    {
        string dir = Temp();
        try
        {
            string cfg = Path.Combine(dir, "c.json");
            File.WriteAllText(cfg, "{ \"jediKnightDir\": \"C:\\\\cfg\\\\jk\" }");
            Func<string, string?> env = n => n == "JK_DIR" ? @"E:\jk" : null;
            Assert.Equal(@"O:\x", GameOption.ResolveDir(@"O:\x", KotorGame.JediKnight, cfg, env));
            Assert.Equal(@"E:\jk", GameOption.ResolveDir(null, KotorGame.JediKnight, cfg, env));
            Assert.Equal(@"C:\cfg\jk", GameOption.ResolveDir(null, KotorGame.JediKnight, cfg, _ => null));
            Assert.Equal(ConfigLoader.DefaultJediKnightDir,
                GameOption.ResolveDir(null, KotorGame.JediKnight, Path.Combine(dir, "absent.json"), _ => null, () => new GamePaths(null, null, null, "w")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ListModules_Jk_ListsLevelsWithReadableNames()
    {
        string dir = Temp();
        try
        {
            WriteGob(dir, "JK1.GOB", (@"jkl\01narshadda.jkl", "x"), (@"jkl\02narshadda.jkl", "x"),
                ("episode.jk", "\"S\"\nTYPE 1\n10: 1 1 LEVEL 01narshadda.jkl 0 0 20 -1\n20: 1 2 LEVEL 02narshadda.jkl 0 0 -1 -1\n"),
                (@"misc\cogstrings.uni", "\"01NARSHADDA\" 0 \"1 - Betrayal\"\n"));
            WriteGob(dir, "JK1CTF.GOB", (@"jkl\c1.jkl", "x"));
            var output = new StringWriter();
            Assert.Equal(0, ListModulesCommand.Run(dir, output, new StringWriter(), KotorGame.JediKnight));
            Assert.Equal(["01narshadda\t1 - Betrayal", "02narshadda\t02narshadda", "c1\tc1"],
                output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ListModules_Jk_MissingInstall_Returns2WithGameName()
    {
        var err = new StringWriter();
        Assert.Equal(2, ListModulesCommand.Run(Path.Combine(Temp(), "absent"), new StringWriter(), err, KotorGame.JediKnight));
        Assert.Contains("Jedi Knight installation not found", err.ToString());
    }

    [SkippableFact]
    public void ListModules_Jk_Real_Lists34Levels()
    {
        Skip.IfNot(JkInstalled, "Jedi Knight is not installed");
        var output = new StringWriter();
        Assert.Equal(0, ListModulesCommand.Run(JkDir, output, new StringWriter(), KotorGame.JediKnight));
        string[] lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(34, lines.Length);
        Assert.StartsWith("01narshadda\t", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("c3\t"));
    }

    [Fact]
    public void ExportJa_Jk_UnknownLevel_And_MissingInstall_Return2()
    {
        string dir = Temp();
        try
        {
            WriteGob(dir, "JK1.GOB", (@"jkl\01narshadda.jkl", "x"));
            var opt = new ExportOptions("", Path.Combine(dir, "b"), Path.Combine(dir, "w")) { JediKnightDir = dir };
            var err = new StringWriter();
            Assert.Equal(2, ExportJaCommand.Run(opt.ForGame(KotorGame.JediKnight), "nope", null, new StringWriter(), err));
            Assert.Contains("nope", err.ToString());

            err = new StringWriter();
            Assert.Equal(2, ExportJaCommand.Run((opt with { JediKnightDir = Path.Combine(dir, "absent") }).ForGame(KotorGame.JediKnight), "01narshadda", null, new StringWriter(), err));
            Assert.Contains("Jedi Knight installation not found", err.ToString());

            err = new StringWriter();
            Assert.Equal(1, ExportJaCommand.Run(opt.ForGame(KotorGame.JediKnight), "01narshadda,02narshadda", null, new StringWriter(), err));
            Assert.Contains("cannot be assembled", err.ToString());
            Assert.False(Directory.Exists(Path.Combine(dir, "b")));   // rien n'est ecrit
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public void ExportJa_Jk_Real_WritesJkPrefixedPk3_ToTheTestFolder()
    {
        Skip.IfNot(JkInstalled, "Jedi Knight is not installed");
        string dir = Temp();
        try
        {
            var opt = new ExportOptions("", Path.Combine(dir, "base"), Path.Combine(dir, "work")) { JediKnightDir = JkDir };
            Directory.CreateDirectory(opt.JaBaseDir);
            var output = new StringWriter();
            var err = new StringWriter();
            int code = ExportJaCommand.Run(opt.ForGame(KotorGame.JediKnight), "c1", null, output, err);
            Assert.True(code == 0, err.ToString());
            Assert.True(File.Exists(Path.Combine(opt.JaBaseDir, "jk_c1.pk3")));
            Assert.Contains("Level c1 (Ctf) -> map jk_c1", output.ToString());
            Assert.Contains("pk3:", output.ToString());
            Assert.Contains("JK_", output.ToString());   // bilan du journal par code
        }
        finally { Directory.Delete(dir, true); }
    }
}
