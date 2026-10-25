// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Kotor.Compiler.Bsp;

/// <summary>Bilan de l'ecriture de la geometrie visible : brushes ecrits, triangles ignores, qualite du mappage de texture, textures utilisees.</summary>
/// <param name="Brushes">Nombre de brushes (prismes) ecrits.</param>
/// <param name="DegenerateTriangles">Triangles d'aire quasi nulle (aucun prisme possible), ignores.</param>
/// <param name="CappedTriangles">Triangles valides non ecrits parce que le plafond de brushes etait atteint (les plus petits sont retires d'abord).</param>
/// <param name="MaxUvError">Plus grand ecart de texture (en fraction de texture) entre le mappage de l'editeur et les UV de la scene, aux sommets.</param>
/// <param name="InexactFaces">Faces dont l'ecart depasse <see cref="VisualMapWriter.UvTolerance"/> (UV avec cisaillement : le format Q3 classique ne sait pas le representer).</param>
public sealed record VisualMapStats(int Brushes, int DegenerateTriangles, int CappedTriangles, double MaxUvError, int InexactFaces,
    IReadOnlyCollection<string> Textures);

/// <summary>
/// Ecrit, dans le worldspawn du <c>.map</c>, la geometrie de la carte : un prisme fin par triangle de rendu. Ces brushes sont a la fois
/// le visible et le solide (comme dans une carte Jedi Academy : pas de couche de collision a part). La face qui porte la
/// surface a sa position exacte et la texture de base ; les autres faces sont <c>caulk</c>. Pour une surface double face, les deux grandes
/// faces portent la texture.
/// <para>
/// Mappage de texture : format Quake 3 classique (le seul que tout editeur configure pour Jedi Academy relit). Une face y est
/// projetee suivant son axe dominant (<c>TextureAxisFromPlane</c> de q3map2) puis tournee, mise a l'echelle et decalee : on retrouve ces
/// cinq nombres par ajustement exact des UV des trois sommets (un affine quelconque sans cisaillement est reproduit a l'identique).
/// </para>
/// </summary>
public static class VisualMapWriter
{
    /// <summary>Epaisseur du prisme sous la surface (unites JA). La face visible reste dans le plan exact de la surface.</summary>
    public const float DefaultThickness = 2f;

    /// <summary>Evasement (unites JA) des flancs au fond du prisme : ils ne sont ainsi jamais coplanaires avec la surface d'un voisin perpendiculaire.</summary>
    public const float FlankFlare = 0.5f;

    /// <summary>Plafond de brushes visuels (un editeur ne tient pas un nombre illimite de brushes) ; les plus petits triangles sont retires d'abord.</summary>
    public const int DefaultMaxBrushes = 300_000;

    /// <summary>Ecart de texture (fraction de texture) a partir duquel une face est comptee comme inexacte.</summary>
    public const double UvTolerance = 0.002;

    /// <summary>Norme du produit vectoriel (2 x aire, unites JA au carre) en dessous de laquelle un triangle est ignore.</summary>
    private const float MinDoubleArea = 0.05f;

    /// <summary>Plus petite hauteur (unites JA) d'un triangle : en dessous c'est une aiguille que l'arrondi a 4 decimales du .map degenere.</summary>
    private const float MinAltitude = 0.05f;

