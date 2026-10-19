using System.Globalization;
using System.Numerics;

namespace Kotor.Core.Export;

/// <summary>Placement d'un <c>misc_model_static</c> : cles <c>origin</c>, <c>angles</c> ("pitch yaw roll", degres) et <c>modelscale_vec</c>.</summary>
public sealed record MiscModelPlacement(Vector3 Origin, Vector3 Angles, Vector3 Scale)
{
    public string OriginKey => Vec(Origin);
    public string AnglesKey => Vec(Angles);
    public string ScaleKey => Vec(Scale);

    private static string Vec(Vector3 v) =>
        string.Create(CultureInfo.InvariantCulture, $"{Num(v.X)} {Num(v.Y)} {Num(v.Z)}");

    private static string Num(float f)
    {
        float r = MathF.Round(f, 4);
        return (r == 0f ? 0f : r).ToString("0.####", CultureInfo.InvariantCulture); // pas de "-0"
    }
}

/// <summary>
/// Conversion d'une matrice affine (convention System.Numerics : p' = p * M) en placement de <c>misc_model_static</c>.
/// Le moteur place un sommet u en origin + u.x * axis[0] * scale.x + u.y * axis[1] * scale.y + u.z * axis[2] * scale.z, avec
/// axis = AnglesToAxis(angles) : axis[0] = forward, axis[1] = -right, axis[2] = up d'AngleVectors (OpenJK-SWGL shared/qcommon/q_math.c:1021, 1325 ;
/// CG_DrawMiscEnts code/cgame/cg_main.cpp:2259, ScaleModelAxis code/cgame/cg_ents.cpp:290).
/// </summary>
public static class MiscModelTransform
{
    /// <summary>Ecart relatif tolere entre la matrice et sa reconstruction (rotation x echelle par axe).</summary>
    public const float Tolerance = 1e-3f;

    /// <summary>
    /// Placement equivalent a <paramref name="m"/>, ou null si la matrice ne s'exprime pas en rotation x echelle par axe positive
    /// (miroir, cisaillement, echelle nulle) : la geometrie doit alors rester dans le BSP.
    /// </summary>
    public static MiscModelPlacement? FromMatrix(Matrix4x4 m)
    {
        Vector3 ix = new(m.M11, m.M12, m.M13), iy = new(m.M21, m.M22, m.M23), iz = new(m.M31, m.M32, m.M33);
        float sx = ix.Length(), sy = iy.Length(), sz = iz.Length();
        if (sx < 1e-6f || sy < 1e-6f || sz < 1e-6f) return null;
        Vector3 fwd = ix / sx, left = iy / sy, up = iz / sz;
        // Repere orthonorme direct (forward x left = up) ; sinon miroir ou cisaillement.
        if (MathF.Abs(Vector3.Dot(fwd, left)) > Tolerance || MathF.Abs(Vector3.Dot(fwd, up)) > Tolerance || MathF.Abs(Vector3.Dot(left, up)) > Tolerance) return null;
        if (Vector3.Dot(Vector3.Cross(fwd, left), up) < 0f) return null;

        Vector3 angles = AxisToAngles(fwd, left, up);
        var (f2, l2, u2) = AnglesToAxis(angles);
        if ((f2 - fwd).Length() > Tolerance * 10 || (l2 - left).Length() > Tolerance * 10 || (u2 - up).Length() > Tolerance * 10) return null;
        return new MiscModelPlacement(m.Translation, angles, new Vector3(sx, sy, sz));
    }

    /// <summary>Angles JA (pitch, yaw, roll en degres) d'un repere orthonorme direct.</summary>
    public static Vector3 AxisToAngles(Vector3 fwd, Vector3 left, Vector3 up)
    {
        // forward = (cp cy, cp sy, -sp) ; left = -right : left.z = sr cp ; up.z = cr cp.
        float sp = Math.Clamp(-fwd.Z, -1f, 1f);
        float pitch = MathF.Asin(sp);
        float cp = MathF.Cos(pitch);
        float yaw, roll;
        if (cp > 1e-4f)
        {
            yaw = MathF.Atan2(fwd.Y, fwd.X);
            roll = MathF.Atan2(left.Z, up.Z);
        }
        else
        {
            // Visee verticale : roll et yaw se confondent ; roll = 0, left = (-sy, cy, 0).
            roll = 0f;
            yaw = MathF.Atan2(-left.X, left.Y);
        }
        const float Deg = 180f / MathF.PI;
        return new Vector3(pitch * Deg, yaw * Deg, roll * Deg);
    }

    /// <summary>AnglesToAxis du moteur : (forward, left, up).</summary>
    public static (Vector3 Forward, Vector3 Left, Vector3 Up) AnglesToAxis(Vector3 angles)
    {
        const float Rad = MathF.PI / 180f;
        float sp = MathF.Sin(angles.X * Rad), cp = MathF.Cos(angles.X * Rad);
        float sy = MathF.Sin(angles.Y * Rad), cy = MathF.Cos(angles.Y * Rad);
        float sr = MathF.Sin(angles.Z * Rad), cr = MathF.Cos(angles.Z * Rad);
        var forward = new Vector3(cp * cy, cp * sy, -sp);
        var right = new Vector3(-sr * sp * cy + cr * sy, -sr * sp * sy - cr * cy, -sr * cp);
        var up = new Vector3(cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp);
        return (forward, -right, up);
    }

    /// <summary>Position monde d'un sommet du modele telle que le moteur la calcule.</summary>
    public static Vector3 Place(MiscModelPlacement p, Vector3 local)
    {
        var (f, l, u) = AnglesToAxis(p.Angles);
        return p.Origin + local.X * p.Scale.X * f + local.Y * p.Scale.Y * l + local.Z * p.Scale.Z * u;
    }
}
