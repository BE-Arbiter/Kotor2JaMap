using System.Globalization;
using System.Numerics;
using System.Text;
using Kotor.Compiler.Bsp;

namespace Kotor.Compiler.Tests;

public sealed class RenderSurfacesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tfu2ja-rs-" + Guid.NewGuid().ToString("N"));

    public RenderSurfacesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // Grille w x h sommets (sommets uniques), 2 triangles par case.
    private string WriteGrid(int w, int h)
    {
        var sb = new StringBuilder();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                sb.Append(string.Create(CultureInfo.InvariantCulture, $"v {x} {y} 0\nvt {(float)x / w} {(float)y / h}\n"));
        sb.Append("vn 0 0 1\n");
        for (int y = 0; y + 1 < h; y++)
            for (int x = 0; x + 1 < w; x++)
            {
                int a = y * w + x + 1, b = a + 1, c = a + w, d = c + 1;
                sb.Append($"f {a}/{a}/1 {b}/{b}/1 {d}/{d}/1\nf {a}/{a}/1 {d}/{d}/1 {c}/{c}/1\n");
            }
        string path = Path.Combine(_dir, "Grid.obj");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    [Fact]
    public void Split_keeps_each_surface_under_vertex_limit()
    {
        string obj = WriteGrid(47, 37); // 1739 sommets
        var surfaces = RenderSurfaces.FromObj(obj, Vector3.Zero, BspTree.Build([]));
        Assert.True(surfaces.Count > 1);
        foreach (var s in surfaces)
        {
            Assert.True(s.Positions.Length <= 1200, $"{s.Positions.Length} sommets");
            Assert.True(s.Indices.Length <= RenderSurfaces.MaxSurfaceIndexes);
            Assert.Equal(s.Positions.Length, s.Uvs.Length);
            Assert.Equal(s.Positions.Length, s.Normals.Length);
            Assert.All(s.Indices, i => Assert.InRange(i, 0, s.Positions.Length - 1));
        }
    }

    [Fact]
    public void Split_keeps_triangle_count_and_winding()
    {
        string obj = WriteGrid(47, 37);
        var surfaces = RenderSurfaces.FromObj(obj, Vector3.Zero, BspTree.Build([]));
        int tris = 46 * 36 * 2;
        Assert.Equal(tris, surfaces.Sum(s => s.Indices.Length / 3));

        var got = new List<(Vector3, Vector3, Vector3)>();
        foreach (var s in surfaces)
            for (int i = 0; i < s.Indices.Length; i += 3)
                got.Add((s.Positions[s.Indices[i]], s.Positions[s.Indices[i + 1]], s.Positions[s.Indices[i + 2]]));
        var expected = new List<(Vector3, Vector3, Vector3)>();
        for (int y = 0; y < 36; y++)
            for (int x = 0; x < 46; x++)
            {
                Vector3 a = new(x, y, 0), b = new(x + 1, y, 0), c = new(x, y + 1, 0), d = new(x + 1, y + 1, 0);
                expected.Add((a, d, b));
                expected.Add((a, c, d));
            }
        Assert.Equal(expected.OrderBy(Key).ToList(), got.OrderBy(Key).ToList());
        Assert.All(got, t => Assert.True(Vector3.Cross(t.Item2 - t.Item1, t.Item3 - t.Item1).Z < 0));
    }

    [Fact]
    public void Horizontal_triangle_with_up_normal_uses_JA_winding()
    {
        // Mesure (etape 15) : dans les cartes JA d origine, cross(v1-v0, v2-v0) . normale < 0 (enroulement horaire vu de la normale).
        string path = Path.Combine(_dir, "Tri.obj");
        File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\nvn 0 0 1\nf 1//1 2//1 3//1\n");
        var s = Assert.Single(RenderSurfaces.FromObj(path, Vector3.Zero, BspTree.Build([])));
        Assert.Equal(3, s.Indices.Length);
        Vector3 p0 = s.Positions[s.Indices[0]], p1 = s.Positions[s.Indices[1]], p2 = s.Positions[s.Indices[2]];
        Assert.True(Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), s.Normals[s.Indices[0]]) < 0);
    }

    [Fact]
    public void Obj_vt_is_flipped_to_Quake_st()
    {
        // OBJ : v = 0 en bas ; Quake/DirectX : t = 0 en haut. Le compilateur re-inverse : t = 1 - vt.
        string path = Path.Combine(_dir, "Uv.obj");
        File.WriteAllText(path, "v 0 0 0\nv 1 0 0\nv 0 1 0\nvt 0.25 0.9\nvt 0.5 0.9\nvt 0.25 0.4\nvn 0 0 1\nf 1/1/1 2/2/1 3/3/1\n");
        var s = Assert.Single(RenderSurfaces.FromObj(path, Vector3.Zero, BspTree.Build([])));
        Assert.Contains(s.Uvs, uv => Math.Abs(uv.X - 0.25f) < 1e-5f && Math.Abs(uv.Y - 0.1f) < 1e-5f);
        Assert.Contains(s.Uvs, uv => Math.Abs(uv.X - 0.25f) < 1e-5f && Math.Abs(uv.Y - 0.6f) < 1e-5f);
    }

    private static string Key((Vector3, Vector3, Vector3) t) => t.ToString();
}
