using System.IO.Compression;
using System.Text;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Layout;
using Kotor.Core.Mdl;
using Kotor.Core.Modules;
using Kotor.Core.Tests.Archives;
using Kotor.Pipeline;

namespace Kotor.Core.Tests;

public sealed class Kotor2Tests
{
    [Fact]
    public void MapPrefix_and_folders()
    {
        Assert.Equal("kotor", KotorGames.MapPrefix(KotorGame.Kotor1));
        Assert.Equal("kotor2", KotorGames.MapPrefix(KotorGame.Kotor2));
        Assert.Equal("kotor", KotorGames.AssetFolder(KotorGame.Kotor1));
        Assert.Equal("kotor2", KotorGames.AssetFolder(KotorGame.Kotor2));
        Assert.Equal("KOTOR", KotorGames.DisplayName(KotorGame.Kotor1));
        Assert.Equal("KOTOR 2", KotorGames.DisplayName(KotorGame.Kotor2));
        Assert.Equal("textures/kotor2/", ShaderNames.PrefixFor(KotorGame.Kotor2));
        Assert.Equal("textures/kotor2/lm/", ShaderNames.LightmapPrefixFor(KotorGame.Kotor2));
        Assert.Equal("textures/kotor/", ShaderNames.Prefix);
        Assert.Equal("wall", ShaderNames.StripPrefix("textures/kotor2/wall"));
        Assert.Equal("wall", ShaderNames.StripPrefix("textures/kotor/wall"));
    }

