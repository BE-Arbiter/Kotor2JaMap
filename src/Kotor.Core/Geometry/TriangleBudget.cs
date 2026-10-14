namespace Kotor.Core.Geometry;

/// <summary>Repartition d'un budget de triangles entre sous-maillages, au prorata de leur taille.</summary>
public static class TriangleBudget
{
    /// <summary>
    /// Renvoie pour chaque sous-maillage sa cible de triangles. Si le total est sous le budget,
    /// les comptes sont rendus tels quels. Sinon repartition proportionnelle (methode du plus grand reste,
    /// deterministe) ; la somme des cibles vaut exactement <paramref name="budget"/>.
    /// </summary>
    public static int[] Allocate(IReadOnlyList<int> triangleCounts, int budget)
    {
        long total = 0;
        foreach (var c in triangleCounts) total += c;
        var result = new int[triangleCounts.Count];
        if (total <= budget)
        {
            for (int i = 0; i < result.Length; i++) result[i] = triangleCounts[i];
            return result;
        }

        var remainders = new (long rem, int index)[result.Length];
        long given = 0;
        for (int i = 0; i < result.Length; i++)
        {
            long scaled = (long)triangleCounts[i] * budget;
            result[i] = (int)(scaled / total);
            remainders[i] = (scaled % total, i);
            given += result[i];
        }
        Array.Sort(remainders, (a, b) => a.rem != b.rem ? b.rem.CompareTo(a.rem) : a.index.CompareTo(b.index));
        for (long k = 0; k < budget - given && k < remainders.Length; k++)
            result[remainders[k].index]++;
        return result;
    }

    /// <summary>
    /// Plancher par modele non marchable : jamais moins de 25 % de ses triangles. En dessous, un tronc ou une racine
    /// perd sa silhouette (a 2 % on obtenait des eclats pointus) ; 25 % garde une forme lisible. Pour respecter le
    /// budget on retire alors des instances entieres (voir <see cref="InstancePruner"/>) au lieu de detruire les modeles.
    /// </summary>
    public const double MinOtherRatio = 0.25;

    /// <summary>Plancher absolu en triangles pour un modele non marchable (un petit modele ne tombe pas sous 12).</summary>
    public const int MinModelTriangles = 12;

    /// <summary>Seuil bas du terrain : on peut le reduire jusqu'a 50 % avant de retirer la moindre instance.</summary>
    public const double MinWalkableRatio = 0.50;

    /// <summary>Plus petit nombre de triangles qu'un modele non marchable de <paramref name="triangles"/> triangles peut garder.</summary>
    public static int FloorTriangles(int triangles)
    {
        if (triangles <= 0) return 0;
        return Math.Min(triangles, Math.Max(MinModelTriangles, (int)Math.Ceiling(triangles * MinOtherRatio)));
    }

    public readonly record struct PriorityAllocation(int WalkableTarget, int OtherTarget);

    /// <summary>
    /// Repartition avec priorite au terrain : on reduit d'abord les triangles non marchables (jusqu'a
    /// <see cref="MinOtherRatio"/>), puis seulement ensuite les marchables (jusqu'a <see cref="MinWalkableRatio"/>).
    /// Le total respecte le budget sauf si les deux seuils bas le depassent deja.
    /// </summary>
    public static PriorityAllocation AllocatePriority(long walkable, long other, int budget)
    {
        budget = Math.Max(budget, 0);
        if (walkable + other <= budget) return new((int)walkable, (int)other);

        long otherFloor = other == 0 ? 0 : Math.Max(1, (long)Math.Ceiling(other * MinOtherRatio));
        long oT = Math.Min(other, Math.Max(budget - walkable, otherFloor));
        long wT = walkable;
        if (walkable + oT > budget)
        {
            long wFloor = walkable == 0 ? 0 : Math.Max(1, (long)Math.Ceiling(walkable * MinWalkableRatio));
            wT = Math.Min(walkable, Math.Max(budget - oT, wFloor));
        }
        return new((int)wT, (int)oT);
    }
}
