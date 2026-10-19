using System.Numerics;
using Kotor.Core;
using Kotor.Core.Export;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Core.Tests.Export;

/// <summary>Garde-fou KOTOR / SWTOR (sans indice, la sortie est inchangee) et indices de shader des surfaces speciales de Jedi Knight.</summary>
public class ShaderPlannerJkTests
{
    private static SceneMesh Mesh(string? lm, string tex = "a", string blending = "default", int hint = 0) =>
        new("r", "n", tex, lm, TxiInfo.Parse("blending " + blending), hint, [Vector3.Zero], [Vector3.UnitZ], [Vector2.Zero],
            lm is null ? null : [Vector2.Zero], [0, 0, 0]);

    private static KotorTexture Tex(bool alpha) => new(1, 1, [0, 0, 0, 255], alpha, TxiInfo.Empty);

    [Fact]
    public void NoHints_ShaderUnchangedForKotor()
    {
        // Lightmapped
        Assert.Equal(
            "textures/kotor/a__b\n{\n\tnopicmip\n\tnomipmaps\n" +
            "\t{\n\t\tmap textures/kotor/lm/b.tga\n\t\ttcGen lightmap\n\t\trgbGen identity\n\t}\n" +
            "\t{\n\t\tmap textures/kotor/a.tga\n\t\tblendFunc GL_DST_COLOR GL_SRC_COLOR\n\t\trgbGen identity\n\t}\n" +
            "\t{\n\t\tmap $lightmap\n\t\tblendFunc GL_ZERO GL_ONE\n\t}\n}\n",
            ShaderPlanner.Write(ShaderPlanner.Plan(Mesh("b"), Tex(false))));
        // Unlit
        Assert.Equal(
            "textures/kotor/a\n{\n\t{\n\t\tmap textures/kotor/a.tga\n\t\trgbGen vertex\n\t}\n}\n",
            ShaderPlanner.Write(ShaderPlanner.Plan(Mesh(null), Tex(false))));
        // Cutout
        Assert.Equal(
            "textures/kotor/a\n{\n\tcull none\n\t{\n\t\tmap textures/kotor/a.tga\n\t\talphaFunc GE128\n\t\tdepthWrite\n\t\trgbGen vertex\n\t}\n}\n",
            ShaderPlanner.Write(ShaderPlanner.Plan(Mesh(null), Tex(true))));
        // Blend (prefixe SWTOR)
        Assert.Equal(
            "textures/swtor/a\n{\n\tsurfaceparm trans\n\tpolygonOffset\n\tcull none\n\t{\n\t\tmap textures/swtor/a.tga\n\t\tblendFunc GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA\n\t\trgbGen vertex\n\t}\n}\n",
            ShaderPlanner.Write(ShaderPlanner.Plan(Mesh(null) with { Alpha = SurfaceAlpha.Blend }, Tex(true), KotorGame.Swtor), KotorGame.Swtor));
    }

    [Fact]
    public void NoHints_MeshCarriesNoneAndPlannedShaderHasNoEffects()
    {
        SceneMesh m = Mesh("b");
        Assert.Equal(ShaderHints.None, m.Hints);
        Assert.True(m.Hints.IsNone);
        PlannedShader p = ShaderPlanner.Plan(m, Tex(false));
        Assert.True(p.Hints.IsNone);
        Assert.Null(p.Hints.AnimFrames);
    }

    [Fact]
    public void Sky_PlansSkyShaderNamedAfterTheTexture()
    {
        var hints = ShaderHints.None with { Sky = true, SkyTile = 3f, Scroll = new Vector2(0.25f, 0f) };
        SceneMesh m = Mesh(null, "sky_01narshadda") with { Hints = hints };
        PlannedShader p = ShaderPlanner.Plan(m, Tex(false), KotorGame.JediKnight);
        Assert.Equal(ShaderKind.Sky, p.Kind);
        Assert.Equal("textures/jk/sky_01narshadda", p.Name); // le meme nom que ModuleScene.SkyShader
        string text = ShaderPlanner.Write(p, KotorGame.JediKnight);
        Assert.Contains("surfaceparm sky", text);
        Assert.Contains("skyParms - 128 -", text);
        Assert.Contains("map textures/jk/sky_01narshadda.tga", text);
        Assert.Contains("tcMod scale 3 3", text);
        Assert.Contains("tcMod scroll -0.25 0", text);
    }

