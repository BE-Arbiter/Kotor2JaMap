using Kotor.Core.Export;

namespace Kotor.Core.Tests.Export;

public class GameLocatorTests
{
    [Fact]
    public void ParseLibraryFolders_reads_paths()
    {
        const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t\t\"label\"\t\t\"\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"32370\"\t\t\"1\"\n\t\t}\n\t}\n"
            + "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}\n";
        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], GameLocator.ParseLibraryFolders(vdf));
    }

    private static string Temp() => Path.Combine(Path.GetTempPath(), "steam_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Detect_finds_games_in_fake_library()
    {
        string steam = Temp(), lib2 = Temp();
        try
        {
            Directory.CreateDirectory(Path.Combine(steam, "steamapps", "common", "swkotor"));
            File.WriteAllText(Path.Combine(steam, "steamapps", "common", "swkotor", "chitin.key"), "");
            string c2 = Path.Combine(lib2, "steamapps", "common");
            Directory.CreateDirectory(Path.Combine(c2, "Knights of the Old Republic II"));
            File.WriteAllText(Path.Combine(c2, "Knights of the Old Republic II", "chitin.key"), "");
            Directory.CreateDirectory(Path.Combine(c2, "Jedi Academy - SWGL_DEV", "GameData", "base"));
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
                "\"libraryfolders\"\n{\n \"0\" { \"path\" \"" + steam.Replace(@"\", @"\\") + "\" }\n \"1\" { \"path\" \"" + lib2.Replace(@"\", @"\\") + "\" }\n}\n");

            GamePaths p = GameLocator.Detect(() => steam);
            Assert.Equal(Path.Combine(steam, "steamapps", "common", "swkotor"), p.KotorDir);
            Assert.Equal(Path.Combine(c2, "Knights of the Old Republic II"), p.Kotor2Dir);
            Assert.Equal(Path.Combine(c2, "Jedi Academy - SWGL_DEV", "GameData", "base"), p.JaBaseDir);
            Assert.EndsWith(Path.Combine("Kotor2Ja", "work"), p.WorkDir);
            Assert.Equal(p.KotorDir, GameLocator.Detect(steam).KotorDir);
        }
        finally
        {
            foreach (string d in new[] { steam, lib2 }) if (Directory.Exists(d)) Directory.Delete(d, true);
        }
    }

    [Fact]
    public void Detect_missing_games_null()
    {
        string steam = Temp();
        try
        {
            Directory.CreateDirectory(Path.Combine(steam, "steamapps", "common"));
            GamePaths p = GameLocator.Detect(() => steam);
            Assert.Null(p.KotorDir);
            Assert.Null(p.Kotor2Dir);
            Assert.Null(p.JaBaseDir);
            Assert.False(string.IsNullOrEmpty(p.WorkDir));
            // a folder without chitin.key is not a game install
            Directory.CreateDirectory(Path.Combine(steam, "steamapps", "common", "swkotor"));
            Assert.Null(GameLocator.Detect(() => steam).KotorDir);
        }
        finally { if (Directory.Exists(steam)) Directory.Delete(steam, true); }
    }
}
