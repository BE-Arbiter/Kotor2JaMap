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

    [Fact]
    public void Md3_splits_surfaces_over_the_vertex_limit()
    {
        int n = 5000; // 5000 sommets -> 2 surfaces de 4096 sommets au plus
        var pos = new Vector3[n]; var nor = new Vector3[n]; var uv = new Vector2[n];
        var idx = new List<int>();
        for (int i = 0; i < n; i++) { pos[i] = new Vector3(i % 100, i / 100, 0); nor[i] = Vector3.UnitZ; }
        for (int i = 0; i + 2 < n; i += 3) idx.AddRange([i, i + 1, i + 2]);
        byte[] md3 = Md3Writer.Write("big", [new ModelPart("t", pos, nor, uv, [.. idx])], "models/kotor/");
        Assert.Equal(2, I(md3, 84));
    }
}
