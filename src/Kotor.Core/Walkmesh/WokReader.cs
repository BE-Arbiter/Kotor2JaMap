using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Kotor.Core.Walkmesh;

/// <summary>
/// Lecteur BWM/WOK "BWM V1.0" (en-tete de 136 octets ; KotorBlender format/bwm/reader.py) :
/// @8 type, @72 nb sommets, @76 offset sommets, @80 nb faces, @84 offset indices, @88 offset materiaux.
/// </summary>
public static class WokReader
{
    private const int HeaderSize = 136;

    public static Walkmesh Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < HeaderSize || Encoding.ASCII.GetString(data, 0, 8) != "BWM V1.0")
            throw new InvalidDataException("BWM V1.0 attendu.");
        long vCount = U32(data, 72), vOff = U32(data, 76), fCount = U32(data, 80), fOff = U32(data, 84), mOff = U32(data, 88);
        Need(data, vOff, vCount * 12, "sommets");
        Need(data, fOff, fCount * 12, "faces");
        Need(data, mOff, fCount * 4, "materiaux");
        var verts = new Vector3[vCount];
        for (int i = 0; i < vCount; i++)
        {
            var s = data.AsSpan((int)vOff + i * 12);
            verts[i] = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(s), BinaryPrimitives.ReadSingleLittleEndian(s[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(s[8..]));
        }
        var tris = new int[fCount * 3];
        for (int i = 0; i < tris.Length; i++)
        {
            long v = U32(data, fOff + i * 4L);
            if (v >= vCount) throw new InvalidDataException($"BWM : indice de sommet {v} hors limites ({vCount}).");
            tris[i] = (int)v;
        }
        var mats = new int[fCount];
        for (int i = 0; i < fCount; i++) mats[i] = (int)U32(data, mOff + i * 4L);
        return new Walkmesh(verts, tris, mats);
    }

    private static long U32(byte[] d, long o) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan((int)o));

    private static void Need(byte[] d, long off, long len, string what)
    {
        if (off < 0 || len < 0 || off + len > d.Length) throw new InvalidDataException($"BWM tronque ({what}).");
    }
}
