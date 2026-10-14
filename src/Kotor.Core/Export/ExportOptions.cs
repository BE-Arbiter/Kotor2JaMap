namespace Kotor.Core.Export;

/// <summary>Options d'export : dossiers, echelle KOTOR (metres) vers JA (unites Quake), luminosite des lightmaps et profil de limites BSP.</summary>
public sealed record ExportOptions(string KotorDir, string JaBaseDir, string WorkDir, float Scale = ExportOptions.DefaultScale,
    float LightmapBrightness = 1.0f, string BspLimits = ExportOptions.DefaultBspLimits)
{
    public const float DefaultScale = 36f;
    public const string DefaultBspLimits = "swgl";

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
