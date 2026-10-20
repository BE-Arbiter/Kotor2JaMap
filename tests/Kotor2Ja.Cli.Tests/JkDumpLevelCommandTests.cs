using Kotor2Ja.Cli.Commands;

namespace Kotor2Ja.Cli.Tests;

public class JkDumpLevelCommandTests
{
    [SkippableFact]
    public void DumpLevel_ListsCogsWithTheirRecognizedBehavior()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDumpLevelCommand.DefaultJkDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        var output = new StringWriter();

        int rc = JkDumpLevelCommand.Run("01narshadda", JkDumpLevelCommand.DefaultJkDir, output, new StringWriter());

        Assert.Equal(0, rc);
        string text = output.ToString();
        Assert.Contains("COG behaviors:", text);
        Assert.Matches(@"00_door\.cog: TouchDoor \(thing \d+", text);
        Assert.Matches(@"Movers: \d+ \(approximate \d+, unsupported \d+\)", text);
    }

    [Fact]
    public void ResolveDir_IsOptionThenJkDirThenJkPathThenSteamDefault()
    {
        Func<string, string?> both = n => n == "JK_DIR" ? @"E:\dir" : n == "JK_PATH" ? @"E:\path" : null;
        Assert.Equal(@"O:\x", JkDumpLevelCommand.ResolveDir(@"O:\x", both));
        Assert.Equal(@"E:\dir", JkDumpLevelCommand.ResolveDir(null, both));
        Assert.Equal(@"E:\path", JkDumpLevelCommand.ResolveDir(null, n => n == "JK_PATH" ? @"E:\path" : null));
        Assert.Equal(JkDumpLevelCommand.DefaultJkDir, JkDumpLevelCommand.ResolveDir(" ", _ => null));
    }
}
