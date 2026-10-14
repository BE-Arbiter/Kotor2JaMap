using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Kotor.Core.Export;

/// <summary>
/// Encodeur PNG minimal : RGBA 8 bits, type de couleur 6, sans entrelacement, filtre 0 sur chaque ligne.
/// Aucune dependance externe (System.IO.Compression.ZLibStream pour l'IDAT, CRC-32 table en interne).
/// </summary>
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Ecrit une image RGBA (4 octets par pixel, lignes de haut en bas, sans padding) au format PNG.</summary>
    public static void WriteRgba(Stream output, int width, int height, ReadOnlySpan<byte> rgba)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "La largeur doit etre > 0.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "La hauteur doit etre > 0.");
        long expected = (long)width * height * 4;
        if (rgba.Length != expected)
            throw new ArgumentException($"Taille RGBA incorrecte : {rgba.Length} octets, attendu {expected}.", nameof(rgba));

        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[0..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..8], (uint)height);
        ihdr[8] = 8;   // profondeur : 8 bits par canal
        ihdr[9] = 6;   // type de couleur : RGBA
        ihdr[10] = 0;  // methode de compression : deflate
        ihdr[11] = 0;  // methode de filtre : adaptatif (filtre 0 utilise)
        ihdr[12] = 0;  // pas d'entrelacement
        WriteChunk(output, "IHDR", ihdr);

        WriteChunk(output, "IDAT", CompressScanlines(width, height, rgba));
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static byte[] CompressScanlines(int width, int height, ReadOnlySpan<byte> rgba)
    {
        int rowBytes = width * 4;
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            Span<byte> filter = stackalloc byte[1];
            filter[0] = 0; // filtre 0 (aucun) sur chaque ligne
            for (int y = 0; y < height; y++)
            {
                z.Write(filter);
                z.Write(rgba.Slice(y * rowBytes, rowBytes));
            }
        }
        return ms.ToArray();
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> typeBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(type, typeBytes);

        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        output.Write(len);
        output.Write(typeBytes);
        output.Write(data);

        // CRC-32 de PNG : couvre le type (4 octets) puis les donnees, sans les longueurs.
        uint crc = UpdateCrc(0xFFFFFFFFu, typeBytes);
        crc = UpdateCrc(crc, data) ^ 0xFFFFFFFFu;

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    // Etat interne du CRC-32 (polynome reflechi 0xEDB88320) : demarre a 0xFFFFFFFF, sortie xorée par l'appelant.
    private static uint UpdateCrc(uint state, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            state = CrcTable[(state ^ b) & 0xFF] ^ (state >> 8);
        return state;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
