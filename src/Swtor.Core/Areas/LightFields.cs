using System.Globalization;
using System.Numerics;

namespace Swtor.Core.Areas;

/// <summary>Type de lumiere (champ <c>0x36A29A30</c>) : 1 = autre/etendue (echelle jusqu'a 600 m), 2 = volume ponctuel (92 %), 3 = projecteur (60 % ont la rampe <c>falloff_spotlight</c>).</summary>
public enum SwtorLightType
{
    Unknown = 0,
    Wide = 1,
    Point = 2,
    Spot = 3,
}

/// <summary>
/// Parametres decodes d'une instance <c>\engine\light.lit</c>. Decodage empirique sur 118 810 lumieres des 267 zones (voir swtor-formats.md) :
/// aucun nom de champ n'est stocke (hash seulement), la signification vient des distributions de valeurs, pas d'une specification.
/// </summary>
/// <param name="Type">Champ <c>0x36A29A30</c> (confiance moyenne : le projecteur se confirme par la rampe de falloff).</param>
/// <param name="Brightness">Champ texte <c>0xBC68CFF3</c> (1 415 valeurs distinctes, mediane 0,9, max 100) : multiplicateur de luminosite (confiance moyenne-basse).</param>
/// <param name="Gain">Champ flottant <c>0xEA6E4F13</c> (0,707 / 0,82 / 1 pour 87 %) : coefficient secondaire (confiance basse).</param>
/// <param name="Extent">Echelle de l'objet (X, Y, Z) en metres : etendue du volume d'eclairage (confiance moyenne).</param>
/// <param name="FalloffRamp">Rampe d'attenuation le long de l'axe de la lumiere (<c>art/lights/falloff_*.dds</c>, 1D, 64 px), sans extension ; confiance haute pour le nom, la courbe est lisible dans le DDS.</param>
/// <param name="ShapeRamp">Profil de section (<c>art/lights/lum_*.dds</c>, 64 x 64).</param>
/// <param name="ColorRamp">Rampe de couleur (<c>fill_RRR_GGG_BBB</c> ou <c>defaultLightRamp</c>).</param>
public sealed record LightParams(SwtorLightType Type, float Brightness, float Gain, Vector3 Extent, string? FalloffRamp, string? ShapeRamp, string? ColorRamp)
{
    public bool IsSpot => Type == SwtorLightType.Spot || string.Equals(FalloffRamp, "falloff_spotlight", StringComparison.OrdinalIgnoreCase);

    /// <summary>Plus grande dimension du volume, en metres.</summary>
    public float MaxExtent => MathF.Max(Extent.X, MathF.Max(Extent.Y, Extent.Z));
}

/// <summary>Lecture des champs de lumiere d'une instance de room.</summary>
public static class LightFields
{
    public const uint LightType = 0x36A29A30;
    public const uint BrightnessText = 0xBC68CFF3;
    public const uint Gain = 0xEA6E4F13;
    public const uint FalloffRamp = 0x3B92D7B4;
    public const uint ShapeRamp = 0x6C84A1CD;
    public const uint ColorRamp = 0x429C878A;

    /// <summary>Parametres d'une lumiere ; valeurs manquantes ou illisibles : luminosite 1, gain 1, type inconnu.</summary>
    public static LightParams Read(RoomInstance light)
    {
        ArgumentNullException.ThrowIfNull(light);
        int type = 0;
        float brightness = 1f, gain = 1f;
        string? falloff = null, shape = null, color = null;
        foreach (RoomField f in light.Fields)
        {
            switch (f.Hash)
            {
                case LightType when f.Type == 3 && f.Value is int t: type = t; break;
                case BrightnessText when f.Type == 8 && f.Value is string s && TryParse(s, out float b): brightness = b; break;
                case Gain when f.Type == 4 && f.Value is float g && float.IsFinite(g): gain = g; break;
                case FalloffRamp when f.Type == 8 && f.Value is string s: falloff = RampName(s); break;
                case ShapeRamp when f.Type == 8 && f.Value is string s: shape = RampName(s); break;
                case ColorRamp when f.Type == 8 && f.Value is string s: color = RampName(s); break;
            }
        }
        return new LightParams(type is >= 1 and <= 3 ? (SwtorLightType)type : SwtorLightType.Unknown, brightness, gain, light.Scale, falloff, shape, color);
    }

