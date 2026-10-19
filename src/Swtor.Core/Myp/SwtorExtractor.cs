namespace Swtor.Core.Myp;

/// <summary>Resultat d'une extraction.</summary>
/// <param name="Entries">Entrees lues dans les archives.</param>
/// <param name="Unnamed">Entrees dont le hash n'est pas dans la liste de noms.</param>
/// <param name="Selected">Entrees nommees retenues par le filtre (apres doublons).</param>
/// <param name="Written">Fichiers ecrits.</param>
/// <param name="Skipped">Fichiers deja presents avec la bonne taille.</param>
/// <param name="Failed">Fichiers en erreur (lecture, decompression, disque).</param>
public sealed record SwtorExtractResult(int Archives, int Entries, int Unnamed, int Selected, int Written, int Skipped, int Failed,
    long Bytes, IReadOnlyList<string> Errors);

/// <summary>
/// Extrait les fichiers des archives .tor vers un dossier de ressources (la racine « resources » lue par <see cref="SwtorAssets"/>).
/// Les noms viennent d'une <see cref="MypNameList"/> ; les fichiers inconnus sont ignores.
/// </summary>
public static class SwtorExtractor
{
    /// <summary>
    /// Dossiers lus par le convertisseur : zones (<c>world</c>), base de donnees du jeu (<c>systemgenerated</c>), modeles, materiaux et
    /// textures (<c>art</c>), chaines de texte. Le reste (sons, animations, interface, personnages hors scope...) n'est pas extrait.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultPrefixes =
        ["world/", "systemgenerated/", "art/", "gamedata/str/", "fr-fr/str/", "en-us/str/", "de-de/str/"];

    /// <summary>Normalise un filtre : separateurs « / », sans « /resources/ » ni « / » initial, avec « / » final pour un dossier.</summary>
    public static string NormalizePrefix(string prefix) => SwtorAssets.Normalize(prefix.Trim());

    /// <summary>
    /// <paramref name="prefixes"/> : dossiers (ou debuts de chemin) a extraire, vide ou null = tout. Doublons entre archives : la derniere
    /// (ordre de <paramref name="archives"/>) l'emporte. <paramref name="overwrite"/> faux : un fichier de meme taille est conserve.
    /// </summary>
    public static SwtorExtractResult Extract(IReadOnlyList<string> archives, MypNameList names, string outRoot,
        IEnumerable<string>? prefixes = null, bool overwrite = false, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(archives);
        using var source = new SwtorGameSource(archives);
        return source.Extract(names, outRoot, prefixes, overwrite, progress, ct);
    }

    /// <summary>« /resources/a/b.gr2 » -> « a/b.gr2 » ; null si le nom est hors de « /resources/ » ou dangereux (« .. », ':', chemin absolu).</summary>
    internal static string? RelativePath(string fullName)
    {
        if (!fullName.StartsWith("/resources/", StringComparison.OrdinalIgnoreCase)) return null;
        string rel = fullName["/resources/".Length..];
        if (rel.Length == 0 || rel.Contains(':') || rel.Contains('\\') || rel.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
        string[] parts = rel.Split('/');
        if (parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.EndsWith(' ') || p.EndsWith('.'))) return null;
        return rel;
    }
}
