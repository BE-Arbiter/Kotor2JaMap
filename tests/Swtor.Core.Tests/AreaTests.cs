using System.Numerics;
using Swtor.Core.Areas;
using static Swtor.Core.Tests.SyntheticArea;

namespace Swtor.Core.Tests;

public class AreaDatReaderTests
{
    [Fact]
    public void Reads_rooms_assets_properties_and_markers()
    {
        byte[] data = Area(["_everywhere_", "room_01"], StandardAssets(), "~0|2|10|is_outdoor|5|false|13|skyscene_room|8|skyscene|", ["arrival_spy"]);
        AreaDat area = AreaDatReader.Read(data);
        Assert.Equal(["_everywhere_", "room_01"], area.Rooms);
        Assert.Equal(GroundTilePath, area.ResolveAsset(GroundTile));
        Assert.Null(area.ResolveAsset(UnlistedType));
        Assert.Equal(false, area.AreaProperties.GetBool("is_outdoor"));
        Assert.Equal("skyscene", area.AreaProperties.Get("skyscene_room"));
        Assert.Equal("arrival_spy", Assert.Single(area.Markers).Name);
    }

    [Fact]
    public void Wrong_magic_throws()
    {
        byte[] data = Area([], []);
        data[6] = (byte)'X';
        Assert.Throws<GameFormatException>(() => AreaDatReader.Read(data));
    }

    [Fact]
    public void Truncated_file_throws_instead_of_crashing()
    {
        byte[] data = Area(["a"], StandardAssets());
        Assert.Throws<GameFormatException>(() => AreaDatReader.Read(data[..60]));
    }

    [Fact]
    public void Properties_parse_sized_names_and_values_and_colors()
    {
        var p = AreaProperties.Parse("~0|4|15|default_minimap|0||18|static_light_front|10|#1,0.5,0,1|14|static_fog_max|3|0.7|20|static_clip_extended|3|100|");
        Assert.Equal("", p.Get("default_minimap"));
        Assert.Equal((1f, 0.5f, 0f, 1f), p.GetColor("static_light_front"));
        Assert.Equal(0.7f, p.GetFloat("static_fog_max"));
        Assert.Equal(100f, p.GetFloat("static_clip_extended"));
        Assert.Null(p.Get("absent"));
    }

    [Fact]
    public void Properties_parse_of_garbage_is_empty()
    {
        Assert.Empty(AreaProperties.Parse("not a property string").Values);
        Assert.Empty(AreaProperties.Parse("").Values);
    }
}

public class RoomDatReaderTests
{
    [Fact]
    public void Reads_model_with_transform_parent_and_listed_kind()
    {
        byte[] data = Room("/world/areas/1/room.dat",
            [new Obj(0x10, GroundTile, Position: new Vector3(6.4f, 0, -3.2f), Euler: new Vector3(0, -90, 0), Scale: new Vector3(2, 2, 2), Parent: 0x99)],
            linked: ["airlock", "hall"]);
        RoomDat room = RoomDatReader.Read(data, StandardAssets());
        Assert.Equal("/world/areas/1/room.dat", room.InternalPath);
        Assert.Equal(["airlock", "hall"], room.LinkedRooms);
        Assert.Equal("1", room.Properties["Area"]);
        RoomInstance i = Assert.Single(room.Instances);
        Assert.Equal(0x10UL, i.Id);
        Assert.Equal(InstanceKind.Model, i.Kind);
        Assert.Equal(GroundTilePath, i.AssetPath);
        Assert.Equal("gr2", i.Extension);
        Assert.Equal(0x99UL, i.ParentId);
        Assert.Equal(new Vector3(6.4f, 0, -3.2f), i.Position);
        Assert.Equal(new Vector3(0, -90, 0), i.EulerDegrees);
        Assert.Equal(new Vector3(2, 2, 2), i.Scale);
        Assert.Equal("environments/test/fx", i.FxName);
    }

    [Fact]
    public void Unlisted_type_is_kept_as_unknown_instance()
    {
        byte[] data = Room("/x.dat", [new Obj(1, UnlistedType, Position: new Vector3(1, 2, 3))]);
        RoomInstance i = Assert.Single(RoomDatReader.Read(data, StandardAssets()).Instances);
        Assert.Equal(InstanceKind.Unknown, i.Kind);
        Assert.Null(i.AssetPath);
        Assert.Equal(UnlistedType, i.TypeId);
        Assert.Equal(new Vector3(1, 2, 3), i.Position);
        // sans table d'assets, tout est inconnu
        Assert.Equal(InstanceKind.Unknown, Assert.Single(RoomDatReader.Read(Room("/x.dat", [new Obj(1, GroundTile)]), (IReadOnlyDictionary<ulong, string>?)null).Instances).Kind);
    }

