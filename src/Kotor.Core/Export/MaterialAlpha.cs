namespace Kotor.Core.Export;

public enum AlphaKind { Opaque, Cutout, Blend }

/// <summary>Mode d'opacite d'un materiau TFU, deduit de ses proprietes (jamais de la texture).</summary>
/// <param name="BlendFunc">Arguments GL de <c>blendFunc</c> (mode <see cref="AlphaKind.Blend"/> seulement).</param>
/// <param name="CullNone">Vrai si <c>CullMode</c> vaut 1 (D3DCULL_NONE : double face).</param>
/// <remarks>
/// La deduction depuis les proprietes de materiau (<c>From</c>) n'est pas reprise en etape 01 : elle depend du modele
/// de materiau TFU (<c>MaterialProperty</c>), non copie. Elle reviendra avec l'import des materiaux.
/// </remarks>
public sealed record MaterialAlpha(AlphaKind Kind, string? BlendFunc, bool CullNone)
{
    public const string DefaultBlend = "GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA";
}
