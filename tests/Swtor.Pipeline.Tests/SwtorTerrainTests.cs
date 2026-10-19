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
