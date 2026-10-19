using System.Globalization;
using System.Numerics;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Swtor.Core;
using Swtor.Core.Dds;
using Swtor.Core.Xml;

namespace Swtor.Pipeline;

/// <summary>Materiau SWTOR resolu : cle de scene (nom de texture exporte), opacite, echelle UV. <c>Hidden</c> : materiau de collision, jamais rendu.</summary>
internal sealed record SwtorMaterialInfo(string Key, SurfaceAlpha Alpha, Vector2 UvScale, bool Hidden, bool NoCollision = false);

/// <summary>Role d'un materiau : opacite JA, rendu supprime (<c>Hidden</c>), exclu de la collision, et code de journal si le materiau est ignore faute de pouvoir le reproduire.</summary>
internal sealed record SwtorMaterialClass(SurfaceAlpha Alpha, bool Hidden, bool NoCollision, string? SkipCode, string? SkipReason);

/// <summary>
/// Resout les noms de materiaux d'un .gr2 : .mat, DiffuseMap, DDS, <see cref="KotorTexture"/>. La cle de scene est le nom du materiau
/// (minuscules, raccourci si besoin) : chaque materiau a son shader et son TGA, ce qui evite les conflits d'opacite entre materiaux
/// qui partageraient une meme image. Les erreurs sont journalisees avec des codes stables et ne levent pas.
/// </summary>
internal sealed class SwtorMaterials(ISwtorModelSource source, int maxTextureSize, ImportLog log)
{
    private readonly Dictionary<string, SwtorMaterialInfo> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Textures decodees par cle de scene (null : introuvable ou illisible, damier de remplacement).</summary>
    public Dictionary<string, KotorTexture?> Textures { get; } = new(StringComparer.Ordinal);

    public SwtorMaterialInfo Resolve(string materialName)
    {
        if (_cache.TryGetValue(materialName, out var cached)) return cached;
        var info = Load(materialName);
        _cache[materialName] = info;
        return info;
    }

    private readonly Dictionary<string, bool> _additive = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Le materiau n'a pas de collision (additif, effet, supprime et non solide) : lit seulement le .mat, sans charger de texture. Absent ou illisible : faux.</summary>
    public bool IsNonSolid(string materialName)
    {
        if (_cache.TryGetValue(materialName, out var cached)) return cached.NoCollision || cached.Alpha == SurfaceAlpha.Additive;
        if (_additive.TryGetValue(materialName, out bool known)) return known;
        bool add = false;
        if (!materialName.StartsWith("util_collision", StringComparison.OrdinalIgnoreCase))
        {
            try { add = source.LoadMaterial(materialName) is { } mat && Classify(materialName, mat) is var c && (c.NoCollision || c.Alpha == SurfaceAlpha.Additive); }
            catch (GameFormatException) { add = false; }
        }
        _additive[materialName] = add;
        return add;
    }

