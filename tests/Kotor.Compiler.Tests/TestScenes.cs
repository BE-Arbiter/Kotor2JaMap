using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core.Geometry;

namespace Kotor.Compiler.Tests;

/// <summary>Scènes synthétiques en mémoire (unités JA) partagées par les tests du compilateur.</summary>
internal static class TestScenes
{
    public static ModelMesh Mesh(Vector3[] positions, int[] triangles) =>
        new() { Name = "t", SubMeshes = [new SubMesh { MaterialName = "m", Positions = positions, Triangles = triangles }] };

    /// <summary>Carré de 100 unités dans le plan Z = 0 (2 triangles).</summary>
    public static ModelMesh FloorMesh() =>
        Mesh([new(0, 0, 0), new(100, 0, 0), new(100, 100, 0), new(0, 100, 0)], [0, 1, 2, 0, 2, 3]);

    public static SurfaceInput FloorSurface(string shader = "textures/kotor/floor", Vector2[]? uv1 = null, bool doubleSided = false) =>
        new(shader, [new(0, 0, 0), new(100, 0, 0), new(100, 100, 0), new(0, 100, 0)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [new(0, 0), new(1, 0), new(1, 1), new(0, 1)], uv1, [0, 1, 2, 0, 2, 3], doubleSided);

    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Entities(IReadOnlyDictionary<string, string>? world = null) =>
    [
        world ?? new Dictionary<string, string> { ["classname"] = "worldspawn" },
        new Dictionary<string, string> { ["classname"] = "info_player_start", ["origin"] = "5 5 70", ["angle"] = "0" },
        new Dictionary<string, string> { ["classname"] = "info_player_deathmatch", ["origin"] = "5 5 70", ["angle"] = "0" },
    ];

    public static CompileInput Input(IReadOnlyList<SurfaceInput>? surfaces = null, ModelMesh? solid = null, ModelMesh? walkable = null,
        string sky = "", IReadOnlyList<IReadOnlyDictionary<string, string>>? entities = null) =>
        new("Test", entities ?? Entities(), surfaces ?? [FloorSurface()], solid ?? FloorMesh(), walkable ?? new ModelMesh { Name = "w" },
            sky, VertexLighting.Default);
}
