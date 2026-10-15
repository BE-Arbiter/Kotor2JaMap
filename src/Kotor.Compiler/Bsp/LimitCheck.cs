// Limites relevées dans q3map2 / NetRadiant-custom et le moteur Jedi Academy (OpenJK, qfiles.h), GPL-2.0.
// Voir THIRD-PARTY-NOTICES.md et docs/superpowers/plans/phase1/reports/step-05-research.md.
namespace Kotor.Compiler.Bsp;

/// <summary>
/// Vérifications de dépassement de lump. Toute violation lève <see cref="BspLimitException"/> :
/// l'appelant n'écrit alors aucun BSP (pas de fichier partiel).
/// </summary>
public static class LimitCheck
{
    public const int MaxShaders = 0x400;
    public const int MaxDrawSurfaces = 0x20000;
    public const int MaxDrawVerts = 0x80000;

    public static void Check(string lumpName, int count, int max, bool collisionRelated = false)
    {
        if (count > max)
            throw new BspLimitException($"BSP limit exceeded: {lumpName} = {max}, {count} required. Reduce the geometry; no partial BSP is written.", collisionRelated);
    }

    /// <summary>Limites par surface de rendu (ParseTriSurf du moteur) : échec propre si le découpage a été contourné.</summary>
    public static void CheckSurface(string shader, int verts, int indexes)
    {
        Check("SURFACE_VERTS(" + shader + ")", verts, RenderSurfaces.MaxSurfaceVerts);
        Check("SURFACE_INDEXES(" + shader + ")", indexes, RenderSurfaces.MaxSurfaceIndexes);
    }

    /// <summary>Lumps liés aux surfaces de rendu.</summary>
    public static void CheckRenderLimits(int shaders, int surfaces, int verts, int indexes, int leafSurfaces, int maxLeafSurfaces = 0x20000)
    {
        Check("MAX_MAP_SHADERS", shaders, MaxShaders);
        Check("MAX_MAP_DRAW_SURFS", surfaces, MaxDrawSurfaces);
        Check("MAX_MAP_DRAW_VERTS", verts, MaxDrawVerts);
        Check("MAX_MAP_DRAW_INDEXES", indexes, Limits.MaxDrawIndexes);
        Check("MAX_MAP_LEAFFACES", leafSurfaces, maxLeafSurfaces);
    }
}
