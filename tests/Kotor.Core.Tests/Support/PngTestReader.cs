using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Kotor.Core.Tests.Support;

/// <summary>Pixel RGBA 8 bits.</summary>
internal readonly record struct TestPixel(byte R, byte G, byte B, byte A);

/// <summary>Image RGBA decodee : pixels ligne par ligne, 4 octets par pixel.</summary>
internal sealed record DecodedPng(int Width, int Height, byte[] Rgba)
{
    public TestPixel At(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return new TestPixel(Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]);
    }
}

/// <summary>
/// Lecteur PNG de test, independant de PngWriter : valide la signature et le CRC de chaque chunk,
/// puis decode un PNG RGBA 8 bits non entrelace avec filtre 0 sur chaque ligne.
/// </summary>
internal static class PngTestReader
{
    private static readonly byte[] ExpectedSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static DecodedPng Read(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(ExpectedSignature))
            throw new InvalidDataException("Signature PNG invalide.");

        int pos = 8;
        int width = 0, height = 0;
        bool seenIhdr = false, seenIend = false;
        var idat = new MemoryStream();

        while (pos < png.Length)
        {
            if (seenIend) throw new InvalidDataException("Donnees apres le chunk IEND.");
            if (pos + 12 > png.Length) throw new InvalidDataException("Chunk tronque.");

            uint len = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos, 4));
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            if (pos + 12 + len > png.Length) throw new InvalidDataException($"Chunk {type} tronque.");

            ReadOnlySpan<byte> typeAndData = png.AsSpan(pos + 4, 4 + (int)len);
            ReadOnlySpan<byte> data = png.AsSpan(pos + 8, (int)len);
            uint storedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + (int)len, 4));
            uint actualCrc = Crc32(typeAndData);
            if (storedCrc != actualCrc)
                throw new InvalidDataException($"CRC invalide dans le chunk {type}.");

            switch (type)
            {
                case "IHDR":
                    if (seenIhdr) throw new InvalidDataException("IHDR en double.");
                    if (len != 13) throw new InvalidDataException("IHDR de taille incorrecte.");
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(data[0..4]);
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..8]);
                    if (data[8] != 8) throw new InvalidDataException($"Profondeur {data[8]} non geree (8 attendu).");
                    if (data[9] != 6) throw new InvalidDataException($"Type de couleur {data[9]} non gere (6 = RGBA attendu).");
                    if (data[10] != 0 || data[11] != 0) throw new InvalidDataException("Methode de compression ou de filtre inconnue.");
                    if (data[12] != 0) throw new InvalidDataException("Entrelacement non gere.");
                    seenIhdr = true;
                    break;
                case "IDAT":
                    if (!seenIhdr) throw new InvalidDataException("IDAT avant IHDR.");
                    idat.Write(data);
                    break;
                case "IEND":
                    if (len != 0) throw new InvalidDataException("IEND non vide.");
                    seenIend = true;
                    break;
                default:
                    throw new InvalidDataException($"Chunk inattendu : {type}.");
            }

            pos += 12 + (int)len;
        }

        if (!seenIhdr || !seenIend) throw new InvalidDataException("IHDR ou IEND manquant.");

        int rowBytes = width * 4;
        byte[] raw = new byte[(rowBytes + 1) * height];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            z.ReadExactly(raw);
            if (z.ReadByte() != -1) throw new InvalidDataException("Donnees d'image en trop.");
        }

        byte[] rgba = new byte[rowBytes * height];
        for (int y = 0; y < height; y++)
        {
            int src = y * (rowBytes + 1);
            if (raw[src] != 0) throw new InvalidDataException($"Ligne {y} : filtre {raw[src]} inattendu (0 attendu).");
            raw.AsSpan(src + 1, rowBytes).CopyTo(rgba.AsSpan(y * rowBytes, rowBytes));
        }

        return new DecodedPng(width, height, rgba);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }
}
