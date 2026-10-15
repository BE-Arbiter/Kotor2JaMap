using System.Numerics;
using Kotor.Core.Walkmesh;
using Kotor.Pipeline.Entities;
using Xunit;

namespace Kotor.Pipeline.Tests;

public sealed class PlaceableCollisionTests
{
    private static Walkmesh Square(float z = 0f) =>
        new([new(-1, -1, z), new(1, -1, z), new(1, 1, z), new(-1, 1, z)], [0, 1, 2, 0, 2, 3], [0, 0]);

    [Fact]
    public void Flat_footprint_is_extruded_to_the_model_height()
    {
        var tris = PlaceableConverter.CollisionTriangles(Square(), 1f);
        // 2 triangles de sol + 4 aretes de bord x 2 triangles + 2 triangles de couvercle (la diagonale est interne : pas de mur).
        Assert.Equal(12, tris.Count);
        Assert.Equal(1f, tris.Max(t => MathF.Max(t.A.Z, MathF.Max(t.B.Z, t.C.Z))));
        // Chaque mur est vertical : normale sans composante Z.
        foreach (var (a, b, c) in tris.Skip(2).Take(8))
            Assert.True(MathF.Abs(Vector3.Cross(b - a, c - a).Z) < 1e-4f);
    }

    [Fact]
    public void Slightly_raised_footprint_still_counts_as_flat()
    {
        Assert.Equal(12, PlaceableConverter.CollisionTriangles(Square(0.003f), 1f).Count);
    }

    [Fact]
    public void Volume_pwk_or_very_low_model_is_unchanged()
    {
        var volume = new Walkmesh([new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)], [0, 1, 2], [0]);
        Assert.Single(PlaceableConverter.CollisionTriangles(volume, 1f));
        Assert.Equal(2, PlaceableConverter.CollisionTriangles(Square(), 0.02f).Count);
        Assert.Equal(2, PlaceableConverter.CollisionTriangles(Square(), float.NaN).Count);
    }
}
