namespace Jk.Core.Text;

/// <summary>Contenu utile d'un <c>episode.jk</c> : titre, type (1 solo, 2 deathmatch, 8 CTF) et niveaux dans l'ordre du fichier (noms de <c>.jkl</c> en minuscules, sans extension).</summary>
public sealed record EpisodeInfo(string Title, int Type, IReadOnlyList<string> Levels)
{
    public static readonly EpisodeInfo Empty = new("", 0, []);
}

/// <summary>
/// Lecture de <c>episode.jk</c> : premiere ligne = titre entre guillemets (traduit via le <c>.uni</c> s'il y est), <c>TYPE n</c>, puis des lignes
/// <c>idx: cd niveau LEVEL fichier.jkl ...</c> (les lignes <c>CUT</c> et <c>DECIDE</c> sont ignorees). Ne leve jamais d'exception.
/// </summary>
public static class EpisodeReader
{
    public static EpisodeInfo Read(string text, UniStrings? uni)
    {
        string title = "";
        int type = 0;
        var levels = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool first = true;
        foreach (var (_, line) in JkTokenizer.Lines(text ?? ""))
        {
            if (first)
            {
                first = false;
                if (line.StartsWith('"'))
                {
                    int end = line.IndexOf('"', 1);
                    string raw = end > 0 ? line[1..end] : line[1..];
                    title = uni?.Get(raw) ?? raw;
                    continue;
                }
            }
            string rest = JkTokenizer.TryStripIndex(line, out _, out string r) ? r : line;
            string[] t = JkTokenizer.Split(rest);
            if (t.Length >= 2 && t[0].Equals("TYPE", StringComparison.OrdinalIgnoreCase) && int.TryParse(t[1], out int ty)) type = ty;
            else if (t.Length >= 4 && t[2].Equals("LEVEL", StringComparison.OrdinalIgnoreCase) && t[3].EndsWith(".jkl", StringComparison.OrdinalIgnoreCase))
            {
                string name = t[3][..^4].ToLowerInvariant();
                if (name.Length > 0 && seen.Add(name)) levels.Add(name);   // un niveau peut revenir (variantes clair / obscur)
            }
        }
        return new EpisodeInfo(title, type, levels);
    }

    /// <summary>Nom lisible d'un niveau : la chaine du <c>.uni</c> (cle = nom du niveau), sinon le nom du fichier.</summary>
    public static string LevelName(string level, UniStrings? uni) => uni?.Get(level) is { Length: > 0 } s ? s : level;
}
