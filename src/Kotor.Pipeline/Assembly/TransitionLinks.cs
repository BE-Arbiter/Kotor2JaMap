using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;

namespace Kotor.Pipeline.Assembly;

/// <summary>Decouvre les transitions entre les modules selectionnes (LinkedToModule / LinkedTo des portes du GIT).</summary>
public static class TransitionLinks
{
    /// <summary>
    /// Une entree par paire de parties (la premiere trouvee). Transition vers un module non selectionne : ignoree sans bruit.
    /// Cible introuvable : LINK_TARGET_NOT_FOUND. Paire deja reliee par une autre porte : LINK_EXTRA_EDGE
    /// (la porte reciproque d'une transition retenue est ignoree sans bruit).
    /// </summary>
    public static IReadOnlyList<DoorLink> Find(IReadOnlyList<AssemblyPart> parts, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(log);
        var links = new List<DoorLink>();
        var pairs = new HashSet<(int, int)>();
        for (int i = 0; i < parts.Count; i++)
        {
            foreach (var door in parts[i].Entities.Where(e => e.Kind == EntityKind.Door && e.LinkedToModule.Length > 0))
            {
                int j = IndexOfModule(parts, door.LinkedToModule);
                if (j < 0 || j == i) continue;
                var target = door.LinkedTo.Length == 0 ? null : parts[j].Entities.FirstOrDefault(e => e.Kind is EntityKind.Door or EntityKind.Waypoint
                    && string.Equals(e.Name, door.LinkedTo, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    log.Warn("LINK_TARGET_NOT_FOUND", $"Door '{door.Name}' of {parts[i].Scene.Info.Module} targets '{door.LinkedTo}' in {parts[j].Scene.Info.Module}, which was not found.");
                    continue;
                }
                if (!pairs.Add((Math.Min(i, j), Math.Max(i, j))))
                {
                    // Porte reciproque de la transition deja retenue (cas normal du jeu) : pas une arete en surplus.
                    if (links.Any(l => l.FromPart == j && l.ToPart == i && ReferenceEquals(l.FromDoor, target))) continue;
                    log.Warn("LINK_EXTRA_EDGE", $"Door '{door.Name}' of {parts[i].Scene.Info.Module} to {parts[j].Scene.Info.Module}: modules already linked, transition ignored.");
                    continue;
                }
                links.Add(new DoorLink(i, door, j, target));
            }
        }
        return links;
    }

    private static int IndexOfModule(IReadOnlyList<AssemblyPart> parts, string module)
    {
        for (int i = 0; i < parts.Count; i++)
            if (string.Equals(parts[i].Scene.Info.Module, module, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
