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

    private static SurfaceInput Quad(string shader, float x0, bool doubleSided = false, bool uv1 = false) =>
        new(shader, [new(x0, 0, 0), new(x0 + 10, 0, 0), new(x0 + 10, 10, 0), new(x0, 10, 0)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
            uv1 ? [new(0, 0), new(1, 0), new(1, 1), new(0, 1)] : null, [0, 1, 2, 0, 2, 3], doubleSided);

    private static Brush Box(Vector3 min, Vector3 max) => new(
    [
        new BspPlane(-Vector3.UnitX, -min.X), new BspPlane(Vector3.UnitX, max.X),
        new BspPlane(-Vector3.UnitY, -min.Y), new BspPlane(Vector3.UnitY, max.Y),
        new BspPlane(-Vector3.UnitZ, -min.Z), new BspPlane(Vector3.UnitZ, max.Z),
    ], min, max);

    [Fact]
    public void Inputs_with_same_shader_merge_into_one_surface()
    {
        var surfaces = RenderSurfaces.FromInputs([Quad("a", 0), Quad("a", 50)], BspTree.Build([]));
        var s = Assert.Single(surfaces);
        Assert.Equal(8, s.Positions.Length);
        Assert.Equal(12, s.Indices.Length);
    }

    [Fact]
    public void Different_shaders_never_merge()
    {
        var surfaces = RenderSurfaces.FromInputs([Quad("a", 0), Quad("b", 0), Quad("a", 20)], BspTree.Build([]));
        Assert.Equal(2, surfaces.Count);
        Assert.Equal(["a", "b"], surfaces.Select(s => s.Shader).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void DoubleSided_and_lightmap_presence_split_groups()
    {
        var tree = BspTree.Build([]);
        Assert.Equal(2, RenderSurfaces.FromInputs([Quad("a", 0), Quad("a", 20, doubleSided: true)], tree).Count);
        Assert.Equal(2, RenderSurfaces.FromInputs([Quad("a", 0), Quad("a", 20, uv1: true)], tree).Count);
    }

    [Fact]
    public void Mesh_spanning_several_leaves_is_one_surface_referenced_by_each_overlapped_leaf()
    {
        // 10 petits brushes alignes sur X : l arbre est coupe en plusieurs feuilles.
        var brushes = Enumerable.Range(0, 10).Select(i => Box(new(i * 100, 0, -20), new(i * 100 + 10, 10, -10))).ToList();
        var tree = BspTree.Build(brushes);
        Assert.True(tree.Leaves.Count > 2);
        var wide = new SurfaceInput("a", [new(0, 0, 0), new(450, 0, 0), new(450, 10, 0), new(0, 10, 0)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], [default, default, default, default], null, [0, 1, 2, 0, 2, 3]);

        var s = Assert.Single(RenderSurfaces.FromInput(wide, tree));

        Assert.True(s.Leaves.Count > 1);
        Assert.Equal(s.Leaves.Count, s.Leaves.Distinct().Count());
        // Les feuilles de cet arbre contiennent toutes un brush (solides) : repli sur les feuilles touchees, mais pas sur les autres.
        Assert.True(s.Leaves.Count < tree.Leaves.Count);
        foreach (Vector3 v in s.Positions)
        {
            int l = RenderSurfaces.FindLeaf(tree, v);
            Assert.Contains(l, s.Leaves);
        }
    }

    [Fact]
    public void Merged_chunks_respect_limits_and_keep_every_triangle()
    {
        var inputs = new List<SurfaceInput> { Grid(30, 30), Grid(30, 30), Grid(47, 37) };
        var surfaces = RenderSurfaces.FromInputs(inputs, BspTree.Build([]));
        Assert.Equal(29 * 29 * 2 * 2 + 46 * 36 * 2, surfaces.Sum(s => s.Indices.Length / 3));
        Assert.All(surfaces, s =>
        {
            Assert.True(s.Positions.Length <= RenderSurfaces.MaxSurfaceVerts);
            Assert.True(s.Indices.Length <= RenderSurfaces.MaxSurfaceIndexes);
        });
        Assert.True(surfaces.Count <= 8, $"{surfaces.Count} surfaces");
    }

    [Fact]
    public void Compact_mode_regroups_distant_triangles_and_cuts_the_leaf_triangle_cost()
    {
        // Beaucoup de petits brushes alignes sur X (feuilles nombreuses) ; 100 quads du meme shader alternent entre deux extremites.
        var brushes = Enumerable.Range(0, 30).Select(i => Box(new(i * 100, 0, -20), new(i * 100 + 10, 10, -10))).ToList();
        var tree = BspTree.Build(brushes);
        var inputs = Enumerable.Range(0, 100).Select(i => Quad("a", i % 2 == 0 ? 0 : 2800)).ToList();

        var loose = RenderSurfaces.FromInputs(inputs, tree);
        var compact = RenderSurfaces.FromInputs(inputs, tree, compact: true);

        Assert.Equal(loose.Sum(s => s.Indices.Length), compact.Sum(s => s.Indices.Length)); // aucun triangle perdu
        Assert.All(compact, s =>
        {
            Assert.True(s.Positions.Length <= RenderSurfaces.MaxSurfaceVerts);
            Assert.True(s.Indices.Length <= RenderSurfaces.MaxSurfaceIndexes);
            Assert.True((long)(s.Indices.Length / 3) * s.Leaves.Count <= RenderSurfaces.MaxLeafTriangleCost || s.Indices.Length == 3);
        });
        Assert.True(LimitCheck.EstimateRtxPrimitives(compact) < LimitCheck.EstimateRtxPrimitives(loose) / 4,
            $"{LimitCheck.EstimateRtxPrimitives(compact)} vs {LimitCheck.EstimateRtxPrimitives(loose)}");
    }

    [Fact]
    public void Default_mode_is_unchanged_by_the_compact_option_being_off()
    {
        var inputs = new List<SurfaceInput> { Grid(30, 30), Quad("b", 5), Grid(47, 37) };
        var a = RenderSurfaces.FromInputs(inputs, BspTree.Build([]));
        var b = RenderSurfaces.FromInputs(inputs, BspTree.Build([]), compact: false);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++) { Assert.Equal(a[i].Positions, b[i].Positions); Assert.Equal(a[i].Indices, b[i].Indices); }
    }
}
