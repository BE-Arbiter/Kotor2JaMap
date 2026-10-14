using System.Numerics;
using System.Text;
using Kotor.Compiler.Bsp;
using BspCompiler = Kotor.Compiler.Compiler;

namespace Kotor.Compiler.Tests;

public sealed class SkyBoxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tfu2ja-sky-" + Guid.NewGuid().ToString("N"));
    private string MapsDir => Path.Combine(_root, "maps");
    private string ModelsDir => Path.Combine(_root, "tfu2ja", "models", "Test");

    public SkyBoxTests()
    {
        Directory.CreateDirectory(MapsDir);
        Directory.CreateDirectory(ModelsDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string WriteMap(string? skyKey)
    {
        string sky = skyKey is null ? "" : $"\"_tfu_sky\" \"{skyKey}\"\n";
        string path = Path.Combine(MapsDir, "Test.map");
        File.WriteAllText(path, "{\n\"classname\" \"worldspawn\"\n" + sky + "}\n" +
            "{\n\"classname\" \"misc_model\"\n\"model\" \"models/Test/floor.obj\"\n\"origin\" \"0 0 0\"\n}\n");
        File.WriteAllText(Path.Combine(ModelsDir, "floor.obj"),
            "v 0 0 0\nv 100 0 0\nv 100 100 0\nv 0 100 0\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nvn 0 0 1\nf 1/1/1 2/2/1 3/3/1\nf 1/1/1 3/3/1 4/4/1\n");
        File.WriteAllText(Path.Combine(ModelsDir, "Test_collision.obj"),
            "v 0 0 0\nv 100 0 0\nv 100 100 0\nv 0 100 0\nf 1 2 3\nf 1 3 4\n");
        return path;
    }

    private sealed record Parsed(List<(string Name, int Surf, int Cont)> Shaders, List<(int Shader, int FirstVert, int NumVerts, int FirstIdx, int NumIdx)> Surfaces,
        List<Vector3> Verts, List<int> Indexes);

    private static Parsed ReadBsp(byte[] b)
    {
        (int o, int l) Lump(int i) => (BitConverter.ToInt32(b, 8 + i * 8), BitConverter.ToInt32(b, 12 + i * 8));
        var p = new Parsed([], [], [], []);
        var (so, sl) = Lump(BspWriter.LumpShaders);
        for (int o = so; o < so + sl; o += 72)
            p.Shaders.Add((Encoding.ASCII.GetString(b, o, 64).TrimEnd('\0'), BitConverter.ToInt32(b, o + 64), BitConverter.ToInt32(b, o + 68)));
        var (fo, fl) = Lump(BspWriter.LumpSurfaces);
        for (int o = fo; o < fo + fl; o += 148)
            p.Surfaces.Add((BitConverter.ToInt32(b, o), BitConverter.ToInt32(b, o + 12), BitConverter.ToInt32(b, o + 16),
                BitConverter.ToInt32(b, o + 20), BitConverter.ToInt32(b, o + 24)));
        var (vo, vl) = Lump(BspWriter.LumpDrawVerts);
        for (int o = vo; o < vo + vl; o += 80)
            p.Verts.Add(new Vector3(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8)));
        var (io, il) = Lump(BspWriter.LumpDrawIndexes);
        for (int o = io; o < io + il; o += 4) p.Indexes.Add(BitConverter.ToInt32(b, o));
        return p;
    }

    [Theory]
    [InlineData(null, "textures/skies/hevil")]
    [InlineData("textures/skies/korriban", "textures/skies/korriban")]
    public void Bsp_contains_sky_shader_and_12_enclosing_triangles(string? key, string expected)
    {
        string map = WriteMap(key);
        string bsp = Path.Combine(MapsDir, "Test.bsp");

        var r = BspCompiler.Compile(map, bsp, Limits.Default);

        Assert.True(r.Ok, r.Error);
        Parsed p = ReadBsp(File.ReadAllBytes(bsp));
        int skyIndex = p.Shaders.FindIndex(s => s.Name == expected);
        Assert.True(skyIndex > 0, "shader de ciel absent du lump SHADERS");
        Assert.Equal(BspWriter.SurfaceSky, p.Shaders[skyIndex].Surf);
        Assert.Equal(0, p.Shaders[skyIndex].Cont); // non solide

        var skySurfs = p.Surfaces.Where(s => s.Shader == skyIndex).ToList();
        Assert.Equal(12, skySurfs.Sum(s => s.NumIdx / 3));

        var skyVerts = skySurfs.SelectMany(s => p.Verts.Skip(s.FirstVert).Take(s.NumVerts)).ToList();
        Vector3 min = skyVerts.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        Vector3 max = skyVerts.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        // Le sol va de (0,0,0) a (100,100,0) : la boite l'englobe avec une marge de 512.
        Assert.True(min.X <= -512 && min.Y <= -512 && min.Z <= -512);
        Assert.True(max.X >= 612 && max.Y >= 612 && max.Z >= 512);
        foreach (Vector3 v in p.Surfaces.Where(s => s.Shader != skyIndex).SelectMany(s => p.Verts.Skip(s.FirstVert).Take(s.NumVerts)))
        {
            Assert.True(v.X > min.X && v.Y > min.Y && v.Z > min.Z && v.X < max.X && v.Y < max.Y && v.Z < max.Z);
        }
    }

    [Fact]
    public void Sky_triangles_face_inward_with_JA_winding()
    {
        var tree = BspTree.Build([]);
        var faces = SkyBox.Build(tree, "textures/skies/hevil", Vector3.Zero, new Vector3(100), 512f);
        Assert.Equal(6, faces.Count);
        Vector3 center = new(50);
        foreach (RenderSurface s in faces)
        {
            Assert.True(s.Sky);
            for (int i = 0; i < s.Indices.Length; i += 3)
            {
                Vector3 a = s.Positions[s.Indices[i]], b = s.Positions[s.Indices[i + 1]], c = s.Positions[s.Indices[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                // Convention JA (etape 15) : cross . normale visible < 0 ; la normale visible pointe vers l'interieur.
                Assert.True(Vector3.Dot(n, s.Normals[s.Indices[i]]) < 0);
                Assert.True(Vector3.Dot(s.Normals[s.Indices[i]], center - a) > 0);
            }
        }
    }
}
