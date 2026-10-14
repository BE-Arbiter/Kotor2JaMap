using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core.Geometry;

namespace Kotor.Compiler.Tests;

public class BspWriterTests
{
    private static byte[] Write(IReadOnlyList<Brush> brushes)
    {
        var ms = new MemoryStream();
        BspWriter.Write(BspTree.Build(brushes), ms);
        return ms.ToArray();
    }

    private static IReadOnlyList<Brush> OneBrush()
    {
        var mesh = new ModelMesh
        {
            Name = "c",
            SubMeshes = [new SubMesh { MaterialName = "m", Positions = [new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)], Triangles = [0, 1, 2] }],
        };
        return BrushBuilder.FromCollision(mesh);
    }

    [Fact]
    public void Header_is_RBSP_1()
    {
        byte[] b = Write(OneBrush());
        Assert.Equal("RBSP", System.Text.Encoding.ASCII.GetString(b, 0, 4));
        Assert.Equal(1, BitConverter.ToInt32(b, 4));
    }

    [Fact]
    public void Lump_directory_has_all_entries()
    {
        byte[] b = Write(OneBrush());
        // 18 lumps (HEADER_LUMPS d'OpenJK) : le plan disait 17, voir rapport.
        Assert.Equal(18, BspWriter.LumpCount);
        Assert.Equal(8 + BspWriter.LumpCount * 8, BspWriter.HeaderSize);
        // Chaque lump tient dans le fichier.
        for (int i = 0; i < BspWriter.LumpCount; i++)
        {
            int ofs = BitConverter.ToInt32(b, 8 + i * 8), len = BitConverter.ToInt32(b, 12 + i * 8);
            Assert.True(ofs >= BspWriter.HeaderSize && ofs + len <= b.Length, $"lump {i}");
        }
        Assert.True(BitConverter.ToInt32(b, 12 + BspWriter.LumpBrushes * 8) == 12); // 1 brush de 12 octets
    }

    [Fact]
    public void Vis_is_single_cluster_novis()
    {
        byte[] b = Write(OneBrush());
        int ofs = BitConverter.ToInt32(b, 8 + BspWriter.LumpVisibility * 8); // sans vis le moteur n a aucun cluster et ne dessine rien
        Assert.True(ofs >= BspWriter.HeaderSize);
        Assert.Equal(1, BitConverter.ToInt32(b, ofs));
        Assert.Equal(16, BitConverter.ToInt32(b, 12 + BspWriter.LumpVisibility * 8));
    }

    [Fact]
    public void Empty_tree_still_writes_valid_file()
    {
        byte[] b = Write([]);
        Assert.Equal("RBSP", System.Text.Encoding.ASCII.GetString(b, 0, 4));
        Assert.True(BitConverter.ToInt32(b, 12 + BspWriter.LumpNodes * 8) >= 36);
    }

    [Fact]
    public void Brush_sides_limit_throws_without_partial_output()
    {
        var brushes = Enumerable.Range(0, 12000).Select(i =>
            new Brush(Enumerable.Repeat(new BspPlane(Vector3.UnitX, i), 5).ToArray(), new Vector3(i), new Vector3(i + 1))).ToList();
        var ms = new MemoryStream();
        Assert.Throws<BspLimitException>(() => BspWriter.Write(BspTree.Build(brushes), ms));
        Assert.Equal(0, ms.Length);
    }
}
