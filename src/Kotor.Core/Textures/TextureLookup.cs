using Kotor.Core.Archives;
using Kotor.Core.Export;

namespace Kotor.Core.Textures;

/// <summary>Recherche et decode une texture ou une lightmap. Diffuse : TPC puis TGA. Lightmap : TGA puis TPC.</summary>
public static class TextureLookup
{
    /// <param name="onLightmapRepaired">Called for a lightmap whose invalid texels (alpha != 255) were repaired: (repaired texels, no valid texel at all).</param>
    public static KotorTexture? Find(ResourceResolver res, string name, TextureKind kind,
        Action<int, bool>? onLightmapRepaired = null)
    {
        ArgumentNullException.ThrowIfNull(res);
        string n = name.ToLowerInvariant();
        if (kind == TextureKind.Diffuse) return FromTpc(res, n) ?? FromTga(res, n);
        KotorTexture? lm = FromTga(res, n) ?? FromTpc(res, n);
        if (lm is null) return null;
        var (repaired, none) = LightmapRepair.Repair(lm.Rgba, lm.Width, lm.Height);
        if (repaired > 0) onLightmapRepaired?.Invoke(repaired, none);
        return lm with { HasAlpha = false };
    }

    private static KotorTexture? FromTpc(ResourceResolver res, string n)
    {
        byte[]? tpc = res.Find(n, ResourceType.Tpc);
        return tpc is null ? null : TpcDecoder.Decode(tpc);
    }

    private static KotorTexture? FromTga(ResourceResolver res, string n)
    {
        byte[]? tga = res.Find(n, ResourceType.Tga);
        if (tga is null) return null;
        var (w, h, rgba) = TgaReader.Read(new MemoryStream(tga));
        byte[]? txi = res.Find(n, ResourceType.Txi);
        bool alpha = false;
        for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] != 255) { alpha = true; break; }
        return new KotorTexture(w, h, rgba, alpha,
            TxiInfo.Parse(txi is null ? null : System.Text.Encoding.ASCII.GetString(txi)));
    }
}
