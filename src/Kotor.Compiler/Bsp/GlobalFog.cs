// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md. Structure dfog_t relevee sur les BSP d'origine de JA (aucun code de q3map2).
using System.Globalization;
using Kotor.Core.Export;

namespace Kotor.Compiler.Bsp;

/// <summary>
/// Brouillard global : une entree du lump FOGS avec <c>brushNum = -1</c> et <c>visibleSide = -1</c> (releve dans hoth2, ffa4,
/// t1_rail, taspir1, vjun1...), et <c>fogNum = 0</c> (index dans FOGS) sur toutes les surfaces, ciel compris. Le shader de brouillard
/// (<c>fogParms</c>) est ecrit dans le .shader du niveau par l'export ; ici seul son nom va dans le BSP.
/// </summary>
/// <param name="Shader">Nom complet du shader de brouillard (<see cref="ShaderNames.FogName"/>).</param>
public sealed record GlobalFog(string Shader)
{
    public const string WorldspawnKey = "_tfu_fog";

    /// <summary>
    /// Lit la cle <c>_tfu_fog</c> ("r g b distance") du worldspawn. Cle absente ou illisible (pas 4 nombres, distance &lt;= 0,
    /// couleur negative) = pas de brouillard, car sans le shader de l'export le nom ne designerait rien.
    /// </summary>
    public static GlobalFog? FromWorldspawn(IReadOnlyDictionary<string, string>? world, string level)
    {
        if (world is null) return null;
        foreach (var kv in world)
        {
            if (!string.Equals(kv.Key, WorldspawnKey, StringComparison.OrdinalIgnoreCase)) continue;
            var parts = kv.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) return null;
            var f = new float[4];
            for (int i = 0; i < 4; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i]) || !float.IsFinite(f[i])) return null;
            if (f[0] < 0 || f[1] < 0 || f[2] < 0 || f[3] <= 0) return null;
            return new GlobalFog(ShaderNames.FogName(level));
        }
        return null;
    }
}
