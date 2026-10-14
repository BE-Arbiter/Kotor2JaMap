using System.Numerics;
using Kotor.Compiler.Bsp;

namespace Kotor.Compiler.Tests;

public sealed class RenderSurfacesTests
{
    // Grille w x h sommets (sommets uniques), 2 triangles par case, UV1 = UV0 + 0.5 si demande.
    private static SurfaceInput Grid(int w, int h, bool withUv1 = false)
    {
        var pos = new List<Vector3>();
        var uv = new List<Vector2>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) { pos.Add(new(x, y, 0)); uv.Add(new((float)x / w, (float)y / h)); }
        var idx = new List<int>();
        for (int y = 0; y + 1 < h; y++)
            for (int x = 0; x + 1 < w; x++)
            {
                int a = y * w + x, b = a + 1, c = a + w, d = c + 1;
                idx.AddRange([a, b, d, a, d, c]);
            }
        return new SurfaceInput("textures/kotor/grid", pos.ToArray(), pos.Select(_ => Vector3.UnitZ).ToArray(), uv.ToArray(),
            withUv1 ? uv.Select(u => u + new Vector2(0.5f)).ToArray() : null, idx.ToArray());
    }

    [Fact]
    public void Split_keeps_each_surface_under_vertex_limit()
    {
        var surfaces = RenderSurfaces.FromInput(Grid(47, 37), BspTree.Build([])); // 1739 sommets
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
        var surfaces = RenderSurfaces.FromInput(Grid(47, 37), BspTree.Build([]));
        Assert.Equal(46 * 36 * 2, surfaces.Sum(s => s.Indices.Length / 3));

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
        var input = new SurfaceInput("s", [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [default, default, default], null, [0, 1, 2]);
        var s = Assert.Single(RenderSurfaces.FromInput(input, BspTree.Build([])));
        Vector3 p0 = s.Positions[s.Indices[0]], p1 = s.Positions[s.Indices[1]], p2 = s.Positions[s.Indices[2]];
        Assert.True(Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), s.Normals[s.Indices[0]]) < 0);
    }

    [Fact]
    public void Uv1_follows_vertices_through_split()
    {
        var surfaces = RenderSurfaces.FromInput(Grid(47, 37, withUv1: true), BspTree.Build([]));
        Assert.True(surfaces.Count > 1);
        foreach (var s in surfaces)
        {
            Assert.NotNull(s.LightmapUvs);
            Assert.Equal(s.Positions.Length, s.LightmapUvs!.Length);
            for (int i = 0; i < s.Uvs.Length; i++) Assert.Equal(s.Uvs[i] + new Vector2(0.5f), s.LightmapUvs[i]);
        }
    }

    [Fact]
    public void Without_uv1_lightmap_uvs_are_null()
    {
        Assert.All(RenderSurfaces.FromInput(Grid(3, 3), BspTree.Build([])), s => Assert.Null(s.LightmapUvs));
    }

    private static string Key((Vector3, Vector3, Vector3) t) => t.ToString();

    [Fact]
    public void Mismatched_attribute_lengths_are_logged_with_shader_name()
    {
        var log = new Kotor.Core.Diagnostics.ImportLog();
        var s = TestScenes.FloorSurface("textures/kotor/bad") with
        {
            Normals = [Vector3.UnitZ], Uv0 = [new Vector2(0, 0)], Uv1 = [new Vector2(0, 0)],
        };

        var surfaces = RenderSurfaces.FromInput(s, BspTree.Build([]), log);

        Assert.NotEmpty(surfaces);
        Assert.Null(surfaces[0].LightmapUvs);
        var w = log.Warnings.Where(x => x.Code == "SURFACE_ATTRIBUTE_MISMATCH").ToList();
        Assert.Equal(3, w.Count);
        Assert.All(w, x => Assert.Contains("textures/kotor/bad", x.Message));
    }
}
