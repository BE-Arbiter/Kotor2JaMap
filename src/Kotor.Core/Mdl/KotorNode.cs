using System.Numerics;

namespace Kotor.Core.Mdl;

public sealed class KotorNode
{
    private readonly List<KotorNode> _children = new();

    public string Name { get; }
    /// <summary>Numero du noeud dans le MDL (en-tete du noeud, +4) : les animations designent les noeuds par ce numero, les noms pouvant se repeter (portes).</summary>
    public int Number { get; init; }
    public NodeKind Kind { get; }
    /// <summary>Relatif au parent (1re image cle du controleur position, sinon en-tete).</summary>
    public Vector3 Position { get; }
    /// <summary>Relatif au parent (controleur orientation 20, compresse ou non, sinon en-tete).</summary>
    public Quaternion Orientation { get; }
    /// <summary>Non nul si Kind contient Mesh (y compris Skin, Dangly, Aabb).</summary>
    public KotorMesh? Mesh { get; }
    /// <summary>Non nul si Kind contient Light.</summary>
    public KotorLight? Light { get; }
    public IReadOnlyList<KotorNode> Children => _children;

    public KotorNode(string name, NodeKind kind, Vector3 position, Quaternion orientation, KotorMesh? mesh, KotorLight? light = null)
    {
        Name = name; Kind = kind; Position = position; Orientation = orientation; Mesh = mesh; Light = light;
    }

    internal void AddChild(KotorNode child) => _children.Add(child);

    /// <summary>Pour les tests : construit une hierarchie sans lire de MDL.</summary>
    public void AddChildForTest(KotorNode child) => _children.Add(child);

    /// <summary>Transformation locale (System.Numerics, vecteurs ligne : rotation puis translation).</summary>
    public Matrix4x4 LocalTransform => Matrix4x4.CreateFromQuaternion(Orientation) * Matrix4x4.CreateTranslation(Position);
}
