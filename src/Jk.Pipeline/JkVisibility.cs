using System.Numerics;
using Jk.Core.Jkl;
using Kotor.Compiler.Bsp;

namespace Jk.Pipeline;

/// <summary>
/// Visibilite entre secteurs JK. Un secteur = une piece (<c>SceneMesh.Room</c> = <c>s&lt;index&gt;</c>) ; les adjoins donnent le graphe.
/// Resultat conservateur (on ne cache jamais ce qui pourrait se voir, tous les adjoins comptent, visibles ou non) : une piece voit
/// elle-meme et les pieces atteintes en <see cref="DefaultHops"/> sauts d'adjoin. Quand le niveau a plus de secteurs que la limite
/// donnee, les secteurs voisins sont fusionnes en groupes (<c>g&lt;n&gt;</c>) : la geometrie ne change pas, seul le nombre de clusters baisse.
/// </summary>
public static class JkVisibility
{
    /// <summary>Sauts d'adjoin suivis. Les secteurs JK sont petits (462 pour 01narshadda) : 8 sauts couvrent couloirs, escaliers et grandes salles.</summary>
    public const int DefaultHops = 8;

    /// <summary>Voisins de chaque secteur : un adjoin relie le secteur de sa surface au secteur de la surface miroir.</summary>
    public static IReadOnlyList<int>[] SectorGraph(JklLevel level)
    {
        int n = level.Sectors.Count;
        var owner = new Dictionary<int, int>(); // indice d'adjoin -> secteur de la surface qui le porte
        for (int s = 0; s < n; s++)
        {
            JklSector sec = level.Sectors[s];
            for (int k = sec.FirstSurface; k < sec.FirstSurface + sec.SurfaceCount && k < level.Surfaces.Count; k++)
                if (level.Surfaces[k].Adjoin >= 0) owner.TryAdd(level.Surfaces[k].Adjoin, s);
        }
        var sets = new SortedSet<int>[n];
        for (int s = 0; s < n; s++) sets[s] = new SortedSet<int>();
        foreach (var (adjoin, from) in owner)
        {
            if (adjoin >= level.Adjoins.Count) continue;
            int mirror = level.Adjoins[adjoin].Mirror;
            if (mirror >= 0 && owner.TryGetValue(mirror, out int to) && to != from) { sets[from].Add(to); sets[to].Add(from); }
        }
        return sets.Select(s => (IReadOnlyList<int>)s.ToList()).ToArray();
    }

