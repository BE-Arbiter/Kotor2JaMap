using Kotor.Core.Export;

namespace Kotor.Core.Tests;

public sealed class TgaWriterTests
{
    [Fact]
    public void Tga_header_is_valid()
    {
        using var ms = new MemoryStream();
        var rgba = new byte[2 * 2 * 4];

        TgaWriter.Write(ms, 2, 2, rgba);

        var bytes = ms.ToArray();
        Assert.Equal(18 + 2 * 2 * 4, bytes.Length);
        Assert.Equal(2, bytes[2]);   // type : image couleur non compressee
        Assert.Equal(32, bytes[16]); // profondeur en bits
        Assert.Equal(2, BitConverter.ToUInt16(bytes, 12)); // largeur
        Assert.Equal(2, BitConverter.ToUInt16(bytes, 14)); // hauteur
    }

    [Fact]
    public void Tga_pixels_are_bgra()
    {
        using var ms = new MemoryStream();
        var rgba = new byte[] { 255, 0, 0, 255 }; // un pixel rouge opaque

        TgaWriter.Write(ms, 1, 1, rgba);

        var bytes = ms.ToArray();
        Assert.Equal(new byte[] { 0x00, 0x00, 0xFF, 0xFF }, bytes[18..22]);
    }
}
