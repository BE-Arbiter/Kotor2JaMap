namespace Kotor.Core.Export;

/// <summary>
/// Nom de carte JA : tous les fichiers de la carte portent ce nom, sans prefixe de jeu : dossier de travail, <c>maps/&lt;nom&gt;.bsp</c>,
/// <c>shaders/&lt;nom&gt;.shader</c>, <c>models/&lt;jeu&gt;/&lt;nom&gt;/</c> et <c>&lt;nom&gt;.pk3</c>. Normalise en minuscules puis valide
/// (<c>[a-z0-9_]</c>, 1 a 32 caracteres : chaque chemin reste sous MAX_QPATH = 64 sans troncature).
/// </summary>
public static class MapNames
{
    public const int MaxLength = 32;

    public static string Normalize(string name) => name.Trim().ToLowerInvariant();

    /// <summary>Message d'erreur si <paramref name="normalizedName"/> est inutilisable, sinon null.</summary>
    public static string? Validate(string normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName) || normalizedName.Length > MaxLength)
            return $"Invalid map name: \"{normalizedName}\" (1 to {MaxLength} characters expected).";
        foreach (char c in normalizedName)
            if (!(c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '_'))
                return $"Invalid map name: \"{normalizedName}\" (only a-z, 0-9 and _ are allowed).";
        return null;
    }
}
