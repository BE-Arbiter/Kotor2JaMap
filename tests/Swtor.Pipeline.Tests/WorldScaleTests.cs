using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Scene;
using Swtor.Core;
using Swtor.Core.Areas;
using Swtor.Core.Tests;
using Xunit;

namespace Swtor.Pipeline.Tests;

/// <summary>T7 : l'echelle du monde SWTOR (le vaisseau de war_planworking etait a la moitie de la taille humaine).</summary>
public class WorldScaleTests
{
    private const string ShipAreaId = "4611686045100172646";

    private static AreaLayout Layout(params SyntheticArea.Obj[] objs) =>
        AreaLayout.Load(SyntheticArea.WriteFolder(["r1"], SyntheticArea.StandardAssets(), _ => objs));

    [Fact]
    public void World_scale_multiplies_positions_and_vertices_together()
    {
        // Defaut : constante derivee du personnage humain (250 u JA par unite GR2 = 6,25 m par unite).
        var layout = Layout(new SyntheticArea.Obj(1, SyntheticArea.GroundTile, Position: new Vector3(6.4f, 0, 0)));
        var r = SwtorSceneBuilder.Build(layout, new FakeSource(), "t", new SwtorBuildOptions(), new ImportLog());
        SceneMesh m = Assert.Single(r.Scene.Meshes);
        float k = SwtorWorldScale.MetresPerGr2Unit;
        Assert.Equal(6.25f, k, 3);
        Assert.Equal(6.4f * k, m.Positions.Min(p => p.X), 2);
        Assert.Equal(9.6f * k, m.Positions.Max(p => p.X), 2);   // (6,4 + 3,2) unites
        Assert.Equal(-3.2f * k, m.Positions.Min(p => p.Y), 2);
    }

    [Fact]
    public void Manual_multiplier_is_applied_on_top_of_the_derived_scale()
    {
        var layout = Layout(new SyntheticArea.Obj(1, SyntheticArea.GroundTile, Position: new Vector3(6.4f, 0, 0)));
        var r = SwtorSceneBuilder.Build(layout, new FakeSource(), "t", new SwtorBuildOptions { WorldScale = 0.5f }, new ImportLog());
        Assert.Equal(6.4f * SwtorWorldScale.MetresPerGr2Unit * 0.5f, Assert.Single(r.Scene.Meshes).Positions.Min(p => p.X), 2);
        Assert.Equal(SwtorWorldScale.MetresPerGr2Unit, SwtorWorldScale.Resolve(0f), 5);
        Assert.Equal(SwtorWorldScale.MetresPerGr2Unit, SwtorWorldScale.Resolve(1f), 5);
    }

    [Fact]
    public void Derived_scale_respects_the_ja_gameplay_constraints()
    {
        float k = SwtorWorldScale.MetresPerGr2Unit * SwtorWorldScale.JaUnitsPerMetre;
        Assert.Equal(250f, k, 3);
        Assert.True(0.07f * k <= SwtorWorldScale.JaStepSize);   // plus haute vraie marche SWTOR (0,07 unite) franchissable
        Assert.InRange(0.06f * k, 15f, 20f);                    // assise de chaise typique ~ genou du joueur
        Assert.InRange(0.36f * k, 1.2f * SwtorWorldScale.JaPlayerHeight, 1.6f * SwtorWorldScale.JaPlayerHeight); // plus petite porte de batiment
    }

    [Fact]
    public void World_scale_is_applied_to_light_position_and_radius()
    {
        var lp = new LightParams(SwtorLightType.Wide, 1f, 1f, new Vector3(2f), null, null, null);
        SceneLight a = SwtorSceneBuilder.MakeLight(1, new Vector3(1, 2, 3), Vector3.One, lp, new SwtorBuildOptions())!;
        SceneLight b = SwtorSceneBuilder.MakeLight(1, new Vector3(1, 2, 3), Vector3.One, lp, new SwtorBuildOptions { WorldScale = 2f })!;
        Assert.Equal(a.Radius * 2f, b.Radius, 3);
    }


