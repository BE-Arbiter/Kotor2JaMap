using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core.Geometry;
using Kotor.Core.Layout;

namespace Kotor.Compiler.Tests;

public class RoomVisibilityTests
{
    private static readonly string[] Rooms = ["a", "b", "c"];

    private static RoomVisibility Vis(params (string Room, string[] Sees)[] entries) =>
        RoomVisibility.Create(Rooms, entries.ToDictionary(e => e.Room, e => (IReadOnlyCollection<string>)e.Sees));

    [Fact]
    public void Matrix_is_symmetric_with_true_diagonal()
    {
        var v = Vis(("a", ["b"]), ("b", []), ("c", []));
        Assert.True(v.Sees(0, 1) && v.Sees(1, 0)); // a voit b => b voit a
        Assert.False(v.Sees(0, 2) || v.Sees(2, 0));
        for (int i = 0; i < 3; i++) Assert.True(v.Sees(i, i));
    }

    [Fact]
    public void Room_without_entry_sees_everything_and_is_seen_by_all()
    {
        var v = Vis(("a", []), ("b", []));
        for (int i = 0; i < 3; i++) { Assert.True(v.Sees(2, i)); Assert.True(v.Sees(i, 2)); }
        Assert.False(v.Sees(0, 1));
    }

    [Fact]
    public void Unknown_names_are_ignored_and_case_is_insensitive()
    {
        var v = Vis(("A", ["B", "zzz"]), ("b", []), ("c", []));
        Assert.True(v.Sees(0, 1));
        Assert.False(v.Sees(0, 2));
    }

    [Fact]
    public void ClusterMatrix_empty_cluster_sees_all_and_is_seen_by_all()
    {
        var v = Vis(("a", []), ("b", []), ("c", []));
        bool[][] m = VisibilityLump.ClusterMatrix([[0], [1], []], v);
        Assert.False(m[0][1]);
        Assert.True(m[2][0] && m[0][2] && m[2][1] && m[1][2]);
    }

    [Fact]
    public void Encode_writes_header_and_bits()
    {
        bool[][] m = [[true, false, true], [false, true, false], [true, false, true]];
        byte[] d = VisibilityLump.Encode(m);
        Assert.Equal(3, BitConverter.ToInt32(d, 0));
        Assert.Equal(1, BitConverter.ToInt32(d, 4));
        Assert.Equal(8 + 3, d.Length);
        Assert.Equal(0b101, d[8]);
        Assert.Equal(0b010, d[9]);
        Assert.Equal(0b101, d[10]);
    }

    [Fact]
    public void VisFile_parses_rooms_and_unions_repeats()
    {
        var vis = VisFile.Parse("RoomA 2\n  RoomB\n  RoomC\nRoomB 1\n  RoomA\nRoomA 1\n  RoomD\n");
        Assert.Equal(["roomb", "roomc", "roomd"], vis.Visible["rooma"].OrderBy(x => x));
        Assert.Single(vis.Visible["roomb"]);
    }

    [Fact]
    public void VisFile_rejects_truncated_entry()
    {
        Assert.Throws<InvalidDataException>(() => VisFile.Parse("a 3\n b\n"));
        Assert.Throws<InvalidDataException>(() => VisFile.Parse("a\n"));
    }

    // Deux pieces cote a cote (x < 100 et x > 100), sans brush : feuilles ouvertes portant le cluster de leur piece.
    private static (BspTree Tree, byte[] Bsp) TwoRoomsBsp(bool seeEachOther)
    {
        RoomBox[] boxes = [new(0, new(0, 0, 0), new(100, 100, 50)), new(1, new(100, 0, 0), new(200, 100, 50))];
        BspTree tree = BspTree.Build([], roomBoxes: boxes);
        var rooms = RoomVisibility.Create(["a", "b"], new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["a"] = seeEachOther ? ["b"] : [],
            ["b"] = seeEachOther ? ["a"] : [],
        });
        var ms = new MemoryStream();
        BspWriter.Write(tree, ms, roomVisibility: rooms);
        return (tree, ms.ToArray());
    }

    [Fact]
    public void Tree_leaves_never_straddle_a_room_box_face()
    {
        var (tree, _) = TwoRoomsBsp(false);
        Assert.True(tree.Clusters.Count >= 2);
        foreach (BspLeaf l in tree.Leaves) Assert.True(l.Rooms!.Count <= 1);
        Assert.Contains(tree.Leaves, l => l.InRoom(0));
        Assert.Contains(tree.Leaves, l => l.InRoom(1));
    }

    [Fact]
    public void Written_bsp_hides_hidden_room_and_shows_visible_room()
    {
        foreach (bool see in new[] { false, true })
        {
            var (tree, bytes) = TwoRoomsBsp(see);
            BspFile bsp = BspReader.Read(bytes);
            Assert.Equal(tree.Clusters.Count, bsp.NumClusters);
            int ca = bsp.Leaves[bsp.FindLeaf(new Vector3(50, 50, 10))].Cluster;
            int cb = bsp.Leaves[bsp.FindLeaf(new Vector3(150, 50, 10))].Cluster;
            Assert.NotEqual(ca, cb);
            Assert.True(bsp.ClusterSees(ca, ca));
            Assert.Equal(see, bsp.ClusterSees(ca, cb));
            Assert.Equal(see, bsp.ClusterSees(cb, ca));
        }
    }

    [Fact]
    public void Without_room_boxes_the_tree_has_no_clusters_and_one_always_visible_cluster_is_written()
    {
        var tree = BspTree.Build([]);
        Assert.Empty(tree.Clusters);
        var ms = new MemoryStream();
        BspWriter.Write(tree, ms);
        BspFile bsp = BspReader.Read(ms.ToArray());
        Assert.Equal(1, bsp.NumClusters);
        Assert.True(bsp.ClusterSees(0, 0));
    }
}
