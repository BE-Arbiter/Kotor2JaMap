namespace Swtor.Pipeline;

/// <summary>
/// Echelle du monde SWTOR : une constante globale (positions ET sommets, ensemble), calibree cote JA (T7-echelle-3).
/// Historique : x1 (40 u par unite GR2) trop petit ; 338 (humain SWTOR 0,205 -> modele JKA de 72 u) trop grand ; 312 (humain -> 64 u,
/// capsule du joueur) "un peu trop grand". Les accessoires SWTOR ne sont pas a la taille du personnage : assise de chaise 0,06 unite
/// (0,29 humain), table 0,10, marche d'escalier 0,025 (maximum courant 0,058), porte de batiment 0,36 a 0,55 (2 a 2,7 humains).
/// Contraintes JA : marche franchissable (STEPSIZE 18 u), assise ~ genou du joueur (15 a 20 u), porte 1,2 a 1,6 fois le joueur (64 u).
/// Retenu : 250 unites JA par unite GR2 (6,25 m JA) : toutes les vraies marches (&lt;= 0,07 unite) restent &lt;= 18 u, assise ~15 u, porte
/// de batiment 90 a 130 u, humain SWTOR = 51 u (les PNJ SWTOR sont plus petits que le joueur JA).
/// </summary>
public static class SwtorWorldScale
{
    /// <summary>Hauteur de la capsule de collision du joueur JA (unites JA), pour comparaison.</summary>
    public const float JaPlayerHeight = 64f;

    /// <summary>Hauteur de marche franchissable par le joueur JA (STEPSIZE de bg_public.h), unites JA.</summary>
    public const float JaStepSize = 18f;

    /// <summary>Hauteur d un humain SWTOR debout (unites GR2), semelle a sommet du crane, mesuree sur les .gr2.</summary>
    public const float HumanHeightGr2 = 0.205f;

    /// <summary>Unites JA par unite GR2 (250 ; essais precedents : 338, 312).</summary>
    public const float JaUnitsPerGr2Unit = 250f;

    /// <summary>Unites par metre de la sortie JA (<c>ExportOptions.Scale</c> par defaut) : la scene est en metres.</summary>
    public const float JaUnitsPerMetre = 40f;

    /// <summary>Metres (JA) par unite GR2 : facteur applique aux positions et aux sommets de la scene (6,25).</summary>
    public const float MetresPerGr2Unit = JaUnitsPerGr2Unit / JaUnitsPerMetre;

    /// <summary>Facteur effectif : la constante derivee fois le multiplicateur manuel (<c>--world-scale</c>, 0 ou moins = 1).</summary>
    public static float Resolve(float manualMultiplier) => MetresPerGr2Unit * (manualMultiplier > 0f ? manualMultiplier : 1f);
}
