using System.Numerics;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Scene;
using Kotor.Pipeline;
using Swtor.Core;
using Swtor.Core.Areas;
using Swtor.Core.Dds;
using Swtor.Core.Gr2;
using Swtor.Core.Tests;
using Swtor.Core.Xml;

namespace Swtor.Pipeline.Tests;

/// <summary>Source en memoire dont le modele a les meshes demandes : un nom contenant "wall" donne un mur (normale +Z SWTOR, bloquant), sinon un sol (normale +Y, marchable).</summary>
internal sealed class MeshSource(params string[] meshes) : ISwtorModelSource
{
    private static Gr2Mesh Mesh(string name)
    {
        bool wall = name.Contains("wall", StringComparison.Ordinal);
        Vector3[] pos = wall
            ? [Vector3.Zero, new Vector3(3.2f, 0, 0), new Vector3(0, 3.2f, 0)]       // cross = +Z
            : [Vector3.Zero, new Vector3(0, 0, 3.2f), new Vector3(3.2f, 0, 0)];      // cross = +Y
        Vector3 n = wall ? Vector3.UnitZ : Vector3.UnitY;
        return new Gr2Mesh
        {
            Name = name, Flags = Gr2VertexFlags.Position | Gr2VertexFlags.Core, VertexCount = 3, Positions = pos, Normals = [n, n, n],
            UvSets = [[Vector2.Zero, Vector2.UnitY, Vector2.UnitX]], Indices = [0, 1, 2],
            Pieces = [new Gr2Piece(0, 1, 0, Vector3.Zero, Vector3.One)], Bones = [],
        };
    }

    public Gr2Model? LoadModel(string resourcePath) =>
        new(1, Vector3.Zero, new Vector3(3.2f), meshes.Select(Mesh).ToList(), ["mat"]);

    public MaterialDef? LoadMaterial(string materialName) =>
        new("Opaque", new Dictionary<string, string> { ["DiffuseMap"] = "/art/x/tex" }, "None");

    public DdsImage? LoadTexture(string resourcePath) => new(4, 4, Enumerable.Repeat((byte)200, 64).ToArray());
}

public class CollisionLightingTests
{
    private static Gr2Model ModelOf(params string[] names) => new MeshSource(names).LoadModel("x")!;

    private static AreaLayout Layout(string[] rooms, params SyntheticArea.Obj[] objs)
    {
        string dir = SyntheticArea.WriteFolder(rooms, SyntheticArea.StandardAssets(), _ => objs);
        return AreaLayout.Load(dir);
    }

    private static SwtorSceneResult Build(AreaLayout l, ISwtorModelSource s, SwtorBuildOptions? o = null, ImportLog? log = null) =>
        SwtorSceneBuilder.Build(l, s, "t", (o ?? new SwtorBuildOptions()) with { WorldScale = 1f / SwtorWorldScale.MetresPerGr2Unit }, log ?? new ImportLog()); // echelle 1 : les attendus sont en unites GR2

    // ---- selection des meshes de collision ----

