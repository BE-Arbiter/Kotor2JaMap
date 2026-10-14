using System.Numerics;
using System.Text;
using Kotor.Core.Walkmesh;

namespace Kotor.Core.Tests.Walkmesh;

public class WokReaderTests
{
    internal static byte[] Build(Vector3[] verts, int[] tris, int[] mats)
    {
        int vOff = 136, fOff = vOff + verts.Length * 12, mOff = fOff + tris.Length * 4;
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("BWM V1.0"));
        w.Write(1u);
        w.Write(new byte[72 - 12]);
        w.Write((uint)verts.Length); w.Write((uint)vOff);
        w.Write((uint)(tris.Length / 3)); w.Write((uint)fOff);
        w.Write((uint)mOff);
        w.Write(new byte[136 - (int)ms.Position]);
        foreach (var v in verts) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        foreach (int i in tris) w.Write((uint)i);
        foreach (int m in mats) w.Write((uint)m);
        return ms.ToArray();
    }

    [Fact]
    public void Reads_synthetic_wok()
    {
        var wm = WokReader.Read(Build([new(0, 0, 0), new(1, 0, 0), new(0, 1, 2)], [0, 1, 2], [4]));
        Assert.Equal(3, wm.Vertices.Length);
        Assert.Equal(new Vector3(0, 1, 2), wm.Vertices[2]);
        Assert.Equal(new[] { 0, 1, 2 }, wm.Triangles);
        Assert.Equal(new[] { 4 }, wm.Materials);
    }

    [Fact]
    public void Rejects_bad_magic() =>
        Assert.Throws<InvalidDataException>(() => WokReader.Read(new byte[200]));

    [Fact]
    public void Rejects_out_of_range_index() =>
        Assert.Throws<InvalidDataException>(() => WokReader.Read(Build([new(0, 0, 0)], [0, 1, 2], [0])));
}
