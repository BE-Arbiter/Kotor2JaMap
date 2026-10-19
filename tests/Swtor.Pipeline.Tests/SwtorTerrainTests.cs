using System.Numerics;
using Kotor.Core.Scene;
using Swtor.Core.Terrain;

namespace Swtor.Pipeline.Tests;

public class SwtorTerrainTests
{
    private static HeightmapTile Flat(int n, float h = 1f, bool[]? present = null) =>
        new(2, n, n, Enumerable.Repeat(h, n * n).ToArray(), present);

    private static Vector3 ZUp(Vector3 v) => SwtorAxes.ToZUp(v);

    [Fact]
    public void Sample_indices_always_keep_the_last_sample()
    {
        Assert.Equal([0, 4, 8, 12, 16, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64], SwtorTerrain.SampleIndices(65, 4));
        Assert.Equal([0, 8, 16, 24, 32, 40, 48, 56, 64], SwtorTerrain.SampleIndices(65, 8));
        Assert.Equal([0, 1], SwtorTerrain.SampleIndices(2, 64));
    }

    [Fact]
    public void Full_resolution_has_two_triangles_per_cell_facing_up()
    {
        HeightmapTile t = Flat(5);
        SwtorTerrainMesh m = SwtorTerrain.BuildMesh([(t, Matrix4x4.Identity)], 1, ZUp, false, 1f);
        Assert.Equal(4 * 4 * 2, m.Triangles.Length / 3);
        Assert.All(m.Normals, n => Assert.True(n.Z > 0.99f));
        for (int i = 0; i < m.Triangles.Length; i += 3)
        {
            Vector3 n = Vector3.Cross(m.Positions[m.Triangles[i + 1]] - m.Positions[m.Triangles[i]], m.Positions[m.Triangles[i + 2]] - m.Positions[m.Triangles[i]]);
            Assert.True(n.Z > 0f);
        }
    }

    [Fact]
    public void Mesh_follows_the_axes_and_the_world_matrix()
    {
        HeightmapTile t = Flat(3, 2f);
        // translation Y de 10 : le sol est a Z = 12 dans la scene (Y haut -> Z haut)
        var m = SwtorTerrain.BuildMesh([(t, Matrix4x4.CreateTranslation(0, 10, 0))], 1, ZUp, false, 1f);
        Assert.All(m.Positions, p => Assert.Equal(12f, p.Z, 3));
        Assert.Equal(-0.2f, m.Positions.Min(p => p.X), 3);
        Assert.Equal(0.2f, m.Positions.Max(p => p.X), 3);
    }

    [Fact]
    public void Decimation_fits_the_budget_and_keeps_tile_edges_aligned()
    {
        var tiles = Enumerable.Range(0, 10).Select(_ => Flat(65)).ToList();
        Assert.Equal(1, SwtorTerrain.ChooseStep(tiles, 10 * 8192));
        int step = SwtorTerrain.ChooseStep(tiles, 10_000);
        Assert.True(step > 1);
        Assert.True(tiles.Sum(t => (long)SwtorTerrain.CountTriangles(t, step)) <= 10_000);
        // bords identiques pour deux dalles voisines : memes indices d'echantillons
        Assert.Equal(SwtorTerrain.SampleIndices(65, step), SwtorTerrain.SampleIndices(65, step));
        Assert.Equal(64, SwtorTerrain.SampleIndices(65, step)[^1]);
        // budget absurde : le pas le plus grossier
        Assert.Equal(64, SwtorTerrain.ChooseStep(tiles, 1));
    }

    [Fact]
    public void Holes_remove_cells_and_a_fully_holed_tile_gives_no_triangle()
    {
        var present = Enumerable.Repeat(true, 25).ToArray();
        present[2 * 5 + 2] = false;
        Assert.Equal((16 - 4) * 2, SwtorTerrain.CountTriangles(Flat(5, 1f, present), 1));
        Assert.Equal(0, SwtorTerrain.CountTriangles(Flat(5, 1f, new bool[25]), 1));
        Assert.Empty(SwtorTerrain.BuildMesh([(Flat(5, 1f, new bool[25]), Matrix4x4.Identity)], 1, ZUp, false, 1f).Triangles);
    }

    private static HeightmapTile WithBlend(HeightmapTile t, byte[] layers, Func<int, int, int, byte> weight)
    {
        int br = 2 * t.Rows - 1, bc = 2 * t.Columns - 1;
        var w = new byte[br * bc * layers.Length];
        for (int fc = 0; fc < bc; fc++)
            for (int fr = 0; fr < br; fr++)
                for (int s = 0; s < layers.Length; s++) w[(fc * br + fr) * layers.Length + s] = weight(fr, fc, s);
        return new HeightmapTile(t.Version, t.Rows, t.Columns, t.Heights, t.VertexPresent) { Layers = layers, BlendWeights = w };
    }

    // Emplacements 0x0C -> couche 1, 0x01 -> couche 2 ; 0xFF (vide) -> aucune ; la couche 0 est le repli.
    private static int[]? Slots(HeightmapTile t) => t.BlendWeights is null ? null : [.. t.Layers.Select(id => id switch { 0x0C => 1, 0x01 => 2, _ => -1 })];

