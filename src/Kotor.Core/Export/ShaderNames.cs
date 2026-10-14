using System.Security.Cryptography;
using System.Text;

namespace Kotor.Core.Export;

/// <summary>
/// Seul endroit qui calcule le nom court d'un materiau. Le lump SHADERS du BSP limite un nom a 64 octets
/// (63 caracteres utiles) : <c>textures/tfu2ja/</c> compris, un nom ne doit pas depasser <see cref="MaxFullLength"/>.
/// </summary>
public static class ShaderNames
{
    public const string Prefix = "textures/tfu2ja/";
    public const int MaxFullLength = 63;
    // Le shader reference l'image par "map textures/tfu2ja/<court>.tga" : ".tga" (4) doit aussi tenir dans MAX_QPATH.
    public const int MaxMaterialLength = MaxFullLength - 16 - 4; // 43, Prefix = 16, ".tga" = 4

    // Prefixes sans information (tous les materiaux TFU les portent) : les retirer garde des noms lisibles apres troncature.
    private static readonly string[] NoisePrefixes = ["game_disc_levels_", "game_disc_"];

    /// <summary>Nom de materiau (sans prefixe) raccourci si besoin : debut + '_' + 8 hex du SHA-256 du nom complet.</summary>
    public static string Short(string materialName)
    {
        ArgumentException.ThrowIfNullOrEmpty(materialName);
        if (materialName.Length <= MaxMaterialLength) return materialName;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(materialName));
        string hex = Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
        string trimmed = materialName;
        foreach (string p in NoisePrefixes)
            if (trimmed.StartsWith(p, StringComparison.OrdinalIgnoreCase) && trimmed.Length > p.Length) { trimmed = trimmed[p.Length..]; break; }
        if (trimmed.Length <= MaxMaterialLength) return trimmed;
        return trimmed[..(MaxMaterialLength - 9)] + "_" + hex;
    }

    /// <summary>Nom complet du shader : <c>textures/tfu2ja/&lt;nom court&gt;</c>.</summary>
    public static string FullName(string materialName) => Prefix + Short(materialName);

    /// <summary>Nom complet du shader de brouillard global d un niveau (partage entre l export et le compilateur).</summary>
    public static string FogName(string level) => FullName(level + "_fog");
}
