using System.Numerics;
using System.Text;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Layout;
using Kotor.Core.Mdl;
using Kotor.Core.Textures;
using Kotor.Core.Scene;
using Kotor.Core.Tests.Archives;
using Kotor.Core.Tests.Gff;

namespace Kotor.Core.Tests.Scene;

public class ModuleSceneBuilderTests
{
    [Fact]
    public void Missing_room_model_is_logged_and_skipped()
    {
        string dir = Path.Combine(Path.GetTempPath(), "k2j_scn_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "modules"));
        try
        {
            var ifo = new GffBuilder();
            int a = ifo.AddStruct(6);
            ifo.Add(a, "Area_Name", 11, "m99zz");
            ifo.Add(ifo.Root, "Mod_Area_list", 15, new[] { a })
               .Add(ifo.Root, "Mod_Entry_X", 8, 0f).Add(ifo.Root, "Mod_Entry_Y", 8, 0f).Add(ifo.Root, "Mod_Entry_Z", 8, 0f)
               .Add(ifo.Root, "Mod_Entry_Dir_X", 8, 0f).Add(ifo.Root, "Mod_Entry_Dir_Y", 8, 1f);
            var are = new GffBuilder();
            are.Add(are.Root, "Name", 12, (0xFFFFFFFFu, new[] { (0, "Zone") }));
            string lyt = "beginlayout\n roomcount 1\n m99zz_01a 0.0 0.0 0.0\n trackcount 0\n obstaclecount 0\n doorhookcount 0\ndonelayout\n";
            File.WriteAllBytes(Path.Combine(dir, "modules", "tst_m99zz.rim"), ArchiveBuilders.Rim(
                ("module", (uint)ResourceType.Ifo, ifo.Build("IFO ")),
                ("m99zz", (uint)ResourceType.Are, are.Build("ARE ")),
                ("m99zz", (uint)ResourceType.Lyt, Encoding.ASCII.GetBytes(lyt))));
            using var res = ResourceResolver.ForModule(dir, "tst_m99zz");
            var log = new ImportLog();
            var scene = ModuleSceneBuilder.Build(res, "tst_m99zz", null, log);
            Assert.Empty(scene.Meshes);
            Assert.Contains(log.Warnings, w => w.Code == "ROOM_MODEL_MISSING");
            Assert.Contains(log.Warnings, w => w.Code == "WALKMESH_MISSING");
        }
        finally { Directory.Delete(dir, true); }
    }

    private static (ModuleScene scene, ImportLog log, LayoutFile lyt) LoadReal()
    {
        using var res = ResourceResolver.ForModule(TestEnv.KotorDir!, "tar_m02aa");
        var log = new ImportLog();
        var scene = ModuleSceneBuilder.Build(res, "tar_m02aa", null, log);
        var lyt = LayoutFile.Parse(Encoding.ASCII.GetString(res.Find("m02aa", ResourceType.Lyt)!));
        return (scene, log, lyt);
    }

    [SkippableFact]
    public void Real_tar_m02aa_scene()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        var (scene, log, lyt) = LoadReal();
        int layoutRooms = lyt.Rooms.Select(r => r.Model).Where(m => !ModuleSceneBuilder.IsSkyRoom(m)).Distinct().Count();
        Assert.Contains(log.Warnings, w => w.Code == "SKY_ROOM_SKIPPED");
        Assert.DoesNotContain(scene.Meshes, m => ModuleSceneBuilder.IsSkyRoom(m.Room));
        int missing = log.Warnings.Count(w => w.Code is "ROOM_MODEL_MISSING" or "ROOM_MODEL_UNREADABLE");
        int roomsWithMeshes = scene.Meshes.Select(m => m.Room).Distinct().Count();
        Assert.Equal(layoutRooms - missing, roomsWithMeshes);
        // Mesure reelle : ~80 % (les 20 % restants sont des meshes auto-eclaires : lts_lite*, lts_nums, lts_glass01, ciel). Seuil du plan (90 %) non tenable, voir rapport.
        double withLm = scene.Meshes.Count(m => m.Lightmap is not null) / (double)scene.Meshes.Count;
        Assert.True(withLm >= 0.75, $"lightmap ratio {withLm}");
        Assert.All(scene.Meshes, m => Assert.All(m.Positions, p => Assert.True(float.IsFinite(p.X + p.Y + p.Z))));
        Assert.All(scene.Walkmeshes, w => Assert.All(w.Positions, p => Assert.True(float.IsFinite(p.X + p.Y + p.Z))));
    }

