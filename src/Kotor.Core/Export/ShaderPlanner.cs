using System.Text;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Core.Export;

/// <remarks><see cref="Sky"/> et <see cref="Water"/> ne sortent que pour des maillages portant des <see cref="ShaderHints"/> (Jedi Knight).</remarks>
public enum ShaderKind { Lightmapped, LightmappedCutout, Additive, Blend, Unlit, UnlitCutout, Multiply, Overlay, Sky, Water }

/// <summary>Shader prevu pour un maillage. <paramref name="Name"/> est le nom complet (<c>textures/kotor/...</c>).</summary>
public sealed record PlannedShader(string Name, ShaderKind Kind, string Texture, string? Lightmap)
{
    /// <summary>Les types qui ecrivent <c>cull none</c> : leurs surfaces sont double face.</summary>
    public bool DoubleSided => Kind is ShaderKind.LightmappedCutout or ShaderKind.UnlitCutout or ShaderKind.Additive or ShaderKind.Blend or ShaderKind.Multiply or ShaderKind.Water;

    /// <summary>Effets demandes par la source (animation, defilement, ciel, eau, lave) ; <see cref="ShaderHints.None"/> pour KOTOR et SWTOR.</summary>
    public ShaderHints Hints { get; init; } = ShaderHints.None;

    /// <summary><see cref="ShaderKind.Overlay"/> : seconde texture fondue par l alpha des sommets, et sa repetition relative a la premiere.</summary>
    public string? Overlay { get; init; }

    public System.Numerics.Vector2 OverlayScale { get; init; } = System.Numerics.Vector2.One;
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
        if (!mesh.Hints.IsNone) return PlanHinted(mesh, texture, game);
        string tex = mesh.Texture;
        string? lm = mesh.Lightmap;
        // With an environment map (envmaptexture / bumpyshinytexture) the alpha channel is the reflection mask, not transparency,
        // unless the TXI explicitly asks for punchthrough or a decal.
        bool punch = mesh.Txi.Blending == "punchthrough";
        bool alpha = texture?.HasAlpha == true && (mesh.Txi.EnvMapTexture is null || punch || mesh.Txi.Decal);
        string plain = ShaderNames.PrefixFor(game) + tex;
        string lit = lm is null ? plain : plain + "__" + lm;

        // Deux couches de sol fondues (terrain SWTOR) : opaque, la seconde par-dessus selon l'alpha des sommets.
        if (mesh.OverlayTexture is { } over)
            return new PlannedShader(ShaderNames.PrefixFor(game) + ShaderNames.Short(tex + "__" + over, game), ShaderKind.Overlay, tex, null)
            { Overlay = over, OverlayScale = mesh.OverlayUvScale };

        // Opacity declared by the source game (SWTOR material alpha mode): not deduced from the texture alpha.
        switch (mesh.Alpha)
        {
            case SurfaceAlpha.Additive: return new PlannedShader(plain, ShaderKind.Additive, tex, null);
            case SurfaceAlpha.Blend: return new PlannedShader(plain, ShaderKind.Blend, tex, null);
            case SurfaceAlpha.Multiply: return new PlannedShader(plain, ShaderKind.Multiply, tex, null);
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
        // Sans lightmap (feuillages, grilles) : meme regle de decoupe par l'alpha, sinon les feuilles restent des quads opaques.
        if (punch || (alpha && mesh.TransparencyHint == 0)) return new PlannedShader(plain, ShaderKind.UnlitCutout, tex, null);
        return new PlannedShader(plain, ShaderKind.Unlit, tex, null);
    }

