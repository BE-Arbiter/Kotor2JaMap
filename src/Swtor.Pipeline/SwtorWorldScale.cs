namespace Swtor.Pipeline;

/// <summary>
/// Facteur d'echelle du monde SWTOR (positions ET sommets, ensemble) pour obtenir des metres reels.
/// Mesure sur war_planworking (kit <c>spc_space/arch</c>, "spc_arch_ss") : plafond des couloirs 1,5 unite, cadre de porte 1,1, fenetre 1,1,
/// caisses 0,15, colonnes 1,4 a 2,3, tout pour un joueur humain de 1,8 m : les modeles sont a environ la moitie de leur taille reelle
/// (les positions sont sur la grille de 3,2 comme les sommets, donc le facteur s'applique aux deux). Aucun champ de gr2, room ou area.dat
/// ne porte ce facteur (verifie : en-tete gr2, champs d'instance, proprietes d'area) ; la valeur est une estimation de proportions humaines
/// (plafond 3 m, porte 2,2 m), pas une constante lue dans le jeu : <c>--world-scale</c> permet de l'ajuster.
/// </summary>
public static class SwtorWorldScale
{
    /// <summary>Facteur des zones construites avec le kit <c>spc_space/arch</c>.</summary>
    public const float SpaceKit = 2f;

    /// <summary>Facteur automatique : <see cref="SpaceKit"/> si la majorite des modeles d'architecture viennent de <c>spc_space/arch</c>, sinon 1.</summary>
    public static float Detect(IEnumerable<string?> assetPaths)
    {
        int arch = 0, space = 0;
        foreach (string? p in assetPaths)
        {
            if (p is null) continue;
            string n = p.Replace('\\', '/');
            if (!n.Contains("/arch/", StringComparison.OrdinalIgnoreCase)) continue;
            arch++;
            if (n.Contains("/art/static/area/spc_space/arch/", StringComparison.OrdinalIgnoreCase)) space++;
        }
        return arch > 0 && space * 2 > arch ? SpaceKit : 1f;
    }
}