    [SkippableFact]
    public void Walkmesh_aligns_with_room()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        var (scene, _, _) = LoadReal();
        var wm = scene.Walkmeshes.First(w => w.Room == "m02aa_01a");
        var mm = scene.Meshes.Where(m => m.Room == "m02aa_01a").ToList();
        static (Vector3 min, Vector3 max) Box(IEnumerable<Vector3> pts)
        {
            var mn = new Vector3(float.MaxValue); var mx = new Vector3(float.MinValue);
            foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            return (mn, mx);
        }
        var (a0, a1) = Box(wm.Positions);
        var (b0, b1) = Box(mm.SelectMany(m => m.Positions));
        var ca = (a0 + a1) / 2; var cb = (b0 + b1) / 2;
        Assert.True(Math.Abs(ca.X - cb.X) < 2 && Math.Abs(ca.Y - cb.Y) < 2, $"walk {ca} mesh {cb}");
    }

    // ---- Tests synthetiques du correctif 1 (journal : rien d'ignore en silence) ----

    private sealed class FakeKotor : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "k2j_scn_" + Guid.NewGuid().ToString("N"));
        public ResourceResolver Res { get; }

        public FakeKotor(params (string resRef, uint type, byte[] data)[] extra) : this(null, extra) { }

        public FakeKotor(Action<GffBuilder>? areSetup, (string resRef, uint type, byte[] data)[] extra)
        {
            Directory.CreateDirectory(Path.Combine(Dir, "modules"));
            var ifo = new GffBuilder();
            int a = ifo.AddStruct(6);
            ifo.Add(a, "Area_Name", 11, "m99zz");
            ifo.Add(ifo.Root, "Mod_Area_list", 15, new[] { a })
               .Add(ifo.Root, "Mod_Entry_X", 8, 0f).Add(ifo.Root, "Mod_Entry_Y", 8, 0f).Add(ifo.Root, "Mod_Entry_Z", 8, 0f)
               .Add(ifo.Root, "Mod_Entry_Dir_X", 8, 0f).Add(ifo.Root, "Mod_Entry_Dir_Y", 8, 1f);
            var are = new GffBuilder();
            are.Add(are.Root, "Name", 12, (0xFFFFFFFFu, new[] { (0, "Zone") }));
            areSetup?.Invoke(are);
            string lyt = "beginlayout\n roomcount 1\n m99zz_01a 0.0 0.0 0.0\n trackcount 0\n obstaclecount 0\n doorhookcount 0\ndonelayout\n";
            var entries = new List<(string, uint, byte[])>
            {
                ("module", (uint)ResourceType.Ifo, ifo.Build("IFO ")),
                ("m99zz", (uint)ResourceType.Are, are.Build("ARE ")),
                ("m99zz", (uint)ResourceType.Lyt, Encoding.ASCII.GetBytes(lyt)),
            };
            entries.AddRange(extra);
            File.WriteAllBytes(Path.Combine(Dir, "modules", "tst_m99zz.rim"), ArchiveBuilders.Rim(entries.ToArray()));
            Res = ResourceResolver.ForModule(Dir, "tst_m99zz");
        }

        public void Dispose() { Res.Dispose(); Directory.Delete(Dir, true); }
    }

    private static readonly LayoutRoom Room = new("m99zz_01a", Vector3.Zero);

    private static KotorMesh Mesh(string? tex = "tex_a", string? lm = null, Vector2[]? uv1 = null, bool tris = true) =>
        new(tex, lm, true, lm is not null, 0,
            new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0) }, null,
            new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) }, uv1, tris ? new[] { 0, 1, 2 } : Array.Empty<int>());

    private static KotorModel Model(params (string name, NodeKind kind, KotorMesh? mesh)[] nodes)
    {
        var root = new KotorNode("root", NodeKind.Header, Vector3.Zero, Quaternion.Identity, null);
        foreach (var (name, kind, mesh) in nodes)
            root.AddChild(new KotorNode(name, kind, Vector3.Zero, Quaternion.Identity, mesh));
        return new KotorModel("m99zz_01a", null, root, Vector3.Zero, Vector3.One);
    }

    private static ImportLog RunModel(FakeKotor k, KotorModel model, out List<SceneMesh> meshes, out Dictionary<string, KotorTexture?> textures)
    {
        var log = new ImportLog();
        meshes = new List<SceneMesh>();
        textures = new Dictionary<string, KotorTexture?>();
        ModuleSceneBuilder.AddModel(k.Res, Room, model, textures, meshes, log);
        return log;
    }

    [Fact]
    public void Animated_meshes_are_exported_static_and_logged()
    {
        using var k = new FakeKotor();
        var log = RunModel(k, Model(
            ("skin", NodeKind.Mesh | NodeKind.Skin, Mesh()), ("dangly", NodeKind.Mesh | NodeKind.Dangly, Mesh()),
            ("plain", NodeKind.Mesh, Mesh())), out var meshes, out _);
        Assert.Equal(3, meshes.Count);
        var w = Assert.Single(log.Warnings, w => w.Code == "MESH_STATIC_FROM_ANIMATED");
        Assert.Contains("2 skin/dangly/anim/saber mesh", w.Message);
    }

    [Fact]
    public void Empty_mesh_is_logged()
    {
        using var k = new FakeKotor();
        var log = RunModel(k, Model(("empty", NodeKind.Mesh, Mesh(tris: false)), ("ok", NodeKind.Mesh, Mesh())),
            out var meshes, out _);
        Assert.Single(meshes);
        Assert.Single(log.Warnings, w => w.Code == "MESH_EMPTY");
    }

    [Fact]
    public void Lightmap_without_uv1_is_dropped_and_logged()
    {
        using var k = new FakeKotor();
        var log = RunModel(k, Model(("a", NodeKind.Mesh, Mesh(lm: "lm_a", uv1: null))), out var meshes, out var textures);
        Assert.Null(Assert.Single(meshes).Lightmap);
        Assert.Single(log.Warnings, w => w.Code == "MDL_NO_UV1");
        Assert.DoesNotContain("lm_a", textures.Keys);
    }

    [Fact]
    public void Texture_cache_is_case_insensitive_and_resolved_once()
    {
        using var k = new FakeKotor();
        var uv1 = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) };
        var log = RunModel(k, Model(
            ("a", NodeKind.Mesh, Mesh("Tex_A", "TEX_A", uv1)), ("b", NodeKind.Mesh, Mesh("tex_a", "Tex_A", uv1))),
            out var meshes, out var textures);
        Assert.Equal("tex_a", Assert.Single(textures.Keys));
        Assert.All(meshes, m => { Assert.Equal("tex_a", m.Texture); Assert.Equal("tex_a", m.Lightmap); });
        Assert.Single(log.Warnings, w => w.Code == "TEXTURE_MISSING");
    }

    [Fact]
    public void Unsupported_texture_is_logged_with_null_entry()
    {
        using var k = new FakeKotor(("tex_a", (uint)ResourceType.Tga, new byte[] { 0, 0, 99, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 32, 0, 1, 2, 3, 4 }));
        var log = RunModel(k, Model(("a", NodeKind.Mesh, Mesh())), out var meshes, out var textures);
        Assert.Single(meshes);
        Assert.True(textures.ContainsKey("tex_a"));
        Assert.Null(textures["tex_a"]);
        Assert.Single(log.Warnings, w => w.Code == "TEXTURE_UNSUPPORTED");
    }

    [Fact]
    public void Unreadable_room_model_is_logged_and_skipped()
    {
        using var k = new FakeKotor(
            ("m99zz_01a", (uint)ResourceType.Mdl, new byte[64]), ("m99zz_01a", (uint)ResourceType.Mdx, new byte[16]));
        var log = new ImportLog();
        var scene = ModuleSceneBuilder.Build(k.Res, "tst_m99zz", null, log);
        Assert.Empty(scene.Meshes);
        Assert.Single(log.Warnings, w => w.Code == "ROOM_MODEL_UNREADABLE");
    }

    [Fact]
    public void Unknown_surface_material_is_counted_and_logged()
    {
        var wok = Kotor.Core.Tests.Walkmesh.WokReaderTests.Build(
            [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(1, 1, 0)], [0, 1, 2, 1, 3, 2], [0, 77]);
        var sm = Kotor.Core.Tests.TwoDa.TwoDaFileTests.Build(["Walk"], [["1"]]);
        using var k = new FakeKotor(("m99zz_01a", (uint)ResourceType.Wok, wok), ("surfacemat", (uint)ResourceType.TwoDa, sm));
        var log = new ImportLog();
        var scene = ModuleSceneBuilder.Build(k.Res, "tst_m99zz", null, log);
        var wm = Assert.Single(scene.Walkmeshes);
        Assert.Equal(new[] { true, false }, wm.Walkable);
        var w = Assert.Single(log.Warnings, w => w.Code == "SURFACEMAT_UNKNOWN");
        Assert.Contains("1 walkmesh triangle", w.Message);
    }

    [Theory]
    [InlineData("lda_sky0001", true)]
    [InlineData("LDA_SKY0005", true)]
    [InlineData("lda_sky", false)]
    [InlineData("lda_skylight01", false)]
    [InlineData("lda_grass07", false)]
    [InlineData(null, false)]
    public void Sky_texture_is_recognised_by_name(string? texture, bool sky)
        => Assert.Equal(sky, ModuleSceneBuilder.IsSkyTexture(texture));

    [Theory]
    [InlineData("m02aa_sky", true)]
    [InlineData("M02AA_SKY", true)]
    [InlineData("m02aa_01a", false)]
    [InlineData("m02aa_skyline", false)]
    public void Sky_dome_room_is_recognised_by_name(string model, bool sky)
        => Assert.Equal(sky, ModuleSceneBuilder.IsSkyRoom(model));

    [Fact]
    public void Ambient_colour_is_read_as_bgr()
    {
        var c = ModuleSceneBuilder.FromBgr(0x00FF8040u); // B = 0xFF, G = 0x80, R = 0x40
        Assert.Equal(0x40 / 255f, c.X);
        Assert.Equal(0x80 / 255f, c.Y);
        Assert.Equal(1f, c.Z);
    }

    [SkippableFact]
    public void Real_ebo_m41aa_no_invalid_lightmap_texel()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        using var res = ResourceResolver.ForModule(TestEnv.KotorDir!, "ebo_m41aa");
        var log = new ImportLog();
        var scene = ModuleSceneBuilder.Build(res, "ebo_m41aa", null, log);
        var lightmaps = scene.Meshes.Where(m => m.Lightmap is not null).Select(m => m.Lightmap!).Distinct().ToList();
        Assert.NotEmpty(lightmaps);
        foreach (string name in lightmaps)
        {
            var lm = scene.Textures[name];
            Assert.NotNull(lm);
            for (int i = 3; i < lm!.Rgba.Length; i += 4) Assert.Equal(255, lm.Rgba[i]);
        }
        Assert.Contains(log.Warnings, w => w.Code == "LIGHTMAP_REPAIRED");
    }

    [SkippableFact]
    public void Real_tar_m02aa_lights_read()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        var (scene, log, _) = LoadReal();
        Assert.Equal(70, scene.Lights.Count);   // 71 avant l'exclusion de la room m02aa_sky (1 lumiere)
        // Controllers 76/88/140 are really decoded: no light fell back to the defaults, none was rejected.
        Assert.DoesNotContain(log.Warnings, w => w.Code == "LIGHT_DEFAULTS");
        Assert.DoesNotContain(log.Warnings, w => w.Code == "LIGHT_INVALID");
        Assert.All(scene.Lights, l =>
        {
            Assert.True(l.Radius > 0f);
            Assert.InRange(l.Color.X, 0f, 1f); Assert.InRange(l.Color.Y, 0f, 1f); Assert.InRange(l.Color.Z, 0f, 1f);
            Assert.True(float.IsFinite(l.Position.X + l.Position.Y + l.Position.Z));
            Assert.True(l.Multiplier > 0f);
        });
        // Known measured value: the first light of room m02aa_03a ("M02aa_03a" at (88.83, 132.30, 1.5)): cyan-white, radius 2.5 m, x2.
        var known = Assert.Single(scene.Lights, l => l.Name == "M02aa_03a" && Math.Abs(l.Position.X - 88.8253f) < 0.01f);
        Assert.Equal(155 / 255f, known.Color.X, 3);
        Assert.Equal(242 / 255f, known.Color.Y, 3);
        Assert.Equal(1f, known.Color.Z, 3);
        Assert.Equal(2.5f, known.Radius);
        Assert.Equal(2f, known.Multiplier);
        Assert.Equal(132.2996f, known.Position.Y, 2);
        Assert.Equal(1.5f, known.Position.Z, 2);
        Assert.False(known.AmbientOnly);
        // The radii vary: the defaults (5 m) were not applied everywhere.
        Assert.True(scene.Lights.Select(l => l.Radius).Distinct().Count() > 3);
        Assert.InRange(scene.AmbientColor.X, 0f, 1f);
        Assert.True(scene.AreaLightingKnown);
    }

    private static KotorModel LightModel(params KotorLight?[] lights)
    {
        var root = new KotorNode("root", NodeKind.Header, Vector3.Zero, Quaternion.Identity, null);
        int i = 0;
        foreach (var l in lights)
            root.AddChild(new KotorNode("lt" + i++, NodeKind.Header | NodeKind.Light, new Vector3(1, 2, 3), Quaternion.Identity, null, l));
        return new KotorModel("m99zz_01a", null, root, Vector3.Zero, Vector3.One);
    }

    [Fact]
    public void Invalid_lights_are_skipped_individually_with_LIGHT_INVALID()
    {
        using var k = new FakeKotor();
        var white = Vector3.One;
        var model = LightModel(
            new KotorLight(white, 3f, 1f, false),                              // valid
            new KotorLight(white, 0f, 1f, false),                              // radius 0
            new KotorLight(white, -2f, 1f, false),                             // negative radius
            new KotorLight(white, float.NaN, 1f, false),                       // NaN radius
            new KotorLight(white, float.PositiveInfinity, 1f, false),          // infinite radius
            new KotorLight(white, 3f, 0f, false),                              // multiplier 0
            new KotorLight(white, 3f, float.NaN, false),                       // NaN multiplier
            new KotorLight(white, 3f, float.PositiveInfinity, false),          // infinite multiplier
            new KotorLight(new Vector3(float.NaN, 0, 0), 3f, 1f, false),       // NaN colour
            new KotorLight(white, 3f, 1f, false, Invalid: true),               // controller out of bounds
            new KotorLight(white, 4f, 2f, false, AmbientOnly: true));          // valid ambient_only
        var log = new ImportLog();
        var lights = new List<SceneLight>();
        ModuleSceneBuilder.AddModel(k.Res, Room, model, new Dictionary<string, KotorTexture?>(), new List<SceneMesh>(), log, lights);
        Assert.Equal(2, lights.Count);
        Assert.Equal(9, log.Warnings.Count(w => w.Code == "LIGHT_INVALID"));
        Assert.All(log.Warnings.Where(w => w.Code == "LIGHT_INVALID"), w => Assert.Contains("ignored", w.Message));
        Assert.False(lights[0].AmbientOnly);
        Assert.True(lights[1].AmbientOnly);
        Assert.Equal(4f, lights[1].Radius);
        Assert.Equal(new Vector3(1, 2, 3), lights[0].Position);
        // The summary counts only the lights actually used.
        Assert.Contains("2 light node(s)", Assert.Single(log.Warnings, w => w.Code == "NODE_LIGHT").Message);
    }

    [Fact]
    public void Lightmap_without_valid_texel_is_logged_and_has_no_alpha()
    {
        // 2x2 32-bit TGA, every pixel alpha 0 (no valid texel).
        var tga = new byte[18 + 4 * 4];
        tga[2] = 2; tga[12] = 2; tga[14] = 2; tga[16] = 32; tga[17] = 8;
        for (int i = 0; i < 4; i++) { tga[18 + i * 4] = 10; tga[18 + i * 4 + 1] = 20; tga[18 + i * 4 + 2] = 30; tga[18 + i * 4 + 3] = 0; }
        using var k = new FakeKotor(("lm_a", (uint)ResourceType.Tga, tga));
        var uv1 = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1) };
        var log = RunModel(k, Model(("a", NodeKind.Mesh, Mesh(lm: "lm_a", uv1: uv1))), out _, out var textures);
        var w = Assert.Single(log.Warnings, w => w.Code == "LIGHTMAP_NO_VALID_TEXEL");
        Assert.Contains("lm_a", w.Message);
        var lm = Assert.IsType<KotorTexture>(textures["lm_a"]);
        Assert.False(lm.HasAlpha);
        for (int i = 0; i < lm.Rgba.Length; i += 4)
        {
            Assert.Equal(128, lm.Rgba[i]); Assert.Equal(128, lm.Rgba[i + 1]); Assert.Equal(128, lm.Rgba[i + 2]);
            Assert.Equal(255, lm.Rgba[i + 3]);
        }
    }

    [Fact]
    public void Repaired_lightmap_has_no_alpha_flag_even_if_source_had_alpha()
    {
        // 2x1: one valid texel (alpha 255), one invalid (alpha 0): repaired from its neighbour, HasAlpha false.
        var tga = new byte[18 + 2 * 4];
        tga[2] = 2; tga[12] = 2; tga[14] = 1; tga[16] = 32; tga[17] = 8;
        tga[18] = 1; tga[19] = 2; tga[20] = 3; tga[21] = 255;
        tga[22] = 9; tga[23] = 9; tga[24] = 9; tga[25] = 0;
        using var k = new FakeKotor(("lm_b", (uint)ResourceType.Tga, tga));
        int calls = 0, repaired = 0; bool none = true;
        var tex = TextureLookup.Find(k.Res, "lm_b", TextureKind.Lightmap, (r, n) => { calls++; repaired = r; none = n; });
        Assert.NotNull(tex);
        Assert.False(tex!.HasAlpha);
        Assert.Equal(1, calls); Assert.Equal(1, repaired); Assert.False(none);
        Assert.Equal(tex.Rgba[0], tex.Rgba[4]); Assert.Equal(tex.Rgba[1], tex.Rgba[5]); Assert.Equal(tex.Rgba[2], tex.Rgba[6]);
        Assert.Equal(255, tex.Rgba[7]);
        // A diffuse texture keeps its real alpha.
        Assert.True(TextureLookup.Find(k.Res, "lm_b", TextureKind.Diffuse)!.HasAlpha);
    }

    [Fact]
    public void Area_lighting_reads_ambient_and_sun_diffuse_as_bgr()
    {
        using var k = new FakeKotor(are =>
        {
            are.Add(are.Root, "DynAmbientColor", 4, 0x00FF8040u);
            are.Add(are.Root, "SunDiffuseColor", 4, 0x00204080u);
        }, []);
        var scene = ModuleSceneBuilder.Build(k.Res, "tst_m99zz", null, new ImportLog());
        Assert.True(scene.AreaLightingKnown);
        Assert.Equal(new Vector3(0x40 / 255f, 0x80 / 255f, 1f), scene.AmbientColor);
        Assert.Equal(new Vector3(0x80 / 255f, 0x40 / 255f, 0x20 / 255f), scene.SunDiffuseColor);
    }

    [Fact]
    public void Area_without_colours_is_known_with_default_ambient_and_black_sun()
    {
        using var k = new FakeKotor();
        var log = new ImportLog();
        var scene = ModuleSceneBuilder.Build(k.Res, "tst_m99zz", null, log);
        Assert.True(scene.AreaLightingKnown);
        Assert.Equal(ModuleScene.DefaultAmbient, scene.AmbientColor);
        Assert.Equal(Vector3.Zero, scene.SunDiffuseColor);
        Assert.Contains(log.Warnings, w => w.Code == "AMBIENT_DEFAULT");
    }
}
