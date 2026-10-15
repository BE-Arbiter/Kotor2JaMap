using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Kotor.Core.Gff;

/// <summary>
/// Lecteur GFF V3.2. Types : 0..9 numeriques (byte, sbyte, ushort, short, uint, int, ulong, long, float, double),
/// 10 string, 11 resref (string), 12 <see cref="GffLocString"/>, 13 byte[], 14 <see cref="GffStruct"/>,
/// 15 IReadOnlyList&lt;GffStruct&gt;, 16 <see cref="Quaternion"/> (fichier : w,x,y,z), 17 <see cref="Vector3"/>.
/// </summary>
public static class GffReader
{
    private static readonly Encoding Win1252 = Register();

    private static Encoding Register()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    private readonly record struct Header(
        int StructOff, int StructCount, int FieldOff, int FieldCount, int LabelOff, int LabelCount,
        int FieldDataOff, int FieldIdxOff, int ListIdxOff);

    public static GffStruct Read(byte[] data, out string fileType)
    {
        if (data.Length < 56) throw new InvalidDataException("GFF too short.");
        fileType = Encoding.ASCII.GetString(data, 0, 4);
        if (Encoding.ASCII.GetString(data, 4, 4) != "V3.2")
            throw new InvalidDataException("Unsupported GFF version: " + Encoding.ASCII.GetString(data, 4, 4));
        int U(int o) => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(o)));
        var h = new Header(U(8), U(12), U(16), U(20), U(24), U(28), U(32), U(40), U(48));
        var cache = new Dictionary<int, GffStruct>();
        return ReadStruct(data, h, 0, cache, 0);
    }

    private static GffStruct ReadStruct(byte[] d, Header h, int idx, Dictionary<int, GffStruct> cache, int depth)
    {
        if (depth > 64) throw new InvalidDataException("GFF: nesting too deep.");
        if (idx < 0 || idx >= h.StructCount) throw new InvalidDataException("GFF: invalid struct index " + idx);
        if (cache.TryGetValue(idx, out var c)) return c;
        int so = h.StructOff + idx * 12;
        uint type = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(so));
        uint dataOrOff = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(so + 4));
        int count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(so + 8)));
        var fields = new Dictionary<string, object?>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int fi = count == 1 ? checked((int)dataOrOff)
                : checked((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(h.FieldIdxOff + checked((int)dataOrOff) + i * 4)));
            if (fi < 0 || fi >= h.FieldCount) throw new InvalidDataException("GFF: invalid field index.");
            int fo = h.FieldOff + fi * 12;
            uint ft = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(fo));
            int li = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(fo + 4)));
            if (li < 0 || li >= h.LabelCount) throw new InvalidDataException("GFF: invalid label index.");
            string label = ReadLabel(d, h.LabelOff + li * 16);
            fields[label] = ReadField(d, h, ft, fo + 8, cache, depth);
        }
        var s = new GffStruct(type, fields);
        cache[idx] = s;
        return s;
    }

    private static string ReadLabel(byte[] d, int off)
    {
        int len = 0;
        while (len < 16 && d[off + len] != 0) len++;
        return Encoding.ASCII.GetString(d, off, len);
    }

    private static object? ReadField(byte[] d, Header h, uint type, int dataPos, Dictionary<int, GffStruct> cache, int depth)
    {
        var raw = d.AsSpan(dataPos, 4);
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        int fd = type >= 6 && type != 8 && type != 14 && type != 15 ? h.FieldDataOff + checked((int)v) : 0;
        switch (type)
        {
            case 0: return raw[0];
            case 1: return (sbyte)raw[0];
            case 2: return BinaryPrimitives.ReadUInt16LittleEndian(raw);
            case 3: return BinaryPrimitives.ReadInt16LittleEndian(raw);
            case 4: return v;
            case 5: return (int)v;
            case 6: return BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(fd));
            case 7: return BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(fd));
            case 8: return BitConverter.UInt32BitsToSingle(v);
            case 9: return BinaryPrimitives.ReadDoubleLittleEndian(d.AsSpan(fd));
            case 10:
            {
                int len = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(fd)));
                return Win1252.GetString(d, fd + 4, len);
            }
            case 11: return Encoding.ASCII.GetString(d, fd + 1, d[fd]);
            case 12: return ReadLocString(d, fd);
            case 13:
            {
                int len = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(fd)));
                return d.AsSpan(fd + 4, len).ToArray();
            }
            case 14: return ReadStruct(d, h, checked((int)v), cache, depth + 1);
            case 15:
            {
                int lo = h.ListIdxOff + checked((int)v);
                int n = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(lo)));
                var list = new List<GffStruct>(n);
                for (int i = 0; i < n; i++)
                    list.Add(ReadStruct(d, h, checked((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(lo + 4 + i * 4))), cache, depth + 1));
                return list;
            }
            case 16:
            {
                float w = F(d, fd), x = F(d, fd + 4), y = F(d, fd + 8), z = F(d, fd + 12);
                return new Quaternion(x, y, z, w);
            }
            case 17: return new Vector3(F(d, fd), F(d, fd + 4), F(d, fd + 8));
            default: throw new InvalidDataException("GFF: unknown field type " + type);
        }
    }

    private static float F(byte[] d, int o) => BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(o));

    private static GffLocString ReadLocString(byte[] d, int fd)
    {
        uint strRef = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(fd + 4));
        int n = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(fd + 8)));
        var map = new Dictionary<int, string>();
        int p = fd + 12;
        for (int i = 0; i < n; i++)
        {
            int id = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p)));
            int len = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 4)));
            map[id] = Win1252.GetString(d, p + 8, len);
            p += 8 + len;
        }
        return new GffLocString(strRef, map);
    }
}
