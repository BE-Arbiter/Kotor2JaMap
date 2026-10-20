namespace Kotor.Core.Export;

/// <summary>
/// Options d'export : dossiers, echelle KOTOR (metres) vers JA (unites Quake), luminosite des lightmaps et profil de limites BSP.
/// <see cref="KotorDir"/> est le dossier du jeu choisi (<see cref="Game"/>). <see cref="Kotor2Dir"/> garde le dossier de KOTOR 2
/// dans les options de base ; <see cref="ForGame"/> en tire les options d'un jeu.
/// <see cref="SwtorDir"/> : racine des ressources SWTOR extraites (cle swtorAssetsDir, variable SWTOR_ASSETS).
/// <see cref="JediKnightDir"/> : dossier d'installation de Star Wars Jedi Knight (cle jediKnightDir, variable JK_DIR).
/// <see cref="MapVisuals"/> : vrai (defaut) = ecrire aussi le .map d'edition avec la geometrie visible texturee (dossier de travail, jamais dans le pk3).
/// <see cref="NoVis"/> : vrai = pas de visibilite par piece (un seul cluster toujours visible, comme q3map2 -novis).
/// <see cref="SwtorFromGame"/> : vrai = lire les zones SWTOR directement dans les archives du jeu (<see cref="SwtorGameDir"/>, detecte si vide) avec un cache dans le dossier de travail ; faux = dossier extrait (<see cref="SwtorDir"/>).
/// </summary>
public sealed record ExportOptions(string KotorDir, string JaBaseDir, string WorkDir, float Scale = ExportOptions.DefaultScale,
    float LightmapBrightness = 1.0f, string BspLimits = ExportOptions.DefaultBspLimits,
    KotorGame Game = KotorGame.Kotor1, string? Kotor2Dir = null, string? SwtorDir = null, bool NoVis = false,
    string? SwtorGameDir = null, bool SwtorFromGame = false, string? JediKnightDir = null, bool MapVisuals = true)
{
    public const float DefaultScale = 40f;
    public const string DefaultBspLimits = "swgl";

    /// <summary>
    /// Options d'un jeu a partir des options de base (dont <see cref="KotorDir"/> est le dossier de KOTOR). Pour KOTOR 2, le dossier
    /// est <see cref="Kotor2Dir"/> (chaine vide s'il n'est pas configure).
    /// </summary>
    public ExportOptions ForGame(KotorGame game) => game switch
    {
        KotorGame.Kotor2 => this with { Game = KotorGame.Kotor2, KotorDir = Kotor2Dir ?? "" },
        KotorGame.Swtor => this with { Game = KotorGame.Swtor }, // SWTOR reads its own extracted assets folder, not KotorDir
        KotorGame.JediKnight => this with { Game = KotorGame.JediKnight }, // JK reads its own folder (JediKnightDir), not KotorDir
        _ => this with { Game = KotorGame.Kotor1 },
    };

    /// <summary>Message d'erreur si les options sont inutilisables (echelle ou luminosite non finie ou &lt;= 0, profil de limites inconnu), sinon null.</summary>
    public string? Validate()
    {
        if (!float.IsFinite(Scale) || Scale <= 0f)
            return $"Invalid scale: {Scale} (must be a finite number > 0).";
        if (!float.IsFinite(LightmapBrightness) || LightmapBrightness <= 0f)
            return $"Invalid lightmap brightness: {LightmapBrightness} (must be a finite number > 0).";
        string p = (BspLimits ?? "").Trim().ToLowerInvariant();
        if (p is not ("swgl" or "vanilla"))
            return $"Unknown BSP limits profile: {BspLimits} (expected: swgl or vanilla).";
        return null;
    }
}
