using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Scene;

namespace Kotor.Pipeline.Assembly;

/// <summary>Transformations de chaque partie, transitions retenues et groupes de parties non reliees a la reference.</summary>
public sealed record Placement(IReadOnlyList<RigidTransform> Transforms, IReadOnlyList<DoorLink> Used, IReadOnlyList<IReadOnlyList<int>> UnlinkedGroups);

/// <summary>Placement automatique : la reference reste a l'identite, les autres parties sont alignees porte contre porte (parcours en largeur).</summary>
public static class AutoPlacer
{
    public static Placement Place(IReadOnlyList<AssemblyPart> parts, int referencePart, IReadOnlyList<DoorLink> links, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(log);
        if (referencePart < 0 || referencePart >= parts.Count) throw new ArgumentOutOfRangeException(nameof(referencePart));
        var transforms = Enumerable.Repeat(RigidTransform.Identity, parts.Count).ToArray();
        var placed = new bool[parts.Count];
        var handled = new HashSet<DoorLink>();
        var used = new List<DoorLink>();
        placed[referencePart] = true;
        var queue = new Queue<int>();
        queue.Enqueue(referencePart);
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            foreach (var link in links)
            {
                if (handled.Contains(link)) continue;
                bool forward = link.FromPart == cur;
                if (!forward && link.ToPart != cur) continue;
                handled.Add(link);
                int other = forward ? link.ToPart : link.FromPart;
                if (placed[other])
                {
                    // Les deux parties sont deja placees par d'autres liens : cette arete fermerait une boucle.
                    log.Warn("LINK_EXTRA_EDGE", $"Link {Name(parts, link.FromPart)} -> {Name(parts, link.ToPart)} closes a loop and is ignored.");
                    continue;
                }
                var anchorCur = Anchor(parts[cur], forward ? link.FromDoor : link.ToTarget, log);
                var anchorOther = Anchor(parts[other], forward ? link.ToTarget : link.FromDoor, log);
                var t = transforms[cur];
                transforms[other] = RigidTransform.AligningDoors(t.Apply(anchorCur.Position), t.ApplyYaw(anchorCur.Yaw), anchorOther.Position, anchorOther.Yaw);
                placed[other] = true;
                used.Add(link);
                queue.Enqueue(other);
            }
        }
        return new Placement(transforms, used, UnlinkedGroups(parts.Count, placed, links));
    }

    private static string Name(IReadOnlyList<AssemblyPart> parts, int i) => parts[i].Scene.Info.Module;

    /// <summary>Un waypoint est remplace par la porte la plus proche de sa partie ; sans porte, le waypoint sert d'ancre.</summary>
    private static ModuleEntity Anchor(AssemblyPart part, ModuleEntity target, ImportLog log)
    {
        if (target.Kind != EntityKind.Waypoint) return target;
        var door = part.Entities.Where(e => e.Kind == EntityKind.Door)
            .OrderBy(e => Vector3.DistanceSquared(e.Position, target.Position)).FirstOrDefault();
        if (door is not null) return door;
        log.Warn("LINK_NO_TARGET_DOOR", $"{part.Scene.Info.Module}: no door near waypoint '{target.Name}', the waypoint position is used as the anchor.");
        return target;
    }

    private static List<IReadOnlyList<int>> UnlinkedGroups(int count, bool[] placed, IReadOnlyList<DoorLink> links)
    {
        var groups = new List<IReadOnlyList<int>>();
        var seen = (bool[])placed.Clone();
        for (int s = 0; s < count; s++)
        {
            if (seen[s]) continue;
            var group = new List<int>();
            var stack = new Stack<int>();
            stack.Push(s);
            seen[s] = true;
            while (stack.Count > 0)
            {
                int n = stack.Pop();
                group.Add(n);
                foreach (var l in links)
                {
                    int o = l.FromPart == n ? l.ToPart : l.ToPart == n ? l.FromPart : -1;
                    if (o >= 0 && !seen[o]) { seen[o] = true; stack.Push(o); }
                }
            }
            group.Sort();
            groups.Add(group);
        }
        return groups;
    }
}
