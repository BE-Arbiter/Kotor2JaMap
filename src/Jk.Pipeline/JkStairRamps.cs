using System.Numerics;
using Jk.Core.Jkl;

namespace Jk.Pipeline;

/// <summary>
/// Rampes de collision pour les marches. Les contremarches de JK montent jusqu'a 0,10 unite (34 u JA a l'echelle retenue) alors que le joueur
/// JA ne franchit que 18 u : sans rien, les escaliers seraient infranchissables. Une contremarche est une surface verticale a quatre sommets
/// dont l'arete haute est un bord d'une surface horizontale (la marche du dessus) et l'arete basse un bord d'une autre (la marche du dessous),
/// avec les memes indices de sommets. Pour chacune dont la hauteur depasse le seuil, on ajoute une surface marchable inclinee (pente de 40 a 45 degres)
/// qui relie l'arete basse, decalee vers l'avant sur la marche du dessous, a l'arete haute. Seule la collision change : le rendu reste intact.
/// </summary>
public static class JkStairRamps
{
    /// <summary>Contremarche la plus haute traitee (au-dela, c'est un mur ou un rebord, pas une marche).</summary>
    public const float MaxRise = 0.12f;

    /// <summary>Decalage de la rampe par rapport a la hauteur de la marche : pente atan(1 / 1,2) = 40 degres, marchable (>= 0,7 de normale Z).</summary>
    public const float RunPerRise = 1.2f;

    public sealed record Ramp(int Sector, Vector3 A, Vector3 B, Vector3 C, Vector3 D);

    /// <summary>Rampes (quadrilateres en unites JK) des contremarches de plus de <paramref name="minRise"/> unites JK ; <paramref name="skipped"/> : trop raides faute de profondeur.</summary>
    public static List<Ramp> Compute(JklLevel level, Func<JklSurface, bool> isSolid, float minRise, out int skipped)
    {
        skipped = 0;
        var ramps = new List<Ramp>();
        // Aretes des surfaces horizontales (sol et plafond compris : seuls les sols sont utilises plus bas, via la normale).
        var floorByEdge = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < level.Surfaces.Count; i++)
        {
            JklSurface s = level.Surfaces[i];
            if (s.Normal.Z < 0.9f || s.VertexIndices.Length < 3) continue;
            for (int k = 0; k < s.VertexIndices.Length; k++)
            {
                var edge = Edge(s.VertexIndices[k], s.VertexIndices[(k + 1) % s.VertexIndices.Length]);
                if (!floorByEdge.TryGetValue(edge, out var list)) floorByEdge[edge] = list = new List<int>();
                list.Add(i);
            }
        }
        if (floorByEdge.Count == 0) return ramps;

        for (int sector = 0; sector < level.Sectors.Count; sector++)
        {
            JklSector sec = level.Sectors[sector];
            for (int i = sec.FirstSurface; i < sec.FirstSurface + sec.SurfaceCount && i < level.Surfaces.Count; i++)
            {
                JklSurface s = level.Surfaces[i];
                if (s.VertexIndices.Length != 4 || MathF.Abs(s.Normal.Z) > 0.1f || !isSolid(s)) continue;
                Vector3[] v = s.VertexIndices.Select(x => level.Vertices[x]).ToArray();
                float zMin = v.Min(p => p.Z), zMax = v.Max(p => p.Z), h = zMax - zMin;
                if (h < minRise || h > MaxRise) continue;
                var top = Enumerable.Range(0, 4).Where(k => v[k].Z > zMax - 5e-4f).ToArray();
                var bottom = Enumerable.Range(0, 4).Where(k => v[k].Z < zMin + 5e-4f).ToArray();
                if (top.Length != 2 || bottom.Length != 2) continue;
                int t0 = s.VertexIndices[top[0]], t1 = s.VertexIndices[top[1]], b0 = s.VertexIndices[bottom[0]], b1 = s.VertexIndices[bottom[1]];
                if (!floorByEdge.ContainsKey(Edge(t0, t1)) || !floorByEdge.TryGetValue(Edge(b0, b1), out var lowerTreads)) continue;

                var dir = new Vector3(s.Normal.X, s.Normal.Y, 0f);
                if (dir.LengthSquared() < 1e-6f) continue;
                dir = Vector3.Normalize(dir);
                // Profondeur de la marche du dessous, mesuree depuis l'arete basse dans le sens de la contremarche.
                float depth = 0f;
                foreach (int f in lowerTreads)
                    foreach (int vi in level.Surfaces[f].VertexIndices)
                        depth = MathF.Max(depth, Vector3.Dot(level.Vertices[vi] - level.Vertices[b0], dir));
                float run = MathF.Min(RunPerRise * h, depth * 0.98f);
                if (run < h) { skipped++; continue; } // pente de plus de 45 degres : non marchable de toute facon
                Vector3 pb0 = level.Vertices[b0] + dir * run, pb1 = level.Vertices[b1] + dir * run;
                ramps.Add(new Ramp(sector, pb0, pb1, level.Vertices[t1], level.Vertices[t0]));
            }
        }
        return ramps;
    }

    private static (int, int) Edge(int a, int b) => a < b ? (a, b) : (b, a);
}
