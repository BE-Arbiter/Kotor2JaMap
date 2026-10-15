using System.Text;
using Kotor.Core.Textures;

namespace Kotor.Core.Export;

/// <summary>Shader d'un modele MD3 : <c>models/&lt;dossier&gt;/&lt;texture&gt;</c>, eclaire par <c>rgbGen lightingDiffuse</c> (pas de lightmap).</summary>
public static class Md3ShaderWriter
{
    /// <summary>Prefixe des shaders de modeles et des chemins de MD3 : <c>models/kotor/</c> ou <c>models/kotor2/</c>.</summary>
    public static string ModelPrefix(KotorGame game) => "models/" + KotorGames.AssetFolder(game) + "/";

    /// <summary>
    /// Bloc shader d'une texture de modele. Une texture avec un vrai alpha (transparence, pas un masque de reflet) recoit
    /// <c>alphaFunc GE128</c> et <c>cull none</c>. <paramref name="texture"/> null : texture introuvable, damier <c>missing</c>.
    /// </summary>
    public static string Write(KotorGame game, string textureName, KotorTexture? texture)
    {
        ArgumentException.ThrowIfNullOrEmpty(textureName);
        bool punch = texture?.Txi.Blending == "punchthrough";
        // Avec un reflet (envmaptexture / bumpyshinytexture) l'alpha est le masque de reflet, comme dans ShaderPlanner.
        bool alpha = texture?.HasAlpha == true && (texture.Txi.EnvMapTexture is null || punch || texture.Txi.Decal);
        string map = ShaderNames.PrefixFor(game) + (texture is null ? ShaderNames.MissingTexture : textureName) + ".tga";
        var b = new StringBuilder();
        b.Append(ModelPrefix(game)).Append(textureName).Append("\n{\n");
        if (alpha) b.Append("\tcull none\n");
        b.Append("\t{\n\t\tmap ").Append(map).Append('\n');
        if (alpha) b.Append("\t\talphaFunc GE128\n");
        b.Append("\t\trgbGen lightingDiffuse\n\t}\n}\n");
        return b.ToString();
    }
}
