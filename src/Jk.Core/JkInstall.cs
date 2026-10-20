using System.Text;
using Jk.Core.Gob;
using Jk.Core.Text;

namespace Jk.Core;

public enum JkLevelKind { SinglePlayer, Multiplayer, Ctf }

/// <summary>Un niveau : nom (nom du .jkl en minuscules, sans extension), GOB d'episode qui le contient et type.</summary>
public sealed record JkLevelRef(string Name, string EpisodeGob, JkLevelKind Kind);

/// <summary>Un niveau et son nom lisible (chaine du <c>.uni</c> de l'episode, sinon nom du fichier).</summary>
public sealed record JkLevelEntry(JkLevelRef Level, string DisplayName);

/// <summary>Lecture d'une installation de Jedi Knight (lecture seule).</summary>
public static class JkInstall
{
    private static readonly (string Gob, JkLevelKind Kind)[] Episodes =
    {
        ("JK1.GOB", JkLevelKind.SinglePlayer),
        ("JK1MP.GOB", JkLevelKind.Multiplayer),
        ("JK1CTF.GOB", JkLevelKind.Ctf),
    };

    /// <summary>Le dossier contient-il une installation de JK (<c>Episode\JK1.GOB</c>) ? Faux pour un chemin vide, absent ou illisible.</summary>
    public static bool IsInstalled(string? jkDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jkDir)) return false;
            string episodeDir = Path.Combine(jkDir.Trim(), "Episode");
            return Directory.Exists(episodeDir)
                && Directory.EnumerateFiles(episodeDir).Any(f => string.Equals(Path.GetFileName(f), "JK1.GOB", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    /// <summary>Niveaux (<c>jkl\*.jkl</c>) de chaque GOB d'episode present dans <c>Episode\</c> ; un GOB absent est ignore.</summary>
    public static IReadOnlyList<JkLevelRef> ListLevels(string jkDir)
    {
        var levels = new List<JkLevelRef>();
        string episodeDir = Path.Combine(jkDir, "Episode");
        if (!Directory.Exists(episodeDir)) return levels;
        foreach (var (gobName, kind) in Episodes)
        {
            string? path = Directory.EnumerateFiles(episodeDir)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), gobName, StringComparison.OrdinalIgnoreCase));
            if (path is null) continue;
            using GobArchive gob = GobArchive.Open(path);
            foreach (GobEntry e in gob.Entries)
            {
                string p = e.Path.Replace('/', '\\');
                if (!p.StartsWith("jkl\\", StringComparison.OrdinalIgnoreCase) || !p.EndsWith(".jkl", StringComparison.OrdinalIgnoreCase)) continue;
                string name = p.Substring(4, p.Length - 8).ToLowerInvariant();
                if (name.Contains('\\')) continue;
                levels.Add(new JkLevelRef(name, gobName, kind));
            }
        }
        return levels;
    }

    /// <summary>
    /// Noms lisibles des niveaux : pour chaque GOB d'episode, l'ordre de <c>episode.jk</c> (les niveaux qui n'y sont pas gardent leur ordre dans le GOB, a la suite)
    /// et la chaine de <c>misc\cogstrings.uni</c> ; sans l'un ou l'autre, le nom du fichier. Un episode illisible garde simplement les noms de fichiers.
    /// </summary>
    public static IReadOnlyList<JkLevelEntry> LevelNames(string jkDir, IReadOnlyList<JkLevelRef> levels)
    {
        var result = new List<JkLevelEntry>();
        string episodeDir = Path.Combine(jkDir, "Episode");
        foreach (var group in levels.GroupBy(l => l.EpisodeGob, StringComparer.OrdinalIgnoreCase))
        {
            UniStrings? uni = null;
            IReadOnlyList<string> order = [];
            try
            {
                string? path = Directory.Exists(episodeDir)
                    ? Directory.EnumerateFiles(episodeDir).FirstOrDefault(f => string.Equals(Path.GetFileName(f), group.Key, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (path is not null)
                {
                    using GobArchive gob = GobArchive.Open(path);
                    GobEntry? uniEntry = Find(gob, @"misc\cogstrings.uni");
                    if (uniEntry is not null) uni = UniStrings.Parse(Encoding.Latin1.GetString(gob.Read(uniEntry)));
                    GobEntry? epEntry = Find(gob, "episode.jk");
                    if (epEntry is not null) order = EpisodeReader.Read(Encoding.Latin1.GetString(gob.Read(epEntry)), uni).Levels;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }

            var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in order) position.TryAdd(name, position.Count);
            foreach (JkLevelRef level in group.OrderBy(l => position.TryGetValue(l.Name, out int p) ? p : int.MaxValue))   // tri stable
                result.Add(new JkLevelEntry(level, EpisodeReader.LevelName(level.Name, uni)));
        }
        return result;
    }

    private static GobEntry? Find(GobArchive gob, string path) =>
        gob.Entries.FirstOrDefault(e => string.Equals(e.Path.Replace('/', '\\'), path, StringComparison.OrdinalIgnoreCase));
}
