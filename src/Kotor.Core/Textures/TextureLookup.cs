using Kotor.Core.Archives;
using Kotor.Core.Export;

namespace Kotor.Core.Textures;

/// <summary>Recherche et decode une texture ou une lightmap. Diffuse : TPC puis TGA. Lightmap : TGA puis TPC.</summary>
public static class TextureLookup
{
    public static KotorTexture? Find(ResourceResolver res, string name, TextureKind kind)
    {
        ArgumentNullException.ThrowIfNull(res);
        string n = name.ToLowerInvariant();
        return kind == TextureKind.Diffuse
            ? FromTpc(res, n) ?? FromTga(res, n)
            : FromTga(res, n) ?? FromTpc(res, n);
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
