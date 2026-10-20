using System.Numerics;
using System.Security.Cryptography;
using Kotor.Compiler.Bsp;
using Kotor.Core.Diagnostics;
using BspCompiler = Kotor.Compiler.Compiler;

namespace Kotor.Compiler.Tests;

public sealed class SubModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "k2j_submodel_" + Guid.NewGuid().ToString("N"));

    public SubModelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private byte[] CompileBytes(CompileInput input)
    {
        string path = Path.Combine(_dir, "m.bsp");
        var r = BspCompiler.Compile(input, path, Limits.Swgl, new ImportLog());
        Assert.True(r.Ok, r.Error);
        return File.ReadAllBytes(path);
    }

    /// <summary>Empreinte du BSP du monde de test, mesuree avant l'ajout des sous-modeles (etape 04).</summary>
    private const string GoldenWorldSha256 = "73E8A0E8F017D36ADD7B09505FE4108DB714105FC8DEAD0F4C0E6CC29E7FC390";

    [Fact]
    public void World_without_submodels_is_byte_identical_to_before()
    {
        string hash = Convert.ToHexString(SHA256.HashData(CompileBytes(TestScenes.Input(sky: "textures/skies/test"))));
        Assert.Equal(GoldenWorldSha256, hash);
    }

    internal static IReadOnlyList<BspPlane> BoxPlanes(Vector3 min, Vector3 max) =>
    [
        new(-Vector3.UnitX, -min.X), new(Vector3.UnitX, max.X), new(-Vector3.UnitY, -min.Y),
        new(Vector3.UnitY, max.Y), new(-Vector3.UnitZ, -min.Z), new(Vector3.UnitZ, max.Z),
    ];

    private static CompileInput WithDoor(Vector3 min, Vector3 max) =>
        TestScenes.Input(sky: "textures/skies/test") with
        {
            SubModels = [new SubModelInput([BoxPlanes(min, max)], "textures/system/clip")],
        };

    [Fact]
    public void Submodel_roundtrip()
    {
        var bsp = BspReader.Read(CompileBytes(WithDoor(new(-10, -2, 0), new(10, 2, 100))));

        Assert.Equal(2, bsp.Models.Count);
        BspModelInfo door = bsp.Models[1];
        Assert.Equal(1, door.BrushCount);
        Assert.Equal(0, door.SurfaceCount);
        Assert.Equal(new Vector3(-10, -2, 0), door.Mins);
        Assert.Equal(new Vector3(10, 2, 100), door.Maxs);
        Assert.Equal(6, bsp.BrushSideCounts[door.FirstBrush]);
        Assert.Equal(bsp.Models[0].BrushCount, door.FirstBrush); // apres les brushes du monde
        Assert.Contains("textures/system/clip", bsp.Shaders);
    }

    [Fact]
    public void Submodel_not_in_world_tree()
    {
        var bsp = BspReader.Read(CompileBytes(WithDoor(new(-10, -2, 0), new(10, 2, 100))));
        BspModelInfo door = bsp.Models[1];

        Assert.NotEmpty(bsp.LeafBrushes); // le monde a bien des brushes dans l'arbre
        Assert.DoesNotContain(bsp.LeafBrushes, b => b >= door.FirstBrush);
        Assert.Equal(bsp.Models[0].BrushCount + 1, bsp.Collision.BrushCount);
    }

    [Fact]
    public void Oriented_submodel_brush_keeps_its_planes_and_bounds()
    {
        var n = Vector3.Normalize(new Vector3(1, 1, 0));
        var t = Vector3.Normalize(new Vector3(-1, 1, 0));
        BspPlane[] planes = [new(n, 5), new(-n, 5), new(t, 1), new(-t, 1), new(Vector3.UnitZ, 100), new(-Vector3.UnitZ, 0)];
        var input = TestScenes.Input() with { SubModels = [new SubModelInput([planes], "textures/system/clip")] };

        var bsp = BspReader.Read(CompileBytes(input));
        BspModelInfo door = bsp.Models[1];
        // Boite 10 x 2 tournee de 45 degres : demi-etendue = (5 + 1) * cos 45.
        Assert.Equal(6 * MathF.Sqrt(0.5f), door.Maxs.X, 2);
        Assert.Equal(-door.Maxs.X, door.Mins.X, 2);
        Assert.Equal(10, bsp.BrushSideCounts[door.FirstBrush]); // 6 bornes axiales + 4 plans orientes (les deux Z coincident avec les bornes)
    }

    private static int ContentsOf(BspFile bsp, string shader) => bsp.ShaderContents[bsp.Shaders.ToList().FindIndex(s => string.Equals(s, shader, StringComparison.OrdinalIgnoreCase))];

    [Fact]
    public void Submodel_default_shader_contents_is_solid()
    {
        var bsp = BspReader.Read(CompileBytes(WithDoor(new(-10, -2, 0), new(10, 2, 100))));
        Assert.Equal(BspContents.Solid, ContentsOf(bsp, "textures/system/clip"));
    }

    [Fact]
    public void Submodel_with_water_or_trigger_contents_is_not_solid()
    {
        var input = TestScenes.Input(sky: "textures/skies/test") with
        {
            SubModels =
            [
                new SubModelInput([BoxPlanes(new(-10, -2, 0), new(10, 2, 100))], "textures/jk/water_336699") { Contents = BspContents.Water },
                new SubModelInput([BoxPlanes(new(20, -2, 0), new(30, 2, 100))], "textures/system/trigger") { Contents = BspContents.Trigger },
                new SubModelInput([BoxPlanes(new(40, -2, 0), new(50, 2, 100))], "textures/system/clip"),
            ],
        };
        var bsp = BspReader.Read(CompileBytes(input));

        Assert.Equal(BspContents.Water, ContentsOf(bsp, "textures/jk/water_336699"));
        Assert.Equal(BspContents.Trigger, ContentsOf(bsp, "textures/system/trigger"));
        Assert.Equal(BspContents.Solid, ContentsOf(bsp, "textures/system/clip")); // le sous-modele de collision reste solide
        Assert.Equal(4, bsp.Models.Count);
    }
}
