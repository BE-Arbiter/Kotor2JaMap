using System.Globalization;
using System.Text.RegularExpressions;

namespace Jk.Core.Text;

/// <summary>
/// Decoupage des fichiers texte de JK (.jkl, .cog...) : lignes sans commentaires (<c>#</c> jusqu'a la fin de ligne),
/// jetons separes par des blancs, prefixes d'index <c>12:</c> (le moteur tolere aussi <c>5;</c>), nombres en culture invariante.
/// </summary>
public static class JkTokenizer
{
    private static readonly Regex IndexPrefix = new(@"^\s*(-?\d+)\s*[:;]\s*(.*)$", RegexOptions.Compiled);

    /// <summary>Lignes non vides, sans commentaire, avec leur numero (base 1).</summary>
    public static IEnumerable<(int Number, string Text)> Lines(string text)
    {
        int n = 0;
        using var reader = new StringReader(text);
        string? raw;
        while ((raw = reader.ReadLine()) is not null)
        {
            n++;
            int hash = raw.IndexOf('#');
            string line = (hash >= 0 ? raw[..hash] : raw).Trim();
            if (line.Length > 0) yield return (n, line);
        }
    }

    public static string[] Split(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Retire un prefixe d'index (<c>12:</c>) ; renvoie false (et laisse la ligne intacte) s'il n'y en a pas.</summary>
    public static bool TryStripIndex(string line, out int index, out string rest)
    {
        Match m = IndexPrefix.Match(line);
        if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
        {
            rest = m.Groups[2].Value;
            return true;
        }
        index = -1;
        rest = line;
        return false;
    }

    public static float ParseFloat(string s) =>
        float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    public static int ParseInt(string s) =>
        int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

    /// <summary>Entier decimal ou hexadecimal (<c>0x4</c>), lu en non signe (les drapeaux peuvent depasser 2^31).</summary>
    public static uint ParseFlags(string s)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return unchecked((uint)long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));
    }
}
