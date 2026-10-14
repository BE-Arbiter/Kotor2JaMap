using System.Numerics;

namespace Kotor.Core.Mdl;

public sealed record KotorModel(string Name, string? SuperModel, KotorNode Root, Vector3 BBoxMin, Vector3 BBoxMax)
{
    /// <summary>Parcours en profondeur ; la transformation est cumulee de la racine au noeud (inclus).</summary>
    public IEnumerable<(KotorNode Node, Matrix4x4 ModelTransform)> Walk()
    {
        var stack = new Stack<(KotorNode, Matrix4x4)>();
        stack.Push((Root, Root.LocalTransform));
        while (stack.Count > 0)
        {
            var (n, t) = stack.Pop();
            yield return (n, t);
            for (int i = n.Children.Count - 1; i >= 0; i--)
                stack.Push((n.Children[i], n.Children[i].LocalTransform * t));
        }
    }
}
