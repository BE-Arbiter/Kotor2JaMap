using Swtor.Core;
using Swtor.Core.Xml;

namespace Swtor.Core.Tests;

public class MaterialReaderTests
{
    [Fact]
    public void Parse_Material_NormalizesTexturePaths()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Material>
              <Derived>Garment</Derived>
              <AlphaMode>Test</AlphaMode>
              <AlphaTestValue>0.25</AlphaTestValue>
              <input><semantic>DiffuseMap</semantic><type>texture</type><value>\art\dynamic\x\tex_d</value></input>
              <input><semantic>GlossMap</semantic><type>texture</type><value>art\dynamic\x\tex_s</value></input>
              <input><semantic>RimWidth</semantic><type>float</type><value>1.5</value></input>
            </Material>
            """;
        var material = MaterialReader.Parse("\uFEFF" + xml);

        Assert.Equal("Garment", material.Shader);
        Assert.Equal("art/dynamic/x/tex_d", material.DiffuseMap);
        Assert.Equal("art/dynamic/x/tex_s", material.TexturePath("GlossMap"));
        Assert.Null(material.TexturePath("Missing"));
        Assert.Equal(("Test", 0.25f), (material.AlphaMode, material.AlphaTestValue));
    }

    [Fact]
    public void Parse_InvalidXml_Throws() => Assert.Throws<GameFormatException>(() => MaterialReader.Parse("<Material>"));
}
