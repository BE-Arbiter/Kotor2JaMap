using System.Text;
using Kotor.Core.Textures;

namespace Kotor.Core.Tests.Textures;

public class TpcDecoderTests
{
    private static byte[] Tpc(uint dataSize, int w, int h, byte enc, byte mips, byte[] data, string? txi = null)
    {
        byte[] hdr = new byte[128];
        BitConverter.GetBytes(dataSize).CopyTo(hdr, 0);
        BitConverter.GetBytes((ushort)w).CopyTo(hdr, 8);
        BitConverter.GetBytes((ushort)h).CopyTo(hdr, 10);
        hdr[12] = enc; hdr[13] = mips;
        var all = hdr.Concat(data);
        if (txi is not null) all = all.Concat(Encoding.ASCII.GetBytes(txi));
        return all.ToArray();
    }

    [Fact]
    public void Tpc_uncompressed_rgba_roundtrip()
    {
        byte[] px = new byte[16];
        for (int i = 0; i < 16; i++) px[i] = (byte)(i * 7 + 1);
        var tex = TpcDecoder.Decode(Tpc(0, 2, 2, 4, 1, px, "blending additive\n"));
        Assert.Equal((2, 2), (tex.Width, tex.Height));
        Assert.True(tex.HasAlpha);
        Assert.Equal("additive", tex.Txi.Blending);
        Assert.Equal(16, tex.Rgba.Length);
        // Le fichier stocke les lignes de bas en haut : la ligne 0 de sortie est la derniere ligne du fichier.
        Assert.Equal(px[8..], tex.Rgba[..8]);
        Assert.Equal(px[..8], tex.Rgba[8..]);
    }

    [Fact]
    public void Tpc_rgb_gray_and_dxt1()
    {
        var rgb = TpcDecoder.Decode(Tpc(0, 1, 1, 2, 1, [10, 20, 30]));
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, rgb.Rgba);
        Assert.False(rgb.HasAlpha);
        var gray = TpcDecoder.Decode(Tpc(0, 1, 1, 1, 1, [77]));
        Assert.Equal(new byte[] { 77, 77, 77, 255 }, gray.Rgba);
        var dxt = TpcDecoder.Decode(Tpc(8, 4, 4, 2, 1, [0x00, 0xF8, 0x1F, 0x00, 0, 0, 0, 0]));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, dxt.Rgba.AsSpan(0, 4).ToArray());
    }

    [Fact]
    public void Tpc_skips_mips_to_find_txi()
    {
        // DXT1 8x8 (32 octets) + mip 4x4 (8 octets), 2 mips declarees.
        byte[] data = new byte[32 + 8];
        var tex = TpcDecoder.Decode(Tpc(32, 8, 8, 2, 2, data, "decal 1\n"));
        Assert.True(tex.Txi.Decal);
    }

    [Fact]
    public void Tpc_cube_keeps_first_face()
    {
        byte[] data = new byte[4 * 4 * 3 * 6];
        var tex = TpcDecoder.Decode(Tpc(0, 4, 24, 2, 1, data));
        Assert.Equal((4, 4), (tex.Width, tex.Height));
        Assert.True(tex.Txi.Raw.ContainsKey("cube"));
    }
}
