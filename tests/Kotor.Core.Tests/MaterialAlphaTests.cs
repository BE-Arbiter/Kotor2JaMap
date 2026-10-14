using Kotor.Core.Export;

namespace Kotor.Core.Tests;

public sealed class MaterialAlphaTests
{
    [Fact]
    public void Blend_shader_has_blendFunc_and_no_alphaFunc_nor_depthWrite()
    {
        string text = ShaderWriter.Build("Moss", "tfu2ja/Moss.tga", new MaterialAlpha(AlphaKind.Blend, "GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA", true));
        Assert.Contains("\t\tblendFunc GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA\n", text);
        Assert.Contains("\tcull disable\n", text);
        Assert.DoesNotContain("alphaFunc", text);
        Assert.DoesNotContain("depthWrite", text);
    }

    [Fact]
    public void Opaque_alpha_object_matches_legacy_opaque_shader()
    {
        Assert.Equal(ShaderWriter.Build("Rock", "tfu2ja/Rock.tga"),
            ShaderWriter.Build("Rock", "tfu2ja/Rock.tga", new MaterialAlpha(AlphaKind.Opaque, null, false)));
    }
}
