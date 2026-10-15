namespace Kotor.Core.Export;

/// <summary>
/// Options d'export : dossiers, echelle KOTOR (metres) vers JA (unites Quake), luminosite des lightmaps et profil de limites BSP.
/// <see cref="KotorDir"/> est le dossier du jeu choisi (<see cref="Game"/>). <see cref="Kotor2Dir"/> garde le dossier de KOTOR 2
/// dans les options de base ; <see cref="ForGame"/> en tire les options d'un jeu.
/// </summary>
public sealed record ExportOptions(string KotorDir, string JaBaseDir, string WorkDir, float Scale = ExportOptions.DefaultScale,
    float LightmapBrightness = 1.0f, string BspLimits = ExportOptions.DefaultBspLimits,
    KotorGame Game = KotorGame.Kotor1, string? Kotor2Dir = null)
{
    public const float DefaultScale = 40f;
    public const string DefaultBspLimits = "swgl";

    /// <summary>
    /// Options d'un jeu a partir des options de base (dont <see cref="KotorDir"/> est le dossier de KOTOR). Pour KOTOR 2, le dossier
    /// est <see cref="Kotor2Dir"/> (chaine vide s'il n'est pas configure).
    /// </summary>
    public ExportOptions ForGame(KotorGame game) => game == KotorGame.Kotor2
        ? this with { Game = KotorGame.Kotor2, KotorDir = Kotor2Dir ?? "" }
        : this with { Game = KotorGame.Kotor1 };

    /// <summary>Message d'erreur si les options sont inutilisables (echelle ou luminosite non finie ou &lt;= 0, profil de limites inconnu), sinon null.</summary>
    public string? Validate()
    {
        if (!float.IsFinite(Scale) || Scale <= 0f)
            return $"Echelle invalide : {Scale} (doit etre un nombre fini > 0).";
        if (!float.IsFinite(LightmapBrightness) || LightmapBrightness <= 0f)
            return $"Luminosite des lightmaps invalide : {LightmapBrightness} (doit etre un nombre fini > 0).";
        string p = (BspLimits ?? "").Trim().ToLowerInvariant();
        if (p is not ("swgl" or "vanilla"))
            return $"Profil de limites BSP inconnu : {BspLimits} (attendu : swgl ou vanilla).";
        return null;
    }
}
