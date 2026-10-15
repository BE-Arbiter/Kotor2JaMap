// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md. Eclairage maison (aucun code de q3map2).
using System.Globalization;
using System.Numerics;
using Kotor.Core.Export;

namespace Kotor.Compiler.Bsp;

/// <summary>
/// Eclairage simple cuit dans les couleurs de sommet : un soleil directionnel plus une ambiante, evalues par sommet
/// a partir de la normale (pas d'ombres, pas de lightmap). Les cles du worldspawn <c>_kotor_sun_dir</c>, <c>_kotor_sun</c>
/// et <c>_kotor_ambient</c> les reglent ; les defauts sont ceux de <see cref="JaDefaults"/>.
/// </summary>
/// <param name="SunDir">Direction de propagation de la lumiere (normalisee), axes Quake.</param>
/// <param name="Sun">Couleur x intensite du soleil (RGB).</param>
/// <param name="Ambient">Couleur ambiante (RGB).</param>
/// <param name="OverbrightShift">Decalage du moteur (<c>r_mapOverBrightBits</c>, 2 par defaut dans JA) : le moteur multiplie les couleurs de sommet du BSP par 2^shift ; on stocke donc la lumiere divisee par 2^shift.</param>
public sealed record VertexLighting(Vector3 SunDir, Vector3 Sun, Vector3 Ambient, int OverbrightShift = VertexLighting.DefaultOverbrightShift)
{
    /// <summary>
    /// Mesure sur les BSP d'origine (assets*.pk3, surfaces eclairees par sommet) : valeur max des sommets, mediane 40 a 100/255,
    /// p95 souvent 255 (satures apres decalage). Compatible avec un decalage x4 (JA : r_mapOverBrightBits = 2) ; en cas de decalage
    /// reel plus faible la scene est simplement plus sombre, jamais saturee.
    /// </summary>
    public const int DefaultOverbrightShift = 2;

    /// <summary>Defauts du compilateur : soleil et ambiante teintes comme DAG_1 (voir <see cref="JaDefaults.SunColor"/> et <see cref="JaDefaults.AmbientColor"/>).</summary>
    public static VertexLighting Default { get; } = new(Vector3.Normalize(JaDefaults.SunDir),
        JaDefaults.SunColor * (float)JaDefaults.Sun, JaDefaults.AmbientColor * (float)JaDefaults.Ambient);

    public static VertexLighting Create(Vector3 sunDir, double sun, double ambient, int shift = DefaultOverbrightShift) =>
        new(sunDir.LengthSquared() > 0 ? Vector3.Normalize(sunDir) : Vector3.Normalize(JaDefaults.SunDir),
            new Vector3((float)sun), new Vector3((float)ambient), shift);

    /// <summary>Lit les cles du worldspawn ; absentes ou illisibles = defauts.</summary>
    public static VertexLighting FromWorldspawn(IReadOnlyDictionary<string, string>? world)
    {
        var d = Default;
        if (world is null) return d;
        Vector3 dir = TryVec(world, CompilerNames.SunDirKey, out var v) && v.LengthSquared() > 0 ? Vector3.Normalize(v) : d.SunDir;
        Vector3 sun = TryColor(world, CompilerNames.SunKey, out var s) ? s : d.Sun;
        Vector3 amb = TryColor(world, CompilerNames.AmbientKey, out var a) ? a : d.Ambient;
        return new VertexLighting(dir, sun, amb, d.OverbrightShift);
    }

    /// <summary>
    /// Couleur RGBA d'un sommet. Face simple : <c>clamp(ambiante + soleil x max(0, n . -dir))</c>.
    /// Double face (<c>cull disable</c>, feuillage decoupe) : <c>abs(n . -dir)</c>, car la face vue peut etre l'envers de la normale ;
    /// sans cela une feuille serait noire (ambiante seule) d'un cote et claire de l'autre selon l'orientation du maillage.
    /// </summary>
    public byte[] Evaluate(Vector3 normal, bool doubleSided = false) => Evaluate(Vector3.Zero, normal, doubleSided);

    /// <summary>
    /// Same as <see cref="Evaluate(Vector3, bool)"/> for a vertex at <paramref name="position"/>: the point lights are added
    /// (linear attenuation <c>1 - d/radius</c>, no shadow; <c>n . l</c> clamped for single-sided faces, <c>|n . l|</c> for double-sided ones).
    /// </summary>
    public byte[] Evaluate(Vector3 position, Vector3 normal, bool doubleSided = false)
    {
        Vector3 n = normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : Vector3.Zero;
        float ndl = Vector3.Dot(n, -SunDir);
        float k = doubleSided ? MathF.Abs(ndl) : MathF.Max(0f, ndl);
        Vector3 total = Ambient + Sun * k;
        foreach (PointLight l in Lights)
        {
            if (l.Radius <= 0f) continue;
            Vector3 toLight = l.Position - position;
            float d = toLight.Length();
            if (d >= l.Radius) continue;
            float att = 1f - d / l.Radius;
            float facing = d < 1e-4f ? 1f : Vector3.Dot(n, toLight / d);
            facing = doubleSided ? MathF.Abs(facing) : MathF.Max(0f, facing);
            total += l.Color * (l.Multiplier * att * facing);
        }
        Vector3 c = Vector3.Clamp(total, Vector3.Zero, Vector3.One) / (1 << OverbrightShift);
        return [ToByte(c.X), ToByte(c.Y), ToByte(c.Z), 255];
    }

    /// <summary>Point lights added to the ambient term (empty = sun and ambient only).</summary>
    public IReadOnlyList<PointLight> Lights { get; init; } = Array.Empty<PointLight>();

    /// <summary>
    /// KOTOR mode: the zone ambient plus the point lights of the rooms, no sun. The sky-less faux sun of the JA defaults is
    /// replaced by the lights of the original module.
    /// </summary>
    public static VertexLighting ForKotor(Vector3 ambient, IReadOnlyList<PointLight> lights, int shift = DefaultOverbrightShift) =>
        new(Vector3.Normalize(JaDefaults.SunDir), Vector3.Zero, ambient, shift) { Lights = lights };

    private static byte ToByte(float f) => (byte)Math.Clamp((int)MathF.Round(f * 255f), 0, 255);

    private static float[]? Floats(IReadOnlyDictionary<string, string> w, string key)
    {
        foreach (var kv in w)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                var parts = kv.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var r = new float[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out r[i])) return null;
                return r;
            }
        return null;
    }

    private static bool TryVec(IReadOnlyDictionary<string, string> w, string key, out Vector3 v)
    {
        var f = Floats(w, key);
        v = f is { Length: 3 } ? new Vector3(f[0], f[1], f[2]) : default;
        return f is { Length: 3 };
    }

    /// <summary>Un nombre (gris) ou trois nombres (RGB).</summary>
    private static bool TryColor(IReadOnlyDictionary<string, string> w, string key, out Vector3 c)
    {
        var f = Floats(w, key);
        c = f switch { { Length: 1 } => new Vector3(f[0]), { Length: 3 } => new Vector3(f[0], f[1], f[2]), _ => default };
        return f is { Length: 1 or 3 } && c.X >= 0 && c.Y >= 0 && c.Z >= 0;
    }
}
