namespace Kotor.Core.Export;

/// <summary>Ecrit le bloc shader d'un materiau (diffuse seule).</summary>
public static class ShaderWriter
{
    /// <summary>
    /// Construit le bloc shader. <paramref name="textureRelativePath"/> est relatif a <c>textures/</c>
    /// (exemple : <c>kotor/Rock.tga</c>). <paramref name="alphaCutout"/> : texture decoupee par alpha
    /// (feuillage) : double face et test alpha.
    /// </summary>
    public static string Build(string materialName, string textureRelativePath, bool alphaCutout = false) =>
        Build(materialName, textureRelativePath,
            new MaterialAlpha(alphaCutout ? AlphaKind.Cutout : AlphaKind.Opaque, null, false));

    /// <summary>
    /// Variante pilotee par le materiau TFU : opaque (rien), decoupe (<c>alphaFunc GE128</c> + <c>depthWrite</c>)
    /// ou melange (<c>blendFunc</c>, sans test alpha ni ecriture de profondeur). Double face pour la decoupe, le melange
    /// et <c>CullMode = 1</c>.
    /// </summary>
    public static string Build(string materialName, string textureRelativePath, MaterialAlpha alpha)
    {
        ArgumentException.ThrowIfNullOrEmpty(materialName);
        ArgumentException.ThrowIfNullOrEmpty(textureRelativePath);
        ArgumentNullException.ThrowIfNull(alpha);

        var name = ShaderNames.FullName(materialName);
        var map = $"textures/{textureRelativePath}";
        bool cullOff = alpha.Kind != AlphaKind.Opaque || alpha.CullNone;

        return
            name + "\n" +
            "{\n" +
            "\tqer_editorimage " + name + "\n" +
            "\tsurfaceparm nolightmap\n" +
            (cullOff ? "\tcull disable\n" : "") +
            "\t{\n" +
            "\t\tmap " + map + "\n" +
            "\t\trgbGen vertex\n" +
            (alpha.Kind == AlphaKind.Cutout ? "\t\talphaFunc GE128\n\t\tdepthWrite\n" : "") +
            (alpha.Kind == AlphaKind.Blend ? "\t\tblendFunc " + (alpha.BlendFunc ?? MaterialAlpha.DefaultBlend) + "\n" : "") +
            "\t}\n" +
            "}\n";
    }

    /// <summary>
    /// Bloc shader du brouillard global d'un niveau. Memes <c>surfaceparm</c> que les brouillards des cartes d'origine
    /// (fogs.shader : nonsolid, nonopaque, fog, trans) plus <c>nolightmap</c> ; <paramref name="distance"/> en unites Quake.
    /// </summary>
    public static string BuildFog(string level, System.Numerics.Vector3 color, double distance)
    {
        ArgumentException.ThrowIfNullOrEmpty(level);
        if (distance <= 0) throw new ArgumentOutOfRangeException(nameof(distance), distance, "The distance must be strictly positive.");
        static string C(float f) => f.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        return
            ShaderNames.FogName(level) + "\n" +
            "{\n" +
            "\tsurfaceparm nonsolid\n" +
            "\tsurfaceparm nonopaque\n" +
            "\tsurfaceparm trans\n" +
            "\tsurfaceparm fog\n" +
            "\tsurfaceparm nolightmap\n" +
            "\tfogParms ( " + C(color.X) + " " + C(color.Y) + " " + C(color.Z) + " ) " +
            distance.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            "}\n";
    }
}
