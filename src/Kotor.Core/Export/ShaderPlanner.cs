using System.Text;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Core.Export;

public enum ShaderKind { Lightmapped, LightmappedCutout, Additive, Blend, Unlit }

/// <summary>Shader prevu pour un maillage. <paramref name="Name"/> est le nom complet (<c>textures/kotor/...</c>).</summary>
public sealed record PlannedShader(string Name, ShaderKind Kind, string Texture, string? Lightmap)
{
    /// <summary>Les types qui ecrivent <c>cull none</c> : leurs surfaces sont double face.</summary>
    public bool DoubleSided => Kind is ShaderKind.LightmappedCutout or ShaderKind.Additive or ShaderKind.Blend;
}

public static class ShaderPlanner
{
    public static PlannedShader Plan(SceneMesh mesh, KotorTexture? texture)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        string tex = mesh.Texture;
        string? lm = mesh.Lightmap;
        bool alpha = texture?.HasAlpha == true;
        bool punch = mesh.Txi.Blending == "punchthrough";
        string plain = ShaderNames.Prefix + tex;
        string lit = lm is null ? plain : plain + "__" + lm;

        if (mesh.Txi.Blending == "additive") return new PlannedShader(plain, ShaderKind.Additive, tex, null);
        if (mesh.TransparencyHint != 0 && alpha && !punch) return new PlannedShader(plain, ShaderKind.Blend, tex, null);
        if (lm is not null && (punch || (alpha && mesh.TransparencyHint == 0)))
            return new PlannedShader(lit, ShaderKind.LightmappedCutout, tex, lm);
        if (lm is not null) return new PlannedShader(lit, ShaderKind.Lightmapped, tex, lm);
        return new PlannedShader(plain, ShaderKind.Unlit, tex, null);
    }

    public static string Write(PlannedShader s)
    {
        ArgumentNullException.ThrowIfNull(s);
        string map = "map " + ShaderNames.Prefix + s.Texture + ".tga";
        string lmMap = s.Lightmap is null ? "" : "map " + ShaderNames.LightmapPrefix + s.Lightmap + ".tga";
        var b = new StringBuilder();
        b.Append(s.Name).Append("\n{\n");
        switch (s.Kind)
        {
            case ShaderKind.Lightmapped:
                b.Append("\t{\n\t\t").Append(lmMap).Append("\n\t\ttcGen lightmap\n\t\trgbGen identity\n\t}\n");
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\tblendFunc GL_DST_COLOR GL_ZERO\n\t\trgbGen identity\n\t}\n");
                break;
            case ShaderKind.LightmappedCutout:
                b.Append("\tcull none\n");
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\talphaFunc GE128\n\t\tdepthWrite\n\t\trgbGen identity\n\t}\n");
                b.Append("\t{\n\t\t").Append(lmMap).Append("\n\t\ttcGen lightmap\n\t\tblendFunc GL_DST_COLOR GL_ZERO\n\t\tdepthFunc equal\n\t\trgbGen identity\n\t}\n");
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
