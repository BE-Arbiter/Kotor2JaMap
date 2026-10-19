namespace Jk.Core.Textures;

/// <summary>
/// Palette de 256 couleurs d'un fichier <c>.cmp</c> de JK : en-tete de 64 octets ("CMP ", version, indicateur de transparence),
/// 768 octets RGB, puis 64 tables de lumiere de 256 octets (indice de palette eclairci) et, si l'indicateur est actif,
/// une table de transparence 256x256 (non utilisee ici).
/// </summary>
public sealed class CmpPalette
{
    private const int HeaderSize = 64;
    private const int PaletteSize = 256 * 3;
    private const int LightLevelCount = 64;

    private readonly byte[] _rgb;
    private readonly byte[]? _light;

    private CmpPalette(byte[] rgb, byte[]? light)
    {
        _rgb = rgb;
        _light = light;
    }

    /// <summary>Palette de repli : niveaux de gris (indice i = gris i), sans tables de lumiere.</summary>
    public static CmpPalette Default { get; } = BuildDefault();

    public bool HasLightTable => _light is not null;

    /// <summary>Nombre de niveaux de lumiere (0 sans table).</summary>
    public int LightLevels => _light is null ? 0 : LightLevelCount;

    public static CmpPalette Read(byte[] cmp)
    {
        if (cmp.Length < HeaderSize + PaletteSize || cmp[0] != 'C' || cmp[1] != 'M' || cmp[2] != 'P' || cmp[3] != ' ')
            throw new InvalidDataException("Not a CMP palette file.");
        var rgb = new byte[PaletteSize];
        Array.Copy(cmp, HeaderSize, rgb, 0, PaletteSize);
        byte[]? light = null;
        int lightStart = HeaderSize + PaletteSize;
        if (cmp.Length >= lightStart + LightLevelCount * 256)
        {
            light = new byte[LightLevelCount * 256];
            Array.Copy(cmp, lightStart, light, 0, light.Length);
        }
        return new CmpPalette(rgb, light);
    }

    public (byte R, byte G, byte B) Rgb(int index)
    {
        int o = (index & 0xFF) * 3;
        return (_rgb[o], _rgb[o + 1], _rgb[o + 2]);
    }

    /// <summary>Indice de palette de <paramref name="index"/> au niveau de lumiere donne (0 = pleine lumiere, 63 = noir) ; sans table, l'indice est inchange.</summary>
    public int LightedIndex(int level, int index)
    {
        if (_light is null) return index & 0xFF;
        level = Math.Clamp(level, 0, LightLevelCount - 1);
        return _light[level * 256 + (index & 0xFF)];
    }

    private static CmpPalette BuildDefault()
    {
        var rgb = new byte[PaletteSize];
        for (int i = 0; i < 256; i++) rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = (byte)i;
        return new CmpPalette(rgb, null);
    }
}
