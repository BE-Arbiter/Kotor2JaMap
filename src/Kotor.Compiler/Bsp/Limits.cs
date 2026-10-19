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
    int MaxBrushSides = 0x20000, int MaxLeafBrushes = 0x40000, int MaxLeafSurfaces = 0x20000, long MaxRtxPrimitives = long.MaxValue,
    int MaxDrawVertices = 0x80000, int MaxDrawIndices = 0x80000)
{
    public const int MaxDrawIndexes = 0x80000;

    /// <summary>
    /// Estimation des primitives du lancer de rayons de rdsp-vulkan (rtx/vk_rtx_bsp.cpp, vk_rtx_estimate_geometry) : somme, sur chaque entree de leafsurface,
    /// des triangles de la surface, soit environ (feuilles listant la surface) x (triangles). Le moteur en fait un calloc de 208 octets par primitive puis un memset ;
    /// si l'allocation echoue (limite de commit, ~110 Gio ici) le memset ecrit en NULL et le jeu plante dans VCRUNTIME140 (war_planworking : 747 millions, 145 Gio).
    /// </summary>
    public const long RtxPrimitiveBytes = 208;

    /// <summary>Seuil d'echec du profil swgl : 500 millions de primitives (~97 Gio), au-dela de la limite de commit d'un PC courant. Mesure : korr_m35aa (KOTOR, 460 millions) charge.</summary>
    public const long RtxPrimitiveCrashThreshold = 500_000_000;

    /// <summary>Seuil d'avertissement (RTX_ESTIMATE_HIGH) : 50 millions de primitives (~10 Gio de memoire commitee).</summary>
    public const long RtxPrimitiveWarnThreshold = 50_000_000;

    /// <summary>Limites du moteur JA vanilla (qfiles.h).</summary>
    public static Limits Vanilla { get; } = new(0x8000, 0x20000, 0x20000, 0x20000, 0x20000, 0x40000, 0x20000);

    /// <summary>Profil SWGL : cm_load.cpp alloue brushes, plans, cotes, noeuds, feuilles et leafbrushes dynamiquement (rien n est verifie au chargement) ; R_LoadMarksurfaces (rd-vulkan/tr_bsp.cpp) alloue aussi les leafsurfaces d apres le lump, sans controle.
    /// Leafsurfaces a 0x200000 : les trois chargeurs (code/rd-vulkan, code/rd-vanilla, shared/rd-rend2 R_LoadMarksurfaces) et CMod_LoadLeafSurfaces
    /// (code/qcommon/cm_load.cpp) allouent count entrees d apres la taille du lump, sans borne ; MAX_MAP_LEAFFACES de qfiles.h n y est pas utilise.
    /// Necessaire a l assemblage end_m01aa + end_m01ab (1 242 104 leaf faces), essaye en jeu.
    /// Le reste (shaders, surfaces, modeles) reste a la valeur vanilla.
    /// Sommets et index de rendu (MAX_MAP_DRAW_VERTS / MAX_MAP_DRAW_INDEXES, 0x80000 chacun) : ces constantes de qfiles.h ne servent qu au compilateur q3map2 ;
    /// les chargeurs de rendu d OpenJK (rd-vanilla et rd-rend2, R_LoadSurfaces) tirent le nombre de sommets et d index de la taille du lump et allouent par surface,
    /// sans les utiliser. Le profil swgl ne les applique donc pas (bornes larges : le lump doit tenir dans un int32). Les limites par surface (SHADER_MAX_VERTEXES / INDEXES) restent verifiees.</summary>
    public static Limits Swgl { get; } = new(0x100000, 0x100000, 0x100000, 0x100000, 0x100000, 0x100000, 0x200000, MaxRtxPrimitives: RtxPrimitiveCrashThreshold,
        MaxDrawVertices: 0x1000000, MaxDrawIndices: 0x4000000);

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
