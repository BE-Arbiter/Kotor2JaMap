using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core.Diagnostics;
using BspCompiler = Kotor.Compiler.Compiler;

namespace Kotor.Compiler.Tests;

public sealed class BspReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "k2j_bspr_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static SurfaceInput Wall()
    {
        Vector3[] pos = [new(0, 0, 0), new(100, 0, 0), new(100, 0, 100), new(0, 0, 100)];
        return new SurfaceInput("textures/kotor/wall", pos, [-Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY],
            [new(0, 0), new(2, 0), new(2, 2), new(0, 2)], [new(0.1f, 0.2f), new(0.3f, 0.2f), new(0.3f, 0.4f), new(0.1f, 0.4f)],
            [0, 1, 2, 0, 2, 3]);
    }

    private static CompileInput TwoSurfaces() =>
        TestScenes.Input([TestScenes.FloorSurface(), Wall()], sky: "textures/skies/test");

    private byte[] CompileBytes(CompileInput input)
    {
        string path = Path.Combine(_dir, "m.bsp");
        var r = BspCompiler.Compile(input, path, Limits.Swgl, new ImportLog());
        Assert.True(r.Ok, r.Error);
        return File.ReadAllBytes(path);
    }

    [Fact]
    public void BspReader_roundtrips_compiled_map()
    {
        CompileInput input = TwoSurfaces();
        BspFile bsp = BspReader.Read(CompileBytes(input));

        Assert.Contains("textures/kotor/floor", bsp.Shaders);
        Assert.Contains("textures/kotor/wall", bsp.Shaders);
        Assert.Contains("info_player_start", bsp.Entities);

        foreach (SurfaceInput src in input.Surfaces)
        {
            BspSurface got = Assert.Single(bsp.Surfaces, s => s.Shader == src.Shader);
            // Le compilateur peut reordonner les sommets : on compare des ensembles (position, UV0, UV1) et de triangles.
            Assert.Equal(VertexSet(src.Positions, src.Uv0, src.Uv1 ?? new Vector2[src.Positions.Length]), VertexSet(got.Positions, got.Uv0, got.Uv1));
            Assert.Equal(TriangleSet(src.Positions, src.Indices), TriangleSet(got.Positions, got.Indices));
            Assert.Equal(src.Positions.Length * 4, got.Colors.Length);
            Assert.Equal(src.Positions.Length, got.Normals.Length);
        }
        Assert.True(bsp.Collision.BrushCount > 0);
        Assert.True(bsp.Collision.BrushSideCount >= bsp.Collision.BrushCount * 6);
        Assert.True(bsp.Collision.PlaneCount > 0);
        // Les 6 faces de la boite de ciel sont relues aussi.
        Assert.Contains(bsp.Surfaces, s => s.Shader == "textures/skies/test");
    }

    [Fact]
    public void Vertex_alpha_reaches_the_drawverts()
    {
        SurfaceInput floor = TestScenes.FloorSurface("textures/swtor/a__b");
        byte[] alpha = [.. Enumerable.Range(0, floor.Positions.Length).Select(i => (byte)(i * 60))];
        BspFile bsp = BspReader.Read(CompileBytes(TestScenes.Input([floor with { VertexAlpha = alpha }], sky: "textures/skies/test")));
        BspSurface got = Assert.Single(bsp.Surfaces, s => s.Shader == "textures/swtor/a__b");
        for (int i = 0; i < floor.Positions.Length; i++)
        {
            int j = Array.IndexOf(got.Positions, floor.Positions[i]);
            Assert.Equal(alpha[i], got.Colors[j * 4 + 3]);
        }
    }

    private static string[] VertexSet(Vector3[] p, Vector2[] uv0, Vector2[]? uv1) =>
        [.. p.Select((v, i) => $"{v}|{uv0[i]}|{(uv1 is null ? "-" : uv1[i].ToString())}").Order(StringComparer.Ordinal)];

    private static string[] TriangleSet(Vector3[] p, int[] idx)
    {
        var r = new List<string>();
        for (int t = 0; t < idx.Length; t += 3)
            r.Add(string.Join(";", new[] { p[idx[t]], p[idx[t + 1]], p[idx[t + 2]] }.Select(v => v.ToString()).Order(StringComparer.Ordinal)));
        return [.. r.Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void BspReader_rejects_bad_header()
    {
        byte[] data = CompileBytes(TwoSurfaces());
        byte[] badMagic = (byte[])data.Clone();
        badMagic[0] = (byte)'X';
        Assert.Contains("RBSP", Assert.Throws<InvalidDataException>(() => BspReader.Read(badMagic)).Message);

        byte[] badVersion = (byte[])data.Clone();
        BitConverter.GetBytes(46).CopyTo(badVersion, 4);
        Assert.Contains("46", Assert.Throws<InvalidDataException>(() => BspReader.Read(badVersion)).Message);

        Assert.Throws<InvalidDataException>(() => BspReader.Read(new byte[10]));
    }

    [Fact]
    public void BspReader_rejects_out_of_range_lump()
    {
        byte[] data = CompileBytes(TwoSurfaces());
        byte[] bad = (byte[])data.Clone();
        BitConverter.GetBytes(data.Length + 4).CopyTo(bad, 8 + BspWriter.LumpDrawVerts * 8);
        var ex = Assert.Throws<InvalidDataException>(() => BspReader.Read(bad));
        Assert.Contains("lump", ex.Message, StringComparison.OrdinalIgnoreCase);

        byte[] longLump = (byte[])data.Clone();
        BitConverter.GetBytes(data.Length).CopyTo(longLump, 12 + BspWriter.LumpSurfaces * 8);
        Assert.Throws<InvalidDataException>(() => BspReader.Read(longLump));
    }

    [Fact]
    public void BspReader_rejects_surface_indices_outside_vertices()
    {
        byte[] data = CompileBytes(TwoSurfaces());
        byte[] bad = (byte[])data.Clone();
        int idxOfs = BitConverter.ToInt32(bad, 8 + BspWriter.LumpDrawIndexes * 8);
        BitConverter.GetBytes(100000).CopyTo(bad, idxOfs);
        Assert.Throws<InvalidDataException>(() => BspReader.Read(bad));
    }

    private static byte[] WithSurface0Field(byte[] data, int fieldOffset, int value)
    {
        byte[] bad = (byte[])data.Clone();
        int surfOfs = BitConverter.ToInt32(bad, 8 + BspWriter.LumpSurfaces * 8);
        BitConverter.GetBytes(value).CopyTo(bad, surfOfs + fieldOffset);
        return bad;
    }

    [Fact]
    public void BspReader_rejects_surface_without_indexes()
    {
        var ex = Assert.Throws<InvalidDataException>(() => BspReader.Read(WithSurface0Field(CompileBytes(TwoSurfaces()), 24, 0)));
        Assert.Contains("Surface 0", ex.Message);
    }

    [Fact]
    public void BspReader_rejects_index_count_not_multiple_of_three()
    {
        var ex = Assert.Throws<InvalidDataException>(() => BspReader.Read(WithSurface0Field(CompileBytes(TwoSurfaces()), 24, 4)));
        Assert.Contains("not a multiple of 3", ex.Message);
    }

    [Fact]
    public void BspReader_rejects_surface_without_vertices()
    {
        var ex = Assert.Throws<InvalidDataException>(() => BspReader.Read(WithSurface0Field(CompileBytes(TwoSurfaces()), 16, 0)));
        Assert.Contains("Surface 0", ex.Message);
    }

    [Fact]
    public void Export_is_deterministic()
    {
        CompileInput input = TwoSurfaces();
        string a = Path.Combine(_dir, "a.bsp"), b = Path.Combine(_dir, "b.bsp");
        Assert.True(BspCompiler.Compile(input, a, Limits.Swgl, new ImportLog()).Ok);
        Assert.True(BspCompiler.Compile(input, b, Limits.Swgl, new ImportLog()).Ok);
        Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
    }
}
