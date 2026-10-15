namespace Kotor.Core.Export;

/// <summary>
/// Nom de carte JA : sert de nom de dossier de travail, de <c>maps/&lt;nom&gt;.bsp</c>, de <c>shaders/kotor_&lt;nom&gt;.shader</c> (K2 : kotor2_)
/// et de <c>kotor_&lt;nom&gt;.pk3</c>. Normalise en minuscules puis valide (<c>[a-z0-9_]</c>, 1 a 48 caracteres, sous MAX_QPATH = 64).
/// </summary>
public static class MapNames
{
    public const int MaxLength = 48;

    public static string Normalize(string name) => name.Trim().ToLowerInvariant();

    /// <summary>Message d'erreur si <paramref name="normalizedName"/> est inutilisable, sinon null.</summary>
    public static string? Validate(string normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName) || normalizedName.Length > MaxLength)
            return $"Nom de carte invalide : \"{normalizedName}\" (1 a {MaxLength} caracteres attendus).";
        foreach (char c in normalizedName)
            if (!(c is >= 'a' and <= 'z' || c is >= '0' and <= '9' || c == '_'))
                return $"Nom de carte invalide : \"{normalizedName}\" (seuls a-z, 0-9 et _ sont autorises).";
        return null;
    }
}
