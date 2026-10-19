using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Core.Mdl;
using Kotor.Core.Textures;
using Kotor.Core.Walkmesh;
using Kotor.Pipeline;
using Kotor.Pipeline.Entities;
using Xunit;

namespace Kotor.Pipeline.Tests;

public sealed class PlaceableConverterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "k2j_plc_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>Modeles synthetiques : un quad de <c>size</c> metres, texture <c>tex</c>.</summary>
    private sealed class FakeAssets(Dictionary<string, float> sizes, bool withPwk = true) : IObjectAssets
    {
        public string? ModelOf(ModuleEntity e, out string? reason)
        {
            reason = null;
            if (e.Template == "plc_big") return "big";
            if (e.Template == "plc_small") return "small";
            if (e.Template == "plc_huge") return "huge";
            if (e.Template == "plc_invis") return "invis";
            reason = $"UTP '{e.Template}' not found";
            return null;
        }

        public KotorModel? LoadModel(string model, out string? reason)
        {
            reason = null;
            if (model == "invis")
            {
                var hidden = new KotorMesh("tex_invis", null, false, false, 0,
                    [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
                    [new(0, 0), new(1, 0), new(1, 1)], null, [0, 1, 2]);
                return new KotorModel(model, null, new KotorNode(model, NodeKind.Header | NodeKind.Mesh, Vector3.Zero, Quaternion.Identity, hidden), Vector3.Zero, Vector3.One);
            }
            if (model == "huge")
            {
                // 65 surfaces de 399 triangles disjoints (1197 sommets) : une seule texture, donc au-dela de 64 surfaces MD3.
                const int tris = 65 * 399;
                var pos = new Vector3[tris * 3]; var nor = new Vector3[tris * 3]; var uv = new Vector2[tris * 3]; var idx = new int[tris * 3];
                for (int t = 0; t < tris; t++)
                {
                    float x = t % 100 * 0.1f, y = t / 100 * 0.1f;
                    pos[t * 3] = new(x, y, 0); pos[t * 3 + 1] = new(x + 0.05f, y, 0); pos[t * 3 + 2] = new(x, y + 0.05f, 0);
                    for (int k = 0; k < 3; k++) { nor[t * 3 + k] = Vector3.UnitZ; idx[t * 3 + k] = t * 3 + k; }
                }
                var big = new KotorMesh("tex_huge", null, true, false, 0, pos, nor, uv, null, idx);
                return new KotorModel(model, null, new KotorNode(model, NodeKind.Header | NodeKind.Mesh, Vector3.Zero, Quaternion.Identity, big), Vector3.Zero, new Vector3(10));
            }
            float s = sizes[model];
            var mesh = new KotorMesh("tex_" + model, null, true, false, 0,
                [new(0, 0, 0), new(s, 0, 0), new(s, s, 0), new(0, s, 0)],
                [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
                [new(0, 0), new(1, 0), new(1, 1), new(0, 1)], null, [0, 1, 2, 0, 2, 3]);
            var root = new KotorNode(model, NodeKind.Header | NodeKind.Mesh, Vector3.Zero, Quaternion.Identity, mesh);
            return new KotorModel(model, null, root, Vector3.Zero, new Vector3(s));
        }

        public Walkmesh? LoadPwk(string model, out string? reason)
        {
            reason = withPwk ? null : "PWK not found";
            return withPwk ? new Walkmesh([new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)], [0, 1, 2], [0]) : null;
        }

        public KotorTexture? LoadTexture(string name, ImportLog log) => new(2, 2, new byte[16], false, TxiInfo.Empty);
    }

    private static ModuleEntity Plc(string template, float x = 1, float y = 2, float yaw = 30) =>
        new(EntityKind.Placeable, template, template, new Vector3(x, y, 0.5f), yaw, null);

    [Fact]
    public void Large_model_gets_modelscale()
    {
        var log = new ImportLog();
        ConvertedEntities r = PlaceableConverter.Convert(new FakeAssets(new() { ["big"] = 30f }), KotorGame.Kotor1, [Plc("plc_big")], 40f, log);

        var e = Assert.Single(r.Entities);
        Assert.Equal("misc_model_static", e["classname"]);
        Assert.Equal("3", e["modelscale"]);            // 30 m x 40 = 1200 -> ceil(1200 / 511) = 3
        Assert.Equal("models/kotor/big.md3", e["model"]);
        Assert.Equal("40 80 20", e["origin"]);         // 1, 2, 0.5 m x 40
        Assert.Equal("0 30 0", e["angles"]);

        // Les sommets ecrits restent dans +-511 : 1200 / 3 = 400 -> 25600 en 1/64.
        byte[] md3 = Assert.Single(r.Models).Data;
        int s = BitConverter.ToInt32(md3, 100);
        int xyz = s + BitConverter.ToInt32(md3, s + 100);
        int nVerts = BitConverter.ToInt32(md3, s + 80);
        for (int i = 0; i < nVerts * 3; i++)
            Assert.InRange(Math.Abs((int)BitConverter.ToInt16(md3, xyz + (i / 3) * 8 + (i % 3) * 2)), 0, 511 * 64);
        Assert.Equal(25600, BitConverter.ToInt16(md3, xyz + 8));
    }

    [Fact]
    public void Small_model_has_no_modelscale_and_yaw_is_normalized()
    {
        ConvertedEntities r = PlaceableConverter.Convert(new FakeAssets(new() { ["small"] = 1f }), KotorGame.Kotor1,
            [Plc("plc_small", yaw: -90)], 40f, new ImportLog());
        var e = Assert.Single(r.Entities);
        Assert.False(e.ContainsKey("modelscale"));
        Assert.Equal("0 270 0", e["angles"]);
    }

    [Fact]
    public void Shared_model_written_once()
    {
        var log = new ImportLog();
        ConvertedEntities r = PlaceableConverter.Convert(new FakeAssets(new() { ["small"] = 1f }), KotorGame.Kotor1,
            [Plc("plc_small", 1), Plc("plc_small", 2), Plc("plc_small", 3)], 40f, log);
        Assert.Single(r.Models);
        Assert.Equal(3, r.Entities.Count);
        Assert.Equal(3, r.Entities.Select(e => e["origin"]).Distinct().Count());
        Assert.Equal(3, r.ExtraSolid.Count);           // une PWK d'un triangle par instance
        Assert.Contains(log.Warnings, w => w.Code == "PLACEABLES" && w.Message.Contains("3 placeable(s) converted") && w.Message.Contains("1 unique MD3"));
        Assert.Single(r.ModelShaders);
        Assert.Contains("rgbGen lightingDiffuse", r.ModelShaders[0]);
        Assert.StartsWith("models/kotor/tex_small\n", r.ModelShaders[0]);
        Assert.Contains("map textures/kotor/tex_small.tga", r.ModelShaders[0]);
    }

    [Theory]
    [InlineData(40f, "models/kotor/small.md3")]
    [InlineData(36f, "models/kotor/s36/small.md3")]
    [InlineData(37.5f, "models/kotor/s37.5/small.md3")]
    public void Md3_path_depends_on_a_non_default_scale(float scale, string expected)
    {
        ConvertedEntities r = PlaceableConverter.Convert(new FakeAssets(new() { ["small"] = 1f }), KotorGame.Kotor1, [Plc("plc_small")], scale, new ImportLog());
        Assert.Equal(expected, Assert.Single(r.Entities)["model"]);
        Assert.Equal(expected, Assert.Single(r.Models).Path);
        Assert.StartsWith("models/kotor/tex_small\n", Assert.Single(r.ModelShaders)); // shaders are shared: no scale folder
    }

    [Fact]
    public void Md3_path_scale_folder_applies_to_kotor2_and_split_models()
    {
        ConvertedEntities r = PlaceableConverter.Convert(new FakeAssets(new() { ["small"] = 1f }), KotorGame.Kotor2, [Plc("plc_small")], 36f, new ImportLog());
        Assert.Equal("models/kotor2/s36/small.md3", Assert.Single(r.Entities)["model"]);
        ConvertedEntities h = PlaceableConverter.Convert(new FakeAssets(new()), KotorGame.Kotor1, [Plc("plc_huge")], 36f, new ImportLog());
        Assert.Equal(["models/kotor/s36/huge_p0.md3", "models/kotor/s36/huge_p1.md3"], h.Models.Select(m => m.Path).ToArray());
    }

    [Fact]
    public void Kotor2_uses_its_own_folders()
    {
        ConvertedEntities r = PlaceableConverter.Convert(new FakeAssets(new() { ["small"] = 1f }), KotorGame.Kotor2, [Plc("plc_small")], 40f, new ImportLog());
        Assert.Equal("models/kotor2/small.md3", Assert.Single(r.Entities)["model"]);
        Assert.StartsWith("models/kotor2/tex_small\n", Assert.Single(r.ModelShaders));
        Assert.Contains("map textures/kotor2/tex_small.tga", r.ModelShaders[0]);
    }

    [Fact]
    public void Collision_is_placed_rotated_and_scaled()
    {
        // Triangle de PWK (0,0,0) (1,0,0) (1,1,0), yaw 90, position (10, 20, 0), echelle 2 : (x, y) -> (-y, x) + pos.
        ConvertedEntities r = PlaceableConverter.Convert(new FakeAssets(new() { ["small"] = 1f }), KotorGame.Kotor1,
            [new ModuleEntity(EntityKind.Placeable, "p", "plc_small", new Vector3(10, 20, 0), 90f, null)], 2f, new ImportLog());
        var (a, b, c) = Assert.Single(r.ExtraSolid);
        Assert.True(Vector3.Distance(a, new Vector3(20, 40, 0)) < 1e-3f);
        Assert.True(Vector3.Distance(b, new Vector3(20, 42, 0)) < 1e-3f);
        Assert.True(Vector3.Distance(c, new Vector3(18, 42, 0)) < 1e-3f);
    }

    [Fact]
    public void No_pwk_is_logged()
    {
        var log = new ImportLog();
        ConvertedEntities r = PlaceableConverter.Convert(new FakeAssets(new() { ["small"] = 1f }, withPwk: false), KotorGame.Kotor1,
            [Plc("plc_small")], 40f, log);
        Assert.Single(r.Entities);
        Assert.Empty(r.ExtraSolid);
        Assert.Contains(log.Warnings, w => w.Code == "PLACEABLE_NO_PWK");
    }

    [Fact]
    public void Model_with_more_surfaces_than_the_engine_limit_is_split_into_files()
    {
        var log = new ImportLog();
        ConvertedEntities r = PlaceableConverter.Convert(new FakeAssets(new()), KotorGame.Kotor1,
            [Plc("plc_huge", 1, 2, 30), Plc("plc_huge", 5, 6, 90)], 1f, log);

        // 65 surfaces -> deux fichiers (64 + 1), nommes _p0 et _p1.
        Assert.Equal(["models/kotor/s1/huge_p0.md3", "models/kotor/s1/huge_p1.md3"], r.Models.Select(m => m.Path).ToArray());
        Assert.Equal(64, BitConverter.ToInt32(r.Models[0].Data, 84));
        Assert.Equal(1, BitConverter.ToInt32(r.Models[1].Data, 84));
        Assert.All(r.Models, m => Assert.Equal("huge_p" + m.Path[^5], Encoding.ASCII.GetString(m.Data, 8, 64).TrimEnd('\0')));

        // Une entite par fichier et par instance, avec la meme origine et les memes angles ; la collision est ajoutee une fois par instance.
        Assert.Equal(4, r.Entities.Count);
        foreach (string origin in new[] { "1 2 0.5", "5 6 0.5" })
        {
            var group = r.Entities.Where(e => e["origin"] == origin).ToList();
            Assert.Equal(["models/kotor/s1/huge_p0.md3", "models/kotor/s1/huge_p1.md3"], group.Select(e => e["model"]).ToArray());
            Assert.Single(group.Select(e => e["angles"]).Distinct());
        }
        Assert.Equal(2, r.ExtraSolid.Count);
        Assert.Contains(log.Warnings, w => w.Code == "PLACEABLES" && w.Message.Contains("2 placeable(s) converted") && w.Message.Contains("2 unique MD3"));

        // Chaque surface respecte les limites du moteur.
        foreach (Md3Asset m in r.Models)
        {
            int surf = BitConverter.ToInt32(m.Data, 100), n = BitConverter.ToInt32(m.Data, 84);
            for (int i = 0; i < n; i++)
            {
                Assert.True(BitConverter.ToInt32(m.Data, surf + 80) < 1200);
                Assert.True(BitConverter.ToInt32(m.Data, surf + 84) * 3 < 7200);
                surf += BitConverter.ToInt32(m.Data, surf + 104);
            }
        }
    }

    [Fact]
    public void Model_without_rendered_mesh_is_skipped_with_its_collision()
    {
        var log = new ImportLog();
        ConvertedEntities r = PlaceableConverter.Convert(new FakeAssets(new()), KotorGame.Kotor1, [Plc("plc_invis")], 40f, log);
        Assert.Empty(r.Entities);
        Assert.Empty(r.Models);
        Assert.Empty(r.ExtraSolid);
        Assert.Contains(log.Warnings, w => w.Code == "PLACEABLE_SKIPPED" && w.Message.Contains("no rendered mesh (invisible blocker, collision not added)"));
    }

    [Fact]
    public void Missing_template_is_logged()
    {
        var log = new ImportLog();
        ConvertedEntities r = PlaceableConverter.Convert(new FakeAssets(new()), KotorGame.Kotor1, [Plc("plc_nowhere")], 40f, log);
        Assert.Empty(r.Entities);
        Assert.Contains(log.Warnings, w => w.Code == "PLACEABLE_SKIPPED" && w.Message.Contains("plc_nowhere") && w.Message.Contains("not found"));
        Assert.Contains(log.Warnings, w => w.Code == "PLACEABLES" && w.Message.Contains("1 skipped"));
    }

    [Fact]
    public void Entity_with_resolved_model_but_unreadable_model_is_skipped()
    {
        var log = new ImportLog();
        var e = Plc("plc_small") with { Model = "ghost" };
        ConvertedEntities r = PlaceableConverter.Convert(new MissingModelAssets(), KotorGame.Kotor1, [e], 40f, log);
        Assert.Empty(r.Entities);
        Assert.Contains(log.Warnings, w => w.Code == "PLACEABLE_SKIPPED" && w.Message.Contains("ghost"));
    }

    private sealed class MissingModelAssets : IObjectAssets
    {
        public string? ModelOf(ModuleEntity e, out string? reason) { reason = "x"; return null; }
        public KotorModel? LoadModel(string model, out string? reason) { reason = $"MDL '{model}' not found"; return null; }
        public Walkmesh? LoadPwk(string model, out string? reason) { reason = null; return null; }
        public KotorTexture? LoadTexture(string name, ImportLog log) => null;
    }

    // ---- Tests sur le jeu installe ----

    private static ExportOptions RealOptions(string root) => new(TestEnv.KotorDir!, Path.Combine(root, "base"), Path.Combine(root, "work"));

    private static string RepoOut()
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !File.Exists(Path.Combine(d, "Kotor2Ja.sln"))) d = Path.GetDirectoryName(d);
        string dir = Path.Combine(d ?? Path.GetTempPath(), "out");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [SkippableFact]
    public void Real_tar_m02aa_placeables()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR n'est pas installe.");
        var opt = RealOptions(_root);
        var log = new ImportLog();
        var scene = PipelineSteps.Preview("tar_m02aa", opt, log);
        using var res = ResourceResolver.ForModule(opt.KotorDir, "tar_m02aa", opt.Game);
        var objects = ModuleEntitiesReader.ReadObjects(res, scene.Info, log);
        ConvertedEntities r = PlaceableConverter.Convert(res, opt.Game, objects, opt.Scale, log);

        var placed = r.Entities.Where(e => e["classname"] == "misc_model_static").ToList();
        Assert.True(placed.Count >= 10, $"{placed.Count} misc_model_static");
        var models = r.Models.Select(m => m.Path).ToHashSet();
        Assert.All(placed, e => Assert.Contains(e["model"], models));

        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (var m in scene.Meshes)
            foreach (Vector3 p in m.Positions) { min = Vector3.Min(min, p * opt.Scale); max = Vector3.Max(max, p * opt.Scale); }
        min -= new Vector3(200); max += new Vector3(200);
        var sb = new StringBuilder();
        sb.AppendLine($"# tar_m02aa : {placed.Count} placeable(s) converted, {r.Models.Count} unique MD3, {r.ExtraSolid.Count} collision triangle(s)");
        sb.AppendLine($"# scene box + 200 : ({min.X:0}, {min.Y:0}, {min.Z:0}) .. ({max.X:0}, {max.Y:0}, {max.Z:0})");
        foreach (var e in placed)
        {
            string[] o = e["origin"].Split(' ');
            var p = new Vector3(float.Parse(o[0], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(o[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(o[2], System.Globalization.CultureInfo.InvariantCulture));
            Assert.True(p.X >= min.X && p.Y >= min.Y && p.Z >= min.Z && p.X <= max.X && p.Y <= max.Y && p.Z <= max.Z, $"{e["model"]} at {e["origin"]} is outside the scene");
            sb.AppendLine($"{e["model"]}\t{e["origin"]}\t{e["angles"]}" + (e.TryGetValue("modelscale", out string? ms) ? $"\tmodelscale {ms}" : ""));
        }
        foreach (var w in log.Warnings.Where(w => w.Code.StartsWith("PLACEABLE", StringComparison.Ordinal))) sb.AppendLine($"# {w.Code}: {w.Message}");
        File.WriteAllText(Path.Combine(RepoOut(), "placeables-tar_m02aa.txt"), sb.ToString());
    }

    [SkippableFact]
    public void Real_tar_m02aa_pk3_contains_md3()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR n'est pas installe.");
        var opt = RealOptions(_root);
        Directory.CreateDirectory(opt.JaBaseDir);
        var log = new ImportLog();
        ExportResult result = ModuleExporter.Export("tar_m02aa", null, opt, log);
        Assert.True(result.Ok, result.Error);

        using var z = ZipFile.OpenRead(result.Pk3Path!);
        var names = z.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
        var md3 = names.Where(n => n.StartsWith("models/kotor/", StringComparison.Ordinal) && n.EndsWith(".md3", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(md3);

        string shader = new StreamReader(z.GetEntry("shaders/tar_m02aa.shader")!.Open()).ReadToEnd();
        var maps = Regex.Matches(shader, @"map\s+(textures/\S+\.tga)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Contains("models/kotor/", shader);
        Assert.All(maps, m => Assert.Contains(m, names));
        // Chaque texture des MD3 a son shader models/kotor/<texture>.
        foreach (string path in md3)
        {
            using var s = z.GetEntry(path)!.Open();
            using var ms = new MemoryStream(); s.CopyTo(ms);
            byte[] d = ms.ToArray();
            Assert.Equal("IDP3", Encoding.ASCII.GetString(d, 0, 4));
            int surf = BitConverter.ToInt32(d, 100), n = BitConverter.ToInt32(d, 84);
            for (int i = 0; i < n; i++)
            {
                int sh = surf + BitConverter.ToInt32(d, surf + 92);
                string name = Encoding.ASCII.GetString(d, sh, 64).TrimEnd('\0');
                Assert.Contains(name + "\n", shader);
                surf += BitConverter.ToInt32(d, surf + 104);
            }
        }

        BspFile bsp = BspReader.Read(ReadAll(z.GetEntry("maps/tar_m02aa.bsp")!));
        Assert.Contains("misc_model_static", bsp.Entities);
        // Les MD3 des objets sont des "model" (misc_model_static), ceux des portes des "model2" (func_door).
        foreach (string m in md3) Assert.True(bsp.Entities.Contains($"\"model\" \"{m}\"") || bsp.Entities.Contains($"\"model2\" \"{m}\""), m);
        Assert.Contains(log.Warnings, w => w.Code == "PLACEABLES");
    }

    [SkippableFact]
    public void Real_kotor2_001ebo_placeables_use_kotor2_folders()
    {
        Skip.If(TestEnv.Kotor2Dir is null, "KOTOR 2 n'est pas installe.");
        var opt = new ExportOptions(TestEnv.Kotor2Dir!, Path.Combine(_root, "base"), Path.Combine(_root, "work"), Game: KotorGame.Kotor2);
        var log = new ImportLog();
        var scene = PipelineSteps.Preview("001ebo", opt, log);
        using var res = ResourceResolver.ForModule(opt.KotorDir, "001ebo", KotorGame.Kotor2);
        var objects = ModuleEntitiesReader.ReadObjects(res, scene.Info, log);
        ConvertedEntities r = PlaceableConverter.Convert(res, KotorGame.Kotor2, objects, opt.Scale, log);
        Assert.NotEmpty(r.Entities);
        Assert.All(r.Entities, e => Assert.StartsWith("models/kotor2/", e["model"]));
        Assert.All(r.ModelShaders, s => Assert.StartsWith("models/kotor2/", s));
    }

    private static byte[] ReadAll(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
