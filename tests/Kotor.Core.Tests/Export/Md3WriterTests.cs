using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Kotor.Core.Export;
using Kotor.Core.Models;

namespace Kotor.Core.Tests.Export;

public class Md3WriterTests
{
    private static ModelPart Part(string tex, float offset = 0f) => new(tex,
        [new(offset, 0, 0), new(offset + 1.25f, 0, 0), new(offset, 2.5f, 0.1f), new(offset + 1, 2, 3)],
        [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitX],
        [new(0, 0), new(1, 0), new(0, 1), new(1, 1)], [0, 1, 2, 1, 3, 2]);

    private static string Str(byte[] d, int off, int len)
    {
        int end = Array.IndexOf(d, (byte)0, off, len);
        return Encoding.ASCII.GetString(d, off, (end < 0 ? off + len : end) - off);
    }

    private static int I(byte[] d, int o) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o));

    [Fact]
    public void Md3_header_and_counts()
    {
        // Parties donnees dans le desordre : l'ordre ecrit est celui des noms de texture.
        byte[] md3 = Md3Writer.Write("plc_test", [Part("zeta", 1f), Part("alpha")], "models/kotor/");

        Assert.Equal("IDP3", Encoding.ASCII.GetString(md3, 0, 4));
        Assert.Equal(15, I(md3, 4));
        Assert.Equal("plc_test", Str(md3, 8, 64));
        Assert.Equal(1, I(md3, 76));            // numFrames
        Assert.Equal(0, I(md3, 80));            // numTags
        Assert.Equal(2, I(md3, 84));            // numSurfaces
        Assert.Equal(md3.Length, I(md3, 104));  // ofsEnd

        int s = I(md3, 100); // ofsSurfaces
        string[] expected = ["alpha", "zeta"];
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal("IDP3", Encoding.ASCII.GetString(md3, s, 4));
            Assert.Equal(expected[i], Str(md3, s + 4, 64));
            Assert.Equal(1, I(md3, s + 72));      // numFrames
            Assert.Equal(1, I(md3, s + 76));      // numShaders
            Assert.Equal(4, I(md3, s + 80));      // numVerts
            Assert.Equal(2, I(md3, s + 84));      // numTriangles
            Assert.Equal("models/kotor/" + expected[i], Str(md3, s + I(md3, s + 92), 64));
            // Sommet 0 : v = 0 en entree, t = 1 - v = 1 dans le fichier.
            Assert.Equal(1f, BitConverter.ToSingle(md3, s + I(md3, s + 96) + 4));
            s += I(md3, s + 104);                 // ofsEnd de la surface
        }
        Assert.Equal(md3.Length, s);

        // Position relue = valeur x 64 arrondie (surface 0 = "alpha").
        int s0 = I(md3, 100);
        int xyz = s0 + I(md3, s0 + 100);
        Assert.Equal((short)Math.Round(1.25f * 64), BinaryPrimitives.ReadInt16LittleEndian(md3.AsSpan(xyz + 8)));
        Assert.Equal((short)Math.Round(2.5f * 64), BinaryPrimitives.ReadInt16LittleEndian(md3.AsSpan(xyz + 16 + 2)));
        Assert.Equal((short)Math.Round(0.1f * 64), BinaryPrimitives.ReadInt16LittleEndian(md3.AsSpan(xyz + 16 + 4)));
    }

    [Fact]
    public void Md3_rejects_out_of_range()
    {
        var part = Part("t") with { Positions = [new(600, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 1, 1)] };
        Assert.Throws<ArgumentException>(() => Md3Writer.Write("m", [part], "models/kotor/"));
    }

    [Fact]
    public void Md3_is_deterministic()
    {
        var a = Md3Writer.Write("m", [Part("b"), Part("a")], "models/kotor/");
        var b = Md3Writer.Write("m", [Part("a"), Part("b")], "models/kotor/");
        Assert.Equal(a, b);
    }

    /// <summary>Partie de <paramref name="tris"/> triangles disjoints (3 sommets chacun) : <c>3 * tris</c> sommets.</summary>
    private static ModelPart Disjoint(int tris)
    {
        var pos = new Vector3[tris * 3]; var nor = new Vector3[tris * 3]; var uv = new Vector2[tris * 3]; var idx = new int[tris * 3];
        for (int t = 0; t < tris; t++)
            for (int k = 0; k < 3; k++) { pos[t * 3 + k] = new Vector3(t % 100 + k, t / 100, 0); nor[t * 3 + k] = Vector3.UnitZ; idx[t * 3 + k] = t * 3 + k; }
        return new ModelPart("t", pos, nor, uv, idx);
    }

    private static (int Verts, int Tris)[] SurfaceCounts(byte[] md3)
    {
        int s = I(md3, 100), n = I(md3, 84);
        var r = new (int, int)[n];
        for (int i = 0; i < n; i++) { r[i] = (I(md3, s + 80), I(md3, s + 84)); s += I(md3, s + 104); }
        return r;
    }

    [Fact]
    public void Engine_limits_are_the_swgl_loader_limits()
    {
        // OpenJK-SWGL rd-vulkan/tr_model.cpp:1105-1118 refuse numVerts >= 1200 et numTriangles * 3 >= 7200 (qfiles.h:33-34).
        Assert.Equal(1199, Md3Writer.MaxVerticesPerSurface);
        Assert.Equal(2399, Md3Writer.MaxTrianglesPerSurface);
    }

    [Fact]
    public void Surface_at_the_limit_stays_whole_and_one_over_is_split()
    {
        // 1197 sommets : une surface. 399 triangles disjoints = 1197 sommets, sous 1199.
        Assert.Equal([(1197, 399)], SurfaceCounts(Md3Writer.Write("b", [Disjoint(399)], "models/kotor/")));

        // 400 triangles disjoints = 1200 sommets > 1199 : deux surfaces (399 + 1 triangles).
        var split = SurfaceCounts(Md3Writer.Write("b", [Disjoint(400)], "models/kotor/"));
        Assert.Equal([(1197, 399), (3, 1)], split);
        Assert.All(split, c => { Assert.True(c.Verts < 1200); Assert.True(c.Tris * 3 < 7200); });
    }

    [Fact]
    public void Triangle_limit_splits_a_part_with_shared_vertices()
    {
        // 2399 triangles sur 3 sommets partages : une surface ; 2400 : deux.
        static ModelPart Fan(int tris) => new("t", [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [new(0, 0), new(1, 0), new(0, 1)], [.. Enumerable.Range(0, tris).SelectMany(_ => new[] { 0, 1, 2 })]);
        Assert.Equal([(3, 2399)], SurfaceCounts(Md3Writer.Write("f", [Fan(2399)], "models/kotor/")));
        Assert.Equal([(3, 2399), (3, 1)], SurfaceCounts(Md3Writer.Write("f", [Fan(2400)], "models/kotor/")));
    }

    [Fact]
    public void Large_part_is_split_below_engine_limits()
    {
        var counts = SurfaceCounts(Md3Writer.Write("big", [Disjoint(5000)], "models/kotor/"));
        Assert.True(counts.Length > 1);
        Assert.All(counts, c => { Assert.True(c.Verts < 1200); Assert.True(c.Tris * 3 < 7200); });
        Assert.Equal(5000, counts.Sum(c => c.Tris));
    }

    [Fact]
    public void Write_rejects_more_surfaces_than_the_model_limit_but_WriteSplit_splits()
    {
        ModelPart[] parts = [.. Enumerable.Range(0, 65).Select(i => Part("tex" + i.ToString("D3")))];
        Assert.Throws<ArgumentException>(() => Md3Writer.Write("m", parts, "models/kotor/"));

        var files = Md3Writer.WriteSplit("m", parts, "models/kotor/");
        Assert.Equal(2, files.Count);
        Assert.Equal(64, I(files[0].Data, 84));
        Assert.Equal(1, I(files[1].Data, 84));
        Assert.Equal("m_p0", Str(files[0].Data, 8, 64));
        Assert.Equal("m_p1", Str(files[1].Data, 8, 64));
        Assert.Equal(["tex064"], files[1].Textures);

        // Un modele qui tient dans un fichier garde son nom.
        var one = Md3Writer.WriteSplit("m", parts.Take(64).ToArray(), "models/kotor/");
        Assert.Single(one);
        Assert.Equal("m", Str(one[0].Data, 8, 64));
    }

    [Fact]
    public void Normals_are_encoded_on_256_steps()
    {
        // Le moteur decode lat/lng d'un octet avec lat * (FUNCTABLE_SIZE / 256) : un quart de tour vaut 64.
        var part = Part("t") with { Normals = [Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ] };
        byte[] md3 = Md3Writer.Write("n", [part], "models/kotor/");
        int s = I(md3, 100), xyz = s + I(md3, s + 100);
        ushort N(int v) => BinaryPrimitives.ReadUInt16LittleEndian(md3.AsSpan(xyz + v * 8 + 6));
        Assert.Equal(0x0040, N(0));   // lat 0, lng 64 (acos(0) = 90 deg)
        Assert.Equal(0x4040, N(1));   // lat 64 (90 deg), lng 64
        Assert.Equal(0xC040, N(2));   // lat 192 (-90 deg), lng 64
        Assert.Equal(0x0000, N(3));   // vers le haut
    }
}
