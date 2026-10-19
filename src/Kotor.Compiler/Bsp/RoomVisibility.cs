// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
namespace Kotor.Compiler.Bsp;

/// <summary>
/// Visibilite entre pieces : matrice symetrique (une piece se voit toujours elle-meme). Les pieces sans entree dans les donnees
/// sont « tout visible » (voient et sont vues de toutes les autres) : on ne cache jamais ce qu'on ne sait pas.
/// </summary>
public sealed class RoomVisibility
{
    public IReadOnlyList<string> Rooms { get; }
    private readonly bool[][] _visible;

    private RoomVisibility(IReadOnlyList<string> rooms, bool[][] visible) { Rooms = rooms; _visible = visible; }

    public int Count => Rooms.Count;

    public bool Sees(int a, int b) => _visible[a][b];

    /// <summary>
    /// <paramref name="visible"/> : piece -> pieces qu'elle voit (noms insensibles a la casse ; les noms inconnus de <paramref name="rooms"/> sont ignores).
    /// La matrice est symetrisee (union), la diagonale est vraie, et une piece absente de <paramref name="visible"/> voit tout.
    /// </summary>
    public static RoomVisibility Create(IReadOnlyList<string> rooms, IReadOnlyDictionary<string, IReadOnlyCollection<string>> visible)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        ArgumentNullException.ThrowIfNull(visible);
        int n = rooms.Count;
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < n; i++) index.TryAdd(rooms[i], i);
        var lookup = new Dictionary<string, IReadOnlyCollection<string>>(visible, StringComparer.OrdinalIgnoreCase);
        var m = new bool[n][];
        for (int i = 0; i < n; i++) m[i] = new bool[n];
        for (int i = 0; i < n; i++)
        {
            m[i][i] = true;
            if (!lookup.TryGetValue(rooms[i], out var list))
            {
                for (int j = 0; j < n; j++) { m[i][j] = true; m[j][i] = true; }
                continue;
            }
            foreach (string name in list)
                if (index.TryGetValue(name, out int j)) { m[i][j] = true; m[j][i] = true; }
        }
        return new RoomVisibility(rooms, m);
    }

    /// <summary>Tout visible (une seule region) : equivalent de l'ancien comportement.</summary>
    public static RoomVisibility AllVisible(IReadOnlyList<string> rooms) => Create(rooms, new Dictionary<string, IReadOnlyCollection<string>>());
}

/// <summary>Lump de visibilite (BSP Q3) : en-tete (numClusters, bytesPerCluster) puis une ligne de bits par cluster.</summary>
public static class VisibilityLump
{
    /// <summary>
    /// Le cluster <c>a</c> voit le cluster <c>b</c> si un des deux est hors de toute piece (ensemble vide : voit tout, vu de tous) ou si une
    /// piece de <c>a</c> voit une piece de <c>b</c>. Symetrique par construction.
    /// </summary>
    public static bool[][] ClusterMatrix(IReadOnlyList<int[]> clusters, RoomVisibility rooms)
    {
        int n = clusters.Count;
        var m = new bool[n][];
        for (int a = 0; a < n; a++)
        {
            m[a] = new bool[n];
            for (int b = 0; b < n; b++)
            {
                if (clusters[a].Length == 0 || clusters[b].Length == 0) { m[a][b] = true; continue; }
                foreach (int ra in clusters[a])
                {
                    foreach (int rb in clusters[b])
                        if (ra < rooms.Count && rb < rooms.Count && rooms.Sees(ra, rb)) { m[a][b] = true; break; }
                    if (m[a][b]) break;
                }
            }
        }
        return m;
    }

    /// <summary>Octets du lump : <c>int numClusters, int bytesPerCluster</c>, puis <c>numClusters</c> lignes de <c>bytesPerCluster</c> octets (bit c%8 de l'octet c/8).</summary>
    public static byte[] Encode(bool[][] matrix)
    {
        int n = matrix.Length, bytes = (n + 7) / 8;
        var data = new byte[8 + n * bytes];
        BitConverter.GetBytes(n).CopyTo(data, 0);
        BitConverter.GetBytes(bytes).CopyTo(data, 4);
        for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
                if (matrix[a][b]) data[8 + a * bytes + (b >> 3)] |= (byte)(1 << (b & 7));
        return data;
    }
}
