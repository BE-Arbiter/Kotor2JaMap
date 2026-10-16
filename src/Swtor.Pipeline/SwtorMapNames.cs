using System.Security.Cryptography;
using System.Text;
using Kotor.Core.Export;

namespace Swtor.Pipeline;

/// <summary>Nom de carte JA d'une zone SWTOR : nom interne mis en minuscules, restreint a <c>[a-z0-9_]</c>, au plus <see cref="MapNames.MaxLength"/> caracteres.</summary>
public static class SwtorMapNames
{
    /// <summary>
    /// <paramref name="internalName"/> (ex. <c>war_planworking</c>) + suffixe optionnel de rooms. Tout caractere hors <c>[a-z0-9_]</c> devient '_'
    /// (les '_' consecutifs sont fusionnes). Trop long : debut + '_' + 6 hex du SHA-256 du nom complet.
    /// </summary>
    public static string FromInternalName(string internalName, IReadOnlyCollection<string>? rooms = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(internalName);
        string full = Sanitize(internalName);
        if (rooms is { Count: > 0 })
            full += "_" + Sanitize(string.Join("_", rooms.OrderBy(r => r, StringComparer.OrdinalIgnoreCase)));
        if (full.Length == 0) full = "area";
        if (full.Length <= MapNames.MaxLength) return full;
        string hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)), 0, 3).ToLowerInvariant();
        return full[..(MapNames.MaxLength - 7)].TrimEnd('_') + "_" + hex;
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s.Trim().ToLowerInvariant())
        {
            char c = ch is >= 'a' and <= 'z' or >= '0' and <= '9' ? ch : '_';
            if (c == '_' && sb.Length > 0 && sb[^1] == '_') continue;
            sb.Append(c);
        }
        return sb.ToString().Trim('_');
    }
}
