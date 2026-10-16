namespace Swtor.Core;

/// <summary>
/// Resout un chemin de ressource SWTOR ("/resources/art/..." ou "art/...") vers un fichier du dossier extrait.
/// Insensible a la casse, accepte "\" et "/".
/// </summary>
public sealed class SwtorAssets
{
    /// <summary>Dossier par defaut des ressources extraites.</summary>
    public const string DefaultRoot = @"C:\jka_tor_assets\resources";

    private readonly Dictionary<string, string[]> _dirCache = new(StringComparer.OrdinalIgnoreCase);

    public string Root { get; }

    public SwtorAssets(string? root = null)
    {
        string? fromEnv = Environment.GetEnvironmentVariable("SWTOR_ASSETS");
        Root = root ?? (string.IsNullOrWhiteSpace(fromEnv) ? DefaultRoot : fromEnv);
    }

    /// <summary>Vrai si le dossier de ressources existe.</summary>
    public bool Exists => Directory.Exists(Root);

    /// <summary>Normalise un chemin de ressource : separateurs "/", sans "/resources/" ni "/" initial.</summary>
    public static string Normalize(string resourcePath)
    {
        string p = resourcePath.Replace('\\', '/').TrimStart('/');
        if (p.StartsWith("resources/", StringComparison.OrdinalIgnoreCase)) p = p["resources/".Length..];
        return p;
    }

    /// <summary>Chemin disque du fichier, ou null s'il n'existe pas.</summary>
    public string? Resolve(string resourcePath)
    {
        string[] parts = Normalize(resourcePath).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Contains("..")) return null;
        string current = Root;
        for (int i = 0; i < parts.Length; i++)
        {
            string direct = Path.Combine(current, parts[i]);
            bool last = i == parts.Length - 1;
            if (last ? File.Exists(direct) : Directory.Exists(direct)) { current = direct; continue; }
            // Recherche insensible a la casse (le disque est deja insensible sous Windows ; utile ailleurs).
            string? match = Directory.Exists(current)
                ? Directory.EnumerateFileSystemEntries(current).FirstOrDefault(e =>
                    string.Equals(Path.GetFileName(e), parts[i], StringComparison.OrdinalIgnoreCase)
                    && (last ? File.Exists(e) : Directory.Exists(e)))
                : null;
            if (match is null) return null;
            current = match;
        }
        return current;
    }

    /// <summary>Lit le fichier ou renvoie null.</summary>
    public byte[]? ReadBytes(string resourcePath) => Resolve(resourcePath) is { } p ? File.ReadAllBytes(p) : null;
}