    [Fact]
    public void Vertex_layer_weights_come_from_the_blend_texel_under_the_vertex()
    {
        // Couche 2 pleine sur les deux premieres rangees de sommets (rangees fines 0 a 2), couche 1 ailleurs.
        HeightmapTile t = WithBlend(Flat(4), [0x0C, 0x01, 0xFF], (fr, fc, s) => s == 2 ? (byte)0 : (fr <= 2) == (s == 1) ? (byte)255 : (byte)0);
        SwtorTerrainMesh m = SwtorTerrain.BuildMesh([(t, Matrix4x4.Identity)], 1, ZUp, false, 1f, Slots, 0);
        Assert.NotNull(m.VertexLayers);
        for (int v = 0; v < m.Positions.Length; v++)
        {
            bool firstRows = m.Positions[v].X < -0.05f; // rangees 0 et 1 : X = -0,3 et -0,1
            Assert.Equal(firstRows ? 2 : 1, m.VertexLayers![4 * v]);
            Assert.Equal(255, m.VertexWeights![4 * v]);
        }
    }

    [Fact]
    public void Tile_without_blend_map_uses_the_fallback_layer()
    {
        SwtorTerrainMesh m = SwtorTerrain.BuildMesh([(Flat(3), Matrix4x4.Identity)], 1, ZUp, false, 1f, Slots, 0);
        var parts = SwtorTerrain.SplitByLayers(m);
        SwtorTerrainPart p = Assert.Single(parts);
        Assert.Equal(0, p.Base);
        Assert.Equal(-1, p.Overlay);
        Assert.Null(p.Alpha);
        Assert.Equal(m.Triangles.Length, p.Triangles.Length);
    }

    [Fact]
    public void Split_gives_single_layer_parts_and_a_blended_pair_with_vertex_alpha()
    {
        // Rangees de sommets 0-1 : couche 1 seule ; rangee 2 : moitie/moitie ; rangees 3-4 : couche 2 seule.
        HeightmapTile t = WithBlend(Flat(5), [0x0C, 0x01], (fr, fc, s) =>
        {
            int over = fr < 4 ? 0 : fr == 4 ? 128 : 255;
            return (byte)(s == 1 ? over : 255 - over);
        });
        SwtorTerrainMesh m = SwtorTerrain.BuildMesh([(t, Matrix4x4.Identity)], 1, ZUp, false, 1f, Slots, 0);
        var parts = SwtorTerrain.SplitByLayers(m);
        Assert.Equal(m.Triangles.Length, parts.Sum(p => p.Triangles.Length));
        SwtorTerrainPart one = Assert.Single(parts, p => p.Base == 1 && p.Overlay == -1);
        SwtorTerrainPart two = Assert.Single(parts, p => p.Base == 2 && p.Overlay == -1);
        SwtorTerrainPart pair = Assert.Single(parts, p => p.Overlay >= 0);
        Assert.Equal((1, 2), (pair.Base, pair.Overlay));
        Assert.Equal(4 * 2, one.Triangles.Length / 3);   // cellules de la rangee 0 seulement (la rangee 1 touche la rangee 2, melangee)
        Assert.Equal(4 * 2, two.Triangles.Length / 3);
        Assert.Equal(4 * 2 * 2, pair.Triangles.Length / 3);
        Assert.NotNull(pair.Alpha);
        for (int v = 0; v < pair.Positions.Length; v++)
        {
            float x = pair.Positions[v].X; // -0,2 / 0 / 0,2 pour les rangees 1 / 2 / 3
            int expected = x < -0.1f ? 0 : x > 0.1f ? 255 : 128;
            Assert.InRange(pair.Alpha![v], expected - 1, expected + 1);
        }
        Assert.All(parts, p => Assert.Equal(p.Positions.Length, p.Uv.Length));
    }

    [Fact]
    public void Ground_texture_is_the_up_facing_material_lying_on_the_terrain()
    {
        var terrain = SwtorTerrain.BuildMesh([(Flat(9, 0f), Matrix4x4.Identity)], 1, ZUp, false, 1f);
        SceneMesh Quad(string tex, float z, float size) => new("r", "n", tex, null, Kotor.Core.Textures.TxiInfo.Empty, 0,
            [new(0, 0, z), new(size, 0, z), new(size, size, z), new(0, size, z)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY], null, [0, 1, 2, 0, 2, 3]);
        var meshes = new[] { Quad("sand", 0.01f, 0.5f), Quad("roof", 5f, 0.7f), Quad("missing", 0f, 0.7f) };
        string? ground = SwtorTerrain.ChooseGroundTexture(meshes, terrain.Positions, 0.25f, k => k != "missing", out SurfaceAlpha alpha);
        Assert.Equal("sand", ground);
        Assert.Equal(SurfaceAlpha.Opaque, alpha);
        Assert.Null(SwtorTerrain.ChooseGroundTexture([Quad("roof", 5f, 1f)], terrain.Positions, 0.25f, _ => true, out _));
    }
}