    // TextureAxisFromPlane de q3map2 : pour chacune des six normales (sol, plafond, mur ouest, est, sud, nord), indices des deux coordonnees
    // conservees (s, t). Les axes de base sont toujours s = +coordonnee s, t = -coordonnee t.
    private static readonly Vector3[] BaseNormals =
        [new(0, 0, 1), new(0, 0, -1), new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0)];
    private static readonly (int S, int T)[] BaseIndices = [(0, 1), (0, 1), (1, 2), (1, 2), (0, 2), (0, 2)];

    private readonly record struct Tri(int Surface, int Index, float DoubleArea);

    public static VisualMapStats Write(TextWriter w, IReadOnlyList<SurfaceInput> surfaces, int maxBrushes = DefaultMaxBrushes,
        float thickness = DefaultThickness)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(surfaces);
        if (!(thickness > 0f)) throw new ArgumentOutOfRangeException(nameof(thickness));

        var tris = new List<Tri>();
        int degenerate = 0;
        for (int s = 0; s < surfaces.Count; s++)
        {
            SurfaceInput surf = surfaces[s];
            for (int i = 0; i + 2 < surf.Indices.Length; i += 3)
            {
                if (!Valid(surf, i)) { degenerate++; continue; }
                Vector3 pa = surf.Positions[surf.Indices[i]], pb = surf.Positions[surf.Indices[i + 1]], pc = surf.Positions[surf.Indices[i + 2]];
                float da = Vector3.Cross(pb - pa, pc - pa).Length();
                float longest = MathF.Sqrt(MathF.Max(Vector3.DistanceSquared(pa, pb), MathF.Max(Vector3.DistanceSquared(pb, pc), Vector3.DistanceSquared(pc, pa))));
                // Aire trop petite, ou aiguille (plus petite hauteur < MinAltitude) : le plan s'effondre a l'arrondi du .map et q3map2 rejette le brush.
                if (da < MinDoubleArea || da / longest < MinAltitude) degenerate++; else tris.Add(new Tri(s, i, da));
            }
        }

        int capped = 0;
        if (tris.Count > maxBrushes)
        {
            capped = tris.Count - Math.Max(0, maxBrushes);
            var keep = new HashSet<int>(Enumerable.Range(0, tris.Count).OrderByDescending(i => tris[i].DoubleArea).ThenBy(i => i).Take(Math.Max(0, maxBrushes)));
            tris = tris.Where((_, i) => keep.Contains(i)).ToList();
        }

        w.Write("// World geometry: one thin textured prism per render triangle. It is what is seen and what is solid, there is no separate collision layer.\n");
        var textures = new SortedSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder(1024);
        double maxErr = 0;
        int inexact = 0;
        foreach (Tri t in tris)
        {
            SurfaceInput surf = surfaces[t.Surface];
            string tex = string.IsNullOrWhiteSpace(surf.VisualTexture) ? surf.Shader : surf.VisualTexture;
            textures.Add(tex);
            sb.Clear();
            double err = AppendPrism(sb, surf, t.Index, tex, thickness);
            w.Write(sb.ToString());
            maxErr = Math.Max(maxErr, err);
            if (err > UvTolerance) inexact++;
        }
        return new VisualMapStats(tris.Count, degenerate, capped, maxErr, inexact, textures);
    }

    /// <summary>Nom de texture tel qu'un <c>.map</c> l'ecrit : sans le prefixe <c>textures/</c> que Radiant et q3map2 ajoutent eux-memes.</summary>
    public static string EditorName(string shader) =>
        shader.StartsWith("textures/", StringComparison.OrdinalIgnoreCase) ? shader["textures/".Length..] : shader;

    private static bool Valid(SurfaceInput s, int i)
    {
        for (int k = 0; k < 3; k++)
        {
            int ix = s.Indices[i + k];
            if (ix < 0 || ix >= s.Positions.Length || ix >= s.Uv0.Length) return false;
            Vector3 p = s.Positions[ix];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)) return false;
            Vector2 uv = s.Uv0[ix]; // UV NaN/infini : la texture serait incalculable (compte comme triangle ecarte)
            if (!float.IsFinite(uv.X) || !float.IsFinite(uv.Y)) return false;
        }
        return true;
    }

    /// <summary>Ecrit un prisme ; renvoie l'ecart de texture maximal des faces texturees.</summary>
    private static double AppendPrism(StringBuilder sb, SurfaceInput s, int i, string tex, float thickness)
    {
        Vector3 a = s.Positions[s.Indices[i]], b = s.Positions[s.Indices[i + 1]], c = s.Positions[s.Indices[i + 2]];
        Vector2 size = s.VisualTextureSize;
        if (!(size.X >= 1f) || !(size.Y >= 1f)) size = new Vector2(128f, 128f);
        Vector2 ta = s.Uv0[s.Indices[i]] * size, tb = s.Uv0[s.Indices[i + 1]] * size, tc = s.Uv0[s.Indices[i + 2]] * size;
        Vector3 n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        Vector3 down = n * thickness;
        Vector3 a2 = a - down, b2 = b - down, c2 = c - down;

        sb.Append("{\n");
        // Face visible (normale n = (p0 - p1) x (p2 - p1), convention Q3) : p0 = b, p1 = a, p2 = c.
        double err = AppendFace(sb, b, a, c, tex, Fit(a, b, c, ta, tb, tc, n, size, out string? top), top);
        // Face arriere : texturee seulement si la surface est double face.
        if (s.DoubleSided) err = Math.Max(err, AppendFace(sb, c2, a2, b2, tex, Fit(a2, b2, c2, ta, tb, tc, -n, size, out string? back), back));
        else AppendCaulk(sb, c2, a2, b2);
        // Trois flancs, normale vers l'exterieur : (p - n t + evasement), p, q. Evases vers l'exterieur en profondeur : un flanc
        // exactement perpendiculaire serait dans le plan de la face texturee d'un mur ou d'un sol voisin (z-fighting caulk/texture).
        AppendCaulk(sb, Flare(a2, a, b, c), a, b);
        AppendCaulk(sb, Flare(b2, b, c, a), b, c);
        AppendCaulk(sb, Flare(c2, c, a, b), c, a);
        sb.Append("}\n");
        return err;
    }

    /// <summary>Point bas <paramref name="below"/> de l'arete (p, q), decale dans le plan du triangle, perpendiculairement a l'arete et en s'eloignant du sommet oppose <paramref name="r"/>.</summary>
    private static Vector3 Flare(Vector3 below, Vector3 p, Vector3 q, Vector3 r)
    {
        Vector3 e = q - p;
        float len = e.Length();
        if (!(len > 0f)) return below;
        e /= len;
        Vector3 toR = r - p;
        Vector3 perp = toR - e * Vector3.Dot(toR, e);
        float pl = perp.Length();
        return pl > 0f ? below - perp / pl * FlankFlare : below;
    }

    private static double AppendFace(StringBuilder sb, Vector3 p0, Vector3 p1, Vector3 p2, string tex, double err, string? texdef)
    {
        Point(sb, p0); Point(sb, p1); Point(sb, p2);
        sb.Append(EditorName(tex)).Append(' ').Append(texdef).Append(" 0 0 0\n");
        return err;
    }

    private static void AppendCaulk(StringBuilder sb, Vector3 p0, Vector3 p1, Vector3 p2)
    {
        Point(sb, p0); Point(sb, p1); Point(sb, p2);
        sb.Append(EditorName(MapWriter.CollisionShader)).Append(" 0 0 0 0.5 0.5 0 0 0\n");
    }

    /// <summary>
    /// Cherche decalage, rotation et echelles (format Q3 classique) tels que le mappage de l'editeur reproduise les UV (en pixels) des trois
    /// sommets. Renvoie l'ecart maximal en fraction de texture ; <paramref name="texdef"/> = "decalageX decalageY rotation echelleX echelleY".
    /// </summary>
    internal static double Fit(Vector3 a, Vector3 b, Vector3 c, Vector2 ta, Vector2 tb, Vector2 tc, Vector3 n, Vector2 size, out string texdef)
    {
        // Axe dominant : meme regle que q3map2 (premier de la liste a gagner sur egalite stricte).
        float best = 0; int axis = 0;
        for (int k = 0; k < 6; k++)
        {
            float d = Vector3.Dot(n, BaseNormals[k]);
            if (d > best) { best = d; axis = k; }
        }
        (int si, int ti) = BaseIndices[axis];
        static float Get(Vector3 v, int idx) => idx == 0 ? v.X : idx == 1 ? v.Y : v.Z;
        Vector2 pa = new(Get(a, si), Get(a, ti)), pb = new(Get(b, si), Get(b, ti)), pc = new(Get(c, si), Get(c, ti));

        Vector2 d1 = pb - pa, d2 = pc - pa, e1 = tb - ta, e2 = tc - ta;
        float det = d1.X * d2.Y - d1.Y * d2.X;
        float g00 = 1f, g01 = 0f, g10 = 0f, g11 = 1f;
        if (MathF.Abs(det) > 1e-6f)
        {
            g00 = (e1.X * d2.Y - e2.X * d1.Y) / det; g01 = (e2.X * d1.X - e1.X * d2.X) / det;
            g10 = (e1.Y * d2.Y - e2.Y * d1.Y) / det; g11 = (e2.Y * d1.X - e1.Y * d2.X) / det;
        }
        Vector2 r0 = new(g00, g01), r1 = new(g10, g11);

        // Modele de q3map2 : ligne s = (cos, sin) / echelleX ; ligne t = (sin, -cos) / echelleY (axes de base s = +, t = -).
        float theta;
        Vector2 u0 = r0.LengthSquared() > 1e-18f ? Vector2.Normalize(r0) : new Vector2(1, 0);
        if (r1.LengthSquared() > 1e-18f)
        {
            Vector2 w1 = Vector2.Normalize(r1);
            Vector2 v1 = new(-w1.Y, w1.X);
            if (Vector2.Dot(v1, u0) < 0) v1 = -v1;
            Vector2 sum = u0 + v1;
            theta = sum.LengthSquared() > 1e-8f ? MathF.Atan2(sum.Y, sum.X) : MathF.Atan2(u0.Y, u0.X);
        }
        else theta = MathF.Atan2(u0.Y, u0.X);
        float cs = MathF.Cos(theta), sn = MathF.Sin(theta);
        float k0 = r0.X * cs + r0.Y * sn;      // 1 / echelleX
        float k1 = r1.X * sn - r1.Y * cs;      // 1 / echelleY
        k0 = ClampInv(k0); k1 = ClampInv(k1);

        // Matrice ajustee, decalage choisi au barycentre pour repartir l'erreur.
        Vector2 Map(Vector2 p) => new(k0 * (cs * p.X + sn * p.Y), k1 * (sn * p.X - cs * p.Y));
        Vector2 shift = (ta + tb + tc) / 3f - Map((pa + pb + pc) / 3f);
        double err = 0;
        foreach (var (p, t) in new[] { (pa, ta), (pb, tb), (pc, tc) })
        {
            Vector2 d = Map(p) + shift - t;
            err = Math.Max(err, Math.Max(Math.Abs(d.X) / size.X, Math.Abs(d.Y) / size.Y));
        }
        // Le motif se repete : le decalage est ramene dans une tour de texture (precision numerique des grands decalages).
        float sx = shift.X - MathF.Floor(shift.X / size.X) * size.X, sy = shift.Y - MathF.Floor(shift.Y / size.Y) * size.Y;
        float deg = theta * 180f / MathF.PI;
        texdef = string.Create(CultureInfo.InvariantCulture, $"{sx:0.####} {sy:0.####} {deg:0.########} {1f / k0:0.########} {1f / k1:0.########}");
        return err;
    }

    private static float ClampInv(float k)
    {
        const float min = 1e-6f;
        if (!float.IsFinite(k) || MathF.Abs(k) < min) return k < 0 ? -min : min;
        return k;
    }

    private static void Point(StringBuilder sb, Vector3 p) =>
        sb.Append("( ").Append(F(p.X)).Append(' ').Append(F(p.Y)).Append(' ').Append(F(p.Z)).Append(" ) ");

    private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
