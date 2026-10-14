using System.Numerics;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Core.Tests.Export;

public class ObjExporterTests
{
    private static SceneMesh Tri(string tex) => new("room1", "node1", tex, null, TxiInfo.Empty, 0,
        [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
        [new(0, 0), new(1, 0), new(0, 1)], null, [0, 1, 2]);

    [Fact]
    public void Obj_export_writes_files()
    {
        string dir = Path.Combine(Path.GetTempPath(), "k2j_obj_" + Guid.NewGuid().ToString("N"));
        try
        {
            var tex = new KotorTexture(2, 2, new byte[16], false, TxiInfo.Empty);
            var scene = new ModuleScene(new ModuleInfo("mod", "area", "Area", default), [Tri("x")],
                [new SceneWalkmesh("room1", [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2], [true])],
                new Dictionary<string, KotorTexture?> { ["x"] = tex });
            ObjExporter.Export(scene, dir);
            string obj = File.ReadAllText(Path.Combine(dir, "mod.obj"));
            Assert.Contains("f 1/1 2/2 3/3", obj);
            Assert.Contains("g room1/node1", obj);
            Assert.Contains("usemtl x", obj);
            Assert.Contains("newmtl x", File.ReadAllText(Path.Combine(dir, "mod.mtl")));
            Assert.True(File.Exists(Path.Combine(dir, "textures", "x.png")));
            Assert.True(File.Exists(Path.Combine(dir, "mod_walkmesh.obj")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Missing_texture_gives_magenta_png()
    {
        string dir = Path.Combine(Path.GetTempPath(), "k2j_obj_" + Guid.NewGuid().ToString("N"));
        try
        {
            var scene = new ModuleScene(new ModuleInfo("mod", "area", "Area", default), [Tri("gone")], [],
                new Dictionary<string, KotorTexture?> { ["gone"] = null });
            ObjExporter.Export(scene, dir);
            Assert.True(File.Exists(Path.Combine(dir, "textures", "gone.png")));
            Assert.False(File.Exists(Path.Combine(dir, "mod_walkmesh.obj")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