    [Fact]
    public void AnimFrames_WriteAnimMapAndKeepTheLightmapStages()
    {
        var hints = ShaderHints.None with { AnimFrames = ["a", "a_c1", "a_c2"], AnimFps = 5f };
        SceneMesh m = Mesh("b") with { Hints = hints };
        PlannedShader p = ShaderPlanner.Plan(m, Tex(false), KotorGame.JediKnight);
        Assert.Equal(ShaderKind.Lightmapped, p.Kind);
        Assert.StartsWith("textures/jk/a__b_", p.Name); // un shader de plus : pas le meme nom que la version fixe
        string text = ShaderPlanner.Write(p, KotorGame.JediKnight);
        Assert.Contains("animMap 5 textures/jk/a.tga textures/jk/a_c1.tga textures/jk/a_c2.tga", text);
        Assert.Contains("map textures/jk/lm/b.tga", text);
        Assert.Contains("tcGen lightmap", text);
        Assert.Contains("map $lightmap", text);
    }

    [Fact]
    public void Scroll_WritesTcModScrollWithSceneToQuakeSignConversion()
    {
        // Contenu qui se deplace de (+0,25 ; +0,5) en UV de scene (v vers le haut) : tc de Quake (t = 1 - v) => scroll (-0,25 ; +0,5).
        SceneMesh m = Mesh(null) with { Hints = ShaderHints.None with { Scroll = new Vector2(0.25f, 0.5f) } };
        string text = ShaderPlanner.Write(ShaderPlanner.Plan(m, Tex(false), KotorGame.JediKnight), KotorGame.JediKnight);
        Assert.Contains("\t\tmap textures/jk/a.tga\n\t\ttcMod scroll -0.25 0.5\n\t\trgbGen vertex\n", text);
    }

    [Fact]
    public void Water_BlendsAndRipples()
    {
        SceneMesh m = Mesh(null) with { Alpha = SurfaceAlpha.Blend, Hints = ShaderHints.None with { Water = true } };
        PlannedShader p = ShaderPlanner.Plan(m, Tex(true), KotorGame.JediKnight);
        Assert.Equal(ShaderKind.Water, p.Kind);
        Assert.True(p.DoubleSided);
        string text = ShaderPlanner.Write(p, KotorGame.JediKnight);
        Assert.Contains("surfaceparm trans", text);
        Assert.Contains("cull none", text);
        Assert.Contains("tcMod turb", text);
        Assert.Contains("blendFunc GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA", text);
    }

    [Fact]
    public void FullBright_UsesIdentityColourInsteadOfVertexLighting()
    {
        SceneMesh m = Mesh(null) with { Hints = ShaderHints.None with { FullBright = true, Lava = true } };
        PlannedShader p = ShaderPlanner.Plan(m, Tex(false), KotorGame.JediKnight);
        Assert.Equal(ShaderKind.Unlit, p.Kind);
        string text = ShaderPlanner.Write(p, KotorGame.JediKnight);
        Assert.Contains("rgbGen identity", text);
        Assert.DoesNotContain("rgbGen vertex", text);
        Assert.Contains("tcMod turb", text); // la lave ondule
    }

    [Fact]
    public void EqualHintsFromDifferentLists_PlanToEqualShaders()
    {
        PlannedShader a = ShaderPlanner.Plan(Mesh(null) with { Hints = ShaderHints.None with { AnimFrames = ["a", "a_c1"], AnimFps = 5f } }, Tex(false), KotorGame.JediKnight);
        PlannedShader b = ShaderPlanner.Plan(Mesh(null) with { Hints = ShaderHints.None with { AnimFrames = new List<string> { "a", "a_c1" }, AnimFps = 5f } }, Tex(false), KotorGame.JediKnight);
        Assert.Equal(a, b); // sinon ModuleExporter y verrait un faux SHADER_NAME_CONFLICT
        PlannedShader c = ShaderPlanner.Plan(Mesh(null) with { Hints = ShaderHints.None with { AnimFrames = ["a", "a_c1"], AnimFps = 6f } }, Tex(false), KotorGame.JediKnight);
        Assert.NotEqual(a.Name, c.Name);
    }

    [Fact]
    public void HintedShaderName_StaysWithinTheBspLimit()
    {
        string longTex = new('x', 30);
        SceneMesh m = Mesh("b", longTex) with { Hints = ShaderHints.None with { Scroll = new Vector2(0.1f, 0.2f) } };
        PlannedShader p = ShaderPlanner.Plan(m, Tex(false), KotorGame.JediKnight);
        Assert.True(p.Name.Length <= ShaderNames.MaxFullLength, p.Name);
    }
}
