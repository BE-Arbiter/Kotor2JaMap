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

    private static byte[] WriteSurface(SurfaceInput input)
    {
        var tree = BspTree.Build([]);
        var ms = new MemoryStream();
        BspWriter.Write(tree, ms, null, null, RenderSurfaces.FromInput(input, tree), VertexLighting.Default);
        return ms.ToArray();
    }

    private static SurfaceInput Tri(Vector2[]? uv1) =>
        new("textures/kotor/t", [new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [new(0, 0), new(1, 0), new(0, 1)], uv1, [0, 1, 2]);

    [Fact]
    public void Lightmap_uvs_written_to_drawverts()
    {
        byte[] b = WriteSurface(Tri([new(0.25f, 0.75f), new(0.25f, 0.75f), new(0.25f, 0.75f)]));
        int vo = BitConverter.ToInt32(b, 8 + 8 * BspWriter.LumpDrawVerts);
        // drawvert : xyz(12) st(8) lightmap[4][2](32) normal(12) color[4][4](16) = 80 octets
        Assert.Equal(0.25f, BitConverter.ToSingle(b, vo + 20));
        Assert.Equal(0.75f, BitConverter.ToSingle(b, vo + 24));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, b.AsSpan(vo + 64, 4).ToArray());
    }

    [Fact]
    public void Surface_without_uv1_uses_vertex_lighting()
    {
        byte[] b = WriteSurface(Tri(null));
        int vo = BitConverter.ToInt32(b, 8 + 8 * BspWriter.LumpDrawVerts);
        Assert.Equal(0f, BitConverter.ToSingle(b, vo + 20));
        Assert.Equal(0f, BitConverter.ToSingle(b, vo + 24));
        Assert.NotEqual(255, b[vo + 64]);
    }

    [Fact]
    public void Collision_shader_is_kotor_namespaced()
    {
        Assert.Equal("textures/kotor/collision", BspWriter.CollisionShader);
    }
}
