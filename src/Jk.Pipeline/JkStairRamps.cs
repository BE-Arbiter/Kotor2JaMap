using System.Numerics;
using Jk.Core.Jkl;

namespace Jk.Pipeline;

/// <summary>
/// Rampes de collision pour les marches. Les contremarches de JK montent jusqu'a 0,10 unite (34 u JA a l'echelle retenue) alors que le joueur
/// JA ne franchit que 18 u : sans rien, les escaliers seraient infranchissables. Une contremarche est une surface verticale a quatre sommets
/// dont l'arete haute est un bord d'une surface horizontale (la marche du dessus) et l'arete basse un bord d'une autre (la marche du dessous),
/// avec les memes indices de sommets. Pour chacune qui est une vraie marche, on ajoute une surface marchable inclinee (pente de 40 a 45 degres)
/// qui relie l'arete basse, decalee vers l'avant sur la marche du dessous, a l'arete haute. Seule la collision change : le rendu reste intact.
///
/// Toutes les contremarches hautes ne sont pas des marches : comptoirs, bars, rebords de balustrade, caisses, plates-formes et bords de fosse
/// ont la meme geometrie (une face verticale entre deux surfaces horizontales) et le joueur JK ne les franchit pas non plus ; leur donner une
/// rampe les rendrait escaladables. Mesure sur les 34 niveaux (voir le rapport T4, « Correctif rampes sur comptoirs ») : les vrais escaliers
/// sont des series de contremarches consecutives de hauteur voisine (0,05 a 0,075), les rebords (0,10) sont presque toujours isoles. Regles :
/// <list type="bullet">
/// <item>largeur au moins egale au diametre du joueur (<see cref="MinWidth"/>), sinon personne ne peut s'y poser ;</item>
/// <item>hauteur au plus <see cref="CurbMaxRise"/> (19 u JA) : bordure ou marche, rampe meme isolee (marge sur le pas de 18 u de JA) ;</item>
/// <item>hauteur au plus <see cref="StairMaxRise"/> (26 u JA) : rampe seulement dans une serie d'au moins 2 contremarches consecutives ;</item>
/// <item>hauteur au plus <see cref="MaxRise"/> : rampe seulement dans une serie d'au moins 3 contremarches ; au-dela c'est un mur.</item>
/// </list>
/// Une serie est une suite montante : la contremarche suivante part de la hauteur du haut de la precedente, a au plus <see cref="MaxTreadDepth"/>
/// devant, avec recouvrement lateral et hauteur voisine (rapport au plus <see cref="MaxRiseRatio"/>).
/// </summary>
public static class JkStairRamps
{
    /// <summary>Contremarche la plus haute traitee (au-dela, c'est un mur, pas une marche).</summary>
    public const float MaxRise = 0.12f;

    /// <summary>Hauteur jusqu'a laquelle une contremarche isolee recoit une rampe (0,055 = 19 u JA : bordure, un pas de JA avec marge).</summary>
    public const float CurbMaxRise = 0.055f;

    /// <summary>Hauteur jusqu'a laquelle deux contremarches consecutives forment un escalier (0,075 = 26 u JA : la plus haute marche vue dans un escalier).</summary>
    public const float StairMaxRise = 0.076f;

    /// <summary>Largeur minimale d'une contremarche pour recevoir une rampe : le diametre du joueur JK (2 x 0,065).</summary>
    public const float MinWidth = 0.13f;

    /// <summary>Profondeur maximale de la marche entre deux contremarches consecutives d'un escalier.</summary>
    public const float MaxTreadDepth = 1.6f;

    /// <summary>Rapport maximal entre les hauteurs de deux contremarches consecutives d'un escalier.</summary>
    public const float MaxRiseRatio = 1.5f;

    /// <summary>Profondeur (unites JK) minimale du palier au-dessus d'une contremarche isolee pour que ce soit un seuil de passage et non un comptoir : 0,6 = 206 u JA, plus que n'importe quel comptoir, bar ou caisse mesure.</summary>
    public const float LandingMinDepth = 0.6f;

    /// <summary>Recouvrement minimal (unites JK) entre l'arete basse du passage et une arete de la contremarche.</summary>
    public const float ThresholdMinOverlap = 0.05f;

