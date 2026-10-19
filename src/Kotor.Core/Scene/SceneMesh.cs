using System.Numerics;
using Kotor.Core.Textures;

namespace Kotor.Core.Scene;

/// <summary>Maillage en positions monde du module (metres KOTOR). Uv0 : convention GL (v=0 en bas), non retournee.</summary>
public sealed record SceneMesh(string Room, string Node, string Texture, string? Lightmap, TxiInfo Txi, int TransparencyHint,
    Vector3[] Positions, Vector3[] Normals, Vector2[] Uv0, Vector2[]? Uv1, int[] Triangles)
{
    /// <summary>Opacity chosen by the source game. <see cref="SurfaceAlpha.Auto"/> (KOTOR): deduced from the texture and the TXI.</summary>
    public SurfaceAlpha Alpha { get; init; } = SurfaceAlpha.Auto;

    /// <summary>
    /// Seconde texture fondue sur <see cref="Texture"/> (sol SWTOR : deux couches de la carte de melange), opacite par sommet <see cref="VertexAlpha"/>.
    /// Null : une seule texture.
    /// </summary>
    public string? OverlayTexture { get; init; }

    /// <summary>Repetition de <see cref="OverlayTexture"/> relative a Uv0 (1 = memes UV).</summary>
    public Vector2 OverlayUvScale { get; init; } = Vector2.One;

    /// <summary>Opacite de <see cref="OverlayTexture"/> par sommet (0 a 255, meme indice que <see cref="Positions"/>) ; null sans seconde texture.</summary>
    public byte[]? VertexAlpha { get; init; }

    /// <summary>Effets de shader demandes par la source (Jedi Knight : ciel, animation, defilement, eau, lave). <see cref="ShaderHints.None"/> (KOTOR, SWTOR) : sortie inchangee.</summary>
    public ShaderHints Hints { get; init; } = ShaderHints.None;
}

/// <summary>
/// Effets de shader d'un maillage, pour les sources qui en ont (Jedi Knight). <see cref="None"/> = aucun effet : le shader est alors celui d'avant.
/// </summary>
/// <param name="AnimFrames">Noms de textures (sans dossier ni extension) des images d'une animation <c>animMap</c> ; null = image fixe. Au moins deux images pour avoir un effet.</param>
/// <param name="AnimFps">Images par seconde de <paramref name="AnimFrames"/>.</param>
/// <param name="Scroll">Vitesse du CONTENU de la texture en largeurs de texture par seconde, en UV de scene (u vers la droite, v vers le haut) ; null = fixe. Le shader ecrit <c>tcMod scroll</c> avec la conversion de signe vers les coordonnees de Quake.</param>
/// <param name="Sky">Surface de ciel : shader <c>surfaceparm sky</c> avec couche de nuages (voir <see cref="SkyTile"/>).</param>
/// <param name="Water">Surface d'eau : melange translucide qui ondule.</param>
/// <param name="Lava">Surface de lave : pleine lumiere qui ondule.</param>
/// <param name="FullBright">Pleine lumiere : couleur de la texture sans eclairage de sommet (<c>rgbGen identity</c>).</param>
public sealed record ShaderHints(IReadOnlyList<string>? AnimFrames, float AnimFps, System.Numerics.Vector2? Scroll, bool Sky, bool Water, bool Lava, bool FullBright)
{
    public static readonly ShaderHints None = new(null, 0f, null, false, false, false, false);

    /// <summary>Ciel : nombre de repetitions de la texture de la couche de nuages (<c>tcMod scale</c>).</summary>
    public float SkyTile { get; init; } = 1f;

    public bool IsNone => this == None;

    /// <summary>Egalite par valeur, listes d'images comprises (une liste est un objet : l'egalite d'enregistrement par defaut les distinguerait).</summary>
    public bool Equals(ShaderHints? other) =>
        other is not null && AnimFps == other.AnimFps && Scroll == other.Scroll && Sky == other.Sky && Water == other.Water && Lava == other.Lava
        && FullBright == other.FullBright && SkyTile == other.SkyTile
        && (AnimFrames is null ? other.AnimFrames is null : other.AnimFrames is not null && AnimFrames.SequenceEqual(other.AnimFrames));

    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(AnimFps); h.Add(Scroll); h.Add(Sky); h.Add(Water); h.Add(Lava); h.Add(FullBright); h.Add(SkyTile);
        if (AnimFrames is not null) foreach (string f in AnimFrames) h.Add(f);
        return h.ToHashCode();
    }

    /// <summary>Suite de caracteres stable qui identifie les effets (cle de regroupement des maillages et base du nom de shader).</summary>
    public string Key
    {
        get
        {
            if (IsNone) return "";
            var c = System.Globalization.CultureInfo.InvariantCulture;
            return string.Join('|',
                AnimFrames is null ? "" : string.Join(',', AnimFrames) + "@" + AnimFps.ToString("0.###", c),
                Scroll is { } s ? s.X.ToString("0.####", c) + "," + s.Y.ToString("0.####", c) : "",
                Sky ? "sky" + SkyTile.ToString("0.###", c) : "", Water ? "water" : "", Lava ? "lava" : "", FullBright ? "fb" : "");
        }
    }
}

/// <summary>Opacity of a mesh declared by its source (SWTOR material alpha mode) instead of deduced from the texture alpha.</summary>
public enum SurfaceAlpha { Auto, Opaque, Cutout, Blend, Additive, Multiply }
