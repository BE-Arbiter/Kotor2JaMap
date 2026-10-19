using System.Numerics;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Swtor.Core;
using Swtor.Core.Areas;
using Swtor.Core.Dds;
using Swtor.Core.Gr2;
using Swtor.Core.Tests;
using Swtor.Core.Xml;

namespace Swtor.Pipeline.Tests;

/// <summary>Source de ressources en memoire : une dalle de sol de 3,2 m (plan XZ, normale +Y) et un mesh de collision a ignorer.</summary>
internal sealed class FakeSource(string material = "floor_mat", bool withMaterial = true, string alphaMode = "None", string shader = "Opaque", string visibility = "Normal", string polyType = "None") : ISwtorModelSource
{
    public int ModelLoads;

    public Gr2Model? LoadModel(string resourcePath)
    {
        ModelLoads++;
        // a=(0,0,0) b=(0,0,3.2) c=(3.2,0,0) : cross(b-a, c-a) = +Y
        Gr2Mesh Quad(string name) => new()
        {
            Name = name, Flags = Gr2VertexFlags.Position | Gr2VertexFlags.Core, VertexCount = 3,
            Positions = [Vector3.Zero, new Vector3(0, 0, 3.2f), new Vector3(3.2f, 0, 0)],
            Normals = [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY],
            UvSets = [[new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 0)]],
            Indices = [0, 1, 2], Pieces = [new Gr2Piece(0, 1, 0, Vector3.Zero, Vector3.One)], Bones = [],
        };
        return new Gr2Model(1, Vector3.Zero, new Vector3(3.2f), [Quad("floor"), Quad("floor_collision")], [material]);
    }

    public MaterialDef? LoadMaterial(string materialName) =>
        withMaterial ? new MaterialDef(shader, new Dictionary<string, string> { ["DiffuseMap"] = "/art/x/tex" }, alphaMode, 0.5f, visibility, polyType) : null;

    public DdsImage? LoadTexture(string resourcePath) => new(4, 4, Enumerable.Repeat((byte)200, 4 * 4 * 4).ToArray());
}

public class SwtorPipelineTests
{
    private static AreaLayout Layout(params SyntheticArea.Obj[] objs)
    {
        string dir = SyntheticArea.WriteFolder(["r1"], SyntheticArea.StandardAssets(), _ => objs);
        return AreaLayout.Load(dir);
    }

    private static SwtorSceneResult Build(AreaLayout layout, ISwtorModelSource src, ImportLog log, SwtorBuildOptions? o = null) =>
        SwtorSceneBuilder.Build(layout, src, "test_area", (o ?? new SwtorBuildOptions()) with { WorldScale = 1f / SwtorWorldScale.MetresPerGr2Unit }, log); // echelle 1 : les attendus sont en unites GR2

    // ---- axes et Euler ----

    [Fact]
    public void Rotation_axes_map_up_to_Z_and_keep_orientation()
    {
        Assert.Equal(new Vector3(0, 0, 1), SwtorAxes.ToZUp(Vector3.UnitY));
        Assert.Equal(new Vector3(1, 0, 0), SwtorAxes.ToZUp(Vector3.UnitX));
        Assert.Equal(new Vector3(1, 2, 3), SwtorAxes.ToZUp(new Vector3(1, 3, -2)));
        Vector3 x = SwtorAxes.ToZUp(Vector3.UnitX), y = SwtorAxes.ToZUp(Vector3.UnitY), z = SwtorAxes.ToZUp(Vector3.UnitZ);
        Assert.True(Vector3.Dot(Vector3.Cross(x, y), z) > 0.99f); // determinant +1 : c'est une rotation
        Assert.False(SwtorAxes.FlipsWinding(AxisConvention.Rotation));
        Assert.True(SwtorAxes.FlipsWinding(AxisConvention.Mirror));
    }

