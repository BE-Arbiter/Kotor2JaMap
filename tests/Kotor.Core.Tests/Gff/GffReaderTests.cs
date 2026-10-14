using System.Numerics;
using Kotor.Core.Gff;

namespace Kotor.Core.Tests.Gff;

public class GffReaderTests
{
    [Fact]
    public void Gff_reads_synthetic_struct()
    {
        var b = new GffBuilder();
        int s1 = b.AddStruct(7), s2 = b.AddStruct(8);
        b.Add(s1, "Val", 5, 11);
        b.Add(s2, "Val", 5, 22);
        b.Add(b.Root, "IntF", 5, -42)
         .Add(b.Root, "ByteF", 0, (byte)200)
         .Add(b.Root, "FloatF", 8, 1.5f)
         .Add(b.Root, "DblF", 9, 2.25)
         .Add(b.Root, "ULongF", 6, 5000000000UL)
         .Add(b.Root, "StrF", 10, "hello")
         .Add(b.Root, "RefF", 11, "m02aa")
         .Add(b.Root, "LocF", 12, (123u, new[] { (0, "Anglais") }))
         .Add(b.Root, "VoidF", 13, new byte[] { 1, 2, 3 })
         .Add(b.Root, "OriF", 16, new[] { 1f, 2f, 3f, 4f })
         .Add(b.Root, "VecF", 17, new[] { 1f, 2f, 3f })
         .Add(b.Root, "List", 15, new[] { s1, s2 });
        var root = GffReader.Read(b.Build("IFO "), out string ft);
        Assert.Equal("IFO ", ft);
        Assert.Equal(-42, root.Get<int>("IntF"));
        Assert.Equal((byte)200, root.Get<byte>("ByteF"));
        Assert.Equal(1.5f, root.Get<float>("FloatF"));
        Assert.Equal(2.25, root.Get<double>("DblF"));
        Assert.Equal(5000000000UL, root.Get<ulong>("ULongF"));
        Assert.Equal("hello", root.Get<string>("StrF"));
        Assert.Equal("m02aa", root.Get<string>("RefF"));
        var loc = root.Get<GffLocString>("LocF")!;
        Assert.Equal(123u, loc.StrRef);
        Assert.Equal("Anglais", loc.Strings[0]);
        Assert.Equal(new byte[] { 1, 2, 3 }, root.Get<byte[]>("VoidF"));
        Assert.Equal(new Quaternion(2f, 3f, 4f, 1f), root.Get<Quaternion>("OriF")); // fichier : w,x,y,z
        Assert.Equal(new Vector3(1, 2, 3), root.Get<Vector3>("VecF"));
        var list = root.GetList("List");
        Assert.Equal(2, list.Count);
        Assert.Equal(7u, list[0].Type);
        Assert.Equal(11, list[0].Get<int>("Val"));
        Assert.Equal(22, list[1].Get<int>("Val"));
    }

    [Fact]
    public void Get_returns_default_for_missing_label()
    {
        var b = new GffBuilder();
        b.Add(b.Root, "IntF", 5, 1);
        var root = GffReader.Read(b.Build("ARE "), out _);
        Assert.Equal(0, root.Get<int>("Nope"));
        Assert.Null(root.Get<string>("Nope"));
        Assert.Null(root.Get<string>("IntF"));
        Assert.Empty(root.GetList("Nope"));
    }

    [Fact]
    public void Bad_version_throws()
    {
        var data = new GffBuilder().Build("ARE ");
        data[4] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => GffReader.Read(data, out _));
    }
}
