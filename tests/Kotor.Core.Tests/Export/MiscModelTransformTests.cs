using System.Numerics;
using Kotor.Core.Export;

namespace Kotor.Core.Tests.Export;

public class MiscModelTransformTests
{
    private static readonly Vector3[] Probes = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new(3, -7, 11), new(-50, 20, 5)];

    private static void AssertSamePlacement(Matrix4x4 m, MiscModelPlacement p)
    {
        foreach (Vector3 v in Probes)
        {
            Vector3 expected = Vector3.Transform(v, m), actual = MiscModelTransform.Place(p, v);
            Assert.True((expected - actual).Length() < 1e-2f * MathF.Max(1f, expected.Length()), $"{v}: {expected} vs {actual} (angles {p.Angles}, scale {p.Scale})");
        }
    }

    [Fact]
    public void Random_rotations_scales_and_translations_round_trip_through_the_engine_formula()
    {
        var rng = new Random(1234);
        for (int i = 0; i < 2000; i++)
        {
            var axis = Vector3.Normalize(new Vector3(rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1) + new Vector3(1e-3f));
            var q = Quaternion.CreateFromAxisAngle(axis, rng.NextSingle() * MathF.Tau);
            var scale = new Vector3(0.2f + rng.NextSingle() * 5, 0.2f + rng.NextSingle() * 5, 0.2f + rng.NextSingle() * 5);
            var t = new Vector3(rng.NextSingle() * 20000 - 10000, rng.NextSingle() * 20000 - 10000, rng.NextSingle() * 2000);
            Matrix4x4 m = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(t);

            MiscModelPlacement? p = MiscModelTransform.FromMatrix(m);
            Assert.NotNull(p);
            AssertSamePlacement(m, p);
        }
    }

    [Theory]
    [InlineData(90f)]
    [InlineData(-90f)]
    public void Straight_up_or_down_pitch_is_handled(float pitchDeg)
    {
        // Pitch JA positif = regard vers le bas : rotation autour de +Y.
        Matrix4x4 m = Matrix4x4.CreateRotationY(pitchDeg * MathF.PI / 180f) * Matrix4x4.CreateRotationZ(0.7f) * Matrix4x4.CreateTranslation(10, 20, 30);
        MiscModelPlacement? p = MiscModelTransform.FromMatrix(m);
        Assert.NotNull(p);
        AssertSamePlacement(m, p);
    }

    [Fact]
    public void Pure_yaw_gives_engine_angles()
    {
        MiscModelPlacement? p = MiscModelTransform.FromMatrix(Matrix4x4.CreateRotationZ(MathF.PI / 2) * Matrix4x4.CreateTranslation(1, 2, 3));
        Assert.NotNull(p);
        Assert.Equal("0 90 0", p.AnglesKey);
        Assert.Equal("1 2 3", p.OriginKey);
        Assert.Equal("1 1 1", p.ScaleKey);
    }

    [Fact]
    public void Mirrored_or_sheared_matrices_stay_in_the_bsp()
    {
        Assert.Null(MiscModelTransform.FromMatrix(Matrix4x4.CreateScale(-1, 1, 1)));
        var shear = Matrix4x4.Identity;
        shear.M21 = 0.5f;
        Assert.Null(MiscModelTransform.FromMatrix(shear));
        Assert.Null(MiscModelTransform.FromMatrix(Matrix4x4.CreateScale(0, 1, 1)));
    }
}