    /// <summary>
    /// Maillage avec effets (Jedi Knight) : le ciel garde le nom de sa texture (c'est le nom de <c>ModuleScene.SkyShader</c>) ; les autres prennent le shader
    /// qu'aurait le maillage sans effet, avec une marque derivee des effets dans le nom (deux animations ou deux vitesses de defilement sont deux shaders).
    /// </summary>
    private static PlannedShader PlanHinted(SceneMesh mesh, KotorTexture? texture, KotorGame game)
    {
        ShaderHints h = mesh.Hints;
        string prefix = ShaderNames.PrefixFor(game);
        if (h.Sky) return new PlannedShader(prefix + mesh.Texture, ShaderKind.Sky, mesh.Texture, null) { Hints = h };
        PlannedShader basic = Plan(mesh with { Hints = ShaderHints.None }, texture, game);
        string tag = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(h.Key)), 0, 4).ToLowerInvariant();
        string body = basic.Name[prefix.Length..] + "_" + tag;
        return basic with { Name = prefix + ShaderNames.Short(body, game), Kind = h.Water ? ShaderKind.Water : basic.Kind, Hints = h };
    }

    private static string Num(float v)
    {
        if (v == 0f) v = 0f; // -0 s'ecrirait "-0"
        return v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Ligne de la couche diffuse : <c>map</c> ou <c>animMap</c>, suivie des <c>tcMod</c> demandes par les effets.</summary>
    private static string DiffuseStage(PlannedShader s, KotorGame game)
    {
        string prefix = ShaderNames.PrefixFor(game);
        ShaderHints h = s.Hints;
        if (h.IsNone) return "map " + prefix + s.Texture + ".tga";
        var b = new StringBuilder();
        if (h.AnimFrames is { Count: >= 2 } frames)
            b.Append("animMap ").Append(Num(h.AnimFps)).Append(' ').Append(string.Join(' ', frames.Select(f => prefix + f + ".tga")));
        else
            b.Append("map ").Append(prefix).Append(s.Texture).Append(".tga");
        if (h.Sky) b.Append("\n\t\ttcMod scale ").Append(Num(h.SkyTile)).Append(' ').Append(Num(h.SkyTile));
        // Le contenu se deplace de (sx, sy) en UV de scene (v vers le haut) : en coordonnees de Quake (t = 1 - v) le contenu bouge de (sx, -sy),
        // et tcMod scroll deplace la texture dans le sens contraire de son argument, d'ou (-sx, +sy).
        if (h.Scroll is { } sc) b.Append("\n\t\ttcMod scroll ").Append(Num(-sc.X)).Append(' ').Append(Num(sc.Y));
        if (h.Water || h.Lava) b.Append("\n\t\ttcMod turb 0 0.03 0 0.15");
        return b.ToString();
    }

    public static string Write(PlannedShader s, KotorGame game = KotorGame.Kotor1)
    {
        ArgumentNullException.ThrowIfNull(s);
        string map = DiffuseStage(s, game);
        string vertexRgb = s.Hints.FullBright ? "identity" : "vertex";
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
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\talphaFunc GE128\n\t\tdepthWrite\n\t\trgbGen ").Append(vertexRgb).Append("\n\t}\n");
                break;
            case ShaderKind.Additive:
                // polygonOffset : ecrans et lueurs colles a leur cadre opaque (SWTOR : a 0,0003 unite pres) ; sans decalage ils scintillent.
                b.Append("\tsurfaceparm nonsolid\n\tsurfaceparm trans\n\tpolygonOffset\n\tcull none\n");
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\tblendFunc GL_ONE GL_ONE\n\t\trgbGen identity\n\t}\n");
                break;
            case ShaderKind.Multiply:
                // Filtre : couleur du fond x texture (decalcomanies, traces, saletes). polygonOffset : pas de z-fighting avec la surface decoree.
                b.Append("\tsurfaceparm nonsolid\n\tsurfaceparm trans\n\tpolygonOffset\n\tcull none\n");
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\tblendFunc GL_DST_COLOR GL_ZERO\n\t\trgbGen identity\n\t}\n");
                break;
            case ShaderKind.Blend:
            case ShaderKind.Water:
                // polygonOffset : vitres posees sur l'ecran ou le cadre (meme raison que les additifs).
                b.Append("\tsurfaceparm trans\n\tpolygonOffset\n\tcull none\n");
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\tblendFunc GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA\n\t\trgbGen ").Append(vertexRgb).Append("\n\t}\n");
                break;
            case ShaderKind.Sky:
                // Ciel Jedi Knight : une couche de nuages (la texture du ciel, repetee et qui derive), sans boite lointaine ("-"), hauteur de nuages par defaut.
                b.Append("\tsurfaceparm sky\n\tsurfaceparm noimpact\n\tsurfaceparm nomarks\n\tsurfaceparm nolightmap\n\tskyParms - 128 -\n");
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\tdepthWrite\n\t}\n");
                break;
            case ShaderKind.Overlay:
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\trgbGen vertex\n\t}\n");
                b.Append("\t{\n\t\tmap ").Append(ShaderNames.PrefixFor(game)).Append(s.Overlay).Append(".tga\n");
                if (s.OverlayScale != System.Numerics.Vector2.One)
                    b.Append("\t\ttcMod scale ").Append(s.OverlayScale.X.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))
                     .Append(' ').Append(s.OverlayScale.Y.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                b.Append("\t\tblendFunc GL_SRC_ALPHA GL_ONE_MINUS_SRC_ALPHA\n\t\trgbGen vertex\n\t\talphaGen vertex\n\t}\n");
                break;
            default:
                b.Append("\t{\n\t\t").Append(map).Append("\n\t\trgbGen ").Append(vertexRgb).Append("\n\t}\n");
                break;
        }
        b.Append("}\n");
        return b.ToString();
    }
}
