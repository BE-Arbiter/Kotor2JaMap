using Kotor.Core.Export;

namespace Kotor.Core.Tests;

public sealed class ShaderWriterTests
{
    [Fact]
    public void Shader_block_matches_format()
    {
        var text = ShaderWriter.Build("Rock", "kotor/Rock.tga");

        var expected =
            "textures/kotor/Rock\n" +
            "{\n" +
            "\tqer_editorimage textures/kotor/Rock\n" +
            "\tsurfaceparm nolightmap\n" +
            "\t{\n" +
            "\t\tmap textures/kotor/Rock.tga\n" +
            "\t\trgbGen vertex\n" +
            "\t}\n" +
            "}\n";

        Assert.Equal(expected, text);
    }

    [Fact]
    public void Cutout_shader_has_cull_disable_alphafunc_depthwrite()
    {
        var text = ShaderWriter.Build("Grass", "kotor/Grass.tga", alphaCutout: true);

        Assert.Contains("\tcull disable\n", text);
        Assert.Contains("\t\talphaFunc GE128\n", text);
        Assert.Contains("\t\tdepthWrite\n", text);
        Assert.Contains("surfaceparm nolightmap", text);
        Assert.Contains("rgbGen vertex", text);
        Assert.Contains("qer_editorimage", text);
    }

    [Fact]
    public void Opaque_shader_has_no_cutout_directives()
    {
        var text = ShaderWriter.Build("Rock", "kotor/Rock.tga");

        Assert.DoesNotContain("cull", text);
        Assert.DoesNotContain("alphaFunc", text);
        Assert.DoesNotContain("depthWrite", text);
    }
}

public sealed class ShaderNamesTests
{
    private static readonly string Long90 = new string('a', 45) + "_" + new string('b', 44);

    [Fact]
    public void Short_name_is_at_most_63_chars()
    {
        string full = ShaderNames.FullName(Long90);
        Assert.True(full.Length <= 63, full);
        Assert.StartsWith("textures/kotor/", full);
        Assert.Equal("Rock", ShaderNames.Short("Rock"));
        Assert.Equal(ShaderNames.Short(Long90), ShaderNames.Short(ShaderNames.Short(Long90))); // idempotent
    }

    [Fact]
    public void Image_path_with_tga_extension_is_at_most_63_chars()
    {
        string m = "game_disc_levels_dagobah_materials_dagobah_swamp_ground_mud_0001_extra_long_name";
        string img = ShaderNames.Prefix + ShaderNames.Short(m) + ".tga";
        Assert.True(img.Length <= 63, img);
        Assert.True(ShaderNames.FullName(m).Length <= 63);
        Assert.True(ShaderNames.Short(Long90).Length + ShaderNames.Prefix.Length + 4 <= 63);
        Assert.DoesNotContain("game_disc_levels_", ShaderNames.Short(m));
        Assert.Equal(ShaderNames.Short(m), ShaderNames.Short(ShaderNames.Short(m)));
    }

    [Fact]
    public void Long_names_with_same_start_get_distinct_short_names()
    {
        string a = "game_disc_levels_dagobah_materials_dagobah_swamp_ground_mud_A";
        string b = "game_disc_levels_dagobah_materials_dagobah_swamp_ground_mud_B";
        Assert.NotEqual(ShaderNames.Short(a), ShaderNames.Short(b));
    }

    [Fact]
    public void Short_name_is_deterministic_and_unique()
    {
        string a = Long90, b = Long90[..^1] + "c";
        Assert.NotEqual(ShaderNames.Short(a), ShaderNames.Short(b));
        Assert.Equal(ShaderNames.Short(a), ShaderNames.Short(a));
    }
}
