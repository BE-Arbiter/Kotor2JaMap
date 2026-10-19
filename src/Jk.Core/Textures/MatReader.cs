using Kotor.Core.Textures;

namespace Jk.Core.Textures;

/// <summary>Materiau JK decode : cellules (la cellule 0 est la texture de depart ; plusieurs cellules = animation).</summary>
public sealed record JkMaterial(string Name, IReadOnlyList<KotorTexture> Cels, bool IsSolidColor, bool HasTransparency);

/// <summary>
/// Lecteur de <c>.mat</c> JK. En-tete de 76 octets : "MAT ", version 0x32, type (0 = couleurs, sinon textures), nombre de cellules,
/// nombre de blocs de donnees, mode de couleur (0 indexe, 1 RGB, 2 RGBA), bits par pixel, puis bits / decalages gauche / decalages droite
/// de R, G, B et bits / decalage gauche / decalage droite de l'alpha. Ensuite un en-tete par cellule : 24 octets (couleurs : type, indice
/// de palette, 4 flottants, 0) ou 40 octets (textures). Pour les textures, chaque bloc de donnees commence par largeur, hauteur,
/// indicateur de transparence, 2 inconnus et nombre de mipmaps ; seul le premier niveau (le plus grand) est decode.
/// </summary>
public static class MatReader
{
    private const int HeaderSize = 76;
    private const int ColorCelSize = 24;
    private const int TextureCelSize = 40;
    private const int DataHeaderSize = 24;
    private const int SolidSize = 8;

    public static JkMaterial Read(string name, byte[] mat, CmpPalette palette)
    {
        if (mat.Length < HeaderSize || mat[0] != 'M' || mat[1] != 'A' || mat[2] != 'T' || mat[3] != ' ')
            throw new InvalidDataException($"Not a MAT file: {name}");
        int type = I32(mat, 8);
        int cels = I32(mat, 12);
        int blocks = I32(mat, 16);
        int mode = I32(mat, 20);
        int bpp = I32(mat, 24);
        if (cels < 0 || cels > 4096) throw new InvalidDataException($"Invalid cel count in {name}");

        if (type == 0) return ReadColors(name, mat, cels, palette);

        if (cels == 0) throw new InvalidDataException($"MAT without cels: {name}");
        if (bpp != 8 && bpp != 16) throw new InvalidDataException($"Unsupported MAT bit depth {bpp} in {name}");
        int dataStart = HeaderSize + cels * TextureCelSize;
        if (blocks < 1 || blocks > 4096 || dataStart > mat.Length) throw new InvalidDataException($"Truncated MAT: {name}");

        // Les blocs sont de taille variable (mipmaps) : on les parcourt une fois pour trouver leurs positions.
        var offsets = new List<int>();
        int pos = dataStart;
        for (int b = 0; b < blocks; b++)
        {
            if (pos + DataHeaderSize > mat.Length) throw new InvalidDataException($"Truncated MAT: {name}");
            int w = I32(mat, pos), h = I32(mat, pos + 4), mips = I32(mat, pos + 20);
            if (w <= 0 || h <= 0 || w > 8192 || h > 8192 || mips < 1 || mips > 16)
                throw new InvalidDataException($"Invalid MAT texture header in {name}");
            offsets.Add(pos);
            long size = DataHeaderSize;
            for (int m = 0; m < mips; m++) size += (long)Math.Max(1, w >> m) * Math.Max(1, h >> m) * (bpp / 8);
            if (pos + size > mat.Length) throw new InvalidDataException($"Truncated MAT: {name}");
            pos += (int)size;
        }

        var textures = new List<KotorTexture>(cels);
        bool anyAlpha = false;
        var channels = Channels(mat);
        for (int c = 0; c < cels; c++)
        {
            int at = offsets[Math.Min(c, offsets.Count - 1)];
            KotorTexture t = bpp == 8 ? Decode8(mat, at, palette) : Decode16(mat, at, mode, channels);
            anyAlpha |= t.HasAlpha;
            textures.Add(t);
        }
        return new JkMaterial(name, textures, false, anyAlpha);
    }

