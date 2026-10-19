using System.Numerics;

namespace Kotor.Pipeline.Preview;

/// <summary>Tir de rayon sur la vue (unites JA) : sert a poser le point d'apparition d'un clic sur le sol.</summary>
public static class PreviewPicker
{
    /// <summary>
    /// Premier point touche par le rayon (les deux faces des triangles). Seuls les lots opaques ou decoupes comptent : ni le ciel, ni
    /// les lots transparents (verre, decalques, lueurs). Null si rien n'est touche.
    /// </summary>
    public static Vector3? Raycast(PreviewModel model, Vector3 origin, Vector3 direction) => RaycastRoom(model, origin, direction)?.Point;

    /// <summary>Comme <see cref="Raycast"/>, avec la room du lot touche (<see cref="PreviewBatch.Room"/>, null si le lot n'en a pas).</summary>
    public static (Vector3 Point, string? Room)? RaycastRoom(PreviewModel model, Vector3 origin, Vector3 direction)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (direction.LengthSquared() < 1e-12f) return null;
        direction = Vector3.Normalize(direction);
        float best = float.MaxValue;
        string? room = null;
        foreach (PreviewBatch b in model.Batches)
        {
            if (b.IsSky || b.Blend is not (PreviewBlend.Opaque or PreviewBlend.Cutout)) continue;
            PreviewVertex[] v = b.Vertices;
            int[] idx = b.Indices;
            for (int i = 0; i + 2 < idx.Length; i += 3)
                if (Intersect(origin, direction, v[idx[i]].Position, v[idx[i + 1]].Position, v[idx[i + 2]].Position) is { } t && t < best) { best = t; room = b.Room; }
        }
        return best == float.MaxValue ? null : (origin + direction * best, room);
    }

    /// <summary>Boite englobante des lots d'une room (hors ciel), pour y amener la camera ; null si la room n'a aucun lot dans la vue.</summary>
    public static (Vector3 Min, Vector3 Max)? RoomBounds(PreviewModel model, string room)
    {
        ArgumentNullException.ThrowIfNull(model);
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        bool any = false;
        foreach (PreviewBatch b in model.Batches)
        {
            if (b.IsSky || !string.Equals(b.Room, room, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (PreviewVertex v in b.Vertices) { min = Vector3.Min(min, v.Position); max = Vector3.Max(max, v.Position); any = true; }
        }
        return any ? (min, max) : null;
    }

    // Moller-Trumbore, sans culling ; distance le long du rayon, ou null.
    private static float? Intersect(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 e1 = b - a, e2 = c - a;
        Vector3 p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-9f) return null;
        float inv = 1f / det;
        Vector3 s = o - a;
        float u = Vector3.Dot(s, p) * inv;
        if (u < 0f || u > 1f) return null;
        Vector3 q = Vector3.Cross(s, e1);
        float w = Vector3.Dot(d, q) * inv;
        if (w < 0f || u + w > 1f) return null;
        float t = Vector3.Dot(e2, q) * inv;
        return t > 1e-3f ? t : null;
    }
}
