using System.IO.Compression;
using Swtor.Core;
using Swtor.Core.Terrain;

namespace Swtor.Core.Tests;

public class HeightmapTileTests
{
    /// <summary>Flux decompresse synthetique : hauteur(r, c) = r + c / 10 (en unites), pourtour compris.</summary>
    internal static byte[] Synthetic(int rows, int cols, bool[]? vertexPresent = null, int version = 2)
    {
        float H(int r, int c) => r + c / 10f;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write((byte)version); w.Write((uint)rows); w.Write((uint)cols);
        w.Write((byte)(vertexPresent is null ? 0 : 1));
        if (vertexPresent is not null)
        {
            var mask = new byte[(rows * cols + 7) / 8];
            for (int i = 0; i < vertexPresent.Length; i++) if (vertexPresent[i]) mask[i / 8] |= (byte)(0x80 >> (i % 8));
            w.Write(mask);
        }
        for (int k = 0; k < rows - 1; k++) { w.Write(H(k, 0)); w.Write(H(k, cols - 1)); }
        for (int k = 0; k < cols - 1; k++) { w.Write(H(0, k)); w.Write(H(rows - 1, k)); }
        for (int r = 1; r < rows - 1; r++)
            for (int c = 1; c < cols - 1; c++) w.Write((ushort)Math.Round(H(r, c) * 512f));
        w.Write(0xABCDEFABu);
        return ms.ToArray();
    }

    [Fact]
    public void Read_decodes_ring_and_interior_heights()
    {
        HeightmapTile t = HeightmapTile.Read(Synthetic(6, 8));
        Assert.Equal(6, t.Rows);
        Assert.Equal(8, t.Columns);
        for (int r = 0; r < 6; r++)
            for (int c = 0; c < 8; c++)
            {
                if (r == 5 && c == 7) continue; // coin non stocke : extrapole
                if (r == 0 && c == 0) continue;
                Assert.Equal(r + c / 10f, t.HeightAt(r, c), 2);
            }
        Assert.Equal(5.7f, t.HeightAt(5, 7), 2); // l'extrapolation d'un plan est exacte
    }

    [Fact]
    public void LocalXZ_is_centred_on_the_object_with_0_2_cells()
    {
        HeightmapTile t = HeightmapTile.Read(Synthetic(65, 65));
        Assert.Equal((-6.4f, -6.4f), t.LocalXZ(0, 0));
        (float x, float z) = t.LocalXZ(64, 32);
        Assert.Equal(6.4f, x, 3);
        Assert.Equal(0f, z, 3);
    }

    [Fact]
    public void Read_decodes_vertex_hole_mask()
    {
        int rows = 5, cols = 5;
        var present = Enumerable.Repeat(true, rows * cols).ToArray();
        present[2 * cols + 2] = false;
        HeightmapTile t = HeightmapTile.Read(Synthetic(rows, cols, present));
        Assert.NotNull(t.VertexPresent);
        Assert.False(t.VertexPresent![2 * cols + 2]);
        Assert.True(t.IsCellPresent(0, 0));
        Assert.False(t.IsCellPresent(1, 1)); // touche le sommet troue
        Assert.False(t.IsCellPresent(2, 2));
        Assert.True(t.IsCellPresent(3, 3));
    }

    [Fact]
    public void ReadBlob_handles_zlib_and_gzip()
    {
        byte[] raw = Synthetic(6, 6);
        foreach (bool gzip in new[] { false, true })
        {
            var ms = new MemoryStream();
            using (Stream z = gzip ? new GZipStream(ms, CompressionLevel.Fastest, true) : new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw);
            Assert.Equal(6, HeightmapTile.ReadBlob(ms.ToArray()).Rows);
        }
    }

    [Fact]
    public void Read_rejects_unknown_version_and_truncated_data()
    {
        Assert.Throws<GameFormatException>(() => HeightmapTile.Read(Synthetic(6, 6, version: 9)));
        byte[] raw = Synthetic(6, 6);
        Assert.Throws<GameFormatException>(() => HeightmapTile.Read(raw[..40]));
        Assert.Throws<GameFormatException>(() => HeightmapTile.ReadBlob([1, 2, 3, 4, 5, 6, 7, 8, 9]));
    }

    [SkippableFact]
    public void Real_terrain_decodes_with_continuous_edges()
    {
        string root = TestEnv.SwtorAssetsDir ?? "";
        Skip.If(root.Length == 0 || !Directory.Exists(Path.Combine(root, "world", "areas")), "SWTOR assets not extracted");
        string folder = Path.Combine(root, "world", "areas", "4611686018427650020");
        Skip.IfNot(File.Exists(Path.Combine(folder, "area.dat")), "Tatooine area not extracted");
        var layout = Swtor.Core.Areas.AreaLayout.Load(folder);
        var tiles = layout.AllInstances.Where(i => i.Kind == Swtor.Core.Areas.InstanceKind.Heightmap).Take(300)
            .Select(i => i.Fields.First(f => f.Type == 9).Value as byte[]).Where(b => b is { Length: > 16 } && b[0] == 0x78)
            .Select(b => HeightmapTile.ReadBlob(b!)).ToList();
        Assert.NotEmpty(tiles);
        int smooth = tiles.Count(t => Enumerable.Range(1, t.Columns - 2).Average(c => Math.Abs(t.HeightAt(0, c) - t.HeightAt(1, c))) < 0.3);
        Assert.True(smooth > tiles.Count * 0.8, "le pourtour doit se raccorder a l'interieur");
    }
}
