using Kotor.Core.Export;

namespace Kotor.Core.Tests;

public sealed class TgaReaderTests
{
    [Fact]
    public void Round_trip_with_TgaWriter()
    {
        byte[] rgba = new byte[3 * 2 * 4];
        for (int i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i * 11 + 3);
        using var ms = new MemoryStream();
        TgaWriter.Write(ms, 3, 2, rgba);
        ms.Position = 0;
        var (w, h, data) = TgaReader.Read(ms);
        Assert.Equal(3, w);
        Assert.Equal(2, h);
        Assert.Equal(rgba, data);
    }

    [Fact]
    public void Reads_24_bit_top_left_origin()
    {
        // 2x1, 24 bits, origine en haut : pixels BGR (3,2,1) puis (6,5,4).
        byte[] file = new byte[18 + 6];
        file[2] = 2; file[12] = 2; file[14] = 1; file[16] = 24; file[17] = 0x20;
        byte[] px = [3, 2, 1, 6, 5, 4];
        px.CopyTo(file, 18);
        var (w, h, rgba) = TgaReader.Read(new MemoryStream(file));
        Assert.Equal((2, 1), (w, h));
        Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }, rgba);
    }

    [Fact]
    public void Rejects_compressed_tga()
    {
        byte[] file = new byte[18 + 4];
        file[2] = 10; file[12] = 1; file[14] = 1; file[16] = 32;
        Assert.Throws<InvalidDataException>(() => TgaReader.Read(new MemoryStream(file)));
    }
}