    [Fact]
    public void Reads_light_color_blob_and_extra_scalar_fields()
    {
        var light = new Obj(5, LightClass, Version: 3, ExtraFields: 5, Extra: f =>
        {
            Vec4Field(f, RoomFieldHashes.LightColor, new Vector4(0.2f, 0.5f, 1f, 1f));
            BlobField(f, 0xAAAA0001, [1, 2, 3, 4, 5]);
            RawField(f, 3, 0xAAAA0002, 7, 0, 0, 0);
            RawField(f, 4, 0xAAAA0003, 0, 0, 0x80, 0x3F);
            RawField(f, 1, 0xAAAA0004, 9, 9, 9, 9);
        });
        RoomInstance i = Assert.Single(RoomDatReader.Read(Room("/x.dat", [light]), StandardAssets()).Instances);
        Assert.Equal(InstanceKind.Light, i.Kind);
        Assert.Equal(3, i.Version);
        Assert.Equal(new Vector4(0.2f, 0.5f, 1f, 1f), i.Color);
        Assert.Equal([1, 2, 3, 4, 5], (byte[])i.Fields.Single(f => f.Hash == 0xAAAA0001).Value);
        Assert.Equal(7, i.Fields.Single(f => f.Hash == 0xAAAA0002).Value);
        Assert.Equal(1f, i.Fields.Single(f => f.Hash == 0xAAAA0003).Value);
    }

    [Fact]
    public void Unknown_field_type_throws_with_offset()
    {
        var bad = new Obj(5, GroundTile, ExtraFields: 1, Extra: f => RawField(f, 42, 1, 0, 0, 0, 0));
        var ex = Assert.Throws<GameFormatException>(() => RoomDatReader.Read(Room("/x.dat", [bad]), StandardAssets()));
        Assert.Contains("42", ex.Message);
    }

    [Fact]
    public void Wrong_magic_and_truncation_throw()
    {
        byte[] data = Room("/x.dat", [new Obj(1, GroundTile)]);
        byte[] wrong = (byte[])data.Clone();
        wrong[5] = (byte)'X';
        Assert.Throws<GameFormatException>(() => RoomDatReader.Read(wrong, StandardAssets()));
        Assert.Throws<GameFormatException>(() => RoomDatReader.Read(data[..120], StandardAssets()));
    }

    [Fact]
    public void Room_without_trailing_property_pair_still_reads()
    {
        RoomDat room = RoomDatReader.Read(Room("/x.dat", [new Obj(1, GroundTile)], propKey: null), StandardAssets());
        Assert.Empty(room.Properties);
        Assert.Single(room.Instances);
    }

    [Theory]
    [InlineData("\\art\\static\\a.gr2", InstanceKind.Model)]
    [InlineData("\\engine\\light.lit", InstanceKind.Light)]
    [InlineData("\\engine\\fxplaceable.fxp", InstanceKind.FxPlaceable)]
    [InlineData("\\engine\\spawn_vehicle.spn_veh", InstanceKind.Spawn)]
    [InlineData("\\engine\\heightmap.hms", InstanceKind.Heightmap)]
    [InlineData("\\engine\\portal.p", InstanceKind.Portal)]
    [InlineData("\\cos\\a\\b.cos", InstanceKind.Other)]
    [InlineData("\\waterfall", InstanceKind.Other)]
    public void Classify_by_extension(string path, InstanceKind kind) => Assert.Equal(kind, RoomDatReader.Classify(path));
}

public class AreaLayoutTests
{
    [Fact]
    public void Rotation_about_y_90_maps_x_to_minus_z()
    {
        Vector3 v = Vector3.Transform(Vector3.UnitX, AreaTransforms.RotationFromEuler(new Vector3(0, 90, 0)));
        Assert.True(Vector3.Distance(v, -Vector3.UnitZ) < 1e-5f, v.ToString());
    }

