using Jk.Core;

namespace Jk.Core.Tests;

public class JkInstallEpisodeTests
{
    [Fact]
    public void LevelNames_FromEpisodeGobs_OrderedByEpisodeFile_NamedByUni()
    {
        string dir = GobBuilder.TempDir();
        try
        {
            GobBuilder.WriteGob(dir, "Episode", "JK1MP.GOB",
                (@"jkl\m4.jkl", "x"), (@"jkl\m2.jkl", "x"), (@"jkl\m5.jkl", "x"),
                ("episode.jk", "\"MP\"\nTYPE 2\n10: 0 1 LEVEL m2.jkl 0 0 20 -1\n20: 0 2 LEVEL m4.jkl 0 0 30 -1\n30: 0 3 LEVEL m5.jkl 0 0 10 -1\n"),
                (@"misc\cogstrings.uni", "MSGS 2\n\"M2\" 0 \"Loading terminal\"\n\"M4\" 0 \"Mining station\"\n"));
            var levels = JkInstall.ListLevels(dir);
            Assert.Equal(3, levels.Count);
            var names = JkInstall.LevelNames(dir, levels);
            Assert.Equal(["m2", "m4", "m5"], names.Select(n => n.Level.Name));   // ordre de episode.jk, pas du GOB
            Assert.Equal("Loading terminal", names[0].DisplayName);
            Assert.Equal("m5", names[2].DisplayName);                          // sans entree .uni : nom du fichier
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LevelNames_NoEpisodeFile_KeepsGobOrderAndFileNames()
    {
        string dir = GobBuilder.TempDir();
        try
        {
            GobBuilder.WriteGob(dir, "Episode", "JK1CTF.GOB", (@"jkl\c2.jkl", "x"), (@"jkl\c1.jkl", "x"));
            var names = JkInstall.LevelNames(dir, JkInstall.ListLevels(dir));
            Assert.Equal(["c2", "c1"], names.Select(n => n.DisplayName));
        }
        finally { Directory.Delete(dir, true); }
    }
}
