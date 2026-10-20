using System.Text;
using Jk.Core;
using Jk.Core.Gob;
using Kotor.Core;
using Kotor.Core.Export;

namespace Jk.Core.Tests;

public class GobArchiveTests
{
    [Fact]
    public void GobArchive_ReadsSyntheticIndex()
    {
        string dir = GobBuilder.TempDir();
        try
        {
            string path = Path.Combine(dir, "a.gob");
            File.WriteAllBytes(path, GobBuilder.Build((@"jkl\a.jkl", "abc")));
            using var gob = GobArchive.Open(path);
            Assert.Single(gob.Entries);
            Assert.Equal(@"jkl\a.jkl", gob.Entries[0].Path);
            Assert.Equal("abc", Encoding.ASCII.GetString(gob.Read(gob.Entries[0])));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void GobArchive_BadMagic_Throws()
    {
        string dir = GobBuilder.TempDir();
        try
        {
            string path = Path.Combine(dir, "bad.gob");
            byte[] bytes = GobBuilder.Build((@"jkl\a.jkl", "abc"));
            bytes[0] = (byte)'X';
            File.WriteAllBytes(path, bytes);
            Assert.Throws<InvalidDataException>(() => GobArchive.Open(path));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class JkInstallTests
{
    private const string RealDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";

    [SkippableFact]
    public void ListLevels_RealInstall_34Levels()
    {
        Skip.IfNot(File.Exists(Path.Combine(RealDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        var levels = JkInstall.ListLevels(RealDir);
        Assert.Equal(25, levels.Count(l => l.Kind == JkLevelKind.SinglePlayer));
        Assert.Equal(6, levels.Count(l => l.Kind == JkLevelKind.Multiplayer));
        Assert.Equal(3, levels.Count(l => l.Kind == JkLevelKind.Ctf));
        Assert.Contains(levels, l => l.Name == "01narshadda");
        Assert.Contains(levels, l => l.Name == "m10");
        Assert.Contains(levels, l => l.Name == "c1");
    }

    [Fact]
    public void ListLevels_SyntheticInstall_ClassifiesByEpisode()
    {
        string dir = GobBuilder.TempDir();
        try
        {
            GobBuilder.WriteGob(dir, "Episode", "JK1.GOB", (@"jkl\01Test.jkl", "x"), (@"mat\a.mat", "y"));
            GobBuilder.WriteGob(dir, "Episode", "JK1MP.GOB", (@"jkl\M10.jkl", "x"));
            var levels = JkInstall.ListLevels(dir);
            Assert.Contains(new JkLevelRef("01test", "JK1.GOB", JkLevelKind.SinglePlayer), levels);
            Assert.Contains(new JkLevelRef("m10", "JK1MP.GOB", JkLevelKind.Multiplayer), levels);
            Assert.Equal(2, levels.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void GameLocator_DetectsJediKnightFolder()
    {
        string steam = GobBuilder.TempDir();
        try
        {
            string jk = Path.Combine(steam, "steamapps", "common", "Star Wars Jedi Knight");
            GobBuilder.WriteGob(jk, "Episode", "JK1.GOB", (@"jkl\a.jkl", "x"));
            // Mysteries of the Sith ne doit pas etre pris pour JK.
            Directory.CreateDirectory(Path.Combine(steam, "steamapps", "common", "Jedi Knight Mysteries of the Sith", "Episode"));
            GamePaths p = GameLocator.Detect(() => steam);
            Assert.Equal(jk, p.JediKnightDir);
        }
        finally { Directory.Delete(steam, true); }
    }

    [Fact]
    public void KotorGames_JediKnight_PrefixAndName()
    {
        Assert.Equal("jk", KotorGames.MapPrefix(KotorGame.JediKnight));
        Assert.Equal("Jedi Knight", KotorGames.DisplayName(KotorGame.JediKnight));
    }

    [Fact]
    public void ConfigLoader_JediKnightDir_EnvOverridesConfig()
    {
        string dir = GobBuilder.TempDir();
        try
        {
            string cfg = Path.Combine(dir, "c.json");
            File.WriteAllText(cfg, "{ \"jediKnightDir\": \"D:\\\\fromcfg\" }");
            GamePaths none() => new(null, null, null, dir);
            Assert.Equal(@"D:\fromcfg", ConfigLoader.Load(cfg, _ => null, none).JediKnightDir);
            Assert.Equal(@"E:\fromenv", ConfigLoader.Load(cfg, n => n == "JK_DIR" ? @"E:\fromenv" : null, none).JediKnightDir);
        }
        finally { Directory.Delete(dir, true); }
    }
}
