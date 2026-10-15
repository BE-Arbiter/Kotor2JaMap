namespace Kotor.Core.Textures;

/// <summary>
/// Repairs KOTOR lightmap atlases. Unused areas of the atlas hold noise marked by alpha != 255 (the useful texels have
/// alpha = 255). The renderer samples neighbouring texels (bilinear filtering, mipmaps, r_picmip), so the noise bleeds
/// into the surfaces (green stains). The invalid texels are replaced by the nearest valid one.
/// </summary>
public static class LightmapRepair
{
    private static readonly (int dx, int dy)[] Neighbours = [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)];

    /// <summary>
    /// Replaces the texels with alpha != 255 by the nearest valid texel (iterated 8-neighbour dilation); alpha = 255 everywhere.
    /// No valid texel: everything becomes (128,128,128,255). Returns the number of repaired texels and whether the image had no valid texel.
    /// </summary>
    public static (int Repaired, bool NoValidTexel) Repair(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        if (width <= 0 || height <= 0 || rgba.Length < checked(width * height * 4))
            throw new ArgumentException("Pixel buffer does not match the dimensions.", nameof(rgba));
        int count = width * height;
        var valid = new bool[count];
        int validCount = 0;
        for (int i = 0; i < count; i++) if (rgba[i * 4 + 3] == 255) { valid[i] = true; validCount++; }
        if (validCount == count) return (0, false);
        if (validCount == 0)
        {
            for (int i = 0; i < count; i++) { rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 128; rgba[i * 4 + 3] = 255; }
            return (count, true);
        }

        int remaining = count - validCount;
        var fill = new List<(int index, int source)>();
        while (remaining > 0)
        {
            fill.Clear();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (valid[i]) continue;
                    foreach (var (dx, dy) in Neighbours)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        int n = ny * width + nx;
                        if (valid[n]) { fill.Add((i, n)); break; }
                    }
                }
            foreach (var (index, source) in fill)
            {
                rgba[index * 4] = rgba[source * 4];
                rgba[index * 4 + 1] = rgba[source * 4 + 1];
                rgba[index * 4 + 2] = rgba[source * 4 + 2];
                rgba[index * 4 + 3] = 255;
            }
            foreach (var (index, _) in fill) valid[index] = true;
            remaining -= fill.Count;
            if (fill.Count == 0) break; // unreachable: at least one valid texel exists
        }
        return (count - validCount, false);
    }
}
