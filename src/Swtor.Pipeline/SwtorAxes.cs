using System.Numerics;

namespace Swtor.Pipeline;

/// <summary>Convention de conversion des axes SWTOR (metres, Y vers le haut) vers ceux de la scene (metres, Z vers le haut).</summary>
public enum AxisConvention
{
    /// <summary>
    /// Rotation propre (determinant +1) : (x, y, z) -> (x, -z, y). Le monde SWTOR est suppose a main droite (comme le monde JA) :
    /// l'orientation du modele est conservee, l'ordre des sommets aussi.
    /// </summary>
    Rotation,

    /// <summary>
    /// Symetrie (determinant -1) : (x, y, z) -> (x, z, y). A utiliser si le monde SWTOR est a main gauche (D3D) : l'image n'est
    /// pas inversee mais l'ordre des sommets l'est (le builder retourne alors les triangles).
    /// </summary>
    Mirror,
}

/// <summary>Conversion des axes SWTOR (Y haut) vers la scene (Z haut).</summary>
public static class SwtorAxes
{
    /// <summary>Convention utilisee par defaut (voir le rapport T4 pour la validation).</summary>
    public const AxisConvention Default = AxisConvention.Rotation;

    public static Vector3 ToZUp(Vector3 v, AxisConvention c = Default) =>
        c == AxisConvention.Rotation ? new Vector3(v.X, -v.Z, v.Y) : new Vector3(v.X, v.Z, v.Y);

    /// <summary>Vrai si la conversion inverse l'orientation (determinant negatif) : l'ordre des sommets doit etre retourne.</summary>
    public static bool FlipsWinding(AxisConvention c = Default) => c == AxisConvention.Mirror;
}