    [SkippableFact]
    public void WarPlanworking_model_instances_rebuild_exactly_the_geometry_of_the_room_meshes()
    {
        string? root = TestEnv.SwtorAssetsDir;
        Skip.If(root is null || !Directory.Exists(Path.Combine(root, "world", "areas", ShipAreaId)), "SWTOR assets not available");
        var layout = AreaLayout.Load(Path.Combine(root!, "world", "areas", ShipAreaId));
        var source = new SwtorAssetsModelSource(new SwtorAssets(root));
        ModuleScene inRooms = SwtorSceneBuilder.Build(layout, source, "war_planworking", new SwtorBuildOptions { StaticModelMinTriangles = 0 }, new ImportLog()).Scene;
        ModuleScene split = SwtorSceneBuilder.Build(layout, source, "war_planworking", null, new ImportLog()).Scene;
        Assert.Empty(inRooms.ModelInstances);
        Assert.NotEmpty(split.ModelInstances);

        // Memes triangles avec ou sans instances : par piece et texture, meme nombre, meme somme des centres et des carres des centres
        // (a 1 mm pres en moyenne ; les arrondis flottants different, l'echelle etant appliquee avant la rotation dans le modele).
        static Dictionary<string, (int N, Vector3 Sum, Vector3 Sq)> Stats(IEnumerable<SceneMesh> meshes)
        {
            var d = new Dictionary<string, (int N, Vector3 Sum, Vector3 Sq)>(StringComparer.Ordinal);
            foreach (SceneMesh m in meshes)
                for (int t = 0; t + 2 < m.Triangles.Length; t += 3)
                {
                    Vector3 c = (m.Positions[m.Triangles[t]] + m.Positions[m.Triangles[t + 1]] + m.Positions[m.Triangles[t + 2]]) / 3f;
                    var (n, s, q) = d.GetValueOrDefault(m.Room + "|" + m.Texture);
                    d[m.Room + "|" + m.Texture] = (n + 1, s + c, q + c * c);
                }
            return d;
        }
        var expected = Stats(inRooms.Meshes);
        var actual = Stats(split.AllMeshes());
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (key, (n, sum, sq)) in expected)
        {
            var (n2, sum2, sq2) = actual[key];
            Assert.Equal(n, n2);
            Assert.True(Vector3.Distance(sum / n, sum2 / n) < 1e-3f, $"{key}: mean centre {sum / n} vs {sum2 / n}");
            Assert.True(Vector3.Distance(sq / n, sq2 / n) < 1e-3f * MathF.Max(1f, (sq / n).Length()), $"{key}: spread {sq / n} vs {sq2 / n}");
        }

