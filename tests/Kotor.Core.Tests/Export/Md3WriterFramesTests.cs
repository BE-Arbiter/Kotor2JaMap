using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Kotor.Core.Export;
using Kotor.Core.Models;

namespace Kotor.Core.Tests.Export;

public class Md3WriterFramesTests
{
    private static ModelPart Part(string tex, float dx, float dz = 0f) => new(tex,
        [new(dx, 0, dz), new(dx + 1.25f, 0, dz), new(dx, 2.5f, dz + 0.1f), new(dx + 1, 2, dz + 3)],
        [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitX],
        [new(0, 0), new(1, 0), new(0, 1), new(1, 1)], [0, 1, 2, 1, 3, 2]);

    private static int I(byte[] d, int o) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o));
    private static float F(byte[] d, int o) => BitConverter.ToSingle(d, o);
    private static string Str(byte[] d, int off, int len)
    {
        int end = Array.IndexOf(d, (byte)0, off, len);
        return Encoding.ASCII.GetString(d, off, (end < 0 ? off + len : end) - off);
    }

    [Fact]
    public void Md3_WriteFrames_HeaderFrameCount()
    {
        IReadOnlyList<IReadOnlyList<ModelPart>> frames =
            [[Part("alpha", 0f), Part("beta", 5f)], [Part("alpha", 10f), Part("beta", 15f)], [Part("alpha", 20f), Part("beta", 25f)]];

        byte[] md3 = Md3Writer.WriteFrames("fan", frames, "models/jk/");

        Assert.Equal("IDP3", Encoding.ASCII.GetString(md3, 0, 4));
        Assert.Equal(3, I(md3, 76));            // numFrames
        Assert.Equal(2, I(md3, 84));            // numSurfaces
        Assert.Equal(md3.Length, I(md3, 104));  // ofsEnd
        int frameOfs = I(md3, 92);
        // Boites par image : le minimum x suit le decalage de la partie la plus a gauche.
        Assert.Equal(0f, F(md3, frameOfs));
        Assert.Equal(10f, F(md3, frameOfs + 56));
        Assert.Equal(20f, F(md3, frameOfs + 112));
        Assert.Equal("frame2", Str(md3, frameOfs + 112 + 40, 16));

        int s = I(md3, 100);
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(3, I(md3, s + 72));    // numFrames de la surface
            int nV = I(md3, s + 80), end = I(md3, s + 104), ofsXyz = I(md3, s + 100);
            Assert.Equal(4, nV);
            Assert.Equal(ofsXyz + 3 * nV * 8, end);
            // Sommet 0, image f : x = (5 * i + 10 * f) * 64 en 1/64 d'unite.
            for (int f = 0; f < 3; f++)
                Assert.Equal((short)((5 * i + 10 * f) * 64), BinaryPrimitives.ReadInt16LittleEndian(md3.AsSpan(s + ofsXyz + f * nV * 8)));
            s += end;
        }
    }

    [Fact]
    public void Md3_WriteFrames_SingleFrame_IdenticalToWrite()
    {
        ModelPart[] parts = [Part("zeta", 1f), Part("alpha", 0f)];
        Assert.Equal(Md3Writer.Write("plc", parts, "models/jk/"), Md3Writer.WriteFrames("plc", [parts], "models/jk/"));
    }

    [Fact]
    public void Md3_WriteFrames_TopologyMismatch_Throws()
    {
        ModelPart other = Part("alpha", 0f) with { Indices = [0, 1, 2] };
        Assert.Throws<ArgumentException>(() => Md3Writer.WriteFrames("x", [[Part("alpha", 0f)], [other]], "p/"));
        Assert.Throws<ArgumentException>(() => Md3Writer.WriteFrames("x", [[Part("alpha", 0f)], [Part("beta", 0f)]], "p/"));
        Assert.Throws<ArgumentException>(() => Md3Writer.WriteFrames("x", [], "p/"));
    }
}
