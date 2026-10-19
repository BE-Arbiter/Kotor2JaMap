namespace Jk.Pipeline;

/// <summary>
/// Echelle du monde JK : une constante unique (positions ET sommets), la scene est en « metres » comme KOTOR et SWTOR
/// (<c>ExportOptions.Scale</c> = 40 unites JA par metre reste celui du pipeline).
/// Mesures sur les donnees (01narshadda et les autres niveaux) :
/// <list type="bullet">
/// <item>Le joueur (<c>walkplayer</c>) a son origine a 0,120 unite JK au-dessus du sol (12 departs sur 12) et <c>eyeoffset</c> z = 0,037 :
/// les yeux sont a 0,157 du sol. Sa sphere de collision a un rayon de 0,065 : sommet de la tete ~ 0,120 + 0,065 = 0,185.</item>
/// <item>Hauteur des passages (surfaces d'adjoin verticales) : les plus bas mesurent 0,20 ; les portes courantes 0,25 a 0,35 ; salles 0,30 a 0,60.</item>
/// <item>Marches (contremarches reliant deux surfaces horizontales) : 0,02 a 0,10 ; 01narshadda : 0,02 (20), 0,03 (36), 0,05 (12), 0,07 (48), 0,10 (46).</item>
/// </list>
/// Contrainte decisive : le sommet de la tete du joueur JA (capsule de 64 u) doit tenir dans la plus petite ouverture que le joueur JK
/// franchit, soit 64 / 0,185 = 346 u par unite JK. Les yeux (60 u pour JA) donneraient 382 ; les ouvertures de 0,20 limitent a 320.
/// Retenu : 344 u JA par unite JK (8,6 m a 40 u/m) : joueur JK = 64 u (tete 63,6 u), yeux 54 u, ouverture de 0,20 = 69 u, porte de 0,30 = 103 u
/// (1,6 fois le joueur), marche de 0,05 = 17 u (franchissable avec <see cref="JaStepSize"/>).
/// Les marches plus hautes (0,07 = 24 u, 0,10 = 34 u) depassent le pas de 18 u de JA : voir <see cref="JkStairRamps"/> (rampe de collision).
/// </summary>
public static class JkWorldScale
{
    /// <summary>Hauteur de la capsule de collision du joueur JA (unites JA).</summary>
    public const float JaPlayerHeight = 64f;

    /// <summary>Hauteur de marche franchissable par le joueur JA (STEPSIZE de bg_public.h), unites JA.</summary>
    public const float JaStepSize = 18f;

    /// <summary>Origine du joueur JK au-dessus du sol (unites JK), mesuree sur les departs des niveaux.</summary>
    public const float PlayerOriginAboveFloor = 0.12f;

    /// <summary>Yeux du joueur JK au-dessus du sol : origine + <c>eyeoffset</c> (0,037).</summary>
    public const float PlayerEyeAboveFloor = 0.157f;

    /// <summary>Sommet de la tete du joueur JK au-dessus du sol : origine + rayon de la sphere de collision (0,065).</summary>
    public const float PlayerTopAboveFloor = 0.185f;

    /// <summary>Unites par metre de la sortie JA (<c>ExportOptions.Scale</c> par defaut) : la scene est en metres.</summary>
    public const float JaUnitsPerMetre = 40f;

    /// <summary>Unites JA par unite JK (344 : la tete du joueur JK, 0,185, fait 63,6 u, un peu moins que la capsule JA de 64 u).</summary>
    public const float JaUnitsPerJkUnit = 344f;

    /// <summary>Metres de la scene par unite JK : facteur applique aux positions et aux sommets (8,6).</summary>
    public const float MetresPerJkUnit = JaUnitsPerJkUnit / JaUnitsPerMetre;
}
