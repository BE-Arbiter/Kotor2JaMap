using System.Text;

namespace Kotor.Core.Tests.Gff;

/// <summary>Constructeur de GFF V3.2 synthetiques (structs a plat, listes de structs).</summary>
internal sealed class GffBuilder
{
    public sealed record Field(string Label, uint Type, object Value);

    private readonly List<(uint type, List<Field> fields)> _structs = new() { (0xFFFFFFFF, new List<Field>()) };

    public int Root => 0;

    public int AddStruct(uint type) { _structs.Add((type, new List<Field>())); return _structs.Count - 1; }

    public GffBuilder Add(int structIdx, string label, uint type, object value)
    {
        _structs[structIdx].fields.Add(new Field(label, type, value));
        return this;
    }

    public byte[] Build(string fileType)
    {
        var labels = new List<string>();
        var fieldData = new MemoryStream();
        var fw = new BinaryWriter(fieldData);
        var fieldIdx = new MemoryStream();
        var fiw = new BinaryWriter(fieldIdx);
        var listIdx = new MemoryStream();
        var liw = new BinaryWriter(listIdx);
        var fields = new MemoryStream();
        var fldw = new BinaryWriter(fields);
        var structs = new MemoryStream();
        var sw = new BinaryWriter(structs);
        int fieldCount = 0;

        foreach (var (type, flds) in _structs)
        {
            uint dataOrOff;
            if (flds.Count == 0) dataOrOff = 0xFFFFFFFF;
            else if (flds.Count == 1) dataOrOff = (uint)fieldCount;
            else dataOrOff = (uint)fieldIdx.Position;
            var indices = new List<int>();
            foreach (var f in flds)
            {
                int li = labels.IndexOf(f.Label);
                if (li < 0) { labels.Add(f.Label); li = labels.Count - 1; }
                uint data = EncodeField(f, fw, liw, listIdx, fieldData);
                fldw.Write(f.Type); fldw.Write((uint)li); fldw.Write(data);
                indices.Add(fieldCount++);
            }
            if (flds.Count > 1) foreach (int i in indices) fiw.Write((uint)i);
            sw.Write(type); sw.Write(dataOrOff); sw.Write((uint)flds.Count);
        }

        var lab = new MemoryStream();
        foreach (string l in labels)
        {
            var b = new byte[16];
            Encoding.ASCII.GetBytes(l, b);
            lab.Write(b);
        }

        const int hdr = 56;
        uint structOff = hdr;
        uint fieldOff = structOff + (uint)structs.Length;
        uint labelOff = fieldOff + (uint)fields.Length;
        uint fdOff = labelOff + (uint)lab.Length;
        uint fiOff = fdOff + (uint)fieldData.Length;
        uint liOff = fiOff + (uint)fieldIdx.Length;

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes(fileType.PadRight(4)));
        w.Write(Encoding.ASCII.GetBytes("V3.2"));
        w.Write(structOff); w.Write((uint)_structs.Count);
        w.Write(fieldOff); w.Write((uint)fieldCount);
        w.Write(labelOff); w.Write((uint)labels.Count);
        w.Write(fdOff); w.Write((uint)fieldData.Length);
        w.Write(fiOff); w.Write((uint)fieldIdx.Length);
        w.Write(liOff); w.Write((uint)listIdx.Length);
        w.Write(structs.ToArray()); w.Write(fields.ToArray()); w.Write(lab.ToArray());
        w.Write(fieldData.ToArray()); w.Write(fieldIdx.ToArray()); w.Write(listIdx.ToArray());
        return ms.ToArray();
    }

    private static uint EncodeField(Field f, BinaryWriter fw, BinaryWriter liw, MemoryStream listIdx, MemoryStream fieldData)
    {
        uint off = (uint)fieldData.Position;
        switch (f.Type)
        {
            case 0: return (byte)f.Value;
            case 1: return (byte)(sbyte)f.Value;
            case 2: return (ushort)f.Value;
            case 3: return (ushort)(short)f.Value;
            case 4: return (uint)f.Value;
            case 5: return (uint)(int)f.Value;
            case 6: fw.Write((ulong)f.Value); return off;
            case 7: fw.Write((long)f.Value); return off;
            case 8: return BitConverter.SingleToUInt32Bits((float)f.Value);
            case 9: fw.Write((double)f.Value); return off;
            case 10: { var b = Encoding.Latin1.GetBytes((string)f.Value); fw.Write((uint)b.Length); fw.Write(b); return off; }
            case 11: { var b = Encoding.ASCII.GetBytes((string)f.Value); fw.Write((byte)b.Length); fw.Write(b); return off; }
            case 12:
            {
                var (strRef, strs) = ((uint, (int lang, string s)[]))f.Value;
                var body = new MemoryStream();
                var bw = new BinaryWriter(body);
                bw.Write(strRef); bw.Write((uint)strs.Length);
                foreach (var (lang, s) in strs)
                {
                    var b = Encoding.Latin1.GetBytes(s);
                    bw.Write((uint)(lang * 2)); bw.Write((uint)b.Length); bw.Write(b);
                }
                fw.Write((uint)body.Length); fw.Write(body.ToArray());
                return off;
            }
            case 13: { var b = (byte[])f.Value; fw.Write((uint)b.Length); fw.Write(b); return off; }
            case 14: return (uint)(int)f.Value;
            case 15:
            {
                var idx = (int[])f.Value;
                uint lo = (uint)listIdx.Position;
                liw.Write((uint)idx.Length);
                foreach (int i in idx) liw.Write((uint)i);
                return lo;
            }
            case 16:
            case 17:
            {
                foreach (float x in (float[])f.Value) fw.Write(x);
                return off;
            }
            default: throw new NotSupportedException();
        }
    }
}
