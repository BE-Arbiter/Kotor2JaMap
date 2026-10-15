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
    /// <summary>Ecart de surface (m²) en dessous duquel les deux poses sont a egalite (erreurs d'arrondi de la rotation).</summary>
    private const float TieToleranceSquareMeters = 0.01f;

    /// <summary>
    /// Place les parties reliees a <paramref name="referencePart"/>. Le yaw d'une porte KOTOR est ambigu a 180° (il ne dit pas de
    /// quel cote sont les pieces) : pour chaque liaison, deux poses gardant les portes confondues sont essayees, theta (dos a dos,
    /// <see cref="RigidTransform.AligningDoors"/>) et theta + 180. La pose retenue est celle dont la surface de recouvrement
    /// (<see cref="OverlapChecker.OverlapArea"/>) avec les parties deja placees est la plus faible ; a egalite, theta.
    /// Retournement : <c>ASSEMBLY_FLIPPED</c>.
    /// </summary>
    public static Placement Place(IReadOnlyList<AssemblyPart> parts, int referencePart, IReadOnlyList<DoorLink> links, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(log);
        if (referencePart < 0 || referencePart >= parts.Count) throw new ArgumentOutOfRangeException(nameof(referencePart));
        var transforms = Enumerable.Repeat(RigidTransform.Identity, parts.Count).ToArray();
        // Sols seuls (walkmeshes) de chaque partie, et sols des parties deja placees dans le repere assemble.
        var floors = parts.Select(p => p.Scene with { Meshes = [], Lights = [] }).ToArray();
        var placedFloors = new ModuleScene?[parts.Count];
        placedFloors[referencePart] = floors[referencePart];
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
                Vector3 doorPos = t.Apply(anchorCur.Position);
                float doorYaw = t.ApplyYaw(anchorCur.Yaw);
                var backToBack = RigidTransform.AligningDoors(doorPos, doorYaw, anchorOther.Position, anchorOther.Yaw);
                var flipped = RigidTransform.AligningDoors(doorPos, doorYaw + 180f, anchorOther.Position, anchorOther.Yaw);
                var floorBack = floors[other].Transformed(backToBack);
                var floorFlip = floors[other].Transformed(flipped);
                // Nettement inferieur : a egalite (aucun sol, recouvrement identique au bruit de la rotation pres), la regle dos a dos est gardee.
                if (OverlapWithPlaced(floorFlip, placedFloors) < OverlapWithPlaced(floorBack, placedFloors) - TieToleranceSquareMeters)
                {
                    transforms[other] = flipped;
                    placedFloors[other] = floorFlip;
                    log.Warn("ASSEMBLY_FLIPPED", $"{Name(parts, cur)} -> {Name(parts, other)}: the back-to-back pose overlaps the placed modules, the module is turned by 180 degrees around the door.");
                }
                else
                {
                    transforms[other] = backToBack;
                    placedFloors[other] = floorBack;
                }
                placed[other] = true;
                used.Add(link);
                queue.Enqueue(other);
            }
        }
        return new Placement(transforms, used, UnlinkedGroups(parts.Count, placed, links));
    }

    private static string Name(IReadOnlyList<AssemblyPart> parts, int i) => parts[i].Scene.Info.Module;

    /// <summary>Surface de recouvrement (m²) d'un sol candidat avec les sols des parties deja placees.</summary>
    private static float OverlapWithPlaced(ModuleScene candidate, ModuleScene?[] placedFloors) =>
        placedFloors.Where(f => f is not null).Sum(f => OverlapChecker.OverlapArea(f!, candidate));

    /// <summary>Porte de la partie la plus proche d'un point (null si la partie n'a pas de porte).</summary>
    internal static ModuleEntity? NearestDoor(AssemblyPart part, Vector3 position) =>
        part.Entities.Where(e => e.Kind == EntityKind.Door)
            .OrderBy(e => Vector3.DistanceSquared(e.Position, position)).FirstOrDefault();

    /// <summary>Un waypoint est remplace par la porte la plus proche de sa partie ; sans porte, le waypoint sert d'ancre.</summary>
    private static ModuleEntity Anchor(AssemblyPart part, ModuleEntity target, ImportLog log)
    {
        if (target.Kind != EntityKind.Waypoint) return target;
        var door = NearestDoor(part, target.Position);
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