    private SwtorMaterialInfo Load(string name)
    {
        string key = ShaderNames.Short(Sanitize(name), KotorGame.Swtor);
        if (name.StartsWith("util_collision", StringComparison.OrdinalIgnoreCase))
            return new SwtorMaterialInfo(key, SurfaceAlpha.Opaque, Vector2.One, true);

        MaterialDef? mat;
        try { mat = source.LoadMaterial(name); }
        catch (GameFormatException ex)
        {
            log.Warn("SWTOR_MATERIAL_UNREADABLE", $"{name}: {ex.Message}");
            Textures[key] = null;
            return new SwtorMaterialInfo(key, SurfaceAlpha.Opaque, Vector2.One, false);
        }
        if (mat is null)
        {
            log.Warn("SWTOR_MATERIAL_MISSING", $"{name}: .mat not found, checkerboard texture used.");
            Textures[key] = null;
            return new SwtorMaterialInfo(key, SurfaceAlpha.Opaque, Vector2.One, false);
        }

        SwtorMaterialClass cls = Classify(name, mat);
        if (cls.SkipCode is not null) log.Warn(cls.SkipCode, $"{name}: {cls.SkipReason}");
        if (cls.Hidden) return new SwtorMaterialInfo(key, cls.Alpha, Vector2.One, true, cls.NoCollision);
        SurfaceAlpha alpha = cls.Alpha;
        Vector2 uvScale = ParseUvScale(mat);
        string? diffuse = mat.DiffuseMap;
        if (diffuse is null)
        {
            log.Warn("SWTOR_TEXTURE_MISSING", $"{name}: material without DiffuseMap, checkerboard texture used.");
            Textures[key] = null;
            return new SwtorMaterialInfo(key, alpha, uvScale, false);
        }
        DdsImage? img;
        try { img = source.LoadTexture(diffuse); }
        catch (GameFormatException ex)
        {
            log.Warn("SWTOR_TEXTURE_UNSUPPORTED", $"{name}: {diffuse}.dds: {ex.Message}");
            Textures[key] = null;
            return new SwtorMaterialInfo(key, alpha, uvScale, false);
        }
        if (img is null)
        {
            log.Warn("SWTOR_TEXTURE_MISSING", $"{name}: {diffuse}.dds not found, checkerboard texture used.");
            Textures[key] = null;
            return new SwtorMaterialInfo(key, alpha, uvScale, false);
        }

        byte[] rgba = img.Rgba;
        int w = img.Width, h = img.Height;
        while (maxTextureSize > 0 && (w > maxTextureSize || h > maxTextureSize) && w > 1 && h > 1)
            (rgba, w, h) = HalfSize(rgba, w, h);
        bool glass = string.Equals(mat.Shader, "Glass", StringComparison.OrdinalIgnoreCase);
        bool hasAlpha = alpha is SurfaceAlpha.Cutout or SurfaceAlpha.Blend;
        if (glass) Fill(rgba, 110);                 // verre : translucide uniforme
        else if (alpha == SurfaceAlpha.Additive) PremultiplyAlpha(rgba);   // additif : l'alpha de la texture est le masque de la lueur (sinon le carton entier est ajoute)
        else if (!hasAlpha) Fill(rgba, 255);        // opaque : l'alpha d'une texture Uber n'est pas de l'opacite (brillance, emissif)
        Textures[key] = new KotorTexture(w, h, rgba, hasAlpha, TxiInfo.Empty);
        if (mat.AlphaMode.Equals("Multiply", StringComparison.OrdinalIgnoreCase))
            log.Warn("SWTOR_ALPHA_MODE_APPROX", $"{name}: alpha mode Multiply not supported, rendered as a translucent surface.");
        return new SwtorMaterialInfo(key, alpha, uvScale, false);
    }

    /// <summary>Noms (sous-chaines) des cartons de lumiere SWTOR : faisceaux et cones de brouillard lumineux.</summary>
    private static readonly string[] LightCardNames = ["fog_cone", "light_cone", "lightcone", "light_shaft", "lightshaft", "lightbeam", "light_beam", "godray", "god_ray"];

