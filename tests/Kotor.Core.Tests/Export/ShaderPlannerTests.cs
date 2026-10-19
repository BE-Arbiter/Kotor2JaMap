using System.Numerics;
using Kotor.Core.Export;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Core.Tests.Export;

public class ShaderPlannerTests
{
    private static SceneMesh Mesh(string? lm, string blending = "default", int hint = 0) =>
        new("r", "n", "a", lm, TxiInfo.Parse("blending " + blending), hint, [Vector3.Zero], [Vector3.UnitZ], [Vector2.Zero],
            lm is null ? null : [Vector2.Zero], [0, 0, 0]);

    private static KotorTexture Tex(bool alpha) => new(1, 1, [0, 0, 0, 255], alpha, TxiInfo.Empty);

    [Fact]
    public void Alpha_texture_without_lightmap_is_cutout()
    {
        Assert.Equal(ShaderKind.UnlitCutout, ShaderPlanner.Plan(Mesh(null), Tex(true)).Kind);
        Assert.Equal(ShaderKind.Blend, ShaderPlanner.Plan(Mesh(null, hint: 1), Tex(true)).Kind);
    }

    [Fact]
    public void Overlay_mesh_gets_a_two_layer_vertex_alpha_shader()
    {
        SceneMesh m = Mesh(null) with { Alpha = SurfaceAlpha.Opaque, OverlayTexture = "g", OverlayUvScale = new Vector2(2f, 0.5f), VertexAlpha = [128] };
        PlannedShader s = ShaderPlanner.Plan(m, Tex(false), KotorGame.Swtor);
        Assert.Equal(ShaderKind.Overlay, s.Kind);
        Assert.Equal("textures/swtor/a__g", s.Name);
        Assert.Equal("g", s.Overlay);
        Assert.False(s.DoubleSided);
        Assert.Equal(
            "textures/swtor/a__g\n{\n\t{\n\t\tmap textures/swtor/a.tga\n\t\trgbGen vertex\n\t}\n" +
            "\t{\n\t\tmap textures/swtor/g.tga\n\t\ttcMod scale 2 0.5\n\t\tblendFunc GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA\n\t\trgbGen vertex\n\t\talphaGen vertex\n\t}\n}\n",
            ShaderPlanner.Write(s, KotorGame.Swtor));
        // Meme repetition : pas de tcMod.
        Assert.DoesNotContain("tcMod", ShaderPlanner.Write(ShaderPlanner.Plan(m with { OverlayUvScale = Vector2.One }, Tex(false), KotorGame.Swtor), KotorGame.Swtor));
    }

    [Fact]
    public void Planner_picks_kind_per_table()
    {
        var add = ShaderPlanner.Plan(Mesh("b", "additive"), Tex(false));
        Assert.Equal(ShaderKind.Additive, add.Kind);
        Assert.Equal("textures/kotor/a", add.Name);

        var blend = ShaderPlanner.Plan(Mesh("b", hint: 1), Tex(true));
        Assert.Equal(ShaderKind.Blend, blend.Kind);
        Assert.Equal("textures/kotor/a", blend.Name);

        var cutPunch = ShaderPlanner.Plan(Mesh("b", "punchthrough", hint: 1), Tex(true));
        Assert.Equal(ShaderKind.LightmappedCutout, cutPunch.Kind);
        Assert.Equal("textures/kotor/a__b", cutPunch.Name);

        var cutAlpha = ShaderPlanner.Plan(Mesh("b"), Tex(true));
        Assert.Equal(ShaderKind.LightmappedCutout, cutAlpha.Kind);

        var lit = ShaderPlanner.Plan(Mesh("b"), Tex(false));
        Assert.Equal(ShaderKind.Lightmapped, lit.Kind);
        Assert.Equal("textures/kotor/a__b", lit.Name);
        Assert.Equal("b", lit.Lightmap);

        var unlit = ShaderPlanner.Plan(Mesh(null), Tex(false));
        Assert.Equal(ShaderKind.Unlit, unlit.Kind);
        Assert.Equal("textures/kotor/a", unlit.Name);
        Assert.Null(unlit.Lightmap);

        // hint sans alpha : pas de Blend
        Assert.Equal(ShaderKind.Unlit, ShaderPlanner.Plan(Mesh(null, hint: 1), Tex(false)).Kind);
        // texture manquante : sans alpha
        Assert.Equal(ShaderKind.Lightmapped, ShaderPlanner.Plan(Mesh("b"), null).Kind);
    }