    private static JkMaterial ReadColors(string name, byte[] mat, int cels, CmpPalette palette)
    {
        if (cels == 0 || HeaderSize + cels * ColorCelSize > mat.Length) throw new InvalidDataException($"Truncated MAT: {name}");
        var list = new List<KotorTexture>(cels);
        for (int c = 0; c < cels; c++)
        {
            int index = I32(mat, HeaderSize + c * ColorCelSize + 4);
            var (r, g, b) = palette.Rgb(index);
            var rgba = new byte[SolidSize * SolidSize * 4];
            for (int i = 0; i < SolidSize * SolidSize; i++)
            {
                rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 255;
            }
            list.Add(new KotorTexture(SolidSize, SolidSize, rgba, false, TxiInfo.Empty));
        }
        return new JkMaterial(name, list, true, false);
    }

    private static KotorTexture Decode8(byte[] mat, int at, CmpPalette palette)
    {
        int w = I32(mat, at), h = I32(mat, at + 4);
        bool transparent = I32(mat, at + 8) != 0;
        int src = at + DataHeaderSize;
        var rgba = new byte[w * h * 4];
        bool hasAlpha = false;
        for (int i = 0; i < w * h; i++)
        {
            byte idx = mat[src + i];
            var (r, g, b) = palette.Rgb(idx);
            rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b;
            if (transparent && idx == 0) { rgba[i * 4 + 3] = 0; hasAlpha = true; }
            else rgba[i * 4 + 3] = 255;
        }
        return new KotorTexture(w, h, rgba, hasAlpha, TxiInfo.Empty);
    }

    private readonly record struct Chan(int Bits, int Shift);

    private static (Chan R, Chan G, Chan B, Chan A) Channels(byte[] mat)
    {
        Chan r = new(I32(mat, 28), I32(mat, 40));
        Chan g = new(I32(mat, 32), I32(mat, 44));
        Chan b = new(I32(mat, 36), I32(mat, 48));
        Chan a = new(I32(mat, 64), I32(mat, 68));
        // En-tete sans description des canaux : RGB565.
        if (r.Bits <= 0 && g.Bits <= 0 && b.Bits <= 0) return (new(5, 11), new(6, 5), new(5, 0), default);
        return (r, g, b, a);
    }

    private static KotorTexture Decode16(byte[] mat, int at, int mode, (Chan R, Chan G, Chan B, Chan A) ch)
    {
        int w = I32(mat, at), h = I32(mat, at + 4);
        bool transparent = I32(mat, at + 8) != 0;
        int src = at + DataHeaderSize;
        bool hasAlphaChannel = mode == 2 && ch.A.Bits > 0;
        var rgba = new byte[w * h * 4];
        bool hasAlpha = false;
        for (int i = 0; i < w * h; i++)
        {
            int v = mat[src + i * 2] | (mat[src + i * 2 + 1] << 8);
            rgba[i * 4] = Expand(v, ch.R);
            rgba[i * 4 + 1] = Expand(v, ch.G);
            rgba[i * 4 + 2] = Expand(v, ch.B);
            byte a = 255;
            if (hasAlphaChannel) a = Expand(v, ch.A);
            else if (transparent && v == 0) a = 0;
            rgba[i * 4 + 3] = a;
            if (a != 255) hasAlpha = true;
        }
        return new KotorTexture(w, h, rgba, hasAlpha, TxiInfo.Empty);
    }

    /// <summary>Extrait un canal de <c>Bits</c> bits a la position <c>Shift</c> et le ramene sur 0..255.</summary>
    private static byte Expand(int value, Chan c)
    {
        if (c.Bits <= 0) return 255;
        int max = (1 << c.Bits) - 1;
        int v = (value >> c.Shift) & max;
        return (byte)((v * 255 + max / 2) / max);
    }

    private static int I32(byte[] b, int o) => BitConverter.ToInt32(b, o);
}
