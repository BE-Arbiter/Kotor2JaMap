namespace Kotor.Core.Export;

/// <summary>Ecrit une image TGA 32 bits non compressee (type 2), origine en bas a gauche.</summary>
public static class TgaWriter
{
    private const int HeaderSize = 18;

    /// <summary>
    /// Ecrit l'image dans <paramref name="output"/>. <paramref name="rgba"/> est en RGBA, ligne du haut en premier
    /// (ordre des images decodees) ; le fichier est en BGRA, ligne du bas en premier.
    /// </summary>
    public static void Write(Stream output, int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (width <= 0 || width > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0 || height > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(height));
        if (rgba.Length != checked(width * height * 4))
            throw new ArgumentException($"Taille RGBA incorrecte : {rgba.Length} octets pour {width}x{height}.", nameof(rgba));

        var buffer = new byte[HeaderSize + rgba.Length];

        // En-tete standard de 18 octets.
        buffer[2] = 2;                                   // type : couleur non compressee
        BitConverter.TryWriteBytes(buffer.AsSpan(12, 2), (ushort)width);
        BitConverter.TryWriteBytes(buffer.AsSpan(14, 2), (ushort)height);
        buffer[16] = 32;                                 // profondeur en bits
        buffer[17] = 0x08;                               // 8 bits alpha, origine en bas a gauche

        int rowBytes = width * 4;
        int dst = HeaderSize;
        for (int y = height - 1; y >= 0; y--)            // bas a gauche : la ligne du bas vient en premier
        {
            int src = y * rowBytes;
            for (int x = 0; x < rowBytes; x += 4, dst += 4)
            {
                buffer[dst] = rgba[src + x + 2];         // B
                buffer[dst + 1] = rgba[src + x + 1];     // G
                buffer[dst + 2] = rgba[src + x];         // R
                buffer[dst + 3] = rgba[src + x + 3];     // A
            }
        }

        output.Write(buffer, 0, buffer.Length);
    }
}
