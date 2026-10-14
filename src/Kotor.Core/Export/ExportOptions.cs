namespace Kotor.Core.Export;

/// <summary>Options d'export : dossiers, echelle KOTOR (metres) vers JA (unites Quake) et luminosite des lightmaps.</summary>
public sealed record ExportOptions(string KotorDir, string JaBaseDir, string WorkDir, float Scale = ExportOptions.DefaultScale,
    float LightmapBrightness = 1.0f)
{
    public const float DefaultScale = 36f;
}
