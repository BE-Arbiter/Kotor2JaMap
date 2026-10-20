// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using Kotor.Compiler.Bsp;

namespace Kotor.Compiler;

/// <summary>Contenus de brush de JA (OpenJK-SWGL code/game/surfaceflags.h) utilises par les sous-modeles.</summary>
public static class BspContents
{
    public const int None = 0;
    public const int Solid = 0x1;
    /// <summary>CONTENTS_WATER : le joueur nage (SV_PointContents lit aussi les brushes des entites a sous-modele).</summary>
    public const int Water = 0x4;
    /// <summary>CONTENTS_TRIGGER : brush traversable, vu seulement par le contact d'entite (trigger_hurt...).</summary>
    public const int Trigger = 0x400;
}

/// <summary>
/// Sous-modele BSP (<c>*1</c>, <c>*2</c>...) : brushes convexes donnes par leurs plans (normale sortante, distance), en unites JA,
/// <b>relatifs a l'<c>origin</c> de l'entite</b> (le moteur decale le mover via son origin), et sans surface visible.
/// <see cref="Shader"/> : shader de collision des cotes. <see cref="Contents"/> : contenu des brushes, ecrit dans le lump des shaders
/// (un meme shader ne devrait servir qu'a un seul contenu : les contenus de plusieurs sous-modeles qui le partagent sont reunis par OU).
/// Solide par defaut (portes) : la sortie des cartes sans cette option ne change pas.
/// </summary>
public sealed record SubModelInput(IReadOnlyList<IReadOnlyList<BspPlane>> Brushes, string Shader)
{
    public int Contents { get; init; } = BspContents.Solid;
}
