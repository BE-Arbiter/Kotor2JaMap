namespace Swtor.Pipeline;

/// <summary>
/// Echelle du monde SWTOR : une constante globale (positions ET sommets, ensemble), derivee des donnees de SWTOR.
/// Reference : le personnage humain. Un humain masculin (bmn) debout mesure 0,205 unite GR2 (semelle -0,001, haut du crane 0,205 ; avec
/// les cheveux et le cou ~0,21 ; le depot SWTOR-Character-maker retient 72 / 338 = 0,213 par le joint du cou). Le Character-maker
/// convertit les personnages en modeles JKA avec un facteur uniforme de 338 (cou SWTOR -> joint <c>cervical</c> JKA) : un humain SWTOR
/// devient un modele JKA de 72 unites. Le pipeline KOTOR fait pareil (40 unites par metre, humain de 1,8 m = 72 unites).
/// Recalibrage apres essai en jeu (carte "un peu trop grande") : l humain SWTOR (0,205 unite, sans cheveux) est mis a 64 unites, hauteur de la
/// capsule de collision du joueur JA (et non plus 72 = modele JKA) : 64 / 0,205 = 312 unites JA par unite GR2 (x0,92 par rapport a 338),
/// soit 312 / 40 = 7,8 m (JA) par unite GR2.
/// Les autres donnees SWTOR donnent le meme ordre de grandeur (meubles et rambardes ~7,5 m par unite, hauteur de marche de l'escalier
/// 0,025 unite = 21 cm, points de couvert espaces de 0,18 unite = 1,5 m) : voir T7-echelle-2.md.
/// </summary>
public static class SwtorWorldScale
{
    /// <summary>Hauteur de la capsule de collision du joueur JA (unites JA), reference de l echelle (le modele JKA en fait 72).</summary>
    public const float JaHumanModelHeight = 64f;

    /// <summary>Hauteur d un humain SWTOR debout (unites GR2), semelle a sommet du crane, mesuree sur les .gr2.</summary>
    public const float HumanHeightGr2 = 0.205f;

    /// <summary>Unites JA par unite GR2 : 64 / 0,205 = 312 (le Character-maker utilise 338 pour le modele JKA de 72).</summary>
    public const float JaUnitsPerGr2Unit = 312f;

    /// <summary>Unites par metre de la sortie JA (<c>ExportOptions.Scale</c> par defaut) : la scene est en metres.</summary>
    public const float JaUnitsPerMetre = 40f;

    /// <summary>Metres (JA) par unite GR2 : facteur applique aux positions et aux sommets de la scene (7,8).</summary>
    public const float MetresPerGr2Unit = JaUnitsPerGr2Unit / JaUnitsPerMetre;

    /// <summary>Facteur effectif : la constante derivee fois le multiplicateur manuel (<c>--world-scale</c>, 0 ou moins = 1).</summary>
    public static float Resolve(float manualMultiplier) => MetresPerGr2Unit * (manualMultiplier > 0f ? manualMultiplier : 1f);
}