    /// <summary>
    /// Groupe de chaque secteur. Sous la limite : un groupe par secteur (identite). Au-dessus : parcours en largeur du graphe (composantes
    /// l'une apres l'autre) decoupe en tranches consecutives de <c>ceil(n / maxRooms)</c> secteurs, donc des groupes connexes.
    /// </summary>
    public static int[] GroupSectors(IReadOnlyList<int>[] graph, int maxRooms)
    {
        int n = graph.Length;
        var group = new int[n];
        if (maxRooms <= 0 || n <= maxRooms)
        {
            for (int i = 0; i < n; i++) group[i] = i;
            return group;
        }
        int chunk = (n + maxRooms - 1) / maxRooms;
        var seen = new bool[n];
        int rank = 0;
        for (int start = 0; start < n; start++)
        {
            if (seen[start]) continue;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.Count > 0)
            {
                int s = queue.Dequeue();
                group[s] = rank++ / chunk;
                foreach (int t in graph[s])
                    if (!seen[t]) { seen[t] = true; queue.Enqueue(t); }
            }
        }
        return group;
    }

    /// <summary>Noms de piece : <c>s&lt;index&gt;</c> pour un groupe d'un seul secteur, sinon <c>g&lt;groupe&gt;</c>.</summary>
    public static string[] RoomNames(int[] group)
    {
        int count = group.Length == 0 ? 0 : group.Max() + 1;
        var members = new List<int>[count];
        for (int g = 0; g < count; g++) members[g] = new List<int>();
        for (int s = 0; s < group.Length; s++) members[group[s]].Add(s);
        return members.Select((m, g) => m.Count == 1 ? $"s{m[0]}" : $"g{g}").ToArray();
    }

    /// <summary>Pieces visibles depuis chaque piece (nom du groupe -> noms), par parcours du graphe de groupes en <paramref name="hops"/> sauts.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Build(IReadOnlyList<int>[] graph, int[] group, string[] roomNames, int hops)
    {
        int groups = roomNames.Length;
        var adj = new HashSet<int>[groups];
        for (int g = 0; g < groups; g++) adj[g] = new HashSet<int>();
        for (int s = 0; s < graph.Length; s++)
            foreach (int t in graph[s])
                if (group[s] != group[t]) adj[group[s]].Add(group[t]);

        var result = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        for (int g = 0; g < groups; g++)
        {
            var seen = new HashSet<int> { g };
            var frontier = new List<int> { g };
            for (int hop = 0; hop < hops && frontier.Count > 0; hop++)
            {
                var next = new List<int>();
                foreach (int r in frontier)
                    foreach (int t in adj[r])
                        if (seen.Add(t)) next.Add(t);
                frontier = next;
            }
            result[roomNames[g]] = seen.Select(i => roomNames[i]).ToList();
        }
        return result;
    }

    /// <summary>
    /// Groupes de secteurs retenus : le plus fin groupement (au plus <paramref name="maxRooms"/> pieces) dont le nombre de clusters de visibilite du BSP
    /// ne depasse pas <paramref name="maxClusters"/>. Les clusters sont les ensembles distincts de boites de pieces qui se recoupent ; la matrice de visibilite
    /// et le temps d'ecriture du BSP croissent comme leur carre (09fuelstation : 4 313 clusters = 4 s, 8 772 = 15 s ; 04escapehouse : 9 533). Les boites de groupes
    /// plus gros se recoupent davantage : on essaie des groupes de plus en plus gros (x 0,7 a chaque essai) jusqu'a tenir sous la limite, au pire une seule piece
    /// (visibilite desactivee). La geometrie ne change jamais.
    /// </summary>
    public static int[] ChooseGroups(JklLevel level, IReadOnlyList<int>[] graph, float jaUnitsPerJkUnit, int maxRooms, int maxClusters, out int clusters)
    {
        int n = graph.Length;
        var sectorBoxes = SectorBoxes(level, jaUnitsPerJkUnit);
        int target = maxRooms > 0 ? Math.Min(maxRooms, n) : n;
        while (true)
        {
            int[] group = GroupSectors(graph, target);
            clusters = ClusterCount(GroupBoxes(group, sectorBoxes));
            if (clusters <= maxClusters || target <= 1) return group;
            target = Math.Max(1, (int)(target * 0.7));
        }
    }

    /// <summary>Boite (unites JA, avec la marge du compilateur) des surfaces dessinees de chaque secteur ; null si le secteur n'a rien de dessine.</summary>
    internal static (Vector3 Min, Vector3 Max)?[] SectorBoxes(JklLevel level, float jaUnitsPerJkUnit)
    {
        var boxes = new (Vector3, Vector3)?[level.Sectors.Count];
        for (int s = 0; s < boxes.Length; s++)
        {
            JklSector sec = level.Sectors[s];
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            bool any = false;
            for (int k = sec.FirstSurface; k < sec.FirstSurface + sec.SurfaceCount && k < level.Surfaces.Count; k++)
            {
                JklSurface f = level.Surfaces[k];
                if (f.Material < 0 || f.Geo == 0 || f.VertexIndices.Length < 3) continue;
                foreach (int v in f.VertexIndices)
                {
                    if (v < 0 || v >= level.Vertices.Length) continue;
                    Vector3 p = level.Vertices[v] * jaUnitsPerJkUnit;
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p); any = true;
                }
            }
            if (any) boxes[s] = (min - new Vector3(Kotor.Compiler.Compiler.RoomBoxMargin), max + new Vector3(Kotor.Compiler.Compiler.RoomBoxMargin));
        }
        return boxes;
    }

    private static List<(Vector3 Min, Vector3 Max)> GroupBoxes(int[] group, (Vector3 Min, Vector3 Max)?[] sectorBoxes)
    {
        var merged = new Dictionary<int, (Vector3 Min, Vector3 Max)>();
        for (int s = 0; s < group.Length; s++)
        {
            if (sectorBoxes[s] is not { } b) continue;
            merged[group[s]] = merged.TryGetValue(group[s], out var m) ? (Vector3.Min(m.Min, b.Min), Vector3.Max(m.Max, b.Max)) : b;
        }
        return merged.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
    }

    /// <summary>Nombre de clusters de visibilite du BSP pour ces boites de pieces (la partition ne depend pas des brushes).</summary>
    internal static int ClusterCount(IReadOnlyList<(Vector3 Min, Vector3 Max)> boxes)
    {
        if (boxes.Count < 2) return 1;
        var tree = BspTree.Build([], null, boxes.Select((b, i) => new RoomBox(i, b.Min, b.Max)).ToList());
        return tree.Clusters.Count;
    }
}