    /// <summary>Nombre decimal avec point (".5", "1.5", "2") ; faux si illisible ou non fini.</summary>
    internal static bool TryParse(string s, out float v) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && float.IsFinite(v);

    /// <summary>Nom de rampe sans dossier ni extension ("art\lights\fill_1" ou "art/lights/fill_1" donnent "fill_1").</summary>
    internal static string? RampName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string n = path.Replace((char)92, '/');
        n = n[(n.LastIndexOf('/') + 1)..];
        int dot = n.LastIndexOf('.');
        return dot > 0 ? n[..dot] : n;
    }
}

/// <summary>Brouillard statique d'une zone (cles <c>static_fog_*</c>).</summary>
public sealed record AreaFog(Vector3 Color, float Min, float Max, float Start, float End);

/// <summary>
/// Reglages d'eclairage d'une zone (cle <c>Area</c> de la section de proprietes d'area.dat). <see cref="SunTravel"/> : direction de propagation
/// de la lumiere directionnelle en repere SWTOR-vers-scene Z haut (azimut / polaire en degres ; polaire superieur a 90 = vers le bas) ; confiance basse
/// pour l'orientation horizontale (aucune zone de reference visible).
/// </summary>
public sealed record AreaLighting(bool IsOutdoor, Vector3 Front, Vector3 Side, Vector3 Back, float Intensity, Vector3 SunTravel, AreaFog? Fog)
{
    /// <summary>Valeurs par defaut quand les proprietes manquent : interieur, front blanc, side gris 0,5, back noir.</summary>
    public static AreaLighting Default { get; } = new(false, Vector3.One, new Vector3(0.5f), Vector3.Zero, 1f, Vector3.Normalize(new Vector3(0.3f, 0.2f, -1f)), null);

    public static AreaLighting From(AreaProperties p)
    {
        ArgumentNullException.ThrowIfNull(p);
        Vector3 Col(string key, Vector3 d) => p.GetColor(key) is { } c ? new Vector3(c.R, c.G, c.B) : d;
        float azimuth = p.GetFloat("light_azimuth") ?? 0f, polar = p.GetFloat("light_polar") ?? 120f;
        float a = azimuth * MathF.PI / 180f, pl = polar * MathF.PI / 180f;
        var travel = new Vector3(MathF.Sin(pl) * MathF.Cos(a), MathF.Sin(pl) * MathF.Sin(a), MathF.Cos(pl));
        travel = travel.LengthSquared() > 1e-9f ? Vector3.Normalize(travel) : Default.SunTravel;

        AreaFog? fog = null;
        float fogMax = p.GetFloat("static_fog_max") ?? 0f;
        if (fogMax > 0f && p.GetColor("static_fog_colors") is { } fc)
            fog = new AreaFog(new Vector3(fc.R, fc.G, fc.B), p.GetFloat("static_fog_min") ?? 0f, fogMax,
                p.GetFloat("static_fog_start") ?? p.GetFloat("static_fog_colors_start") ?? 0f, p.GetFloat("static_fog_colors_end") ?? p.GetFloat("static_fog_ramp") ?? 0f);
        return new AreaLighting(p.GetBool("is_outdoor") ?? false, Col("static_light_front", Vector3.One), Col("static_light_side", new Vector3(0.5f)),
            Col("static_light_back", Vector3.Zero), p.GetFloat("static_light_intensity") ?? 1f, travel, fog);
    }
}
