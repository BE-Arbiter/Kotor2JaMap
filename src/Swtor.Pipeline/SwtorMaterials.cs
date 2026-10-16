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
internal sealed record SwtorMaterialInfo(string Key, SurfaceAlpha Alpha, Vector2 UvScale, bool Hidden);

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

        SurfaceAlpha alpha = AlphaOf(mat);
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
        else if (!hasAlpha) Fill(rgba, 255);        // opaque ou additif : l'alpha de la texture ne sert a rien
        Textures[key] = new KotorTexture(w, h, rgba, hasAlpha, TxiInfo.Empty);
        if (mat.AlphaMode.Equals("Multiply", StringComparison.OrdinalIgnoreCase))
            log.Warn("SWTOR_ALPHA_MODE_APPROX", $"{name}: alpha mode Multiply not supported, rendered as a translucent surface.");
        return new SwtorMaterialInfo(key, alpha, uvScale, false);
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
