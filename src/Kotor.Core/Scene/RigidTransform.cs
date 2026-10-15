using System.Numerics;

namespace Kotor.Core.Scene;

/// <summary>
/// Transformation rigide d'une scene : rotation d'un yaw (degres, autour de Z, anti-horaire) puis translation (metres KOTOR).
/// </summary>
public readonly record struct RigidTransform(float YawDegrees, Vector3 Translation)
{
    public static RigidTransform Identity => new(0f, Vector3.Zero);

    /// <summary>Rotation seule d'un vecteur (directions, normales).</summary>
    public Vector3 ApplyDirection(Vector3 d)
    {
        float r = YawDegrees * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
        return new Vector3(c * d.X - s * d.Y, s * d.X + c * d.Y, d.Z);
    }

    /// <summary>Rotation Z puis translation d'un point.</summary>
    public Vector3 Apply(Vector3 p) => ApplyDirection(p) + Translation;

    /// <summary>Yaw d'une entite apres transformation, normalise dans ]-180, 180].</summary>
    public float ApplyYaw(float yawDegrees) => Normalize(yawDegrees + YawDegrees);

    /// <summary>Compose : applique d'abord this, puis <paramref name="next"/>.</summary>
    public RigidTransform Then(RigidTransform next) =>
        new(Normalize(YawDegrees + next.YawDegrees), next.ApplyDirection(Translation) + next.Translation);

    /// <summary>
    /// Transformation a appliquer au module de B pour que sa porte (posB, yawB) coincide avec celle de A (posA, yawA), dos a dos :
    /// theta = yawA + 180 - yawB, translation = posA - R(theta) posB.
    /// </summary>
    public static RigidTransform AligningDoors(Vector3 posA, float yawA, Vector3 posB, float yawB)
    {
        var rot = new RigidTransform(Normalize(yawA + 180f - yawB), Vector3.Zero);
        return rot with { Translation = posA - rot.ApplyDirection(posB) };
    }

    private static float Normalize(float deg)
    {
        deg %= 360f;
        if (deg > 180f) deg -= 360f;
        else if (deg <= -180f) deg += 360f;
        return deg;
    }
}
