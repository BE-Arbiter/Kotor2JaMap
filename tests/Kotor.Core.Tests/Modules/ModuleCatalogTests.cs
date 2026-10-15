using Kotor.Core.Archives;
using Kotor.Core.Modules;
using Kotor.Core.Tests.Archives;
using Kotor.Core.Tests.Gff;

namespace Kotor.Core.Tests.Modules;

public class ModuleCatalogTests
{
    [Fact]
    public void ListModuleNames_excludes_s_rims_and_lists_mods()
    {
        string dir = Path.Combine(Path.GetTempPath(), "k2j_cat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "modules"));
        try
        {
            foreach (string n in new[] { "b.rim", "a_s.rim", "A.rim" }) File.WriteAllBytes(Path.Combine(dir, "modules", n), new byte[8]);
            File.WriteAllBytes(Path.Combine(dir, "modules", "c.mod"), new byte[8]);
            Assert.Equal(new[] { "a", "b", "c" }, ModuleCatalog.ListModuleNames(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_reads_synthetic_module()
    {
        string dir = Path.Combine(Path.GetTempPath(), "k2j_cat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "modules"));
        try
        {
            var ifo = new GffBuilder();
            int a = ifo.AddStruct(6);
            ifo.Add(a, "Area_Name", 11, "m99zz");
            ifo.Add(ifo.Root, "Mod_Area_list", 15, new[] { a })
               .Add(ifo.Root, "Mod_Entry_X", 8, 1f).Add(ifo.Root, "Mod_Entry_Y", 8, 2f).Add(ifo.Root, "Mod_Entry_Z", 8, 3f)
               .Add(ifo.Root, "Mod_Entry_Dir_X", 8, 0f).Add(ifo.Root, "Mod_Entry_Dir_Y", 8, 1f);
            var are = new GffBuilder();
            are.Add(are.Root, "Name", 12, (0xFFFFFFFFu, new[] { (0, "Zone Test") }));
            File.WriteAllBytes(Path.Combine(dir, "modules", "tst_m99zz.rim"), ArchiveBuilders.Rim(
                ("module", (uint)ResourceType.Ifo, ifo.Build("IFO ")),
                ("m99zz", (uint)ResourceType.Are, are.Build("ARE "))));
            using var res = ResourceResolver.ForModule(dir, "tst_m99zz");
            var info = ModuleCatalog.Load(res, "tst_m99zz", null);
            Assert.Equal("m99zz", info.AreaResRef);
            Assert.Equal("Zone Test", info.AreaName);
            Assert.Equal(new System.Numerics.Vector3(1, 2, 3), info.Entry.Position);
            Assert.Equal(new System.Numerics.Vector2(0, 1), info.Entry.Direction);
        }
        finally { Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public void Real_tar_m02aa_info()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        using var res = ResourceResolver.ForModule(TestEnv.KotorDir!, "tar_m02aa");
        var tlk = Kotor.Core.Tlk.TlkFile.Open(Path.Combine(TestEnv.KotorDir!, "dialog.tlk"));
        var info = ModuleCatalog.Load(res, "tar_m02aa", tlk);
        Assert.Equal("m02aa", info.AreaResRef);
        Assert.False(string.IsNullOrEmpty(info.AreaName));
        Assert.NotEqual("m02aa", info.AreaName);
        Assert.True(float.IsFinite(info.Entry.Position.X) && float.IsFinite(info.Entry.Position.Y) && float.IsFinite(info.Entry.Position.Z));
        Assert.True(info.Entry.Direction.Length() > 0);
    }

    [SkippableFact]
    public void Real_catalog_has_23_taris_modules()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        Assert.Equal(23, ModuleCatalog.ListModuleNames(TestEnv.KotorDir!).Count(n => n.StartsWith("tar_")));
    }
}