    [Fact]
    public void Collision_override_wins_over_everything()
    {
        var sel = SwtorSceneBuilder.SelectCollisionMeshes(ModelOf("vis", "generated_collision", "collision", "collision_override"));
        Assert.Equal(["collision_override"], sel.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Explicit_collision_replaces_generated_collision()
    {
        var sel = SwtorSceneBuilder.SelectCollisionMeshes(ModelOf("vis", "generated_collision", "COLLISION01"));
        Assert.Equal(["COLLISION01"], sel.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Generated_collision_alone_is_used_and_no_collision_means_fallback()
    {
        Assert.Equal(["generated_collision"], SwtorSceneBuilder.SelectCollisionMeshes(ModelOf("vis", "generated_collision")).Select(m => m.Name).ToArray());
        Assert.Empty(SwtorSceneBuilder.SelectCollisionMeshes(ModelOf("vis")));
    }

    [Theory]
    [InlineData("SPACE", true)]
    [InlineData("outer_space", true)]
    [InlineData("f2_bridge_space", true)]
    [InlineData("cine_doombringer_space_room01", true)]
    [InlineData("spaceport", false)]
    [InlineData("room_01", false)]
    public void Space_rooms_are_recognised(string name, bool expected) => Assert.Equal(expected, SwtorSceneBuilder.IsSpaceRoom(name));

    // ---- construction de la scene ----

    [Fact]
    public void Collision_mesh_drives_the_walkmesh_and_render_meshes_are_not_solid()
    {
        // Rendu : sol (marchable) ; collision : un mur seul -> le walkmesh est le mur (1 solide, 0 marchable).
        var r = Build(Layout(["r1"], new SyntheticArea.Obj(1, SyntheticArea.GroundTile)), new MeshSource("floor", "generated_collision_wall"));
        Assert.Single(r.Scene.Meshes);
        Assert.False(r.Scene.RenderMeshesAreSolid);
        Assert.Equal(0, r.Stats.WalkableTriangles);
        Assert.Equal(1, r.Stats.SolidTriangles);
        Assert.Equal(1, r.Stats.CollisionInstances);
        Assert.Equal(0, r.Stats.FallbackInstances);
    }

    [Fact]
    public void Model_without_collision_falls_back_to_visible_geometry()
    {
        var log = new ImportLog();
        var r = Build(Layout(["r1"], new SyntheticArea.Obj(1, SyntheticArea.GroundTile)), new MeshSource("floor"), log: log);
        Assert.Equal(1, r.Stats.FallbackInstances);
        Assert.Equal(0, r.Stats.CollisionInstances);
        Assert.Equal(1, r.Stats.WalkableTriangles);
        Assert.Contains(log.Warnings, w => w.Code == "SWTOR_COLLISION_FALLBACK");
    }

    [Fact]
    public void Disabling_collision_meshes_restores_the_visible_geometry_as_collision()
    {
        var r = Build(Layout(["r1"], new SyntheticArea.Obj(1, SyntheticArea.GroundTile)), new MeshSource("floor", "generated_collision_wall"),
            new SwtorBuildOptions { UseCollisionMeshes = false });
        Assert.True(r.Scene.RenderMeshesAreSolid);
        Assert.Equal(1, r.Stats.WalkableTriangles);
        Assert.Equal(0, r.Stats.SolidTriangles);
    }

    [Fact]
    public void Space_room_is_excluded_by_default_and_included_on_request()
    {
        var layout = Layout(["r1", "SPACE"], new SyntheticArea.Obj(1, SyntheticArea.GroundTile));
        var src = new MeshSource("floor", "generated_collision");
        Assert.Equal(1, Build(layout, src).Stats.Rooms);
        Assert.Equal(2, Build(layout, src, new SwtorBuildOptions { IncludeSpaceRoom = true }).Stats.Rooms);
        Assert.Equal(1, Build(layout, src, new SwtorBuildOptions { Rooms = ["SPACE"] }).Stats.Rooms); // choix explicite : respecte
        Assert.Equal(SwtorSceneBuilder.SpaceSkyShader, Build(layout, src).Scene.SkyShader);
    }

    [Fact]
    public void Room_subset_keeps_only_requested_rooms()
    {
        var layout = Layout(["r1", "r2"], new SyntheticArea.Obj(1, SyntheticArea.GroundTile));
        var src = new MeshSource("floor", "generated_collision");
        Assert.Equal(2, Build(layout, src).Stats.Rooms);
        var one = Build(layout, src, new SwtorBuildOptions { Rooms = ["R2"] });
        Assert.Equal(1, one.Stats.Rooms);
        Assert.All(one.Scene.Meshes, m => Assert.Equal("r2", m.Room));
        Assert.Equal(1, one.Stats.RoomTriangles["r2"]);
    }

    [Fact]
    public void Export_plan_adds_no_render_geometry_to_solid_when_not_solid()
    {
        var r = Build(Layout(["r1"], new SyntheticArea.Obj(1, SyntheticArea.GroundTile)), new MeshSource("floor", "generated_collision_wall"));
        var opt = new ExportOptions("k", "b", "w").ForGame(KotorGame.Swtor);
        ExportPlan plan = ModuleExporter.BuildPlan(r.Scene, "t", opt, new ImportLog());
        Assert.Single(plan.Input.Solid.SubMeshes);                   // le mur de collision seulement
        Assert.Equal("walkmesh_solid", plan.Input.Solid.SubMeshes[0].MaterialName);
        var kotor = ModuleExporter.BuildPlan(r.Scene with { RenderMeshesAreSolid = true }, "t", opt, new ImportLog());
        Assert.Equal(2, kotor.Input.Solid.SubMeshes.Count);          // + la surface visible (chemin KOTOR inchange)
    }

    // ---- lumieres ----

    private static LightParams Params(SwtorLightType t = SwtorLightType.Point, float brightness = 1f, float gain = 1f, float extent = 1f, string? falloff = null) =>
        new(t, brightness, gain, new Vector3(extent, extent / 2, extent / 4), falloff, null, null);

    [Fact]
    public void Light_radius_follows_the_largest_scale_within_bounds()
    {
        var o = new SwtorBuildOptions();
        Assert.Equal(3f, SwtorSceneBuilder.MakeLight(1, Vector3.Zero, Vector3.One, Params(extent: 2f), o)!.Radius, 3);
        Assert.Equal(o.LightMinRadius, SwtorSceneBuilder.MakeLight(1, Vector3.Zero, Vector3.One, Params(extent: 0.1f), o)!.Radius, 3);
        Assert.Equal(o.LightMaxRadius, SwtorSceneBuilder.MakeLight(1, Vector3.Zero, Vector3.One, Params(extent: 300f), o)!.Radius, 3);
    }

    [Fact]
    public void Light_brightness_is_capped_spots_are_dimmed_and_dark_lights_dropped()
    {
        var o = new SwtorBuildOptions();
        Assert.Equal(o.LightMaxBrightness, SwtorSceneBuilder.MakeLight(1, Vector3.Zero, Vector3.One, Params(brightness: 100f), o)!.Multiplier, 3);
        Assert.Equal(0.6f, SwtorSceneBuilder.MakeLight(1, Vector3.Zero, Vector3.One, Params(SwtorLightType.Spot), o)!.Multiplier, 3);
        Assert.Equal(0.6f, SwtorSceneBuilder.MakeLight(1, Vector3.Zero, Vector3.One, Params(falloff: "falloff_spotlight"), o)!.Multiplier, 3);
        Assert.Null(SwtorSceneBuilder.MakeLight(1, Vector3.Zero, Vector3.One, Params(brightness: 0f), o));
        Assert.Null(SwtorSceneBuilder.MakeLight(1, Vector3.Zero, Vector3.Zero, Params(), o));
    }

    [Fact]
    public void Light_instances_become_scene_lights_with_decoded_fields()
    {
        void Fields(List<byte> f)
        {
            SyntheticArea.Vec4Field(f, RoomFieldHashes.LightColor, new Vector4(1, 0.5f, 0.25f, 1));
            SyntheticArea.RawField(f, 3, LightFields.LightType, 3, 0, 0, 0);
            SyntheticArea.RawField(f, 4, LightFields.Gain, BitConverter.GetBytes(0.5f));
        }
        var layout = Layout(["r1"],
            new SyntheticArea.Obj(1, SyntheticArea.GroundTile),
            new SyntheticArea.Obj(2, SyntheticArea.LightClass, Version: 3, Position: new Vector3(1, 2, 3), Scale: new Vector3(2, 1, 1), Extra: Fields, ExtraFields: 3));
        var r = Build(layout, new MeshSource("floor", "generated_collision"));
        SceneLight l = Assert.Single(r.Scene.Lights);
        Assert.Equal(new Vector3(1, 0.5f, 0.25f), l.Color);
        Assert.Equal(3f, l.Radius, 3);                        // 2 x 1,5
        Assert.Equal(0.5f * 0.6f, l.Multiplier, 3);            // gain 0,5, projecteur x 0,6
        Assert.True(Vector3.Distance(new Vector3(1, -3, 2), l.Position) < 1e-4f); // (x, -z, y)
    }

    // ---- eclairage de zone ----

    private static ModuleScene EmptyScene() =>
        new(new Kotor.Core.Modules.ModuleInfo("m", "m", "m", new Kotor.Core.Modules.ModuleEntry(Vector3.Zero, Vector2.UnitX)), [], [],
            new Dictionary<string, Kotor.Core.Textures.KotorTexture?>());

    [Fact]
    public void Indoor_area_with_lights_gets_a_dim_ambient_and_no_sun_with_lights()
    {
        var o = new SwtorBuildOptions();
        var s = SwtorSceneBuilder.ApplyAreaLighting(EmptyScene(), AreaLighting.Default, o, hasLights: true);
        Assert.Equal(new Vector3(o.AmbientLit), s.AmbientColor);
        Assert.False(s.SunWithLights);
        Assert.Equal(new Vector3(o.AmbientUnlit), SwtorSceneBuilder.ApplyAreaLighting(EmptyScene(), AreaLighting.Default, o, hasLights: false).AmbientColor);
    }

    [Fact]
    public void Outdoor_area_keeps_the_sun_with_lights_and_its_direction()
    {
        var area = AreaLighting.From(new AreaProperties(new Dictionary<string, string>
        {
            ["is_outdoor"] = "true", ["static_light_front"] = "#1,0.5,0,1", ["static_light_intensity"] = "1", ["light_polar"] = "180", ["light_azimuth"] = "0",
        }));
        var s = SwtorSceneBuilder.ApplyAreaLighting(EmptyScene(), area, new SwtorBuildOptions(), hasLights: true);
        Assert.True(s.SunWithLights);
        Assert.True(s.SunDiffuseColor.X > s.SunDiffuseColor.Y && s.SunDiffuseColor.Y > 0);
        Assert.True(s.SunDirection!.Value.Z < -0.99f);         // polaire 180 : la lumiere descend
    }

    [Fact]
    public void Sun_with_lights_keeps_the_sun_term_in_the_compiler_lighting()
    {
        var lights = new List<Kotor.Compiler.Bsp.PointLight> { new(Vector3.Zero, Vector3.One, 100f, 1f) };
        var sun = Kotor.Compiler.Bsp.VertexLighting.Default with { SunWithLights = true };
        var input = new Kotor.Compiler.CompileInput("m", [], [], new Kotor.Core.Geometry.ModelMesh { Name = "s", SubMeshes = [] },
            new Kotor.Core.Geometry.ModelMesh { Name = "w", SubMeshes = [] }, "sky", sun, lights);
        Assert.True(Kotor.Compiler.Compiler.EffectiveLighting(input).Sun.X > 0f);
        Assert.Equal(Vector3.Zero, Kotor.Compiler.Compiler.EffectiveLighting(input with { Lighting = sun with { SunWithLights = false } }).Sun); // KOTOR : sans soleil
    }
}
