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
        var layout = Layout(new SyntheticArea.Obj(1, SyntheticArea.GroundTile, Position: new Vector3(6.4f, 0, 0)));
        var r = SwtorSceneBuilder.Build(layout, new FakeSource(), "t", new SwtorBuildOptions { WorldScale = 2f }, new ImportLog());
        SceneMesh m = Assert.Single(r.Scene.Meshes);
        Assert.Equal(12.8f, m.Positions.Min(p => p.X), 3);   // 6,4 m x 2
        Assert.Equal(19.2f, m.Positions.Max(p => p.X), 3);   // (6,4 + 3,2) m x 2 : la dalle fait 6,4 m
        Assert.Equal(-6.4f, m.Positions.Min(p => p.Y), 3);
    }

    [Fact]
    public void World_scale_is_applied_to_light_position_and_radius()
    {
        var lp = new LightParams(SwtorLightType.Wide, 1f, 1f, new Vector3(2f), null, null, null);
        SceneLight a = SwtorSceneBuilder.MakeLight(1, new Vector3(1, 2, 3), Vector3.One, lp, new SwtorBuildOptions())!;
        SceneLight b = SwtorSceneBuilder.MakeLight(1, new Vector3(1, 2, 3), Vector3.One, lp, new SwtorBuildOptions { WorldScale = 2f })!;
        Assert.Equal(a.Radius * 2f, b.Radius, 3);
    }

    [Fact]
    public void Space_station_kit_is_detected_and_other_kits_stay_at_one()
    {
        Assert.Equal(SwtorWorldScale.SpaceKit, SwtorWorldScale.Detect([@"\art\static\area\spc_space\arch\spc_arch_ss_hall_reg32m.gr2", @"\art\static\area\spc_space\arch\spc_arch_ss_mod_floor_plain.gr2", @"\art\static\area\all_all\item\x.gr2"]));
        Assert.Equal(1f, SwtorWorldScale.Detect([@"\art\static\area\crl_corellia\arch\city\a.gr2", @"\art\static\area\all_all\item\x.gr2"]));
        Assert.Equal(1f, SwtorWorldScale.Detect([]));
    }

    [SkippableFact]
    public void WarPlanworking_has_human_scale_in_the_scene()
    {
        string? root = TestEnv.SwtorAssetsDir;
        Skip.If(root is null || !Directory.Exists(Path.Combine(root, "world", "areas", ShipAreaId)), "SWTOR assets not available");
        var layout = AreaLayout.Load(Path.Combine(root!, "world", "areas", ShipAreaId));
        var r = SwtorSceneBuilder.Build(layout, new SwtorAssetsModelSource(new SwtorAssets(root)), "war_planworking", null, new ImportLog());
        Vector3 spawn = r.Scene.Info.Entry.Position;

        // Plafond au-dessus du point d'apparition : entre 2,8 m et 8 m (un humain de 1,8 m tient debout, le plafond n'est pas un hangar).
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
        Assert.InRange(ceiling - spawn.Z, 2.5f, 8f);

        // Les dalles de 3,2 m du kit sont a l'echelle du monde : la plus grande piece du vaisseau fait plus de 10 m de long.
        Assert.True(r.Scene.Meshes.Max(m => m.Positions.Max(p => p.X)) - r.Scene.Meshes.Min(m => m.Positions.Min(p => p.X)) > 25f);
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
            // Hauteur du monde en unites JA (40 u/m) : un plafond de 3 m = 120 u, le vaisseau entier reste sous 12 m de haut hors coque.
            float height = maxs.Z - mins.Z;
            Assert.True(height >= 120f && height <= 600f, $"mins {mins} maxs {maxs}");
            Assert.True(maxs.X - mins.X > 1000f);   // > 25 m de large
            string ent = r.Bsp.Entities;
            var block = System.Text.RegularExpressions.Regex.Matches(ent, @"\{[^}]*\}").Select(x => x.Value).First(b => b.Contains("info_player_start"));
            var m = System.Text.RegularExpressions.Regex.Match(block, @"""origin""\s+""(-?[\d.]+) (-?[\d.]+) (-?[\d.]+)""");
            Assert.True(m.Success);
            float[] o = [float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture)];
            Assert.InRange(o[0], mins.X, maxs.X);
            Assert.InRange(o[1], mins.Y, maxs.Y);
            Assert.InRange(o[2], mins.Z, maxs.Z - 100f); // plafond au moins 2,5 m au-dessus
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
