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
        // Defaut : constante derivee du personnage humain (338 u JA par unite GR2 = 8,45 m par unite).
        var layout = Layout(new SyntheticArea.Obj(1, SyntheticArea.GroundTile, Position: new Vector3(6.4f, 0, 0)));
        var r = SwtorSceneBuilder.Build(layout, new FakeSource(), "t", new SwtorBuildOptions(), new ImportLog());
        SceneMesh m = Assert.Single(r.Scene.Meshes);
        float k = SwtorWorldScale.MetresPerGr2Unit;
        Assert.Equal(8.45f, k, 3);
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
    public void Derived_scale_makes_a_swtor_human_a_ja_human_model()
    {
        // Humain SWTOR 0,213 unite GR2 -> 72 unites JA (modele JKA / humain KOTOR de 1,8 m a 40 u/m).
        Assert.Equal(SwtorWorldScale.JaHumanModelHeight, SwtorWorldScale.HumanHeightGr2 * SwtorWorldScale.MetresPerGr2Unit * SwtorWorldScale.JaUnitsPerMetre, 0.2f);
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
    public void WarPlanworking_has_human_scale_in_the_scene()
    {
        string? root = TestEnv.SwtorAssetsDir;
        Skip.If(root is null || !Directory.Exists(Path.Combine(root, "world", "areas", ShipAreaId)), "SWTOR assets not available");
        var layout = AreaLayout.Load(Path.Combine(root!, "world", "areas", ShipAreaId));
        var r = SwtorSceneBuilder.Build(layout, new SwtorAssetsModelSource(new SwtorAssets(root)), "war_planworking", null, new ImportLog());
        Vector3 spawn = r.Scene.Info.Entry.Position;

        // Plafond au-dessus du point d apparition : 1,4 unite GR2 x 8,45 = ~11,8 m (salle de vaisseau capital, pas un couloir humain).
        float ceiling = float.MaxValue;
        foreach (SceneMesh m in r.Scene.Meshes)
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
        Assert.True(r.Scene.Meshes.Max(m => m.Positions.Max(p => p.X)) - r.Scene.Meshes.Min(m => m.Positions.Min(p => p.X)) > 100f);
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
            var r = SwtorExporter.Export(Path.Combine(root!, "world", "areas", ShipAreaId), new SwtorAreaSpec(ShipAreaId, []), null, new SwtorAssets(root), opt, null, new ImportLog());
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
