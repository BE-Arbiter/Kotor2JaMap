using System.Text.RegularExpressions;

namespace Swtor.Core.Myp;

/// <summary>Trouve les archives .tor d'une installation de SWTOR (dossier <c>Assets</c> et <c>swtor\retailclient</c>).</summary>
public static partial class SwtorGameLocator
{
    private const string GameFolderName = "Star Wars - The Old Republic";

    /// <summary>
    /// Archives .tor de l'installation. <paramref name="dir"/> peut etre la racine du jeu, son dossier <c>Assets</c> ou
    /// <c>swtor\retailclient</c>. Les archives du client de test (<c>swtor_test_*</c>) sont ignorees sauf si <paramref name="pts"/>.
    /// Ordre stable (nom, insensible a la casse) : en cas de doublon, l'archive suivante l'emporte.
    /// </summary>
    public static IReadOnlyList<string> FindArchives(string dir, bool pts = false)
    {
        var scan = new List<string>();
        string full = Path.GetFullPath(dir);
        var roots = new List<string> { full };
        if (Directory.GetParent(full) is { } p1) { roots.Add(p1.FullName); if (p1.Parent is { } p2) roots.Add(p2.FullName); }
        scan.Add(full);
        foreach (string r in roots)
        {
            scan.Add(Path.Combine(r, "Assets"));
            scan.Add(Path.Combine(r, "swtor", "retailclient"));
            if (pts) scan.Add(Path.Combine(r, "publictest", "retailclient"));
        }
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string d in scan.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(d)) continue;
            foreach (string f in Directory.EnumerateFiles(d, "*.tor", SearchOption.TopDirectoryOnly))
            {
                bool test = Path.GetFileName(f).Contains("swtor_test_", StringComparison.OrdinalIgnoreCase);
                if (test == pts) files.TryAdd(Path.GetFullPath(f), f);
            }
        }
        return files.Keys.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Installation de SWTOR detectee (variable SWTOR_PATH, Steam et ses bibliotheques, EA / BioWare), ou null.</summary>
    public static string? Detect(Func<string, string?>? getEnv = null, Func<string, string?>? readFile = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        readFile ??= p => File.Exists(p) ? File.ReadAllText(p) : null;
        foreach (string c in Candidates(getEnv, readFile))
            if (Directory.Exists(c) && FindArchives(c).Count > 0) return c;
        return null;
    }
    internal static IEnumerable<string> Candidates(Func<string, string?> getEnv, Func<string, string?> readFile)
    {
        if (getEnv("SWTOR_PATH") is { } env && !string.IsNullOrWhiteSpace(env)) yield return env;
        string pf86 = getEnv("ProgramFiles(x86)") is { Length: > 0 } a ? a : @"C:\Program Files (x86)";
        string pf = getEnv("ProgramFiles") is { Length: > 0 } b ? b : @"C:\Program Files";
        string steam = Path.Combine(pf86, "Steam");
        yield return Path.Combine(steam, "steamapps", "common", GameFolderName);
        if (readFile(Path.Combine(steam, "steamapps", "libraryfolders.vdf")) is { } vdf)
            foreach (string lib in ParseLibraryFolders(vdf))
                yield return Path.Combine(lib, "steamapps", "common", GameFolderName);
        yield return Path.Combine(pf86, "Electronic Arts", "BioWare", GameFolderName);
        yield return Path.Combine(pf, "Electronic Arts", "BioWare", GameFolderName);
        yield return Path.Combine(pf86, "EA Games", GameFolderName);
        yield return Path.Combine(pf86, "Origin Games", GameFolderName);
        yield return Path.Combine(pf, "EA Games", GameFolderName);
    }

    /// <summary>Les entrees « path » d'un libraryfolders.vdf (antislashs doubles ramenes a un).</summary>
    public static IReadOnlyList<string> ParseLibraryFolders(string vdfText) =>
        [.. PathEntry().Matches(vdfText ?? "").Select(m => m.Groups[1].Value.Replace(@"\\", @"\")).Where(p => p.Length > 0)];

    [GeneratedRegex("\"path\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathEntry();
}