        // Placement moteur (origin, angles, modelscale_vec) = position de l'instance, en unites JA.
        const float scale = 40f;
        foreach (SceneModelInstance inst in split.ModelInstances.Take(300))
        {
            Matrix4x4 m = inst.Transform;
            m.Translation *= scale;
            MiscModelPlacement p = MiscModelTransform.FromMatrix(m)!;
            Assert.NotNull(p);
            Assert.True(Vector3.Distance(p.Scale, Vector3.One) < 1e-3f, $"instance scale {p.Scale}: must be in the model, not in modelscale");
            foreach (Vector3 v in split.Models[inst.Model].Meshes[0].Positions.Take(20))
            {
                Vector3 expectedJa = Vector3.Transform(v, inst.Transform) * scale, actualJa = MiscModelTransform.Place(p, v * scale);
                Assert.True((expectedJa - actualJa).Length() < 0.5f, $"{split.Models[inst.Model].Name}: {expectedJa} vs {actualJa}");
            }
        }
    }

    [SkippableFact]
    public void WarPlanworking_has_human_scale_in_the_scene()
    {
        string? root = TestEnv.SwtorAssetsDir;
        Skip.If(root is null || !Directory.Exists(Path.Combine(root, "world", "areas", ShipAreaId)), "SWTOR assets not available");
        var layout = AreaLayout.Load(Path.Combine(root!, "world", "areas", ShipAreaId));
        var r = SwtorSceneBuilder.Build(layout, new SwtorAssetsModelSource(new SwtorAssets(root)), "war_planworking", null, new ImportLog());
        Vector3 spawn = r.Scene.Info.Entry.Position;

        // Plafond au-dessus du point d apparition : 1,4 unite GR2 x 7,8 = ~10,9 m (salle de vaisseau capital, pas un couloir humain).
        float ceiling = float.MaxValue;
        foreach (SceneMesh m in r.Scene.AllMeshes())
            for (int i = 0; i + 2 < m.Triangles.Length; i += 3)
            {
                Vector3 a = m.Positions[m.Triangles[i]], b = m.Positions[m.Triangles[i + 1]], c = m.Positions[m.Triangles[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.Z >= 0 || !Inside(spawn, a, b, c)) continue;
                float z = a.Z - (n.X * (spawn.X - a.X) + n.Y * (spawn.Y - a.Y)) / n.Z;
                if (z > spawn.Z + 0.3f) ceiling = MathF.Min(ceiling, z);
            }
        Assert.True(ceiling < float.MaxValue, "no ceiling above the spawn point");
        Assert.InRange(ceiling - spawn.Z, 4f, 16f);

        // Les dalles de 3,2 m du kit sont a l'echelle du monde : le vaisseau entier fait plus de 100 m de long.
        Assert.True(r.Scene.AllMeshes().Max(m => m.Positions.Max(p => p.X)) - r.Scene.AllMeshes().Min(m => m.Positions.Min(p => p.X)) > 100f);
    }

    [SkippableFact]
    public void WarPlanworking_heavy_models_become_misc_model_static_within_the_engine_limit()
    {
        string? root = TestEnv.SwtorAssetsDir;
        Skip.If(root is null || !Directory.Exists(Path.Combine(root, "world", "areas", ShipAreaId)), "SWTOR assets not available");
        string tmp = Path.Combine(Path.GetTempPath(), "swtor_md3_" + Guid.NewGuid().ToString("N"));
        try
        {
            var opt = new ExportOptions("", Path.Combine(tmp, "base"), Path.Combine(tmp, "work"), Game: Kotor.Core.KotorGame.Swtor);
            Directory.CreateDirectory(opt.JaBaseDir);
            var log = new ImportLog();
            var r = SwtorExporter.Export(Path.Combine(root!, "world", "areas", ShipAreaId), new SwtorAreaSpec(ShipAreaId, []), null, new SwtorAssets(root), opt, null, log);
            Assert.True(r.Export.Ok, r.Export.Error);
            string ents = r.Bsp!.Entities;
            var blocks = System.Text.RegularExpressions.Regex.Matches(ents, @"\{[^}]*\}").Select(x => x.Value).Where(b => b.Contains("\"misc_model_static\"")).ToList();
            Assert.NotEmpty(blocks);
            Assert.InRange(blocks.Count, 1, MiscModelBudget.EngineMaxEntities);
            Assert.DoesNotContain(blocks, b => b.Contains("modelscale")); // modelscale fausse l eclairage du moteur
            Assert.Contains(log.Warnings, w => w.Code == "STATIC_MODELS");
            string stage = Path.Combine(opt.WorkDir, r.MapName, "pk3");
            string shaders = File.ReadAllText(Path.Combine(stage, "shaders", ShaderNames.ShaderFileName(r.MapName)));
            foreach (string block in blocks)
            {
                string model = System.Text.RegularExpressions.Regex.Match(block, @"""model""\s+""([^""]+)""").Groups[1].Value;
                Assert.True(model.Length is > 4 and < 64, model);
                byte[] md3 = File.ReadAllBytes(Path.Combine(stage, model.Replace('/', Path.DirectorySeparatorChar)));
                Assert.Equal("IDP3", System.Text.Encoding.ASCII.GetString(md3, 0, 4));
            }
            Assert.Contains("rgbGen lightingDiffuse", shaders);
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }

    [SkippableFact]
    public void WarPlanworking_bsp_has_human_scale_in_ja_units_and_spawn_is_inside()
    {
        string? root = TestEnv.SwtorAssetsDir;
        Skip.If(root is null || !Directory.Exists(Path.Combine(root, "world", "areas", ShipAreaId)), "SWTOR assets not available");
        string tmp = Path.Combine(Path.GetTempPath(), "swtor_t7_" + Guid.NewGuid().ToString("N"));
        try
        {
            var opt = new ExportOptions("", Path.Combine(tmp, "base"), Path.Combine(tmp, "work"), Game: Kotor.Core.KotorGame.Swtor);
            Directory.CreateDirectory(opt.JaBaseDir);
            var r = SwtorExporter.Export(Path.Combine(root!, "world", "areas", ShipAreaId), new SwtorAreaSpec(ShipAreaId, []), null, new SwtorAssets(root), opt,
                new SwtorBuildOptions { StaticModelMinTriangles = 0 }, new ImportLog()); // tout dans le BSP : on mesure l echelle du BSP
            Assert.True(r.Export.Ok, r.Export.Error);
            var verts = r.Bsp!.Surfaces.Where(s => !s.Shader.StartsWith("textures/skies", StringComparison.OrdinalIgnoreCase)).SelectMany(s => s.Positions).ToArray();
            Vector3 mins = verts.Aggregate(new Vector3(float.MaxValue), Vector3.Min), maxs = verts.Aggregate(new Vector3(float.MinValue), Vector3.Max);
            // Hauteur du monde en unites JA (40 u/m) : plafond de 1,4 unite GR2 = ~470 u ; le vaisseau entier reste sous 3 000 u de haut hors ciel (des pieces descendent sous le sol).
            float height = maxs.Z - mins.Z;
            Assert.True(height >= 400f && height <= 3000f, $"mins {mins} maxs {maxs}");
            Assert.True(maxs.X - mins.X > 4000f);   // > 100 m de large
            string ent = r.Bsp.Entities;
            var block = System.Text.RegularExpressions.Regex.Matches(ent, @"\{[^}]*\}").Select(x => x.Value).First(b => b.Contains("info_player_start"));
            var m = System.Text.RegularExpressions.Regex.Match(block, @"""origin""\s+""(-?[\d.]+) (-?[\d.]+) (-?[\d.]+)""");
            Assert.True(m.Success);
            float[] o = [float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)];
            Assert.InRange(o[0], mins.X, maxs.X);
            Assert.InRange(o[1], mins.Y, maxs.Y);
            Assert.InRange(o[2], mins.Z, maxs.Z - 300f); // plafond tres au-dessus
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }

    private static bool Inside(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        static float Cross(Vector3 o, Vector3 u, Vector3 v) => (u.X - o.X) * (v.Y - o.Y) - (u.Y - o.Y) * (v.X - o.X);
        float d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
}
