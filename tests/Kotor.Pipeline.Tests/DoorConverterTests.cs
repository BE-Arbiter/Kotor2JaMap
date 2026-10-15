using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text.RegularExpressions;
using Kotor.Compiler;
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

public sealed class DoorConverterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "k2j_door_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    /// <summary>Porte synthetique : un quad vertical de 2 m (X) sur 3 m (Z), sans epaisseur. <c>door_wide</c> : trop de surfaces pour un MD3.</summary>
    private sealed class FakeDoorAssets : IObjectAssets
    {
        public string? ModelOf(ModuleEntity e, out string? reason)
        {
            reason = null;
            if (e.Template == "door_a") return "dor_a";
            if (e.Template == "door_wide") return "dor_wide";
            reason = $"UTD '{e.Template}' not found";
            return null;
        }

        public KotorModel? LoadModel(string model, out string? reason)
        {
            reason = null;
            if (model == "dor_wide")
            {
                // 65 surfaces de 399 triangles disjoints : au-dela de 64 surfaces MD3, donc plusieurs fichiers.
                const int tris = 65 * 399;
                var pos = new Vector3[tris * 3]; var nor = new Vector3[tris * 3]; var uv = new Vector2[tris * 3]; var idx = new int[tris * 3];
                for (int t = 0; t < tris; t++)
                {
                    float x = t % 100 * 0.1f, z = t / 100 * 0.1f;
                    pos[t * 3] = new(x, 0, z); pos[t * 3 + 1] = new(x + 0.05f, 0, z); pos[t * 3 + 2] = new(x, 0, z + 0.05f);
                    for (int k = 0; k < 3; k++) { nor[t * 3 + k] = -Vector3.UnitY; idx[t * 3 + k] = t * 3 + k; }
                }
                var big = new KotorMesh("tex_wide", null, true, false, 0, pos, nor, uv, null, idx);
                return new KotorModel(model, null, new KotorNode(model, NodeKind.Header | NodeKind.Mesh, Vector3.Zero, Quaternion.Identity, big), Vector3.Zero, new Vector3(10));
            }
            var mesh = new KotorMesh("tex_door", null, true, false, 0,
                [new(-1, 0, 0), new(1, 0, 0), new(1, 0, 3), new(-1, 0, 3)],
                [-Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY, -Vector3.UnitY],
                [new(0, 0), new(1, 0), new(1, 1), new(0, 1)], null, [0, 1, 2, 0, 2, 3]);
            var root = new KotorNode(model, NodeKind.Header | NodeKind.Mesh, Vector3.Zero, Quaternion.Identity, mesh);
            return new KotorModel(model, null, root, Vector3.Zero, new Vector3(3));
        }

        public Walkmesh? LoadPwk(string model, out string? reason) { reason = "PWK not used by doors"; return null; }

        public KotorTexture? LoadTexture(string name, ImportLog log) => new(2, 2, new byte[16], false, TxiInfo.Empty);
    }

    private static ModuleEntity Door(string template = "door_a", float yaw = 90, string linked = "", bool locked = false) =>
        new(EntityKind.Door, template, template, new Vector3(2, 3, 0.25f), yaw, null, null, linked, locked);

    private static (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) Run(ImportLog log, params ModuleEntity[] doors) =>
        DoorConverter.Convert(new FakeDoorAssets(), KotorGame.Kotor1, doors, 40f, 1, log);

    [Fact]
    public void Door_entity_keys()
    {
        var (r, sub) = Run(new ImportLog(), Door(yaw: 90));

        var e = Assert.Single(r.Entities);
        Assert.Equal("func_door", e["classname"]);
        Assert.Equal("*1", e["model"]);
        Assert.Equal("models/kotor/dor_a.md3", e["model2"]);
        Assert.Equal("80 120 10", e["origin"]);
        Assert.Equal("0 90 0", e["modelAngles"]);
        Assert.Equal("-1", e["angle"]);
        Assert.Equal("8", e["lip"]);
        Assert.Equal("200", e["speed"]);
        Assert.Equal("3", e["wait"]);
        Assert.False(e.ContainsKey("spawnflags"));
        Assert.Single(r.Models);
        Assert.Empty(r.ExtraSolid); // les portes ne sont pas dans la collision du monde
        Assert.Single(sub);
        Assert.Equal(DoorConverter.ClipShader, sub[0].Shader);
    }

    [Fact]
    public void Transition_door_locked()
    {
        var (r, _) = Run(new ImportLog(), Door(linked: "tar_m02ab"));
        Assert.Equal("16", Assert.Single(r.Entities)["spawnflags"]);
    }

    [Fact]
    public void Locked_utd_door_is_locked_and_summary_is_logged()
    {
        var log = new ImportLog();
        var (r, _) = Run(log, Door(locked: true), Door(), Door("door_none"));
        Assert.Equal(2, r.Entities.Count);
        Assert.Equal("16", r.Entities[0]["spawnflags"]);
        Assert.Contains(log.Warnings, w => w.Code == "DOOR_SKIPPED" && w.Message.Contains("door_none"));
        Assert.Contains(log.Warnings, w => w.Code == "DOORS" && w.Message.Contains("2 door(s) converted (1 locked), 1 skipped"));
    }

    [Fact]
    public void Submodel_numbers_follow_first_index_and_brush_is_the_rotated_box()
    {
        var (r, sub) = DoorConverter.Convert(new FakeDoorAssets(), KotorGame.Kotor1, [Door(yaw: 0), Door(yaw: 90)], 40f, 5, new ImportLog());
        Assert.Equal(["*5", "*6"], r.Entities.Select(e => e["model"]));

        // Modele : x -1..1 m, y 0 (epaisseur minimale de 2 unites), z 0..3 m ; x40 -> x -40..40, z 0..120.
        CheckBounds(sub[0].Brushes[0], new Vector3(-40, -1, 0), new Vector3(40, 1, 120));
        // Tourne de 90 degres : la largeur passe sur Y.
        CheckBounds(sub[1].Brushes[0], new Vector3(-1, -40, 0), new Vector3(1, 40, 120));
    }

    [Fact]
    public void Two_doors_of_one_model_give_one_md3_and_two_distinct_submodels()
    {
        var (r, sub) = DoorConverter.Convert(new FakeDoorAssets(), KotorGame.Kotor1, [Door(yaw: 0), Door(yaw: 90)], 40f, 3, new ImportLog());
        Assert.Equal("models/kotor/dor_a.md3", Assert.Single(r.Models).Path);
        Assert.Equal(2, r.Entities.Count);
        Assert.All(r.Entities, e => Assert.Equal("func_door", e["classname"]));
        Assert.Equal(["*3", "*4"], r.Entities.Select(e => e["model"]));
        Assert.Equal(2, sub.Count);
        Assert.Single(r.Entities.Select(e => e["model2"]).Distinct());
    }

    [Fact]
    public void Door_md3_path_depends_on_a_non_default_scale()
    {
        var (r, _) = DoorConverter.Convert(new FakeDoorAssets(), KotorGame.Kotor1, [Door()], 37.5f, 1, new ImportLog());
        Assert.Equal("models/kotor/s37.5/dor_a.md3", Assert.Single(r.Entities)["model2"]);
        Assert.Equal("models/kotor/s37.5/dor_a.md3", Assert.Single(r.Models).Path);
    }

    private static void CheckBounds(IReadOnlyList<BspPlane> planes, Vector3 min, Vector3 max)
    {
        Assert.Equal(6, planes.Count);
        var b = BrushBounds.FromPlanes(planes);
        Assert.NotNull(b);
        Assert.Equal(min.X, b!.Value.Min.X, 2); Assert.Equal(min.Y, b.Value.Min.Y, 2); Assert.Equal(min.Z, b.Value.Min.Z, 2);
        Assert.Equal(max.X, b.Value.Max.X, 2); Assert.Equal(max.Y, b.Value.Max.Y, 2); Assert.Equal(max.Z, b.Value.Max.Z, 2);
    }

    [Fact]
    public void Door_model_split_in_several_files_is_skipped()
    {
        var log = new ImportLog();
        var (r, sub) = Run(log, Door("door_wide"));
        Assert.Empty(r.Entities);
        Assert.Empty(sub);
        Assert.Contains(log.Warnings, w => w.Code == "DOOR_SKIPPED" && w.Message.Contains("door model too large for one MD3"));
    }

    [Fact]
    public void Merge_keeps_one_copy_of_shared_assets()
    {
        var (a, _) = Run(new ImportLog(), Door());
        var (b, _) = Run(new ImportLog(), Door(yaw: 10));
        ConvertedEntities m = a.Merge(b);
        Assert.Equal(2, m.Entities.Count);
        Assert.Single(m.Models);
        Assert.Single(m.ModelShaders);
        Assert.Same(a, a.Merge(ConvertedEntities.Empty));
    }

    [Fact]
    public void Compiler_receives_door_submodels()
    {
        var (r, sub) = Run(new ImportLog(), Door(yaw: 0));
        var world = new Dictionary<string, string> { ["classname"] = "worldspawn" };
        var floor = new SurfaceInput("textures/kotor/floor", [new(0, 0, 0), new(100, 0, 0), new(100, 100, 0), new(0, 100, 0)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ], [new(0, 0), new(1, 0), new(1, 1), new(0, 1)], null, [0, 1, 2, 0, 2, 3]);
        var solid = new Kotor.Core.Geometry.ModelMesh
        {
            Name = "s",
            SubMeshes = [new Kotor.Core.Geometry.SubMesh { MaterialName = "m", Positions = floor.Positions, Triangles = floor.Indices }],
        };
        var input = new CompileInput("Door", [world, .. r.Entities], [floor], solid, new Kotor.Core.Geometry.ModelMesh { Name = "w" },
            "", VertexLighting.Default) { SubModels = sub };
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "door.bsp");
        var res = global::Kotor.Compiler.Compiler.Compile(input, path, Limits.Swgl, new ImportLog());
        Assert.True(res.Ok, res.Error);
        BspFile bsp = BspReader.Read(File.ReadAllBytes(path));
        Assert.Equal(2, bsp.Models.Count);
        Assert.Contains("\"model\" \"*1\"", bsp.Entities);
    }

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
    public void Real_tar_m02aa_doors()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR n'est pas installe.");
        var opt = RealOptions(_root);
        Directory.CreateDirectory(opt.JaBaseDir);
        var log = new ImportLog();
        ExportResult result = ModuleExporter.Export("tar_m02aa", null, opt, log);
        Assert.True(result.Ok, result.Error);

        using var z = ZipFile.OpenRead(result.Pk3Path!);
        var names = z.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
        using var ms = new MemoryStream();
        using (var s = z.GetEntry("maps/tar_m02aa.bsp")!.Open()) s.CopyTo(ms);
        BspFile bsp = BspReader.Read(ms.ToArray());

        var doors = Regex.Matches(bsp.Entities, "\\{[^}]*\"classname\" \"func_door\"[^}]*\\}").Select(m => m.Value).ToList();
        Assert.NotEmpty(doors);
        foreach (string d in doors)
        {
            var model = Regex.Match(d, "\"model\" \"\\*(\\d+)\"");
            Assert.True(model.Success, d);
            int n = int.Parse(model.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.InRange(n, 1, bsp.Models.Count - 1);
            Assert.Equal(1, bsp.Models[n].BrushCount);
            Assert.Equal(0, bsp.Models[n].SurfaceCount);
            string model2 = Regex.Match(d, "\"model2\" \"([^\"]+)\"").Groups[1].Value;
            Assert.Contains(model2, names);
        }
        Assert.Equal(doors.Count + 1, bsp.Models.Count);
        Assert.DoesNotContain(bsp.LeafBrushes, b => b >= bsp.Models[0].BrushCount);

        var sb = new System.Text.StringBuilder();
        foreach (string d in doors) sb.AppendLine(d.Replace("\n", " "));
        foreach (var w in log.Warnings.Where(w => w.Code.StartsWith("DOOR", StringComparison.Ordinal))) sb.AppendLine($"# {w.Code}: {w.Message}");
        File.WriteAllText(Path.Combine(RepoOut(), "doors-tar_m02aa.txt"), sb.ToString());
    }
}