    /// <summary>
    /// Classe un materiau SWTOR. Regles : <c>Visibility=Hidden</c> ou <c>util_collision*</c> : jamais dessine ; texture de remplissage <c>fx_transparent</c> : jamais dessine (la texture est entierement transparente) ;
    /// AnimatedVFX additif nomme comme un faisceau/cone de lumiere : ignore (code <c>SWTOR_VFX_SKIPPED</c>) car son aspect vient de masques animes et d'un fresnel qu'un shader JA statique ne reproduit pas ;
    /// le reste suit <see cref="AlphaOf"/>. Les surfaces cachees avec <c>PolyType</c> Ignore, les effets et les additifs n'ont pas de collision.
    /// </summary>
    internal static SwtorMaterialClass Classify(string name, MaterialDef mat)
    {
        SurfaceAlpha alpha = AlphaOf(mat);
        bool ignoreCollide = mat.PolyType.Equals("Ignore", StringComparison.OrdinalIgnoreCase);
        if (mat.Visibility.Equals("Hidden", StringComparison.OrdinalIgnoreCase))
            return new SwtorMaterialClass(alpha, true, ignoreCollide, null, null);
        string lower = name.ToLowerInvariant();
        if (lower.StartsWith("fx_transparent", StringComparison.Ordinal) || (mat.DiffuseMap?.Contains("fx_transparent", StringComparison.OrdinalIgnoreCase) ?? false))
            return new SwtorMaterialClass(alpha, true, true, null, null);
        if (alpha == SurfaceAlpha.Additive && mat.Shader.StartsWith("AnimatedVFX", StringComparison.OrdinalIgnoreCase) && LightCardNames.Any(lower.Contains))
            return new SwtorMaterialClass(SurfaceAlpha.Additive, true, true, "SWTOR_VFX_SKIPPED",
                "light cone / beam card (AnimatedVFX, additive with animated masks and fresnel fade) not reproducible by a static JA shader, skipped instead of drawn as a flat bright card.");
        return new SwtorMaterialClass(alpha, false, alpha == SurfaceAlpha.Additive, null, null);
    }

    private static SurfaceAlpha AlphaOf(MaterialDef mat)
    {
        if (string.Equals(mat.Shader, "Glass", StringComparison.OrdinalIgnoreCase)) return SurfaceAlpha.Blend;
        return mat.AlphaMode.ToLowerInvariant() switch
        {
            "test" => SurfaceAlpha.Cutout,
            "full" or "multiply" => SurfaceAlpha.Blend,
            "add" => SurfaceAlpha.Additive,
            _ => SurfaceAlpha.Opaque,
        };
    }

    private static Vector2 ParseUvScale(MaterialDef mat)
    {
        string? v = mat.Inputs.TryGetValue("UvScaling", out var a) ? a : null;
        if (v is null) return Vector2.One;
        string[] p = v.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (p.Length >= 2 && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            && float.IsFinite(x) && float.IsFinite(y) && x > 0 && y > 0)
            return new Vector2(x, y);
        return Vector2.One;
    }

    /// <summary>Nom de fichier et de shader sur : minuscules, <c>[a-z0-9_]</c> seulement.</summary>
    internal static string Sanitize(string name) =>
        new(name.ToLowerInvariant().Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' ? c : '_').ToArray());

    /// <summary>rgb *= alpha, puis alpha = 255 (la texture additive n'a plus besoin de canal alpha).</summary>
    private static void PremultiplyAlpha(byte[] rgba)
    {
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            int a = rgba[i + 3];
            rgba[i] = (byte)(rgba[i] * a / 255); rgba[i + 1] = (byte)(rgba[i + 1] * a / 255); rgba[i + 2] = (byte)(rgba[i + 2] * a / 255);
            rgba[i + 3] = 255;
        }
    }

    private static void Fill(byte[] rgba, byte a)
    {
        for (int i = 3; i < rgba.Length; i += 4) rgba[i] = a;
    }

    /// <summary>Reduction par 2 (moyenne 2 x 2 par canal).</summary>
    internal static (byte[] Rgba, int W, int H) HalfSize(byte[] src, int w, int h)
    {
        int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
        var dst = new byte[nw * nh * 4];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
                for (int c = 0; c < 4; c++)
                {
                    int sum = 0;
                    for (int dy = 0; dy < 2; dy++)
                        for (int dx = 0; dx < 2; dx++)
                            sum += src[(Math.Min(h - 1, y * 2 + dy) * w + Math.Min(w - 1, x * 2 + dx)) * 4 + c];
                    dst[(y * nw + x) * 4 + c] = (byte)((sum + 2) / 4);
                }
        return (dst, nw, nh);
    }
}
