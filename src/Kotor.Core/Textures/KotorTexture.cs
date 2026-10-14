namespace Kotor.Core.Textures;

/// <summary>Texture decodee en RGBA 8 bits. La ligne 0 de <see cref="Rgba"/> est le haut de l'image.</summary>
public sealed record KotorTexture(int Width, int Height, byte[] Rgba, bool HasAlpha, TxiInfo Txi);

public enum TextureKind { Diffuse, Lightmap }
