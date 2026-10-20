namespace Jk.Core.Text;

/// <summary>
/// Table de chaines d'un fichier <c>.uni</c> de JK (<c>misc\cogstrings.uni</c>) : lignes <c>"CLE" nombre "texte"</c>, commentaires <c>#</c> en debut de ligne
/// ou apres la chaine. Cles insensibles a la casse. Un guillemet fermant manquant (le jeu en contient) est toleree : la fin de ligne ferme la chaine.
/// </summary>
public sealed class UniStrings
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _map.Count;

    /// <summary>Texte de la cle, ou null.</summary>
    public string? Get(string key) => _map.TryGetValue(key, out string? v) ? v : null;

    public static UniStrings Parse(string text)
    {
        var uni = new UniStrings();
        using var reader = new StringReader(text ?? "");
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] != '"') continue;   // MSGS, END, commentaires
            int keyEnd = line.IndexOf('"', 1);
            if (keyEnd < 0) continue;
            string key = line[1..keyEnd];
            // Entre la cle et la chaine : le numero inutilise.
            int open = line.IndexOf('"', keyEnd + 1);
            if (key.Length == 0 || open < 0) continue;
            int close = line.IndexOf('"', open + 1);
            string value = close < 0 ? line[(open + 1)..].Trim() : line[(open + 1)..close];
            uni._map.TryAdd(key, value);
        }
        return uni;
    }
}
