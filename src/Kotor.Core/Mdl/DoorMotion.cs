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
    /// <summary>Au-dela, la porte garde l'ancien comportement (une boite qui monte) : trop de panneaux pour autant d'entites.</summary>
    public const int MaxPieces = 8;
    private static readonly string[] Names = ["opening1", "opening2", "open1", "open2"];

    /// <summary>
    /// Deduit le mouvement de l'animation d'ouverture du MDL (<c>opening1</c>, a defaut <c>opening2</c>). Dans une animation KOTOR, une position
    /// est un deplacement par rapport a celle du noeud (dor_lts02 : deux panneaux a +-3,19 m qui coulissent de +-3 m) et une orientation est absolue
    /// (l'animation <c>closed</c> reprend l'orientation de repos). JA ne fait pas tourner un <c>func_door</c> : un maillage est donc ramene a une
    /// translation, celle de son centre entre la pose fermee et la derniere pose de l'animation (un panneau qui pivote glisse sans tourner).
    /// Retourne null si le modele n'a pas d'animation d'ouverture, si aucun maillage ne bouge, ou s'il y a plus de <see cref="MaxPieces"/> groupes.
    /// </summary>
    public static DoorMotion? Analyze(KotorModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        KotorAnimation? anim = null;
        foreach (string n in Names)
            if ((anim = model.Animations.FirstOrDefault(a => a.Name == n && a.Length > 0 && a.Tracks.Count > 0)) is not null) break;
        if (anim is null) return null;

        var tracks = anim.Tracks.GroupBy(t => t.NodeNumber).ToDictionary(g => g.Key, g => g.First());
        var groups = new List<(Vector3 Offset, List<KotorNode> Nodes)>();
        var fixedNodes = new List<KotorNode>();

        void Visit(KotorNode node, Matrix4x4 restParent, Matrix4x4 finalParent)
        {
            Vector3 pos = node.Position;
            Quaternion rot = node.Orientation;
            if (tracks.TryGetValue(node.Number, out AnimationTrack? t))
            {
                if (t.Positions.Count > 1) pos += t.Positions[^1].Value - t.Positions[0].Value;
                if (t.Orientations.Count > 0) rot = Quaternion.Normalize(t.Orientations[^1].Value);
            }
            Matrix4x4 rest = node.LocalTransform * restParent;
            Matrix4x4 end = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos) * finalParent;
            if (node.Mesh is { Render: true } mesh && !node.Kind.HasFlag(NodeKind.Aabb) && mesh.Positions.Length > 0)
            {
                Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
                foreach (Vector3 v in mesh.Positions) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
                Vector3 center = (lo + hi) * 0.5f;
                Vector3 offset = Vector3.Transform(center, end) - Vector3.Transform(center, rest);
                if (offset.Length() < MinOffset) fixedNodes.Add(node);
                else
                {
                    int g = groups.FindIndex(x => Vector3.Distance(x.Offset, offset) < MinOffset);
                    if (g < 0) groups.Add((offset, [node])); else groups[g].Nodes.Add(node);
                }
            }
            foreach (KotorNode child in node.Children) Visit(child, rest, end);
        }
        Visit(model.Root, Matrix4x4.Identity, Matrix4x4.Identity);
        if (groups.Count == 0 || groups.Count > MaxPieces) return null;
        return new DoorMotion(anim.Length, [.. groups.Select(g => new DoorPieceSpec(g.Nodes, g.Offset))], fixedNodes);
    }
}
