using System.Text;

namespace Kotor.Core.Textures;

/// <summary>
/// Decodeur TPC (premier niveau de mip seulement). Entete de 128 octets, donnees a partir de l'octet 128,
/// texte TXI apres toutes les mips. Reference : xoreos tpc.cpp.
/// </summary>
public static class TpcDecoder
{
    private const int HeaderSize = 128;

    /// <summary>
    /// Vrai si le fichier stocke les lignes de bas en haut (convention OpenGL, xoreos ne retourne rien a la lecture
    /// et laisse le moteur interpreter v=0 comme le bas). Verifie visuellement sur des textures reelles.
    /// </summary>
    internal const bool StoredBottomUp = true;

    public static KotorTexture Decode(byte[] tpc)
    {
        ArgumentNullException.ThrowIfNull(tpc);
        if (tpc.Length < HeaderSize) throw new InvalidDataException("TPC tronque (en-tete).");
        uint dataSize = BitConverter.ToUInt32(tpc, 0);
        int w = BitConverter.ToUInt16(tpc, 8);
        int h = BitConverter.ToUInt16(tpc, 10);
        int enc = tpc[12];
        int mips = Math.Max(1, (int)tpc[13]);
        if (w == 0 || h == 0) throw new InvalidDataException("TPC de dimensions nulles.");

        bool compressed = dataSize > 0;
        bool cube = h == 6 * w;
        int faceH = cube ? w : h;
        int layers = cube ? 6 : 1;

        // Taille d'un niveau de mip pour la face (w x faceH niveau 0).
        int bpp;
        int blockBytes = 0;
        switch (enc)
        {
            case 1: bpp = 1; if (compressed) throw new InvalidDataException("TPC gris compresse non gere."); break;
            case 2: bpp = 3; blockBytes = compressed ? 8 : 0; break;
            case 4: bpp = 4; blockBytes = compressed ? 16 : 0; break;
            default: throw new InvalidDataException($"Encodage TPC {enc} non gere (1, 2 ou 4 seulement).");
        }

        long LevelSize(int lw, int lh) => blockBytes > 0
            ? (long)Math.Max(1, (lw + 3) / 4) * Math.Max(1, (lh + 3) / 4) * blockBytes
            : (long)lw * lh * bpp;

        long face0 = LevelSize(w, faceH);
        long total = 0;
        for (int l = 0; l < layers; l++)
        {
            int mw = w, mh = faceH;
            for (int m = 0; m < mips; m++)
            {
                total += LevelSize(mw, mh);
                mw = Math.Max(1, mw / 2); mh = Math.Max(1, mh / 2);
            }
        }
        if (HeaderSize + face0 > tpc.Length) throw new InvalidDataException("TPC tronque (pixels).");

        var data = new ReadOnlySpan<byte>(tpc, HeaderSize, (int)face0);
        byte[] rgba;
        bool hasAlpha;
        if (blockBytes == 8) { rgba = DxtDecoder.DecodeDxt1(data, w, faceH); hasAlpha = AnyTransparent(rgba); }
        else if (blockBytes == 16) { rgba = DxtDecoder.DecodeDxt5(data, w, faceH); hasAlpha = true; }
        else
        {
            rgba = new byte[checked(w * faceH * 4)];
            for (int i = 0, n = w * faceH; i < n; i++)
            {
                switch (bpp)
                {
                    case 1: rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = data[i]; rgba[i * 4 + 3] = 255; break;
                    case 3: rgba[i * 4] = data[i * 3]; rgba[i * 4 + 1] = data[i * 3 + 1]; rgba[i * 4 + 2] = data[i * 3 + 2]; rgba[i * 4 + 3] = 255; break;
                    default: Buffer.BlockCopy(tpc, HeaderSize + i * 4, rgba, i * 4, 4); break;
                }
            }
            hasAlpha = bpp == 4;
        }

        if (StoredBottomUp) FlipRows(rgba, w, faceH);

        string? txiText = null;
        long txiStart = HeaderSize + total;
        if (txiStart < tpc.Length)
            txiText = Encoding.ASCII.GetString(tpc, (int)txiStart, tpc.Length - (int)txiStart);
        var txi = TxiInfo.Parse(txiText);
        if (cube)
        {
            var raw = new Dictionary<string, string>(txi.Raw) { ["cube"] = "1" };
            txi = txi with { Raw = raw };
        }
        return new KotorTexture(w, faceH, rgba, hasAlpha, txi);
    }

    private static bool AnyTransparent(byte[] rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] != 255) return true;
        return false;
    }

    private static void FlipRows(byte[] rgba, int w, int h)
    {
        int stride = w * 4;
        byte[] tmp = new byte[stride];
        for (int y = 0; y < h / 2; y++)
        {
            int a = y * stride, b = (h - 1 - y) * stride;
            Buffer.BlockCopy(rgba, a, tmp, 0, stride);
            Buffer.BlockCopy(rgba, b, rgba, a, stride);
            Buffer.BlockCopy(tmp, 0, rgba, b, stride);
        }
    }
}
