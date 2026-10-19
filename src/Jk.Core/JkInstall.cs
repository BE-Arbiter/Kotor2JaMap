using Jk.Core.Gob;

namespace Jk.Core;

public enum JkLevelKind { SinglePlayer, Multiplayer, Ctf }

/// <summary>Un niveau : nom (nom du .jkl en minuscules, sans extension), GOB d'episode qui le contient et type.</summary>
public sealed record JkLevelRef(string Name, string EpisodeGob, JkLevelKind Kind);

/// <summary>Lecture d'une installation de Jedi Knight (lecture seule).</summary>
public static class JkInstall
{
    private static readonly (string Gob, JkLevelKind Kind)[] Episodes =
    {
        ("JK1.GOB", JkLevelKind.SinglePlayer),
        ("JK1MP.GOB", JkLevelKind.Multiplayer),
        ("JK1CTF.GOB", JkLevelKind.Ctf),
    };

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
}
