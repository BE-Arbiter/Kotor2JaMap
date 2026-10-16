using Swtor.Core.Dds;
using Swtor.Core.Gr2;
using Swtor.Core.Xml;

namespace Swtor.Core.Tests;

/// <summary>Tests d'integration sur les ressources SWTOR extraites. Ignores si le dossier est absent.</summary>
public class RealAssetsTests
{
    private const string ArchDir = "art/static/area/crl_corellia/arch/city";

    private static SwtorAssets RequireAssets()
    {
        Skip.If(TestEnv.SwtorAssetsDir is null, "Ressources SWTOR absentes");
        return new SwtorAssets();
    }

    [SkippableFact]
    public void Gr2_RealArchitecture_ParsesWithGeometryAndMaterials()
    {
        var assets = RequireAssets();
        string? path = assets.Resolve($"{ArchDir}/crl_arch_building_112x112_01.gr2");
        Skip.If(path is null, "gr2 absent");
        var model = Gr2Reader.Parse(File.ReadAllBytes(path!));

        Assert.Equal(["crl_arch_buildings_panels", "crl_arch_buildings_windows", "crl_arch_city_tram_station_interior_trim"], model.Materials);
        var mesh = model.Meshes.Single(m => m.Name == "crl_arch_building_112x112_01");
        Assert.Equal(1984, mesh.VertexCount);
        Assert.Equal(1490 * 3, mesh.Indices.Length);
        Assert.Equal(Gr2VertexFlags.Position | Gr2VertexFlags.Core, mesh.Flags);
        Assert.NotNull(mesh.Normals);
        Assert.Single(mesh.UvSets);
        Assert.Equal(3, mesh.Pieces.Count);
        Assert.All(mesh.Indices, i => Assert.True(i < mesh.VertexCount));
        // Un batiment de 112x112 "pas" fait environ 12 unites de large.
        Assert.InRange(model.BoundsMax.X - model.BoundsMin.X, 12f, 13f);
        // Le fichier contient aussi un mesh de collision sans normales ni UV.
        var collision = model.Meshes.Single(m => m.Name == "generated_collision");
        Assert.Equal(Gr2VertexFlags.Position, collision.Flags);
        Assert.Empty(collision.UvSets);
    }

    [SkippableFact]
    public void Material_ThenDds_ChainResolvesToImage()
    {
        var assets = RequireAssets();
        string? matPath = assets.Resolve("art/shaders/materials/crl_arch_city_foundation.mat");
        Skip.If(matPath is null, "mat absent");
        var mat = MaterialReader.Parse(File.ReadAllText(matPath!));

        Assert.Equal("Uber", mat.Shader);
        Assert.Equal("art/static/area/crl_corellia/arch/city/texture/crl_arch_city_foundation_d", mat.DiffuseMap);
        Assert.NotNull(mat.TexturePath("RotationMap1"));

        string? ddsPath = assets.Resolve(mat.DiffuseMap + ".dds");
        Assert.NotNull(ddsPath);
        var image = DdsReader.Decode(File.ReadAllBytes(ddsPath!));
        Assert.Equal((512, 512), (image.Width, image.Height));
        Assert.Equal(512 * 512 * 4, image.Rgba.Length);
    }

    [SkippableFact]
    public void AllMaterialsOfAGr2_HaveAMatAndADiffuseDds()
    {
        var assets = RequireAssets();
        string? path = assets.Resolve($"{ArchDir}/crl_arch_building_112x112_01.gr2");
        Skip.If(path is null, "gr2 absent");
        foreach (string name in Gr2Reader.Parse(File.ReadAllBytes(path!)).Materials)
        {
            string? mat = assets.Resolve($"art/shaders/materials/{name}.mat");
            Assert.True(mat is not null, $"mat manquant : {name}");
            string? diffuse = MaterialReader.Parse(File.ReadAllText(mat!)).DiffuseMap;
            Assert.NotNull(diffuse);
            Assert.NotNull(assets.Resolve(diffuse + ".dds"));
        }
    }
}