    [Fact]
    public void Euler_yaw_sends_X_to_minus_Z_and_order_is_Z_then_X_then_Y()
    {
        static Vector3 Rot(Vector3 deg, Vector3 v) => Vector3.Transform(v, AreaTransforms.RotationFromEuler(deg));
        AssertNear(new Vector3(0, 0, -1), Rot(new Vector3(0, 90, 0), Vector3.UnitX));
        AssertNear(new Vector3(0, 1, 0), Rot(new Vector3(0, 0, 90), Vector3.UnitX));
        AssertNear(new Vector3(0, 0, 1), Rot(new Vector3(90, 0, 0), Vector3.UnitY));
        // qy * qx * qz applique Z d'abord (roulis), puis X (tangage), puis Y (lacet : +Z -> +X pour +90).
        AssertNear(new Vector3(1, 0, 0), Rot(new Vector3(90, 90, 0), Vector3.UnitY));
        // Z puis X puis Y : +X -> (Rz 90) +Y -> (Rx 90) +Z -> (Ry 90) +X.
        AssertNear(new Vector3(1, 0, 0), Rot(new Vector3(90, 90, 90), Vector3.UnitX));
        // applique incline (-69, 0, -90) : le roulis ne change pas la normale +Z, puis le tangage la releve : (0 ; 0,934 ; 0,358).
        AssertNear(new Vector3(0, MathF.Sin(69 * MathF.PI / 180), MathF.Cos(69 * MathF.PI / 180)), Rot(new Vector3(-69, 0, -90), Vector3.UnitZ));
        // inclinaison d un ecran mural : pitch X (20) dans le repere de l objet puis lacet -90 : la normale +Z de l ecran s incline dans le plan vertical (-cos20, -sin20, 0), pas en roulis.
        AssertNear(new Vector3(-MathF.Cos(20 * MathF.PI / 180), -MathF.Sin(20 * MathF.PI / 180), 0), Rot(new Vector3(20, -90, 0), Vector3.UnitZ));
    }

    private static void AssertNear(Vector3 e, Vector3 a) => Assert.True(Vector3.Distance(e, a) < 1e-4f, $"{e} != {a}");

    // ---- scene synthetique ----

    [Fact]
    public void Floor_tile_is_placed_in_world_converted_to_Z_up_and_walkable()
    {
        var layout = Layout(new SyntheticArea.Obj(1, SyntheticArea.GroundTile, Position: new Vector3(6.4f, 0, 0)));
        var log = new ImportLog();
        var r = Build(layout, new FakeSource(), log);

        SceneMesh m = Assert.Single(r.Scene.Meshes);          // le mesh *_collision est ecarte
        Assert.Equal(1, r.Stats.SkippedCollisionMeshes);
        Assert.All(m.Positions, p => Assert.Equal(0f, p.Z, 4));
        Assert.Equal(6.4f, m.Positions.Min(p => p.X), 3);
        Assert.Equal(9.6f, m.Positions.Max(p => p.X), 3);
        Assert.Equal(-3.2f, m.Positions.Min(p => p.Y), 3);    // Z SWTOR 3,2 -> Y = -3,2
        Assert.Equal(0f, m.Positions.Max(p => p.Y), 3);
        Assert.All(m.Normals, n => AssertNear(Vector3.UnitZ, n));
        Vector3 a = m.Positions[m.Triangles[0]], b = m.Positions[m.Triangles[1]], c = m.Positions[m.Triangles[2]];
        Assert.True(Vector3.Cross(b - a, c - a).Z > 0);        // reste anti-horaire vu du dessus
        Assert.Equal(1, r.Stats.WalkableTriangles);
        Assert.Equal(0, r.Stats.SolidTriangles);
        Assert.DoesNotContain(log.Warnings, w => w.Code.Contains("MISSING"));
        Assert.NotNull(r.Scene.Textures[m.Texture]);
    }

    [Fact]
    public void Entry_is_on_the_floor()
    {
        var r = Build(Layout(new SyntheticArea.Obj(1, SyntheticArea.GroundTile)), new FakeSource(), new ImportLog());
        Vector3 p = r.Scene.Info.Entry.Position;
        Assert.Equal(0f, p.Z, 3);
        Assert.InRange(p.X, 0f, 3.2f);
        Assert.InRange(p.Y, -3.2f, 0f);
    }

    [Fact]
    public void Yaw_rotation_and_model_cache()
    {
        var src = new FakeSource();
        var layout = Layout(
            new SyntheticArea.Obj(1, SyntheticArea.GroundTile, Euler: new Vector3(0, 90, 0)),
            new SyntheticArea.Obj(2, SyntheticArea.GroundTile, Position: new Vector3(10, 0, 0)));
        var r = Build(layout, src, new ImportLog());
        Assert.Equal(1, src.ModelLoads);                       // un seul chargement pour deux instances
        Assert.Equal(2, r.Stats.ModelInstances);
        SceneMesh m = Assert.Single(r.Scene.Meshes);           // meme materiau, meme room : un seul mesh
        // Lacet +90 sur Y : +X -> -Z et +Z -> +X ; en Z haut (x, -z, y) la dalle couvre x et y dans [0, 3,2].
        var first = m.Positions.Take(3).ToArray();
        Assert.Equal(0f, first.Min(p => p.X), 3);
        Assert.Equal(3.2f, first.Max(p => p.X), 3);
        Assert.Equal(0f, first.Min(p => p.Y), 3);
        Assert.Equal(3.2f, first.Max(p => p.Y), 3);
    }

