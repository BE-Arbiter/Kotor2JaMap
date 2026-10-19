using System.Numerics;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Pipeline.Preview;

namespace Swtor.Pipeline.Tests;

/// <summary>Selection des rooms sur la carte (grandes zones) : filtre de la scene deja construite et room du lot touche par un rayon.</summary>
public class RoomMapSelectionTests
{
    private static SceneMesh Mesh(string room, float x) => new(room, "n", "tex", null, TxiInfo.Empty, 0,
        [new Vector3(x, 0, 0), new Vector3(x + 1, 0, 0), new Vector3(x, 1, 0)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
        new Vector2[3], null, [0, 1, 2]);

    private static SceneWalkmesh Walk(string room) => new(room, [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2], [true]);

    private static ModuleScene Scene() =>
        new(new ModuleInfo("m", "a", "A", new ModuleEntry(Vector3.Zero, Vector2.UnitX)),
            [Mesh("A", 0), Mesh("b", 10), Mesh("C", 20)], [Walk("A"), Walk("b"), Walk("C")], new Dictionary<string, KotorTexture?>())
        {
            RoomVisibility = new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["A"] = ["A", "b", "C"],
                ["b"] = ["b", "C"],
                ["C"] = ["C"],
            },
        };

    [Fact]
    public void KeepRooms_drops_other_rooms_from_meshes_walkmeshes_and_visibility()
    {
        ModuleScene kept = SwtorSceneFilter.KeepRooms(Scene(), ["a", "B"]);   // sans tenir compte de la casse
        Assert.Equal(["A", "b"], kept.Meshes.Select(m => m.Room));
        Assert.Equal(["A", "b"], kept.Walkmeshes.Select(w => w.Room));
        Assert.Equal(["A", "b"], kept.RoomVisibility!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(["A", "b"], kept.RoomVisibility!["A"].OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(["b"], kept.RoomVisibility!["b"]);
    }

    [Fact]
    public void KeepRooms_leaves_a_scene_without_visibility_data_without_it()
    {
        ModuleScene scene = Scene() with { RoomVisibility = null };
        Assert.Null(SwtorSceneFilter.KeepRooms(scene, ["A"]).RoomVisibility);
    }

    [Fact]
    public void RaycastRoom_returns_the_room_of_the_nearest_batch()
    {
        PreviewVertex V(float x, float y, float z) => new(new Vector3(x, y, z), Vector3.UnitZ, Vector2.Zero, Vector2.Zero);
        PreviewBatch Quad(string room, float z) => new(room, "missing", null, PreviewBlend.Opaque, true,
            [V(-1, -1, z), V(1, -1, z), V(0, 1, z)], [0, 1, 2], Room: room);
        var model = new PreviewModel([Quad("far", 0), Quad("near", 5)], new Dictionary<string, PreviewTexture>(), [], [], null, 0, Vector3.Zero, Vector3.Zero);

        var hit = PreviewPicker.RaycastRoom(model, new Vector3(0, 0, 10), -Vector3.UnitZ);
        Assert.Equal("near", hit?.Room);
        Assert.Equal(5f, hit!.Value.Point.Z, 3);
        Assert.Null(PreviewPicker.RaycastRoom(model, new Vector3(50, 0, 10), -Vector3.UnitZ));
    }

    [Fact]
    public void Selection_round_trips_through_json_and_ignores_unknown_rooms()
    {
        var sel = new SwtorRoomSelection("123", "war_test", [new("a", 10, false), new("b", 20, false), new("c", 30, false)]);
        sel.SelectAll(false);
        sel.Set("b", true);
        string json = sel.ToFile("my_map").ToJson();

        var other = new SwtorRoomSelection("123", "war_test", [new("a", 10, false), new("b", 20, false)]);
        SwtorSelectionFile file = SwtorSelectionFile.FromJson(json)!;
        Assert.Equal("my_map", file.MapName);
        Assert.Empty(other.Apply(file));
        Assert.Equal(["b"], other.SelectedRooms);

        var third = new SwtorRoomSelection("123", "war_test", [new("a", 10, false)]);
        Assert.Equal(["b"], third.Apply(file));
        Assert.Empty(third.SelectedRooms);
    }
}
