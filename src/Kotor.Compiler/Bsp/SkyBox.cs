// Inspiré de la démarche de q3map2 / NetRadiant-custom (boîte de ciel autour de la carte, GPL-2.0) : aucun code copié.
// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;

namespace Kotor.Compiler.Bsp;

/// <summary>
/// Boîte de ciel : 6 faces (12 triangles) englobant toute la géométrie avec une marge, visibles depuis l'intérieur,
/// portant un shader de ciel d'origine (<c>skyParms</c>). Une surface par face, rattachée à la feuille de son centre.
/// </summary>
public static class SkyBox
{
    public const float DefaultMargin = 512f;

    /// <summary>Boîte englobant les surfaces et les brushes de l'arbre ; <c>false</c> si aucune géométrie.</summary>
    public static bool TryComputeBounds(BspTree tree, IReadOnlyList<RenderSurface> surfaces, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);
        bool any = false;
        foreach (RenderSurface s in surfaces)
            foreach (Vector3 p in s.Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); any = true; }
        foreach (Brush b in tree.Brushes) { min = Vector3.Min(min, b.Min); max = Vector3.Max(max, b.Max); any = true; }
        return any;
    }

    public static IReadOnlyList<RenderSurface> Build(BspTree tree, string shader, Vector3 min, Vector3 max, float margin = DefaultMargin)
    {
        Vector3 lo = min - new Vector3(margin), hi = max + new Vector3(margin);
        var list = new List<RenderSurface>(6);
        for (int axis = 0; axis < 3; axis++)
            foreach (int sign in new[] { -1, 1 })
            {
                // Plan de la face : coordonnée fixe sur 'axis', les deux autres axes balayent la face.
                int u = (axis + 1) % 3, v = (axis + 2) % 3;
                float fixedCoord = sign < 0 ? Get(lo, axis) : Get(hi, axis);
                Vector3 inward = Vector3.Zero;
                inward = Set(inward, axis, -sign);

                Vector3 Corner(bool uHi, bool vHi)
                {
                    Vector3 p = Vector3.Zero;
                    p = Set(p, axis, fixedCoord);
                    p = Set(p, u, uHi ? Get(hi, u) : Get(lo, u));
                    p = Set(p, v, vHi ? Get(hi, v) : Get(lo, v));
                    return p;
                }
                Vector3 a = Corner(false, false), b = Corner(true, false), c = Corner(true, true), d = Corner(false, true);
                Vector3[] pos = [a, b, c, d];
                int[] idx = [0, 1, 2, 0, 2, 3];
                // Enroulement JA (étape 15) : le produit mixte (cross des arêtes . normale visible) doit être négatif.
                // La face est vue depuis l'intérieur : la normale visible est 'inward'.
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), inward) > 0) idx = [0, 2, 1, 0, 3, 2];
                Vector3 center = (a + c) * 0.5f;
                list.Add(new RenderSurface(shader, pos, [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY],
                    [inward, inward, inward, inward], idx, RenderSurfaces.FindLeaf(tree, center), Sky: true));
            }
        return list;
    }

    private static float Get(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    private static Vector3 Set(Vector3 v, int i, float f)
    {
        if (i == 0) v.X = f; else if (i == 1) v.Y = f; else v.Z = f;
        return v;
    }
}
