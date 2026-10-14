using System.Text;
using Kotor.Core.TwoDa;

namespace Kotor.Core.Tests.TwoDa;

public class TwoDaFileTests
{
    internal static byte[] Build(string[] cols, string[][] rows)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("2DA V2.b\n"));
        foreach (string c in cols) { w.Write(Encoding.ASCII.GetBytes(c)); w.Write((byte)'\t'); }
        w.Write((byte)0);
        w.Write((uint)rows.Length);
        for (int r = 0; r < rows.Length; r++) { w.Write(Encoding.ASCII.GetBytes(r.ToString())); w.Write((byte)'\t'); }
        var data = new MemoryStream();
        var offsets = new List<ushort>();
        foreach (var row in rows)
            foreach (string cell in row)
            {
                offsets.Add((ushort)data.Position);
                data.Write(Encoding.ASCII.GetBytes(cell)); data.WriteByte(0);
            }
        foreach (ushort o in offsets) w.Write(o);
        w.Write((ushort)data.Length);
        w.Write(data.ToArray());
        return ms.ToArray();
    }

    [Fact]
    public void TwoDa_reads_synthetic()
    {
        var t = TwoDaFile.Read(Build(["Label", "Walk"], [["dirt", "0"], ["stone", "1"]]));
        Assert.Equal(2, t.RowCount);
        Assert.Equal("1", t.Get(1, "Walk"));
        Assert.Equal("dirt", t.Get(0, "label"));
        Assert.Null(t.Get(5, "Walk"));
        Assert.Null(t.Get(0, "nope"));
    }

    [Fact]
    public void TwoDa_rejects_bad_header() =>
        Assert.Throws<InvalidDataException>(() => TwoDaFile.Read(new byte[40]));
}
