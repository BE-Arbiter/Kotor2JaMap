namespace Kotor.Core.Export;

/// <summary>Instance candidate a une sortie en <c>misc_model_static</c> : <paramref name="Md3Files"/> entites (un fichier MD3 = une entite) retirent <paramref name="Triangles"/> triangles du BSP.</summary>
public sealed record MiscModelCandidate(int Id, string Model, long Triangles, int Md3Files);

/// <summary>
/// Choix des instances sorties du BSP sous le plafond du moteur (MAX_MISC_ENTS = 2000, OpenJK-SWGL code/cgame/cg_main.cpp:2176 :
/// au-dela, Com_Error au chargement). Glouton par triangles retires par entite : optimal ici puisque chaque instance se prend seule.
/// </summary>
public static class MiscModelBudget
{
    public const int EngineMaxEntities = 2000;

    /// <summary>Ids retenus. <paramref name="budget"/> : entites disponibles ; <paramref name="minTrianglesPerEntity"/> : en dessous, l'instance reste dans le BSP.</summary>
    public static IReadOnlySet<int> Select(IEnumerable<MiscModelCandidate> candidates, int budget, long minTrianglesPerEntity)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var chosen = new HashSet<int>();
        int left = budget;
        foreach (MiscModelCandidate c in candidates
            .Where(c => c.Md3Files > 0 && c.Triangles >= minTrianglesPerEntity * c.Md3Files)
            .OrderByDescending(c => (double)c.Triangles / c.Md3Files)
            .ThenBy(c => c.Model, StringComparer.Ordinal)
            .ThenBy(c => c.Id))
        {
            if (left <= 0) break;
            if (c.Md3Files > left) continue;
            chosen.Add(c.Id);
            left -= c.Md3Files;
        }
        return chosen;
    }
}