    [Fact]
    public void Missing_material_is_journaled_with_a_stable_code_and_uses_the_checkerboard()
    {
        var log = new ImportLog();
        var r = Build(Layout(new SyntheticArea.Obj(1, SyntheticArea.GroundTile)), new FakeSource(withMaterial: false), log);
        Assert.Contains(log.Warnings, w => w.Code == "SWTOR_MATERIAL_MISSING");
        Assert.Single(r.Scene.Meshes);
        Assert.Null(r.Scene.Textures[r.Scene.Meshes[0].Texture]);
    }

    [Theory]
    [InlineData("Test", "Opaque", SurfaceAlpha.Cutout)]
    [InlineData("Add", "Opaque", SurfaceAlpha.Additive)]
    [InlineData("Full", "Opaque", SurfaceAlpha.Blend)]
    [InlineData("None", "Opaque", SurfaceAlpha.Opaque)]
    [InlineData("None", "Glass", SurfaceAlpha.Blend)]
    public void Material_alpha_mode_selects_the_surface_opacity(string mode, string shader, SurfaceAlpha expected)
    {
        var r = Build(Layout(new SyntheticArea.Obj(1, SyntheticArea.GroundTile)), new FakeSource(alphaMode: mode, shader: shader), new ImportLog());
        Assert.Equal(expected, r.Scene.Meshes[0].Alpha);
    }

    private static SwtorSceneResult BuildMat(string name, string shader, string alphaMode, ImportLog log, string visibility = "Normal", string polyType = "None") =>
        Build(Layout(new SyntheticArea.Obj(1, SyntheticArea.GroundTile)), new FakeSource(name, true, alphaMode, shader, visibility, polyType), log);

    [Fact]
    public void Hidden_and_fx_transparent_materials_are_never_drawn()
    {
        foreach (var (name, vis) in new[] { ("util_blue_hidden", "Hidden"), ("occluder_wall", "Hidden"), ("fx_transparent", "Normal") })
        {
            var r = BuildMat(name, "Uber", "None", new ImportLog(), vis, "Ignore");
            Assert.Empty(r.Scene.Meshes);
        }
    }

    [Fact]
    public void Non_colliding_hidden_materials_do_not_enter_the_fallback_collision()
    {
        // Sans mesh de collision dans le .gr2 : la geometrie visible sert de collision, sauf materiaux caches non solides.
        var opts = new SwtorBuildOptions { UseCollisionMeshes = true };
        SwtorSceneResult Run(string polyType) => Build(Layout(new SyntheticArea.Obj(1, SyntheticArea.GroundTile)), new FakeSource("util_blue_hidden", true, "None", "Uber", "Hidden", polyType), new ImportLog(), opts);
        SwtorSceneResult ignore = Run("Ignore"), collide = Run("Collide");
        Assert.Equal(0, ignore.Stats.WalkableTriangles + ignore.Stats.SolidTriangles);
        Assert.True(collide.Stats.WalkableTriangles + collide.Stats.SolidTriangles > 0);
    }

    [Fact]
    public void Light_cone_cards_are_skipped_with_a_journal_code()
    {
        var log = new ImportLog();
        var r = BuildMat("standard_light_fog_cone", "AnimatedVFX", "Add", log, polyType: "Ignore");
        Assert.Empty(r.Scene.Meshes);
        Assert.Contains(log.Warnings, w => w.Code == "SWTOR_VFX_SKIPPED");
    }

    [Fact]
    public void Other_additive_materials_stay_additive_with_alpha_baked_into_rgb()
    {
        var r = BuildMat("all_vfx_forcefield_x", "AnimatedUV", "Add", new ImportLog(), polyType: "Ignore");
        SceneMesh m = Assert.Single(r.Scene.Meshes);
        Assert.Equal(SurfaceAlpha.Additive, m.Alpha);
        KotorTexture t = r.Scene.Textures[m.Texture]!;
        Assert.False(t.HasAlpha);
        Assert.Equal(200 * 200 / 255, t.Rgba[0]);   // rgb * alpha / 255
        Assert.Equal(255, t.Rgba[3]);
    }

