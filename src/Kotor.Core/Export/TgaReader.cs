namespace Kotor.Core.Export;

/// <summary>Lit une image TGA non compressee (type 2), 24 ou 32 bits. Complement de <see cref="TgaWriter"/>.</summary>
public static class TgaReader
{
    private const int HeaderSize = 18;

    /// <summary>Renvoie la largeur, la hauteur et les pixels en RGBA, ligne du haut en premier.</summary>
    /// <exception cref="InvalidDataException">Format non gere (compresse, palette, autre profondeur) ou fichier tronque.</exception>
    public static (int Width, int Height, byte[] Rgba) Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        byte[] h = new byte[HeaderSize];
        ReadExactly(input, h, "en-tete");

        int idLength = h[0];
        int colorMapType = h[1];
        int imageType = h[2];
        int colorMapLength = h[5] | (h[6] << 8);
        int colorMapEntryBits = h[7];
        int width = h[12] | (h[13] << 8);
        int height = h[14] | (h[15] << 8);
        int bits = h[16];
        bool topToBottom = (h[17] & 0x20) != 0;
        bool rightToLeft = (h[17] & 0x10) != 0;

        if (imageType != 2) throw new InvalidDataException($"TGA de type {imageType} non gere (seul le type 2, non compresse, l'est).");
        if (bits is not (24 or 32)) throw new InvalidDataException($"TGA {bits} bits non gere (24 ou 32).");
        if (width <= 0 || height <= 0) throw new InvalidDataException("TGA de dimensions nulles.");

        long skip = idLength + (colorMapType != 0 ? (long)colorMapLength * ((colorMapEntryBits + 7) / 8) : 0);
        if (skip > 0) ReadExactly(input, new byte[skip], "identifiant/palette");

        int bpp = bits / 8;
        byte[] raw = new byte[checked(width * height * bpp)];
        ReadExactly(input, raw, "pixels");

        byte[] rgba = new byte[checked(width * height * 4)];
        for (int y = 0; y < height; y++)
        {
            int srcRow = (topToBottom ? y : height - 1 - y) * width * bpp;
            int dstRow = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int s = srcRow + (rightToLeft ? width - 1 - x : x) * bpp;
                int d = dstRow + x * 4;
                rgba[d] = raw[s + 2];
                rgba[d + 1] = raw[s + 1];
                rgba[d + 2] = raw[s];
                rgba[d + 3] = bpp == 4 ? raw[s + 3] : (byte)255;
            }
        }
        return (width, height, rgba);
    }

    private static void ReadExactly(Stream input, byte[] buffer, string what)
    {
        try { input.ReadExactly(buffer); }
        catch (EndOfStreamException) { throw new InvalidDataException($"TGA tronque ({what})."); }
    }
}
