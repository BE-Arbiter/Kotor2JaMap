using System.Text.RegularExpressions;

namespace Kotor.Core.Export;

/// <summary>Folders found on the machine. A null game folder means the game was not found.</summary>
public sealed record GamePaths(string? KotorDir, string? Kotor2Dir, string? JaBaseDir, string WorkDir, string? JediKnightDir = null);

/// <summary>Finds the KOTOR, KOTOR 2 and Jedi Academy folders through the Steam libraries. Only reads; never writes.</summary>
public static partial class GameLocator
{
    private const string FallbackSteamRoot = @"C:\Program Files (x86)\Steam";

    /// <summary>Default work folder: %LOCALAPPDATA%\Kotor2Ja\work.</summary>
    public static string DefaultWorkDir()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
        return Path.Combine(local, "Kotor2Ja", "work");
    }

    /// <summary>
    /// Reads steamapps\libraryfolders.vdf (Steam path from HKCU\Software\Valve\Steam\SteamPath, else C:\Program Files (x86)\Steam)
    /// and looks, in every library, for steamapps\common\swkotor, "Knights of the Old Republic II" (both need chitin.key)
    /// and "Jedi Academy*\GameData\base".
    /// </summary>
    public static GamePaths Detect(string? steamRootOverride = null) =>
        Detect(steamRootOverride is null ? ReadRegistrySteamPath : () => steamRootOverride);

    /// <summary>Same, with the Steam root supplied by <paramref name="steamPath"/> (tests do not touch the registry).</summary>
    public static GamePaths Detect(Func<string?> steamPath)
    {
        string work = DefaultWorkDir();
        try
        {
            string? root = steamPath();
            if (string.IsNullOrWhiteSpace(root)) root = FallbackSteamRoot;
            root = root.Replace('/', Path.DirectorySeparatorChar);

            var libraries = new List<string> { root };
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            try
            {
                if (File.Exists(vdf))
                    foreach (string lib in ParseLibraryFolders(File.ReadAllText(vdf)))
                        if (!libraries.Contains(lib, StringComparer.OrdinalIgnoreCase)) libraries.Add(lib);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { /* unreadable list: the Steam root alone */ }

            string? k1 = null, k2 = null, ja = null, jk = null;
            foreach (string lib in libraries)
            {
                // A library that throws (disconnected drive, access denied) is skipped; the others are still searched.
                try
                {
                    string common = Path.Combine(lib, "steamapps", "common");
                    if (!Directory.Exists(common)) continue;
                    k1 ??= GameFolder(common, "swkotor");
                    k2 ??= GameFolder(common, "Knights of the Old Republic II");
                    ja ??= FindJaBase(common);
                    jk ??= JediKnightFolder(common);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
            return new GamePaths(k1, k2, ja, work, jk);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new GamePaths(null, null, null, work);
        }
    }

    /// <summary>The "path" entries of a libraryfolders.vdf text (backslashes are unescaped).</summary>
    public static IReadOnlyList<string> ParseLibraryFolders(string vdfText)
    {
        var list = new List<string>();
        foreach (Match m in PathEntry().Matches(vdfText ?? ""))
        {
            string p = m.Groups[1].Value.Replace(@"\\", @"\");
            if (p.Length > 0) list.Add(p);
        }
        return list;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathEntry();

    private static string? GameFolder(string common, string name)
    {
        string dir = Path.Combine(common, name);
        return File.Exists(Path.Combine(dir, "chitin.key")) ? dir : null;
    }

    /// <summary>"Star Wars Jedi Knight" containing Episode\JK1.GOB (the Mysteries of the Sith folder has another name).</summary>
    private static string? JediKnightFolder(string common)
    {
        string dir = Path.Combine(common, "Star Wars Jedi Knight");
        return File.Exists(Path.Combine(dir, "Episode", "JK1.GOB")) ? dir : null;
    }

    private static string? FindJaBase(string common)
    {
        var found = new List<string>();
        foreach (string dir in Directory.EnumerateDirectories(common, "Jedi Academy*").Order(StringComparer.OrdinalIgnoreCase))
        {
            string b = Path.Combine(dir, "GameData", "base");
            if (Directory.Exists(b)) found.Add(b);
        }
        // The SWGL build is the target of the converter: prefer it over a vanilla install.
        return found.FirstOrDefault(p => p.Contains("SWGL", StringComparison.OrdinalIgnoreCase)) ?? found.FirstOrDefault();
    }

    private static string? ReadRegistrySteamPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath") as string;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }
}