    [Fact]
    public void Opaque_uber_keeps_its_rgb_and_ignores_the_texture_alpha()
    {
        var r = BuildMat("some_wall", "Uber", "None", new ImportLog(), polyType: "Collide");
        KotorTexture t = r.Scene.Textures[Assert.Single(r.Scene.Meshes).Texture]!;
        Assert.Equal(200, t.Rgba[0]);
        Assert.Equal(255, t.Rgba[3]);
    }

    [Fact]
    public void Alpha_selects_unlit_cutout_and_blend_shaders()
    {
        SceneMesh Mesh(SurfaceAlpha a) => new("r", "n", "tex", null, TxiInfo.Empty, 0, [], [], [], null, []) { Alpha = a };
        var tex = new KotorTexture(1, 1, new byte[4], true, TxiInfo.Empty);
        Assert.Equal(ShaderKind.UnlitCutout, ShaderPlanner.Plan(Mesh(SurfaceAlpha.Cutout), tex, KotorGame.Swtor).Kind);
        Assert.Equal(ShaderKind.Blend, ShaderPlanner.Plan(Mesh(SurfaceAlpha.Blend), tex, KotorGame.Swtor).Kind);
        Assert.Equal(ShaderKind.Unlit, ShaderPlanner.Plan(Mesh(SurfaceAlpha.Opaque), tex, KotorGame.Swtor).Kind);
        Assert.StartsWith("textures/swtor/", ShaderPlanner.Plan(Mesh(SurfaceAlpha.Opaque), tex, KotorGame.Swtor).Name);
    }

    // ---- noms ----

    [Theory]
    [InlineData("war_planworking", "war_planworking")]
    [InlineData("War-Plan Working!", "war_plan_working")]
    [InlineData("a__b", "a_b")]
    public void Map_names_are_sanitized(string input, string expected) => Assert.Equal(expected, SwtorMapNames.FromInternalName(input));

    [Fact]
    public void Long_map_names_are_shortened_deterministically_and_stay_valid()
    {
        string n = SwtorMapNames.FromInternalName(new string('x', 80) + "_end");
        Assert.True(n.Length <= MapNames.MaxLength);
        Assert.Equal(n, SwtorMapNames.FromInternalName(new string('x', 80) + "_end"));
        Assert.Null(MapNames.Validate(n));
        Assert.NotEqual(n, SwtorMapNames.FromInternalName(new string('x', 80) + "_enx"));
        Assert.Equal("a_r1_r2", SwtorMapNames.FromInternalName("a", ["r2", "R1"]));
    }

    [Fact]
    public void Pk3_and_shader_prefix_for_swtor()
    {
        Assert.Equal("swtor", KotorGames.MapPrefix(KotorGame.Swtor));
        Assert.Equal("kotor", KotorGames.MapPrefix(KotorGame.Kotor1));
        Assert.Equal("kotor2", KotorGames.MapPrefix(KotorGame.Kotor2));
        Assert.Equal("textures/swtor/", ShaderNames.PrefixFor(KotorGame.Swtor));
    }

    [Theory]
    [InlineData("war_planworking", "war_planworking", 0)]
    [InlineData("123:a,b", "123", 2)]
    [InlineData(@"C:\x\y", @"C:\x\y", 0)]
    public void Area_spec_parses_rooms(string text, string area, int rooms)
    {
        var s = SwtorAreaSpec.Parse(text);
        Assert.Equal(area, s.Area);
        Assert.Equal(rooms, s.Rooms.Count);
    }

    // ---- integration (ressources reelles) ----

    private const string ShipAreaId = "4611686045100172646";

