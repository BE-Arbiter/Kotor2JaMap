// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using Kotor.Compiler.Bsp;

namespace Kotor.Compiler;

/// <summary>
/// Sous-modele BSP (<c>*1</c>, <c>*2</c>...) : brushes convexes donnes par leurs plans (normale sortante, distance), en unites JA,
/// <b>relatifs a l'<c>origin</c> de l'entite</b> (le moteur decale le mover via son origin), et sans surface visible.
/// <see cref="Shader"/> : shader de collision des cotes (contenu solide).
/// </summary>
public sealed record SubModelInput(IReadOnlyList<IReadOnlyList<BspPlane>> Brushes, string Shader);