    [Fact]
    public void Child_world_transform_composes_with_parent()
    {
        string dir = WriteFolder(["room_a", "room_b"], StandardAssets(), room => room == "room_a"
            ? [new Obj(1, GroundTile, Position: new Vector3(10, 0, 0), Euler: new Vector3(0, 90, 0))]
            : [new Obj(2, GroundTile, Parent: 1, Position: new Vector3(1, 0, 0)), new Obj(3, GroundTile, Parent: 2, Position: new Vector3(0, 1, 0))]);
        try
        {
            AreaLayout layout = AreaLayout.Load(dir);
            Assert.Equal(3, layout.AllInstances.Count());
            Assert.Equal("room_b", layout.RoomOf(2));
            // l'enfant est a (1,0,0) dans le repere du parent tourne de 90 deg autour de Y : x -> -z, puis translation (10,0,0)
            Vector3 w2 = layout.WorldPosition(layout.Find(2)!);
            Assert.True(Vector3.Distance(w2, new Vector3(10, 0, -1)) < 1e-4f, w2.ToString());
            Vector3 w3 = layout.WorldPosition(layout.Find(3)!);
            Assert.True(Vector3.Distance(w3, new Vector3(10, 1, -1)) < 1e-4f, w3.ToString());
            Assert.Equal(["arrival_test"], layout.Area.Markers.Select(m => m.Name));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Parent_cycle_does_not_loop_forever()
    {
        string dir = WriteFolder(["r"], StandardAssets(), _ =>
            [new Obj(1, GroundTile, Parent: 2, Position: Vector3.UnitX), new Obj(2, GroundTile, Parent: 1, Position: Vector3.UnitY)]);
        try
        {
            AreaLayout layout = AreaLayout.Load(dir);
            Vector3 w = layout.WorldPosition(layout.Find(1)!);
            Assert.True(float.IsFinite(w.X));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Missing_room_file_is_reported()
    {
        string dir = WriteFolder(["r1", "r2"], StandardAssets(), _ => []);
        try
        {
            File.Delete(Path.Combine(dir, "r2.dat"));
            AreaLayout layout = AreaLayout.Load(dir);
            Assert.Equal(["r2"], layout.MissingRooms);
            Assert.Single(layout.Rooms);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResolveFolder_accepts_folder_and_rejects_traversal()
    {
        string dir = WriteFolder(["r"], StandardAssets(), _ => []);
        try
        {
            var assets = new SwtorAssets(Path.GetTempPath());
            Assert.Equal(dir, AreaLayout.ResolveFolder(dir, assets));
            Assert.Null(AreaLayout.ResolveFolder("..\\x", assets));
            Assert.Null(AreaLayout.ResolveFolder("12345678", assets));
        }
        finally { Directory.Delete(dir, true); }
    }
}

/// <summary>Tests d'integration sur les zones SWTOR extraites (ignores si le dossier est absent).</summary>
public class RealAreaTests
{
    /// <summary>Petit interieur : vaisseau Sith avec sas, couloir et salle (flashpoint de debut de classe, ciel spatial).</summary>
    private const string ShipAreaId = "4611686045100172646";

    private static string RequireArea(string id)
    {
        Skip.If(TestEnv.SwtorAssetsDir is null, "Ressources SWTOR absentes");
        string? folder = AreaLayout.ResolveFolder(id, new SwtorAssets());
        Skip.If(folder is null, "Zone absente");
        return folder!;
    }

    private const string DarkCouncilAreaId = "4611686142106770000";

    /// <summary>
    /// Preuve de l'ordre d'Euler : les 12 appliques <c>spc_item_ss_wall_light01</c> de la salle du Conseil noir sont deux familles de
    /// 6 pieces disposees en cercle autour de la verticale du parent. Dans une meme famille, les rotations ne different donc que d'un
    /// lacet pur (la verticale reste la verticale) et le lacet envoie la position d'une piece sur celle d'une autre. Seul l'ordre
    /// X, Z, Y le verifie (ecart 0 deg ; 79,9 deg avec l'ancien ordre Z, Y, X).
    /// </summary>
    [SkippableFact]
    public void DarkCouncil_wall_lights_differ_by_a_pure_yaw()
    {
        AreaLayout layout = AreaLayout.Load(RequireArea(DarkCouncilAreaId));
        var lights = layout.Rooms.Single(r => r.Name == "station_dark_council_chamber").Room.Instances
            .Where(i => i.AssetPath?.EndsWith("spc_item_ss_wall_light01.gr2", StringComparison.OrdinalIgnoreCase) == true && i.Scale.X > 0.5f)
            .ToList();
        Assert.Equal(12, lights.Count);
        foreach (var family in lights.GroupBy(i => (i.ParentId, MathF.Round(i.EulerDegrees.X))))
        {
            var members = family.ToList();
            Assert.True(members.Count >= 3, $"famille {family.Key} : {members.Count}");
            Quaternion q0 = AreaTransforms.RotationFromEuler(members[0].EulerDegrees);
            foreach (var m in members.Skip(1))
            {
                Quaternion rel = AreaTransforms.RotationFromEuler(m.EulerDegrees) * Quaternion.Inverse(q0);
                Vector3 up = Vector3.Transform(Vector3.UnitY, rel);
                Assert.True(Vector3.Distance(up, Vector3.UnitY) < 1e-3f, $"{m.Id:X} : la verticale devient {up}");
            }
            // le lacet relatif (signe compris) amene la position de la premiere piece sur celle d'une autre de la famille
            var other = members.First(m => Math.Abs(m.EulerDegrees.Y - members[0].EulerDegrees.Y) > 45f);
            Quaternion yaw = AreaTransforms.RotationFromEuler(other.EulerDegrees) * Quaternion.Inverse(q0);
            Vector3 moved = Vector3.Transform(members[0].Position, yaw);
            Assert.Contains(members, m => Vector3.Distance(new Vector3(moved.X, 0, moved.Z), new Vector3(m.Position.X, 0, m.Position.Z)) < 0.1f);
        }
    }

    /// <summary>Un ecran mural incline de 20 deg (X) est un tangage : sa normale monte ou descend de sin(20 deg), elle ne tourne pas dans le plan du mur.</summary>
    [SkippableFact]
    public void ShipInterior_wall_monitors_are_pitched_not_rolled()
    {
        AreaLayout layout = AreaLayout.Load(RequireArea(ShipAreaId));
        var monitors = layout.AllInstances.Where(i => i.AssetPath?.EndsWith("all_item_tech_wall_monitor.gr2", StringComparison.OrdinalIgnoreCase) == true
            && MathF.Abs(i.EulerDegrees.X - 20f) < 0.1f).ToList();
        Assert.NotEmpty(monitors);
        foreach (var m in monitors)
        {
            Vector3 n = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, layout.World(m)));
            Assert.True(MathF.Abs(MathF.Abs(n.Y) - MathF.Sin(20f * MathF.PI / 180f)) < 0.01f, $"{m.Id:X} : normale {n}");
        }
    }

    [SkippableFact]
    public void ShipInterior_has_expected_rooms_and_every_model_exists()
    {
        AreaLayout layout = AreaLayout.Load(RequireArea(ShipAreaId));
        Assert.Equal(["_everywhere_", "room_01", "airlock_01", "hallway_01", "SPACE", "skyscene"], layout.Area.Rooms);
        Assert.Empty(layout.MissingRooms);
        Assert.Equal(135, layout.Area.Assets.Count);
        Assert.Contains(layout.Area.Markers, m => m.Name == "arrival_sith_warrior");
        Assert.Equal(false, layout.Area.AreaProperties.GetBool("is_outdoor"));
        Assert.Equal(294, layout.Rooms.Single(r => r.Name == "room_01").Room.Instances.Count);

        var assets = new SwtorAssets();
        var models = layout.AllInstances.Where(i => i.Kind == InstanceKind.Model).ToList();
        Assert.True(models.Count > 400);
        Assert.All(models.Select(m => m.AssetPath!).Distinct(), p => Assert.NotNull(assets.Resolve(p)));
        Assert.Contains(layout.AllInstances, i => i.Kind == InstanceKind.Light && i.Color is not null);
    }

    [SkippableFact]
    public void ShipInterior_floor_tiles_sit_on_the_3_2_metre_grid_and_parents_resolve()
    {
        AreaLayout layout = AreaLayout.Load(RequireArea(ShipAreaId));
        var floors = layout.AllInstances.Where(i => i.AssetPath?.EndsWith("spc_arch_ss_mod_floor_plain.gr2", StringComparison.OrdinalIgnoreCase) == true).ToList();
        Assert.NotEmpty(floors);
        foreach (var f in floors)
        {
            Assert.Equal(0f, f.Position.Y);
            Assert.True(Math.Abs(f.Position.X / 3.2f - MathF.Round(f.Position.X / 3.2f)) < 1e-3f, $"x={f.Position.X}");
            Assert.True(Math.Abs(f.Position.Z / 3.2f - MathF.Round(f.Position.Z / 3.2f)) < 1e-3f, $"z={f.Position.Z}");
        }
        Assert.All(layout.AllInstances.Where(i => i.ParentId != 0), i => Assert.NotNull(layout.Find(i.ParentId)));
    }

    [SkippableTheory]
    [InlineData("4611686041700070345")]
    [InlineData("4611686050082471254")]
    [InlineData("4611686019013922786")]
    public void Other_areas_parse_completely(string id)
    {
        AreaLayout layout = AreaLayout.Load(RequireArea(id));
        Assert.Empty(layout.MissingRooms);
        Assert.NotEmpty(layout.Area.Assets);
    }
}