    [SkippableFact]
    public void WarPlanworking_floor_tiles_are_on_the_3_2m_grid_and_scene_has_expected_shape()
    {
        string? root = TestEnv.SwtorAssetsDir;
        Skip.If(root is null || !Directory.Exists(Path.Combine(root, "world", "areas", ShipAreaId)), "SWTOR assets not available");
        var assets = new SwtorAssets(root);
        var layout = AreaLayout.Load(Path.Combine(root!, "world", "areas", ShipAreaId));
        foreach (var inst in layout.AllInstances.Where(i => i.AssetPath?.Contains("mod_floor_plain") == true))
        {
            Vector3 t = layout.World(inst).Translation;
            Assert.True(Math.Abs(t.X / 3.2f - MathF.Round(t.X / 3.2f)) < 0.01f && Math.Abs(t.Z / 3.2f - MathF.Round(t.Z / 3.2f)) < 0.01f, $"{t}");
        }

        var log = new ImportLog();
        var r = SwtorSceneBuilder.Build(layout, new SwtorAssetsModelSource(assets), "war_planworking", null, log);
        Assert.Equal(0, r.Stats.MissingModels);
        Assert.True(r.Stats.Meshes > 20);
        Assert.True(r.Stats.WalkableTriangles > 1000);
        Assert.DoesNotContain(log.Warnings, w => w.Code is "SWTOR_MODEL_MISSING" or "SWTOR_MATERIAL_MISSING" or "SWTOR_TEXTURE_MISSING" or "SWTOR_NO_FLOOR");
        // Chaque triangle reste anti-horaire par rapport a sa normale : la conversion d'axes n'inverse rien.
        int bad = 0, total = 0;
        foreach (var m in r.Scene.Meshes)
            for (int i = 0; i + 2 < m.Triangles.Length; i += 3)
            {
                int a = m.Triangles[i], b = m.Triangles[i + 1], c = m.Triangles[i + 2];
                if (Vector3.Dot(Vector3.Cross(m.Positions[b] - m.Positions[a], m.Positions[c] - m.Positions[a]), m.Normals[a] + m.Normals[b] + m.Normals[c]) < 0) bad++;
                total++;
            }
        Assert.True(bad < total / 100, $"{bad}/{total} triangles against their normals");
        Assert.InRange(r.Scene.Info.Entry.Position.Z, -0.2f * SwtorWorldScale.MetresPerGr2Unit, 0.2f * SwtorWorldScale.MetresPerGr2Unit); // sol principal a Y = 0 (SWTOR)
    }

    private const string CouncilAreaId = "4611686142106770000";

    [SkippableFact]
    public void DarkCouncil_has_no_opaque_light_cards_or_hidden_utility_surfaces()
    {
        string? root = TestEnv.SwtorAssetsDir;
        Skip.If(root is null || !Directory.Exists(Path.Combine(root, "world", "areas", CouncilAreaId)), "SWTOR Dark Council assets not available");
        var layout = AreaLayout.Load(Path.Combine(root!, "world", "areas", CouncilAreaId));
        var log = new ImportLog();
        var r = SwtorSceneBuilder.Build(layout, new SwtorAssetsModelSource(new SwtorAssets(root)), "dark_council",
            new SwtorBuildOptions { Rooms = ["station_dark_council_chamber"] }, log);
        Assert.Contains(log.Warnings, w => w.Code == "SWTOR_VFX_SKIPPED" && w.Message.Contains("standard_light_fog_cone"));
        foreach (SceneMesh m in r.Scene.Meshes)
        {
            Assert.DoesNotContain("fog_cone", m.Texture);
            Assert.DoesNotContain("fx_transparent", m.Texture);
            Assert.DoesNotContain("_hidden", m.Texture);
            Assert.DoesNotContain("occluder", m.Texture);
        }
        // Les additifs restants ont une texture sans alpha (masque deja applique au rgb) : jamais de carton blanc opaque.
        foreach (SceneMesh m in r.Scene.Meshes.Where(x => x.Alpha == SurfaceAlpha.Additive))
            Assert.False(r.Scene.Textures[m.Texture]!.HasAlpha);
    }

    [SkippableFact]
    public void WarPlanworking_exports_a_swtor_pk3()
    {
        string? root = TestEnv.SwtorAssetsDir;
        Skip.If(root is null || !Directory.Exists(Path.Combine(root, "world", "areas", ShipAreaId)), "SWTOR assets not available");
        string tmp = Path.Combine(Path.GetTempPath(), "swtor_t4_" + Guid.NewGuid().ToString("N"));
        try
        {
            var opt = new ExportOptions("", Path.Combine(tmp, "base"), Path.Combine(tmp, "work"), Game: KotorGame.Swtor);
            Directory.CreateDirectory(opt.JaBaseDir);
            var assets = new SwtorAssets(root);
            var log = new ImportLog();
            var r = SwtorExporter.Export(Path.Combine(root!, "world", "areas", ShipAreaId), new SwtorAreaSpec(ShipAreaId, []), null, assets, opt, null, log);
            Assert.True(r.Export.Ok, r.Export.Error);
            Assert.Equal("war_planworking", r.MapName);
            Assert.Equal(Path.Combine(opt.JaBaseDir, "swtor_war_planworking.pk3"), r.Export.Pk3Path);
            Assert.True(File.Exists(r.Export.Pk3Path));
            Assert.DoesNotContain(log.Warnings, w => w.Code is "COLLISION_CAPPED" or "WALKABLE_CAPPED" or "BSP_ERROR"); // collision des .gr2 : sous les limites
            Assert.True(r.Stats!.CollisionInstances > 300);
            Assert.True(r.Bsp!.Collision.BrushCount < 60000);
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }
}
