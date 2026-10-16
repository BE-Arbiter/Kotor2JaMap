using System.Security.Cryptography;
using System.Text;

namespace Kotor.Core.Export;

/// <summary>
/// Seul endroit qui calcule le nom court d'un materiau. Le lump SHADERS du BSP limite un nom a 64 octets
/// (63 caracteres utiles) : <c>textures/kotor/</c> compris, un nom ne doit pas depasser <see cref="MaxFullLength"/>.
/// </summary>
public static class ShaderNames
{
    public const string Prefix = "textures/kotor/";
    /// <summary>Dossier des lightmaps dans le pk3.</summary>
    public const string LightmapPrefix = Prefix + "lm/";
    /// <summary>Nom (sans extension) de la texture de remplacement (damier magenta).</summary>
    public const string MissingTexture = "missing";
    public const int MaxFullLength = 63;
    // Le shader reference l'image par "map textures/kotor/<court>.tga" : ".tga" (4) doit aussi tenir dans MAX_QPATH.
    public static readonly int MaxMaterialLength = MaxFullLength - Prefix.Length - 4; // 44, Prefix = 15, ".tga" = 4

    // Prefixes sans information (tous les materiaux TFU les portent) : les retirer garde des noms lisibles apres troncature.
    private static readonly string[] NoisePrefixes = ["game_disc_levels_", "game_disc_"];

    /// <summary>Nom de materiau (sans prefixe) raccourci si besoin : debut + '_' + 8 hex du SHA-256 du nom complet.</summary>
    public static string Short(string materialName, KotorGame game = KotorGame.Kotor1)
    {
        ArgumentException.ThrowIfNullOrEmpty(materialName);
        int max = MaxFullLength - PrefixFor(game).Length - 4;
        if (materialName.Length <= max) return materialName;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(materialName));
        string hex = Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        string trimmed = materialName;
        foreach (string p in NoisePrefixes)
            if (trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase) && trimmed.Length > p.Length) { trimmed = trimmed[p.Length..]; break; }
        if (trimmed.Length <= max) return trimmed;
        return trimmed[..(max - 9)] + "_" + hex;
    }

    /// <summary>Nom complet du shader : <c>textures/kotor/&lt;nom court&gt;</c>.</summary>
    public static string FullName(string materialName, KotorGame game = KotorGame.Kotor1) => PrefixFor(game) + Short(materialName, game);

    /// <summary>Texture folder prefix of a game: <c>textures/kotor/</c> or <c>textures/kotor2/</c>.</summary>
    public static string PrefixFor(KotorGame game) => "textures/" + KotorGames.AssetFolder(game) + "/";

    public static string LightmapPrefixFor(KotorGame game) => PrefixFor(game) + "lm/";

    /// <summary>Removes the texture folder prefix of either game (null if the name has none).</summary>
    public static string? StripPrefix(string fullName)
    {
        foreach (KotorGame g in new[] { KotorGame.Kotor2, KotorGame.Kotor1, KotorGame.Swtor })
            if (fullName.StartsWith(PrefixFor(g), StringComparison.Ordinal)) return fullName[PrefixFor(g).Length..];
        return null;
    }

    /// <summary>Nom complet du shader de brouillard global d un niveau (partage entre l export et le compilateur).</summary>
    public static string FogName(string level) => FullName(level + "_fog");
}