    /// <summary>Decalage de la rampe par rapport a la hauteur de la marche : pente atan(1 / 1,2) = 40 degres, marchable (>= 0,7 de normale Z).</summary>
    public const float RunPerRise = 1.2f;

    public sealed record Ramp(int Sector, Vector3 A, Vector3 B, Vector3 C, Vector3 D);

    /// <summary>
    /// Bilan : <see cref="Placed"/> rampes posees ; <see cref="Steep"/> trop raides faute de profondeur ; <see cref="Isolated"/> contremarches hautes sans
    /// serie (comptoirs, rebords, caisses) laissees bloquantes ; <see cref="Narrow"/> plus etroites que le joueur ;
    /// <see cref="Thresholds"/> (comptees dans <see cref="Placed"/>) contremarches isolees qui ont recu une rampe parce qu'elles sont un seuil de passage.
    /// </summary>
    public sealed record Stats(int Placed, int Steep, int Isolated, int Narrow, int Thresholds = 0);

    /// <summary>Rampes (quadrilateres en unites JK) des vraies marches de plus de <paramref name="minRise"/> unites JK, avec le bilan (posees, trop raides, isolees laissees bloquantes, trop etroites).</summary>
    public static List<Ramp> Compute(JklLevel level, Func<JklSurface, bool> isSolid, float minRise, out Stats stats)
    {
        var ramps = new List<Ramp>();
        stats = new Stats(0, 0, 0, 0);
        // Aretes des surfaces de sol uniquement (Normal.Z >= 0,9 : les plafonds, a normale vers le bas, sont exclus).
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

        // 1) Toutes les contremarches candidates (de minRise a MaxRise).
        var risers = new List<Riser>();
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
                // Appariement par proximite XY : le sommet bas b0 est celui situe sous t0 (la rotation des sommets du quad dans le fichier est quelconque).
                int t0 = s.VertexIndices[top[0]], t1 = s.VertexIndices[top[1]];
                int bA = s.VertexIndices[bottom[0]], bB = s.VertexIndices[bottom[1]];
                float dA = XyDistSq(level.Vertices[t0], level.Vertices[bA]), dB = XyDistSq(level.Vertices[t0], level.Vertices[bB]);
                int b0 = dA <= dB ? bA : bB, b1 = dA <= dB ? bB : bA;
                if (!floorByEdge.ContainsKey(Edge(t0, t1)) || !floorByEdge.TryGetValue(Edge(b0, b1), out var lowerTreads)) continue;

                var dir = new Vector3(s.Normal.X, s.Normal.Y, 0f);
                if (dir.LengthSquared() < 1e-6f) continue;
                dir = Vector3.Normalize(dir);
                risers.Add(new Riser(sector, t0, t1, b0, b1, dir, h, zMin, zMax, lowerTreads, floorByEdge[Edge(t0, t1)]));
            }
        }

        // 2) Series : pour chaque contremarche, nombre de contremarches consecutives montantes au-dessus et en dessous.
        Vector3 Mid(int a, int b) => (level.Vertices[a] + level.Vertices[b]) * 0.5f;
        var above = new int[risers.Count];
        var below = new int[risers.Count];
        var next = new List<int>[risers.Count];
        for (int i = 0; i < risers.Count; i++)
        {
            Riser r = risers[i];
            Vector3 lat = new(-r.Dir.Y, r.Dir.X, 0f), topMid = Mid(r.T0, r.T1);
            float width = (level.Vertices[r.T1] - level.Vertices[r.T0]).Length();
            next[i] = [];
            for (int j = 0; j < risers.Count; j++)
            {
                Riser o = risers[j];
                if (j == i || MathF.Abs(o.ZBottom - r.ZTop) > 5e-4f || Vector3.Dot(o.Dir, r.Dir) < 0.9f) continue;
                if (MathF.Max(o.H, r.H) / MathF.Min(o.H, r.H) > MaxRiseRatio) continue;
                Vector3 d = Mid(o.B0, o.B1) - topMid;
                float along = Vector3.Dot(d, -r.Dir), side = MathF.Abs(Vector3.Dot(d, lat));
                float widthO = (level.Vertices[o.T1] - level.Vertices[o.T0]).Length();
                if (along < -0.01f || along > MaxTreadDepth || side > (width + widthO) * 0.5f) continue;
                next[i].Add(j);
            }
        }
        // Plus long chemin montant (resp. descendant) : le graphe est sans cycle (la hauteur monte strictement) ; memoisation.
        var memoUp = new int[risers.Count]; Array.Fill(memoUp, -1);
        int Up(int i)
        {
            if (memoUp[i] >= 0) return memoUp[i];
            int best = 0;
            foreach (int j in next[i]) best = Math.Max(best, 1 + Up(j));
            return memoUp[i] = best;
        }
        var prev = new List<int>[risers.Count];
        for (int i = 0; i < risers.Count; i++) prev[i] = [];
        for (int i = 0; i < risers.Count; i++) foreach (int j in next[i]) prev[j].Add(i);
        var memoDown = new int[risers.Count]; Array.Fill(memoDown, -1);
        int Down(int i)
        {
            if (memoDown[i] >= 0) return memoDown[i];
            int best = 0;
            foreach (int j in prev[i]) best = Math.Max(best, 1 + Down(j));
            return memoDown[i] = best;
        }
        for (int i = 0; i < risers.Count; i++) { above[i] = Up(i); below[i] = Down(i); }

        // Largeur effective : une marche large peut etre decoupee en plusieurs contremarches voisines (meme plan, sommets hauts partages) ;
        // la largeur testee est celle de l'ensemble.
        var parent = Enumerable.Range(0, risers.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        for (int i = 0; i < risers.Count; i++)
            for (int j = i + 1; j < risers.Count; j++)
            {
                Riser a = risers[i], b = risers[j];
                if (MathF.Abs(a.ZTop - b.ZTop) > 5e-4f || MathF.Abs(a.ZBottom - b.ZBottom) > 5e-4f || Vector3.Dot(a.Dir, b.Dir) < 0.99f) continue;
                if (a.T0 == b.T0 || a.T0 == b.T1 || a.T1 == b.T0 || a.T1 == b.T1) parent[Find(i)] = Find(j);
            }
        var groupWidth = new float[risers.Count];
        for (int i = 0; i < risers.Count; i++) groupWidth[Find(i)] += (level.Vertices[risers[i].T1] - level.Vertices[risers[i].T0]).Length();

        // Passages franchissables : arete basse (z, extremites) des surfaces d'adjoin verticales dont le joueur peut traverser l'ouverture (fenetres et adjoins fermes exclus).
        var portals = new List<(float Z, Vector3 A, Vector3 B)>();
        foreach (JklSurface s in level.Surfaces)
        {
            if (s.Adjoin < 0 || MathF.Abs(s.Normal.Z) > 0.3f || isSolid(s) || s.VertexIndices.Length < 3) continue;
            Vector3[] v = [.. s.VertexIndices.Select(x => level.Vertices[x])];
            float zLow = v.Min(p => p.Z);
            Vector3[] low = [.. v.Where(p => p.Z < zLow + 5e-3f)];
            if (low.Length >= 2) portals.Add((zLow, low[0], low[1]));
        }

        // 3) Decision et construction des rampes.
        int steep = 0, isolated = 0, narrow = 0, thresholds = 0;
        for (int i = 0; i < risers.Count; i++)
        {
            Riser r = risers[i];
            if (groupWidth[Find(i)] < MinWidth) { narrow++; continue; }
            int series = above[i] + below[i] + 1;
            bool stair = r.H <= CurbMaxRise || (r.H <= StairMaxRise ? series >= 2 : series >= 3);
            bool threshold = false;
            if (!stair && IsThreshold(level, r, portals))
            {
                // Seuil de passage : une marche isolee sous un passage franchissable, qui mene a un palier (pas un comptoir) ; la laisser bloquante murerait le passage.
                stair = true; threshold = true;
            }
            if (!stair) { isolated++; continue; }

            // Profondeur de la marche du dessous, mesuree depuis l'arete basse dans le sens de la contremarche.
            float depth = 0f;
            foreach (int f in r.LowerTreads)
                foreach (int vi in level.Surfaces[f].VertexIndices)
                    depth = MathF.Max(depth, Vector3.Dot(level.Vertices[vi] - level.Vertices[r.B0], r.Dir));
            float run = MathF.Min(RunPerRise * r.H, depth * 0.98f);
            if (run < r.H) { steep++; continue; } // pente de plus de 45 degres : non marchable de toute facon
            Vector3 pb0 = level.Vertices[r.B0] + r.Dir * run, pb1 = level.Vertices[r.B1] + r.Dir * run;
            // Quad pb0 -> pb1 -> t1 -> t0 : cotes opposes pb0-t0 et pb1-t1 non croises (appariement ci-dessus) ; on impose une normale vers le haut.
            Vector3 ta = level.Vertices[r.T0], tb = level.Vertices[r.T1];
            if (Vector3.Cross(pb1 - pb0, tb - pb0).Z < 0f) ramps.Add(new Ramp(r.Sector, ta, tb, pb1, pb0));
            else ramps.Add(new Ramp(r.Sector, pb0, pb1, tb, ta));
            if (threshold) thresholds++;
        }
        stats = new Stats(ramps.Count, steep, isolated, narrow, thresholds);
        return ramps;
    }

    /// <summary>
    /// Seuil de passage : un passage franchissable (surface d'adjoin verticale) a son arete basse sur l'arete haute ou basse de la contremarche (sur au moins <see cref="ThresholdMinOverlap"/>),
    /// et la marche du dessus est un palier d'au moins <see cref="LandingMinDepth"/> de profondeur. Un comptoir, un bar ou une caisse ont aussi un passage au-dessus de leur rebord (le volume
    /// au-dessus est un autre secteur) mais un dessus etroit : la profondeur du palier les distingue.
    /// </summary>
    private static bool IsThreshold(JklLevel level, Riser r, List<(float Z, Vector3 A, Vector3 B)> portals)
    {
        float landing = 0f;
        foreach (int f in r.UpperTreads)
            foreach (int vi in level.Surfaces[f].VertexIndices)
                landing = MathF.Max(landing, Vector3.Dot(level.Vertices[vi] - level.Vertices[r.T0], -r.Dir));
        if (landing < LandingMinDepth) return false;
        foreach (var (z, a, b) in portals)
        {
            if (MathF.Abs(z - r.ZTop) < 0.01f && Overlap(a, b, level.Vertices[r.T0], level.Vertices[r.T1]) >= ThresholdMinOverlap) return true;
            if (MathF.Abs(z - r.ZBottom) < 0.01f && Overlap(a, b, level.Vertices[r.B0], level.Vertices[r.B1]) >= ThresholdMinOverlap) return true;
        }
        return false;
    }

    /// <summary>Longueur (XY, unites JK) du recouvrement du segment ab avec le segment cd, s'ils sont quasi colineaires (ecart perpendiculaire < 0,03) ; 0 sinon.</summary>
    private static float Overlap(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        float ex = d.X - c.X, ey = d.Y - c.Y, len = MathF.Sqrt(ex * ex + ey * ey);
        if (len < 1e-6f) return 0f;
        ex /= len; ey /= len;
        float Perp(Vector3 p) => MathF.Abs((p.X - c.X) * -ey + (p.Y - c.Y) * ex);
        if (Perp(a) > 0.03f || Perp(b) > 0.03f) return 0f;
        float ta = (a.X - c.X) * ex + (a.Y - c.Y) * ey, tb = (b.X - c.X) * ex + (b.Y - c.Y) * ey;
        return MathF.Max(0f, MathF.Min(MathF.Max(ta, tb), len) - MathF.Max(MathF.Min(ta, tb), 0f));
    }

    private sealed record Riser(int Sector, int T0, int T1, int B0, int B1, Vector3 Dir, float H, float ZBottom, float ZTop, List<int> LowerTreads, List<int> UpperTreads);

    private static float XyDistSq(Vector3 a, Vector3 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private static (int, int) Edge(int a, int b) => a < b ? (a, b) : (b, a);
}
