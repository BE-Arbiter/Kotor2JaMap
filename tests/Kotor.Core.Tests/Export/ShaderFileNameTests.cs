using Kotor.Core.Export;

namespace Kotor.Core.Tests.Export;

public class ShaderFileNameTests
{
    [Fact]
    public void Shader_file_is_named_after_the_map()
    {
        Assert.Equal("demo.shader", ShaderNames.ShaderFileName("demo"));
        Assert.Equal("war_planworking.shader", ShaderNames.ShaderFileName("war_planworking"));
    }

    [Fact]
    public void Longest_map_name_fits_the_engine_path_buffer()
    {
        // Le moteur lit "shaders/<fichier>" dans 64 octets : au-dela de 63 caracteres le fichier entier est ignore.
        Assert.True(("shaders/" + ShaderNames.ShaderFileName(new string('x', MapNames.MaxLength))).Length <= 63);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public void Map_names_are_limited_to_32_characters(int length, bool valid)
    {
        Assert.Equal(32, MapNames.MaxLength);
        Assert.Equal(valid, MapNames.Validate(new string('a', length)) is null);
    }
}
