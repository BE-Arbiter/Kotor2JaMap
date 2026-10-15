// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;

namespace Kotor.Compiler.Bsp;

/// <summary>Boite englobante d'un brush convexe donne par ses plans (sommets = intersections de trois plans contenues dans tous les autres).</summary>
public static class BrushBounds
{
    private const float Epsilon = 0.01f;

    /// <summary>Null si le brush est vide, degenere ou non borne.</summary>
    public static (Vector3 Min, Vector3 Max)? FromPlanes(IReadOnlyList<BspPlane> planes)
    {
        int n = planes.Count;
        if (n < 4) return null;
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        bool any = false;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                for (int k = j + 1; k < n; k++)
                {
                    Vector3 n1 = planes[i].Normal, n2 = planes[j].Normal, n3 = planes[k].Normal;
                    float det = Vector3.Dot(n1, Vector3.Cross(n2, n3));
                    if (MathF.Abs(det) < 1e-6f) continue;
                    Vector3 p = (planes[i].Dist * Vector3.Cross(n2, n3) + planes[j].Dist * Vector3.Cross(n3, n1)
                        + planes[k].Dist * Vector3.Cross(n1, n2)) / det;
                    bool inside = true;
                    for (int m = 0; m < n && inside; m++)
                        inside = Vector3.Dot(planes[m].Normal, p) <= planes[m].Dist + Epsilon;
                    if (!inside) continue;
                    lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                    any = true;
                }
        if (!any) return null;
        Vector3 size = hi - lo;
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0 || !float.IsFinite(size.X + size.Y + size.Z)) return null;
        return (lo, hi);
    }
}