    private static string FakeInstall()
    {
        string dir = Path.Combine(Path.GetTempPath(), "k2j_k2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "modules"));
        return dir;
    }

    [Fact]
    public void Resolver_prefers_mod_over_rim()
    {
        string dir = FakeInstall();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "modules", "tst_m01.rim"),
                ArchiveBuilders.Rim(("area1", (uint)ResourceType.Lyt, new byte[] { 0x01 })));
            File.WriteAllBytes(Path.Combine(dir, "modules", "tst_m01.mod"),
                ArchiveBuilders.Erf("MOD V1.0", ("area1", (ushort)ResourceType.Lyt, new byte[] { 0x02 })));
            using var r = ResourceResolver.ForModule(dir, "tst_m01", KotorGame.Kotor2);
            Assert.Equal(new byte[] { 0x02 }, r.Find("area1", ResourceType.Lyt));
            Assert.Contains("tst_m01.mod", r.FindSource("area1", ResourceType.Lyt));
            Assert.Equal(["tst_m01"], ModuleCatalog.ListModuleNames(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Resolver_reads_dlg_erf_only_for_kotor2()
    {
        string dir = FakeInstall();
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "modules", "tst_m01.rim"), ArchiveBuilders.Rim());
            File.WriteAllBytes(Path.Combine(dir, "modules", "tst_m01_dlg.erf"),
                ArchiveBuilders.Erf("ERF V1.0", ("talk", (ushort)ResourceType.Lyt, new byte[] { 0x07 })));
            using (var k2 = ResourceResolver.ForModule(dir, "tst_m01", KotorGame.Kotor2))
                Assert.Equal(new byte[] { 0x07 }, k2.Find("talk", ResourceType.Lyt));
            using (var k1 = ResourceResolver.ForModule(dir, "tst_m01"))
                Assert.Null(k1.Find("talk", ResourceType.Lyt));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ForGame_selects_the_game_folder()
    {
        var o = new ExportOptions("k1", "j", "w", Kotor2Dir: "k2");
        Assert.Equal("k1", o.ForGame(KotorGame.Kotor1).KotorDir);
        ExportOptions o2 = o.ForGame(KotorGame.Kotor2);
        Assert.Equal("k2", o2.KotorDir);
        Assert.Equal(KotorGame.Kotor2, o2.Game);
        Assert.Equal("", new ExportOptions("k1", "j", "w").ForGame(KotorGame.Kotor2).KotorDir);
    }

    [Fact]
    public void Config_reads_kotor2Dir()
    {
        string p = Path.Combine(Path.GetTempPath(), "cfg2_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(p, "{ \"kotor2Dir\": \"D:\\\\k2\" }");
        try { Assert.Equal(@"D:\k2", ConfigLoader.Load(p).Kotor2Dir); }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Shader_planner_uses_the_game_folder()
    {
        var mesh = new Kotor.Core.Scene.SceneMesh("r", "n", "a", "b", Kotor.Core.Textures.TxiInfo.Empty, 0, [], [], [], null, []);
        PlannedShader p = ShaderPlanner.Plan(mesh, null, KotorGame.Kotor2);
        Assert.Equal("textures/kotor2/a__b", p.Name);
        string text = ShaderPlanner.Write(p, KotorGame.Kotor2);
        Assert.Contains("map textures/kotor2/a.tga", text);
        Assert.Contains("map textures/kotor2/lm/b.tga", text);
    }

    [SkippableFact]
    public void Real_k2_catalog_lists_modules()
    {
        Skip.If(TestEnv.Kotor2Dir is null, "KOTOR 2 absent");
        var names = ModuleCatalog.ListModuleNames(TestEnv.Kotor2Dir!);
        Assert.True(names.Count > 50, names.Count.ToString());
        Assert.Contains("001ebo", names);
    }

    [SkippableFact]
    public void Real_k2_room_models_read()
    {
        Skip.If(TestEnv.Kotor2Dir is null, "KOTOR 2 absent");
        int modulesTested = 0;
        foreach (string module in new[] { "001ebo", "101per", "202tel" })
        {
            if (!ModuleCatalog.ListModuleNames(TestEnv.Kotor2Dir!).Contains(module)) continue;
            modulesTested++;
            using var r = ResourceResolver.ForModule(TestEnv.Kotor2Dir!, module, KotorGame.Kotor2);
            var ifo = Kotor.Core.Gff.GffReader.Read(r.Find("module", ResourceType.Ifo)!, out _);
            string area = ifo.GetList("Mod_Area_list")[0].Get<string>("Area_Name")!.ToLowerInvariant();
            var lyt = LayoutFile.Parse(Encoding.ASCII.GetString(r.Find(area, ResourceType.Lyt)!));
            int rooms = 0, textured = 0, lightmapped = 0;
            foreach (string model in lyt.Rooms.Select(x => x.Model).Distinct())
            {
                byte[]? mdl = r.Find(model, ResourceType.Mdl), mdx = r.Find(model, ResourceType.Mdx);
                if (mdl is null || mdx is null) continue;
                var m = MdlReader.Read(mdl, mdx);
                rooms++;
                foreach (var (node, _) in m.Walk())
                {
                    if (node.Mesh is not { } mesh) continue;
                    foreach (int i in mesh.Triangles) Assert.InRange(i, 0, mesh.Positions.Length - 1);
                    foreach (var p in mesh.Positions) Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z));
                    if (mesh.Render && mesh.Texture0 is not null && mesh.Uv0 is not null) textured++;
                    if (mesh.HasLightmap && mesh.Uv1?.Length == mesh.Positions.Length) lightmapped++;
                }
            }
            Assert.True(rooms >= 3, $"{module}: {rooms} room models read");
            Assert.True(textured > 0, module);
            Assert.True(lightmapped > 0, module);
        }
        Assert.True(modulesTested >= 1);
    }

    [SkippableFact]
    public void Real_k2_preview_and_export()
    {
        Skip.If(TestEnv.Kotor2Dir is null, "KOTOR 2 absent");
        string root = Path.Combine(Path.GetTempPath(), "k2j_k2exp_" + Guid.NewGuid().ToString("N"));
        try
        {
            var opt = new ExportOptions(TestEnv.Kotor2Dir!, Path.Combine(root, "base"), Path.Combine(root, "work"), Game: KotorGame.Kotor2);
            Directory.CreateDirectory(opt.JaBaseDir);
            var log = new ImportLog();
            var scene = PipelineSteps.Preview("001ebo", opt, log);
            Assert.NotEmpty(scene.Meshes);
            var imported = PipelineSteps.Import(scene, null, opt, log);
            var exported = PipelineSteps.Export(imported, opt, log);
            string pk3 = PipelineSteps.CreatePk3(exported, imported, opt, log).Pk3Path;
            Assert.Equal(Path.Combine(opt.JaBaseDir, "001ebo.pk3"), pk3);
            using var z = ZipFile.OpenRead(pk3);
            Assert.Contains(z.Entries, e => e.FullName == "maps/001ebo.bsp");
            Assert.Contains(z.Entries, e => e.FullName == "shaders/001ebo.shader");
            Assert.Contains(z.Entries, e => e.FullName.StartsWith("textures/kotor2/") && e.FullName.EndsWith(".tga"));
            Assert.DoesNotContain(z.Entries, e => e.FullName.StartsWith("textures/kotor/"));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
