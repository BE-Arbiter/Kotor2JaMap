using Kotor.Core.Diagnostics;
using Swtor.Core.Areas;

namespace Swtor.Pipeline;

/// <summary>
/// Visibilite entre rooms d'une zone SWTOR, deduite des portails (<c>\engine\portal.p</c>) et de la liste des rooms liees des fichiers de room.
/// Un portail est un objet de la room source dont le champ texte <see cref="PortalTargetHash"/> vaut <c>"&lt;id u64 de la room cible&gt; &lt;nom de la room cible&gt;"</c>
/// (Haute : 28 256 portails sur 28 371 ont une cible, toujours de cette forme). Les portails vont par paires (un de chaque cote de la porte).
/// Resultat conservateur (on ne cache jamais ce qui pourrait se voir) : une room voit elle-meme, les rooms atteintes en
/// <see cref="PortalHops"/> sauts de portail (graphe non oriente), et les rooms de sa liste de rooms liees (dans les deux sens).
/// </summary>
public static class SwtorVisibility
{
    /// <summary>Champ texte d'un portail : id et nom de la room cible.</summary>
    public const uint PortalTargetHash = 0xB04139FD;

    /// <summary>Nombre de sauts de portail suivis. 2 : une room voit les rooms voisines et les voisines de ses voisines (couloirs, portes ouvertes).</summary>
    public const int PortalHops = 2;

    /// <summary>Nom de la room cible d'un portail ; null si l'objet n'est pas un portail ou n'a pas de cible.</summary>
    public static string? PortalTarget(RoomInstance portal)
    {
        if (portal.Kind != InstanceKind.Portal) return null;
        string? s = portal.GetString(PortalTargetHash);
        if (s is null) return null;
        int sp = s.IndexOf(' ');
        string name = (sp >= 0 ? s[(sp + 1)..] : s).Trim();
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// Rooms visibles depuis chaque room exportee. Une room sans portail ni room liee n'a pas d'entree : elle voit tout (repli).
    /// Les rooms hors de <paramref name="exported"/> servent au parcours du graphe mais ne figurent pas dans le resultat.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Build(AreaLayout layout, IEnumerable<string> exported, ImportLog log)
    {
        var exportedSet = new HashSet<string>(exported, StringComparer.OrdinalIgnoreCase);
        var names = layout.Rooms.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var portal = names.ToDictionary(n => n, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var linked = names.ToDictionary(n => n, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        int portals = 0;
        foreach (var (room, dat) in layout.Rooms)
        {
            foreach (RoomInstance i in dat.Instances)
            {
                if (i.Kind != InstanceKind.Portal) continue;
                portals++;
                if (PortalTarget(i) is { } t && names.Contains(t) && !t.Equals(room, StringComparison.OrdinalIgnoreCase))
                {
                    portal[room].Add(t);
                    portal[t].Add(room);
                }
            }
            foreach (string l in dat.LinkedRooms)
                if (names.Contains(l) && !l.Equals(room, StringComparison.OrdinalIgnoreCase)) { linked[room].Add(l); linked[l].Add(room); }
        }

        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        int fallback = 0;
        foreach (string room in exportedSet)
        {
            if (!names.Contains(room)) continue;
            if (portal[room].Count == 0 && linked[room].Count == 0) { fallback++; continue; }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { room };
            var frontier = new List<string> { room };
            for (int hop = 0; hop < PortalHops && frontier.Count > 0; hop++)
            {
                var next = new List<string>();
                foreach (string r in frontier)
                    foreach (string n in portal[r])
                        if (seen.Add(n)) next.Add(n);
                frontier = next;
            }
            seen.UnionWith(linked[room]);
            result[room] = seen.Where(exportedSet.Contains).ToList();
        }
        log.Warn("SWTOR_VIS", $"Room visibility: {portals} portal(s), {result.Count} of {exportedSet.Count} room(s) with data ({PortalHops} portal hops + linked rooms), {fallback} without data (visible from everywhere). Not verified in game.");
        return result;
    }
}
