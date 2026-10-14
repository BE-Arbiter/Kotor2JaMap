namespace Kotor.Core.Textures;

/// <summary>Decodeurs DXT1 et DXT5 vers RGBA. Les dimensions non multiples de 4 sont rognees.</summary>
public static class DxtDecoder
{
    public static byte[] DecodeDxt1(ReadOnlySpan<byte> d, int w, int h) => Decode(d, w, h, false);

    public static byte[] DecodeDxt5(ReadOnlySpan<byte> d, int w, int h) => Decode(d, w, h, true);

    private static byte[] Decode(ReadOnlySpan<byte> d, int w, int h, bool dxt5)
    {
        int bw = (w + 3) / 4, bh = (h + 3) / 4, bs = dxt5 ? 16 : 8;
        if (d.Length < (long)bw * bh * bs)
            throw new InvalidDataException($"Donnees DXT tronquees ({d.Length} octets, {(long)bw * bh * bs} attendus).");
        byte[] rgba = new byte[checked(w * h * 4)];
        Span<byte> alpha = stackalloc byte[16];
        Span<byte> pal = stackalloc byte[16];
        for (int by = 0; by < bh; by++)
        for (int bx = 0; bx < bw; bx++)
        {
            var blk = d.Slice((by * bw + bx) * bs, bs);
            var cb = dxt5 ? blk[8..] : blk;
            int c0 = cb[0] | (cb[1] << 8), c1 = cb[2] | (cb[3] << 8);
            Expand(c0, pal[0..]);
            Expand(c1, pal[4..]);
            bool threeColor = !dxt5 && c0 <= c1;
            for (int k = 0; k < 3; k++)
            {
                pal[8 + k] = threeColor ? (byte)((pal[k] + pal[4 + k]) / 2) : (byte)((2 * pal[k] + pal[4 + k]) / 3);
                pal[12 + k] = threeColor ? (byte)0 : (byte)((pal[k] + 2 * pal[4 + k]) / 3);
            }
            pal[3] = pal[7] = pal[11] = 255;
            pal[15] = threeColor ? (byte)0 : (byte)255;

            if (dxt5) BuildAlpha(blk, alpha);
            uint bits = (uint)(cb[4] | (cb[5] << 8) | (cb[6] << 16) | (cb[7] << 24));
            for (int py = 0; py < 4; py++)
            for (int px = 0; px < 4; px++)
            {
                int x = bx * 4 + px, y = by * 4 + py;
                int i = py * 4 + px;
                int idx = (int)((bits >> (2 * i)) & 3);
                if (x >= w || y >= h) continue;
                int o = (y * w + x) * 4;
                rgba[o] = pal[idx * 4]; rgba[o + 1] = pal[idx * 4 + 1]; rgba[o + 2] = pal[idx * 4 + 2];
                rgba[o + 3] = dxt5 ? alpha[i] : pal[idx * 4 + 3];
            }
        }
        return rgba;
    }

    private static void Expand(int c, Span<byte> o)
    {
        int r = (c >> 11) & 31, g = (c >> 5) & 63, b = c & 31;
        o[0] = (byte)((r << 3) | (r >> 2));
        o[1] = (byte)((g << 2) | (g >> 4));
        o[2] = (byte)((b << 3) | (b >> 2));
    }

    private static void BuildAlpha(ReadOnlySpan<byte> blk, Span<byte> alpha)
    {
        int a0 = blk[0], a1 = blk[1];
        Span<int> tab = stackalloc int[8];
        tab[0] = a0; tab[1] = a1;
        if (a0 > a1)
            for (int i = 1; i <= 6; i++) tab[1 + i] = ((7 - i) * a0 + i * a1) / 7;
        else
        {
            for (int i = 1; i <= 4; i++) tab[1 + i] = ((5 - i) * a0 + i * a1) / 5;
            tab[6] = 0; tab[7] = 255;
        }
        ulong bits = 0;
        for (int i = 0; i < 6; i++) bits |= (ulong)blk[2 + i] << (8 * i);
        for (int i = 0; i < 16; i++) alpha[i] = (byte)tab[(int)((bits >> (3 * i)) & 7)];
    }
}
