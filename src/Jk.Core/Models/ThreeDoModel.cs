using System.Numerics;

namespace Jk.Core.Models;

/// <summary>Un sommet d'une face : indice de sommet et indice de sommet de texture (-1 = aucun).</summary>
public readonly record struct ThreeDoCorner(int Vertex, int TexVertex);

/// <summary>
/// Une face d'un maillage 3DO. <see cref="Type"/> : drapeaux de face (0x1 double face, 0x2 translucide, voir jk-formats.md).
/// <see cref="Geo"/> : mode de geometrie (0 non dessinee, 1 points, 2 fil de fer, 3 couleur pleine, 4 texturee).
/// </summary>
public sealed record ThreeDoFace(int Material, uint Type, int Geo, int Light, int Tex, float ExtraLight, ThreeDoCorner[] Corners);

/// <summary>Un maillage : sommets, coordonnees de texture (en pixels), normales de sommets et de faces, faces.</summary>
public sealed record ThreeDoMesh(string Name, float Radius, int GeometryMode, int LightingMode, int TextureMode,
    Vector3[] Vertices, float[] Intensities, Vector2[] TexVertices, Vector3[] VertexNormals, ThreeDoFace[] Faces, Vector3[] FaceNormals);

/// <summary>Niveau de detail : liste de maillages (le geoset 0 est le plus detaille).</summary>
public sealed record ThreeDoGeoset(IReadOnlyList<ThreeDoMesh> Meshes);

/// <summary>
/// Noeud de la hierarchie : <see cref="Mesh"/> (-1 = aucun), <see cref="Parent"/> (-1 = racine), position et rotation (pitch, yaw, roll en degres)
/// relatives au parent, pivot.
/// </summary>
public sealed record ThreeDoNode(int Index, uint Flags, uint Type, int Mesh, int Parent, int Child, int Sibling, int NumChildren,
    Vector3 Position, Vector3 PitchYawRoll, Vector3 Pivot, string Name);

/// <summary>Modele 3DO lu : materiaux (noms de fichiers .mat), geosets, noeuds.</summary>
public sealed class ThreeDoModel
{
    public required IReadOnlyList<string> Materials { get; init; }
    public required IReadOnlyList<ThreeDoGeoset> Geosets { get; init; }
    public required IReadOnlyList<ThreeDoNode> Nodes { get; init; }
    public float Radius { get; init; }
    public Vector3 InsertOffset { get; init; }

    /// <summary>
    /// Matrice monde du REPERE de chaque noeud (convention System.Numerics : vecteurs ligne, <c>v * M</c>). Noeud = rotation puis translation, composee
    /// avec le parent : <c>M = R(pyr) * T(pos) * M_parent</c>. Le pivot n est PAS dans cette matrice : les sommets d un maillage sont decales de
    /// <see cref="ThreeDoNode.Pivot"/> dans le repere du noeud (sommet monde = <c>(v + pivot) * M</c>, mesure sur les donnees, voir jk-formats.md).
    /// <paramref name="pose"/> remplace (position, pitch/yaw/roll) d'un noeud (animation) : parametres = indice du noeud et pose de repos.
    /// </summary>
    public Matrix4x4[] NodeMatrices(Func<int, (Vector3 Position, Vector3 PitchYawRoll), (Vector3 Position, Vector3 PitchYawRoll)>? pose = null)
    {
        var world = new Matrix4x4[Nodes.Count];
        var done = new bool[Nodes.Count];
        for (int i = 0; i < Nodes.Count; i++) Resolve(i, 0);
        return world;

        Matrix4x4 Resolve(int i, int depth)
        {
            if (done[i]) return world[i];
            if (depth > Nodes.Count) throw new InvalidDataException("3DO hierarchy has a cycle.");
            ThreeDoNode n = Nodes[i];
            var (pos, pyr) = (n.Position, n.PitchYawRoll);
            if (pose is not null) (pos, pyr) = pose(i, (pos, pyr));
            Matrix4x4 local = Rotation(pyr) * Matrix4x4.CreateTranslation(pos);
            world[i] = n.Parent >= 0 && n.Parent < Nodes.Count ? local * Resolve(n.Parent, depth + 1) : local;
            done[i] = true;
            return world[i];
        }
    }

    /// <summary>
    /// Rotation JK (pitch, yaw, roll en degres ; Z vers le haut) : roll autour de Y, puis pitch autour de X, puis yaw autour de Z, sens
    /// trigonometrique (un lacet de 90 degres envoie +X sur +Y). Memes angles que ceux des things du .jkl.
    /// </summary>
    public static Matrix4x4 Rotation(Vector3 pitchYawRoll)
    {
        const float ToRad = MathF.PI / 180f;
        return Matrix4x4.CreateRotationY(pitchYawRoll.Z * ToRad) * Matrix4x4.CreateRotationX(pitchYawRoll.X * ToRad)
            * Matrix4x4.CreateRotationZ(pitchYawRoll.Y * ToRad);
    }
}
