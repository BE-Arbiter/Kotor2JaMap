using System.Text;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Core.Export;

public enum ShaderKind { Lightmapped, LightmappedCutout, Additive, Blend, Unlit, UnlitCutout }

/// <summary>Shader prevu pour un maillage. <paramref name="Name"/> est le nom complet (<c>textures/kotor/...</c>).</summary>
public sealed record PlannedShader(string Name, ShaderKind Kind, string Texture, string? Lightmap)
{
    /// <summary>Les types qui ecrivent <c>cull none</c> : leurs surfaces sont double face.</summary>
    public bool DoubleSided => Kind is ShaderKind.LightmappedCutout or ShaderKind.UnlitCutout or ShaderKind.Additive or ShaderKind.Blend;
}

public static class ShaderPlanner
{
    /// <summary>
    /// Diffuse x lightmap x 2 (<c>dst * src + src * dst</c>), the KOTOR lightmap convention that the application preview also uses.
    /// With the engine defaults (<c>r_overBrightBits 0</c>, <c>r_mapOverBrightBits 0</c>) the usual <c>GL_DST_COLOR GL_ZERO</c> filter
    /// leaves the unscaled KOTOR lightmaps about twice too dark.
    /// </summary>
    public const string LightmapBlend = "GL_DST_COLOR GL_SRC_COLOR";

    /// <summary>
    /// Last stage of a lightmapped shader: a <c>$lightmap</c> stage that changes nothing (<c>GL_ZERO GL_ONE</c> keeps the framebuffer).
    /// The engine only keeps a shader's lightmap index (and so only copies the drawvert lightmap UVs for <c>tcGen lightmap</c>) when the
    /// shader has such a stage; with explicit lightmap TGAs only, rd-vulkan tr_shader.cpp:4483 resets the index and the UVs stay unset,
    /// which samples a single lightmap texel per surface (flat shading).
    /// </summary>
    public const string LightmapMarkerStage = "\t{\n\t\tmap $lightmap\n\t\tblendFunc GL_ZERO GL_ONE\n\t}\n";

    public static PlannedShader Plan(SceneMesh mesh, KotorTexture? texture, KotorGame game = KotorGame.Kotor1)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        string tex = mesh.Texture;
        string? lm = mesh.Lightmap;
        // With an environment map (envmaptexture / bumpyshinytexture) the alpha channel is the reflection mask, not transparency,
        // unless the TXI explicitly asks for punchthrough or a decal.
        bool punch = mesh.Txi.Blending == "punchthrough";
        bool alpha = texture?.HasAlpha == true && (mesh.Txi.EnvMapTexture is null || punch || mesh.Txi.Decal);
        string plain = ShaderNames.PrefixFor(game) + tex;
        string lit = lm is null ? plain : plain + "__" + lm;

        // Opacity declared by the source game (SWTOR material alpha mode): not deduced from the texture alpha.
        switch (mesh.Alpha)
        {
            case SurfaceAlpha.Additive: return new PlannedShader(plain, ShaderKind.Additive, tex, null);
            case SurfaceAlpha.Blend: return new PlannedShader(plain, ShaderKind.Blend, tex, null);
            case SurfaceAlpha.Cutout:
                return lm is null ? new PlannedShader(plain, ShaderKind.UnlitCutout, tex, null)
                                  : new PlannedShader(lit, ShaderKind.LightmappedCutout, tex, lm);
            case SurfaceAlpha.Opaque:
                return lm is null ? new PlannedShader(plain, ShaderKind.Unlit, tex, null)
                                  : new PlannedShader(lit, ShaderKind.Lightmapped, tex, lm);
        }

        if (mesh.Txi.Blending == "additive") return new PlannedShader(plain, ShaderKind.Additive, tex, null);
        if (mesh.TransparencyHint != 0 && alpha && !punch) return new PlannedShader(plain, ShaderKind.Blend, tex, null);
        if (lm is not null && (punch || (alpha && mesh.TransparencyHint == 0)))
            return new PlannedShader(lit, ShaderKind.LightmappedCutout, tex, lm);
        if (lm is not null) return new PlannedShader(lit, ShaderKind.Lightmapped, tex, lm);
        return new PlannedShader(plain, ShaderKind.Unlit, tex, null);
    }

    public static string Write(PlannedShader s, KotorGame game = KotorGame.Kotor1)
    {
        ArgumentNullException.ThrowIfNull(s);
        string map = "map " + ShaderNames.PrefixFor(game) + s.Texture + ".tga";
        string lmMap = s.Lightmap is null ? "" : "map " + ShaderNames.LightmapPrefixFor(game) + s.Lightmap + ".tga";
        var b = new StringBuilder();
        b.Append(s.Name).Append("\n{\n");
        // The lightmap atlases are tiny: no reduction and no mipmaps (rd-vulkan tr_shader.cpp ParseShader: nomipmaps / nopicmip).
        if (s.Kind is ShaderKind.Lightmapped or ShaderKind.LightmappedCutout) b.Append("\tnopicmip\n\tnomipmaps\n");
        switch (s.Kind)
        {
            case ShaderKind.Lightmapped:
                b.Append("\t{\n\t\t").Append(lmMap).Append("\n\t\ttcGen lightmap\n\t\trgbGen identity\n\t}\n");
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\tblendFunc ").Append(LightmapBlend).Append("\n\t\trgbGen identity\n\t}\n");
                b.Append(LightmapMarkerStage);
                break;
            case ShaderKind.LightmappedCutout:
                b.Append("\tcull none\n");
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\talphaFunc GE128\n\t\tdepthWrite\n\t\trgbGen identity\n\t}\n");
                b.Append("\t{\n\t\t").Append(lmMap).Append("\n\t\ttcGen lightmap\n\t\tblendFunc ").Append(LightmapBlend).Append("\n\t\tdepthFunc equal\n\t\trgbGen identity\n\t}\n");
                b.Append(LightmapMarkerStage);
                break;
            case ShaderKind.UnlitCutout:
                b.Append("\tcull none\n");
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\talphaFunc GE128\n\t\tdepthWrite\n\t\trgbGen vertex\n\t}\n");
                break;
            case ShaderKind.Additive:
                b.Append("\tsurfaceparm nonsolid\n\tsurfaceparm trans\n\tcull none\n");
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\tblendFunc GL_ONE GL_ONE\n\t\trgbGen identity\n\t}\n");
                break;
            case ShaderKind.Blend:
                b.Append("\tsurfaceparm trans\n\tcull none\n");
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\tblendFunc GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA\n\t\trgbGen vertex\n\t}\n");
                break;
            default:
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\trgbGen vertex\n\t}\n");
                break;
        }
        b.Append("}\n");
        return b.ToString();
    }
}
