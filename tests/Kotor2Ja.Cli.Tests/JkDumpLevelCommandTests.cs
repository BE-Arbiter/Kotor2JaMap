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
}
