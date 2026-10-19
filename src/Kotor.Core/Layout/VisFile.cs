using System.Globalization;

namespace Kotor.Core.Layout;

/// <summary>
/// Fichier VIS (texte) d'un module : pour chaque piece (modele du LYT) la liste des pieces visibles depuis elle.
/// Format : une ligne <c>piece n</c> suivie de <c>n</c> lignes indentees (une piece chacune). Noms en minuscules.
/// </summary>
public sealed class VisFile
{
    /// <summary>Piece (minuscules) -> pieces visibles depuis elle (hors elle-meme sauf si listee).</summary>
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Visible { get; }

    private VisFile(Dictionary<string, IReadOnlyCollection<string>> visible) { Visible = visible; }

    public static VisFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        string[] t = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        while (i < t.Length)
        {
            string room = t[i++].ToLowerInvariant();
            if (i >= t.Length || !int.TryParse(t[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
                throw new InvalidDataException($"Invalid VIS entry for room '{room}': visible-room count expected.");
            i++;
            if (i + n > t.Length) throw new InvalidDataException($"Invalid VIS entry for room '{room}': {n} room(s) announced, file truncated.");
            var list = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int k = 0; k < n; k++) list.Add(t[i++].ToLowerInvariant());
            // Une piece repetee : union.
            if (result.TryGetValue(room, out var prev)) list.UnionWith(prev);
            result[room] = list;
        }
        return new VisFile(result);
    }
}
