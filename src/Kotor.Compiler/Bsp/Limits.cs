// Limites relevées dans q3map2 / NetRadiant-custom et le moteur Jedi Academy (OpenJK, qfiles.h), GPL-2.0.
// Voir THIRD-PARTY-NOTICES.md et docs/superpowers/plans/phase1/reports/step-05-research.md.
namespace Kotor.Compiler.Bsp;

public sealed class BspLimitException(string message, bool collisionRelated = false) : Exception(message)
{
    /// <summary>Vrai si la limite depend de la collision (brushes, cotes, plans, noeuds, feuilles, leafbrushes) : reduire la collision peut alors aider.</summary>
    public bool CollisionRelated { get; } = collisionRelated;
}

/// <summary>Limites BSP du moteur JA (RBSP v1).</summary>
public sealed record Limits(int MaxBrushes, int MaxPlanes, int MaxNodes, int MaxLeaves,
    int MaxBrushSides = 0x20000, int MaxLeafBrushes = 0x40000, int MaxLeafSurfaces = 0x20000)
{
    public const int MaxDrawIndexes = 0x80000;

    /// <summary>Limites du moteur JA vanilla (qfiles.h).</summary>
    public static Limits Vanilla { get; } = new(0x8000, 0x20000, 0x20000, 0x20000, 0x20000, 0x40000, 0x20000);

    /// <summary>Profil SWGL : cm_load.cpp alloue brushes, plans, cotes, noeuds, feuilles et leafbrushes dynamiquement (rien n est verifie au chargement) ; R_LoadMarksurfaces (rd-vulkan/tr_bsp.cpp) alloue aussi les leafsurfaces d apres le lump, sans controle.
    /// Leafsurfaces a 0x200000 : les trois chargeurs (code/rd-vulkan, code/rd-vanilla, shared/rd-rend2 R_LoadMarksurfaces) et CMod_LoadLeafSurfaces
    /// (code/qcommon/cm_load.cpp) allouent count entrees d apres la taille du lump, sans borne ; MAX_MAP_LEAFFACES de qfiles.h n y est pas utilise.
    /// Necessaire a l assemblage end_m01aa + end_m01ab (1 242 104 leaf faces), essaye en jeu.
    /// Le reste (shaders, draw verts/indexes, surfaces, modeles) reste a la valeur vanilla.</summary>
    public static Limits Swgl { get; } = new(0x100000, 0x100000, 0x100000, 0x100000, 0x100000, 0x100000, 0x200000);

    /// <summary>"swgl" (defaut) ou "vanilla", insensible a la casse.</summary>
    public static Limits FromName(string? name) => (name ?? "swgl").Trim().ToLowerInvariant() switch
    {
        "swgl" => Swgl,
        "vanilla" => Vanilla,
        _ => throw new ArgumentException($"Unknown BSP limits profile: {name} (expected: swgl or vanilla).", nameof(name)),
    };

    public void CheckBrushes(int count) => Check("MAX_MAP_BRUSHES", count, MaxBrushes);
    public void CheckPlanes(int count) => Check("MAX_MAP_PLANES", count, MaxPlanes);
    public void CheckNodes(int count) => Check("MAX_MAP_NODES", count, MaxNodes);
    public void CheckLeaves(int count) => Check("MAX_MAP_LEAFS", count, MaxLeaves);

    private static void Check(string name, int count, int max)
    {
        if (count > max)
            throw new BspLimitException($"BSP limit exceeded: {name} = {max}, {count} required. Reduce the collision geometry; no partial BSP is written.", collisionRelated: true);
    }
}
