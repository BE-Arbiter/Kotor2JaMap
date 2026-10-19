using Kotor.Core;
using Kotor.Core.Export;
using Kotor.Core.Scene;
using Swtor.Core.Xml;

namespace Swtor.Pipeline.Tests;

public class MultiplyDecalTests
{
    [Fact]
    public void Multiply_material_is_its_own_surface_kind_without_collision()
    {
        var mat = new MaterialDef("Uber", new Dictionary<string, string>(), AlphaMode: "Multiply");
        SwtorMaterialClass c = SwtorMaterials.Classify("blastermark_small", mat);
        Assert.Equal(SurfaceAlpha.Multiply, c.Alpha);
        Assert.False(c.Hidden);
        Assert.True(c.NoCollision);
    }

    [Fact]
    public void BlendToWhite_makes_transparent_texels_neutral_and_keeps_opaque_ones()
    {
        byte[] rgba = [100, 50, 0, 255,   10, 20, 30, 0,   100, 100, 100, 128];
        SwtorMaterials.BlendToWhite(rgba);
        Assert.Equal([100, 50, 0, 255], rgba[..4]);
        Assert.Equal([255, 255, 255, 255], rgba[4..8]);
        Assert.InRange(rgba[8], (byte)176, (byte)178);   // a mi-chemin entre 100 et 255
        Assert.Equal(255, rgba[11]);
    }

    [Fact]
    public void Planner_writes_a_filter_shader_for_multiply()
    {
        var mesh = new SceneMesh("r", "n", "decal", null, Kotor.Core.Textures.TxiInfo.Empty, 0, [System.Numerics.Vector3.Zero],
            [System.Numerics.Vector3.UnitZ], [System.Numerics.Vector2.Zero], null, [0, 0, 0]) { Alpha = SurfaceAlpha.Multiply };
        PlannedShader p = ShaderPlanner.Plan(mesh, null, KotorGame.Swtor);
        Assert.Equal(ShaderKind.Multiply, p.Kind);
        Assert.True(p.DoubleSided);
        string text = ShaderPlanner.Write(p, KotorGame.Swtor);
        Assert.Contains("blendFunc GL_DST_COLOR GL_ZERO", text);
        Assert.Contains("polygonOffset", text);
        Assert.Contains("surfaceparm nonsolid", text);
    }
}
