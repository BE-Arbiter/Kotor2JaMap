using Kotor.Core.Textures;

namespace Kotor.Core.Tests.Textures;

public class DxtDecoderTests
{
    [Fact]
    public void Dxt1_decodes_known_block()
    {
        byte[] block = [0x00, 0xF8, 0x1F, 0x00, 0, 0, 0, 0];
        byte[] px = DxtDecoder.DecodeDxt1(block, 4, 4);
        Assert.Equal(64, px.Length);
        for (int i = 0; i < 16; i++)
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, px.AsSpan(i * 4, 4).ToArray());
    }

    [Fact]
    public void Dxt1_three_color_mode_has_transparent_index3()
    {
        // c0 < c1 : l'indice 3 est transparent. c0 = 0x001F (bleu) < c1 = 0xF800.
        byte[] block = [0x1F, 0x00, 0x00, 0xF8, 0xFF, 0, 0, 0]; // ligne 0 : indice 3
        byte[] px = DxtDecoder.DecodeDxt1(block, 4, 4);
        Assert.Equal(0, px[3]);
        Assert.Equal(255, px[4 * 4 + 3]); // ligne 1 : indice 0, opaque
    }

    [Fact]
    public void Dxt5_decodes_alpha_block()
    {
        ulong idx = 0;
        for (int i = 0; i < 16; i++) idx |= 1UL << (3 * i);
        byte[] block = new byte[16];
        block[0] = 255; block[1] = 0;
        for (int i = 0; i < 6; i++) block[2 + i] = (byte)(idx >> (8 * i));
        block[8] = 0x00; block[9] = 0xF8; block[10] = 0x1F; block[11] = 0x00;
        byte[] px = DxtDecoder.DecodeDxt5(block, 4, 4);
        for (int i = 0; i < 16; i++) Assert.Equal(0, px[i * 4 + 3]);
    }

    [Fact]
    public void Dxt_non_multiple_of_4_is_cropped()
    {
        byte[] px = DxtDecoder.DecodeDxt1([0x00, 0xF8, 0x1F, 0x00, 0, 0, 0, 0], 2, 2);
        Assert.Equal(16, px.Length);
    }
}
