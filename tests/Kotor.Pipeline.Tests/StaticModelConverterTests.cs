using System.Globalization;
using System.Numerics;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Pipeline.Entities;

namespace Kotor.Pipeline.Tests;

public sealed class StaticModelConverterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "k2j_static_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private ExportOptions Opt()
    {
        var o = new ExportOptions("", Path.Combine(_root, "base"), Path.Combine(_root, "work"), Game: KotorGame.Swtor);
        Directory.CreateDirectory(o.JaBaseDir);
        return o;
    }

    /// <summary>Grille 16 x 16 cases (512 triangles) de 0,5 m, montee sur 3 m : un objet de 8 m (320 u JA), centre local (4, 4, 3).</summary>
    private static SceneModel Model()
    {
        var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        for (int y = 0; y <= 16; y++)
            for (int x = 0; x <= 16; x++) { pos.Add(new(x * 0.5f, y * 0.5f, 3f)); nrm.Add(Vector3.UnitZ); uv.Add(new(x / 16f, y / 16f)); }
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                int a = y * 17 + x, b = a + 1, c = a + 17, d = c + 1;
                tri.AddRange([a, b, d, a, d, c]);
            }
        var mesh = new SceneMesh("", "m", "t", null, TxiInfo.Empty, 0, [.. pos], [.. nrm], [.. uv], null, [.. tri]) { Alpha = SurfaceAlpha.Opaque };
        return new SceneModel("art/static/test/plate.gr2", [mesh]);
    }

    private static ModuleScene Scene(int instances)
    {
        var floor = new SceneWalkmesh("r", [new(-50, -50, 0), new(50, -50, 0), new(50, 50, 0), new(-50, 50, 0)], [0, 1, 2, 0, 2, 3], [true, true]);
        var tex = new Dictionary<string, KotorTexture?> { ["t"] = new KotorTexture(2, 2, new byte[16], false, TxiInfo.Empty) };
        var inst = Enumerable.Range(0, instances)
            .Select(i => new SceneModelInstance(0, "r", Matrix4x4.CreateRotationZ(i * 0.4f) * Matrix4x4.CreateTranslation(i * 10f, 0, 0)))
            .ToList();
        return new ModuleScene(new ModuleInfo("tst", "tst", "Zone", new ModuleEntry(Vector3.Zero, Vector2.UnitX)), [], [floor], tex)
        {
            Models = [Model()],
            ModelInstances = inst,
        };
    }

    [Fact]
    public void Instances_within_budget_become_misc_model_static_and_the_rest_goes_back_to_the_bsp()
    {
        var log = new ImportLog();
        var (converted, compiled) = StaticModelConverter.Convert(Scene(5), "demo", Opt(), log, budget: 3);

        Assert.Equal(3, converted.Entities.Count);
        Assert.Empty(compiled.ModelInstances);
        Assert.Equal(2, compiled.Meshes.Count); // deux instances remises dans le BSP
        Md3Asset md3 = Assert.Single(converted.Models);
        Assert.Equal("models/swtor/demo/m0.md3", md3.Path);
        Assert.Equal("IDP3", System.Text.Encoding.ASCII.GetString(md3.Data, 0, 4));
        Assert.Contains(converted.ModelShaders, s => s.Contains("rgbGen lightingDiffuse") && !s.Contains("rgbGen vertex"));
        Assert.Contains(log.Warnings, w => w.Code == "STATIC_MODELS");

        float scale = Opt().Scale;
        foreach (var (e, i) in converted.Entities.Select((e, i) => (e, i)))
        {
            Assert.Equal("misc_model_static", e["classname"]);
            Assert.Equal(md3.Path, e["model"]);
            // Echelle dans le MD3 : pas de modelscale (il fausserait l eclairage) ; origine = centre du modele place.
            Assert.False(e.ContainsKey("modelscale_vec"));
            float[] o = [.. e["origin"].Split(' ').Select(s => float.Parse(s, CultureInfo.InvariantCulture))];
            Vector3 c = Vector3.Transform(new Vector3(4, 4, 3), Matrix4x4.CreateRotationZ(i * 0.4f) * Matrix4x4.CreateTranslation(i * 10f, 0, 0)) * scale;
            Assert.True(Vector3.Distance(c, new Vector3(o[0], o[1], o[2])) < 0.01f, $"{c} vs {e["origin"]}");
            float[] a = [.. e["angles"].Split(' ').Select(s => float.Parse(s, CultureInfo.InvariantCulture))];
            Assert.Equal(i * 0.4f * 180f / MathF.PI, a[1], 2);
        }
    }

    [Fact]
    public void Scene_without_instances_is_unchanged()
    {
        ModuleScene scene = Scene(0);
        var (converted, compiled) = StaticModelConverter.Convert(scene, "demo", Opt(), new ImportLog());
        Assert.Same(scene, compiled);
        Assert.Empty(converted.Entities);
    }

    [Fact]
    public void Full_pipeline_writes_the_md3_and_its_entities()
    {
        var opt = Opt();
        var log = new ImportLog();
        ImportStepResult imp = PipelineSteps.Import(Scene(4), "demo", opt, log);
        ExportStepResult exp = PipelineSteps.Export(imp, opt, log);

        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(exp.Bsp.Entities, "misc_model_static").Count);
        Assert.True(File.Exists(Path.Combine(exp.StagingDir, "models", "swtor", "demo", "m0.md3")));
        Assert.Contains("models/swtor/t\n", File.ReadAllText(Path.Combine(exp.StagingDir, "shaders", "demo.shader")));
    }
}
