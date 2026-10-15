using System.Numerics;

namespace Kotor.Core.Mdl;

/// <summary>Pieces d'une porte qui bougent ensemble : noeuds a maillage (references) et deplacement final en metres, repere du modele.</summary>
public sealed record DoorPieceSpec(IReadOnlyList<KotorNode> Nodes, Vector3 Offset);

/// <summary>
/// Mouvement d'ouverture d'une porte : <see cref="Moving"/> = groupes de maillages qui se translatent du meme vecteur
/// (<see cref="Duration"/> secondes), <see cref="Fixed"/> = maillages qui ne bougent pas (cadre).
/// </summary>
public sealed record DoorMotion(float Duration, IReadOnlyList<DoorPieceSpec> Moving, IReadOnlyList<KotorNode> Fixed)
{
    /// <summary>Deplacement minimal (m) pour qu'un groupe compte comme mobile.</summary>
    public const float MinOffset = 0.02f;
    /// <summary>Rotation d'une cle a l'autre (degres) au-dela de laquelle l'animation n'est pas une translation.</summary>
    public const float MaxRotation = 5f;
    private static readonly string[] Names = ["opening1", "opening2", "open1", "open2"];

    /// <summary>
    /// Deduit le mouvement de l'animation d'ouverture du MDL (<c>opening1</c>, a defaut <c>opening2</c>). Les positions d'une animation
    /// KOTOR sont des deplacements par rapport a la position du noeud (verifie sur dor_lts02 : deux panneaux a +-3,19 m qui coulissent de +-3 m).
    /// Retourne null si le modele n'a pas d'animation d'ouverture, si elle fait tourner un noeud, ou si aucun maillage ne bouge.
    /// </summary>
    public static DoorMotion? Analyze(KotorModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        KotorAnimation? anim = null;
        foreach (string n in Names)
            if ((anim = model.Animations.FirstOrDefault(a => a.Name == n && a.Length > 0 && a.Tracks.Count > 0)) is not null) break;
        if (anim is null) return null;

        var delta = new Dictionary<int, Vector3>();
        foreach (AnimationTrack t in anim.Tracks)
        {
            if (t.Orientations.Count > 1)
            {
                Quaternion a = t.Orientations[0].Value, b = t.Orientations[^1].Value;
                float dot = MathF.Min(1f, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))));
                if (2f * MathF.Acos(dot) * 180f / MathF.PI > MaxRotation) return null;
            }
            if (t.Positions.Count > 1) delta[t.NodeNumber] = t.Positions[^1].Value - t.Positions[0].Value;
        }

        var groups = new List<(Vector3 Offset, List<KotorNode> Nodes)>();
        var fixedNodes = new List<KotorNode>();
        void Visit(KotorNode node, Matrix4x4 parentTransform, Vector3 parentOffset)
        {
            Vector3 offset = parentOffset;
            if (delta.TryGetValue(node.Number, out Vector3 d)) offset += Vector3.TransformNormal(d, parentTransform);
            if (node.Mesh is { Render: true } && !node.Kind.HasFlag(NodeKind.Aabb))
            {
                if (offset.Length() < MinOffset) fixedNodes.Add(node);
                else
                {
                    int g = groups.FindIndex(x => Vector3.Distance(x.Offset, offset) < MinOffset);
                    if (g < 0) groups.Add((offset, [node])); else groups[g].Nodes.Add(node);
                }
            }
            Matrix4x4 t = node.LocalTransform * parentTransform;
            foreach (KotorNode child in node.Children) Visit(child, t, offset);
        }
        Visit(model.Root, Matrix4x4.Identity, Vector3.Zero);
        if (groups.Count == 0) return null;
        return new DoorMotion(anim.Length, [.. groups.Select(g => new DoorPieceSpec(g.Nodes, g.Offset))], fixedNodes);
    }
}
