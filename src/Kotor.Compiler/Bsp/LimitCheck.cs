namespace Kotor.Compiler.Bsp;

/// <summary>Aucune limite BSP n'est appliquee a l'ecriture : seule l'estimation RTX reste, pour l'avertissement RTX_ESTIMATE_HIGH.</summary>
public static class LimitCheck
{
    /// <summary>Estimation des primitives RTX du moteur (voir <see cref="Limits.RtxPrimitiveBytes"/>) : somme sur les surfaces de (feuilles x triangles).</summary>
    public static long EstimateRtxPrimitives(IEnumerable<RenderSurface> surfaces) =>
        surfaces.Sum(s => (long)s.Leaves.Count * (s.Indices.Length / 3));
}
