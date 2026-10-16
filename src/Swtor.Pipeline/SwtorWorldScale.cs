namespace Swtor.Pipeline;

/// <summary>
/// Echelle du monde SWTOR : une constante globale (positions ET sommets, ensemble), derivee des donnees de SWTOR.
/// Reference : le personnage humain. Un humain masculin (bmn) debout mesure 0,205 unite GR2 (semelle -0,001, haut du crane 0,205 ; avec
/// les cheveux et le cou ~0,21 ; le depot SWTOR-Character-maker retient 72 / 338 = 0,213 par le joint du cou). Le Character-maker
/// convertit les personnages en modeles JKA avec un facteur uniforme de 338 (cou SWTOR -> joint <c>cervical</c> JKA) : un humain SWTOR
/// devient un modele JKA de 72 unites. Le pipeline KOTOR fait pareil (40 unites par metre, humain de 1,8 m = 72 unites).
/// Ici : 338 unites JA par unite GR2, soit 338 / 40 = 8,45 m (JA) par unite GR2.
/// Les autres donnees SWTOR donnent le meme ordre de grandeur (meubles et rambardes ~7,5 m par unite, hauteur de marche de l'escalier
/// 0,025 unite = 21 cm, points de couvert espaces de 0,18 unite = 1,5 m) : voir T7-echelle-2.md.
/// </summary>
public static class SwtorWorldScale
{
    /// <summary>Hauteur JKA d'un modele humain (unites JA) : celle du Character-maker et de 40 u/m x 1,8 m.</summary>
    public const float JaHumanModelHeight = 72f;

    /// <summary>Hauteur d'un humain SWTOR debout (unites GR2) : joint du cou du Character-maker, 72 / 338.</summary>
    public const float HumanHeightGr2 = 0.213f;

    /// <summary>Facteur uniforme du Character-maker (SWTOR -> JKA) : unites JA par unite GR2.</summary>
    public const float JaUnitsPerGr2Unit = 338f;

    /// <summary>Unites par metre de la sortie JA (<c>ExportOptions.Scale</c> par defaut) : la scene est en metres.</summary>
    public const float JaUnitsPerMetre = 40f;

    /// <summary>Metres (JA) par unite GR2 : facteur applique aux positions et aux sommets de la scene (environ 8,45).</summary>
    public const float MetresPerGr2Unit = JaUnitsPerGr2Unit / JaUnitsPerMetre;

    /// <summary>Facteur effectif : la constante derivee fois le multiplicateur manuel (<c>--world-scale</c>, 0 ou moins = 1).</summary>
    public static float Resolve(float manualMultiplier) => MetresPerGr2Unit * (manualMultiplier > 0f ? manualMultiplier : 1f);
}