    [Fact]
    public void Lightmapped_shader_text_is_exact()
    {
        string expected =
            "textures/kotor/a__b\n{\n\tnopicmip\n\tnomipmaps\n\t{\n\t\tmap textures/kotor/lm/b.tga\n\t\ttcGen lightmap\n\t\trgbGen identity\n\t}\n" +
            "\t{\n\t\tmap textures/kotor/a.tga\n\t\tblendFunc GL_DST_COLOR GL_SRC_COLOR\n\t\trgbGen identity\n\t}\n" +
            "\t{\n\t\tmap $lightmap\n\t\tblendFunc GL_ZERO GL_ONE\n\t}\n}\n";
        Assert.Equal(expected, ShaderPlanner.Write(new PlannedShader("textures/kotor/a__b", ShaderKind.Lightmapped, "a", "b")));
    }

    [Fact]
    public void Other_shader_texts_are_exact()
    {
        Assert.Equal(
            "textures/kotor/a__b\n{\n\tnopicmip\n\tnomipmaps\n\tcull none\n\t{\n\t\tmap textures/kotor/a.tga\n\t\talphaFunc GE128\n\t\tdepthWrite\n\t\trgbGen identity\n\t}\n" +
            "\t{\n\t\tmap textures/kotor/lm/b.tga\n\t\ttcGen lightmap\n\t\tblendFunc GL_DST_COLOR GL_SRC_COLOR\n\t\tdepthFunc equal\n\t\trgbGen identity\n\t}\n" +
            "\t{\n\t\tmap $lightmap\n\t\tblendFunc GL_ZERO GL_ONE\n\t}\n}\n",
            ShaderPlanner.Write(new PlannedShader("textures/kotor/a__b", ShaderKind.LightmappedCutout, "a", "b")));
        Assert.Equal(
            "textures/kotor/a\n{\n\tsurfaceparm nonsolid\n\tsurfaceparm trans\n\tpolygonOffset\n\tcull none\n\t{\n\t\tmap textures/kotor/a.tga\n\t\tblendFunc GL_ONE GL_ONE\n\t\trgbGen identity\n\t}\n}\n",
            ShaderPlanner.Write(new PlannedShader("textures/kotor/a", ShaderKind.Additive, "a", null)));
        Assert.Equal(
            "textures/kotor/a\n{\n\tsurfaceparm trans\n\tpolygonOffset\n\tcull none\n\t{\n\t\tmap textures/kotor/a.tga\n\t\tblendFunc GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA\n\t\trgbGen vertex\n\t}\n}\n",
            ShaderPlanner.Write(new PlannedShader("textures/kotor/a", ShaderKind.Blend, "a", null)));
        Assert.Equal(
            "textures/kotor/a\n{\n\t{\n\t\tmap textures/kotor/a.tga\n\t\trgbGen vertex\n\t}\n}\n",
            ShaderPlanner.Write(new PlannedShader("textures/kotor/a", ShaderKind.Unlit, "a", null)));
    }

    private static SceneMesh EnvMesh(string blending = "default", string extra = "") =>
        new("r", "n", "a", "b", TxiInfo.Parse($"envmaptexture cm_x\nblending {blending}\n{extra}"), 0, [Vector3.Zero], [Vector3.UnitZ],
            [Vector2.Zero], [Vector2.Zero], [0, 0, 0]);

    [Fact]
    public void Envmap_alpha_is_not_cutout()
    {
        Assert.Equal(ShaderKind.Lightmapped, ShaderPlanner.Plan(EnvMesh(), Tex(true)).Kind);
    }

    [Fact]
    public void Punchthrough_still_cutout()
    {
        Assert.Equal(ShaderKind.LightmappedCutout, ShaderPlanner.Plan(EnvMesh("punchthrough"), Tex(true)).Kind);
    }

    [Fact]
    public void Envmap_with_decal_keeps_alpha()
    {
        Assert.Equal(ShaderKind.LightmappedCutout, ShaderPlanner.Plan(EnvMesh(extra: "decal 1"), Tex(true)).Kind);
    }

    [Fact]
    public void Lightmapped_shader_has_nopicmip()
    {
        foreach (var kind in new[] { ShaderKind.Lightmapped, ShaderKind.LightmappedCutout })
        {
            string text = ShaderPlanner.Write(new PlannedShader("textures/kotor/a__b", kind, "a", "b"));
            Assert.Contains("\tnopicmip\n", text);
            Assert.Contains("\tnomipmaps\n", text);
        }
        Assert.DoesNotContain("nopicmip", ShaderPlanner.Write(new PlannedShader("textures/kotor/a", ShaderKind.Unlit, "a", null)));
    }
}
