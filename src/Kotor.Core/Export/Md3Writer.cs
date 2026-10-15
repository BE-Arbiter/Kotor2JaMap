using System.Numerics;
using System.Text;
using Kotor.Core.Models;

namespace Kotor.Core.Export;

/// <summary>
/// Ecrivain MD3 (IDP3, version 15), une image, sans tag. Format : Quake 3 qfiles.h (md3Header_t, md3Frame_t, md3Surface_t, md3Shader_t,
/// md3XyzNormal_t). Sortie deterministe : surfaces triees par nom de texture. Aucun ecrivain MD3 n'existe dans le projet TFU : code nouveau.
/// </summary>
public static class Md3Writer
{
    /// <summary>Limite des coordonnees : un short en 1/64 d'unite, soit 32767 / 64 = 511.98.</summary>
    public const float MaxCoordinate = 511.98f;
    public const int MaxVerticesPerSurface = 4096, MaxTrianglesPerSurface = 8192, MaxSurfaces = 32;
    private const int HeaderSize = 108, FrameSize = 56, SurfaceHeaderSize = 108, ShaderSize = 68;

    /// <summary>
    /// Une image, une surface par <see cref="ModelPart"/> (decoupee si elle depasse 4096 sommets ou 8192 triangles), shader = <paramref name="shaderPrefix"/> + texture.
    /// Les positions sont en unites JA. UV : GL en entree, t = 1 - v dans le fichier.
    /// </summary>
    /// <exception cref="ArgumentException">Une coordonnee depasse +-511.98, un nom est trop long, trop de surfaces, ou une partie est incoherente.</exception>
    public static byte[] Write(string name, IReadOnlyList<ModelPart> partsJaUnits, string shaderPrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(partsJaUnits);
        ArgumentNullException.ThrowIfNull(shaderPrefix);
        if (name.Length > 63) throw new ArgumentException($"MD3 name too long (max 63 characters): {name}", nameof(name));

        var surfaces = new List<Surface>();
        foreach (ModelPart part in partsJaUnits.OrderBy(p => p.Texture, StringComparer.Ordinal))
        {
            string shader = shaderPrefix + part.Texture;
            if (shader.Length > 63) throw new ArgumentException($"MD3 shader name too long (max 63 characters): {shader}", nameof(partsJaUnits));
            Validate(part);
            surfaces.AddRange(Split(part, shader));
        }
        if (surfaces.Count == 0) throw new ArgumentException("An MD3 needs at least one surface.", nameof(partsJaUnits));
        if (surfaces.Count > MaxSurfaces) throw new ArgumentException($"Too many MD3 surfaces ({surfaces.Count}, max {MaxSurfaces}).", nameof(partsJaUnits));

        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        float radius = 0;
        foreach (Surface s in surfaces)
            foreach (Vector3 p in s.Positions)
            {
                min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                radius = MathF.Max(radius, p.Length());
            }

        int offSurfaces = HeaderSize + FrameSize;
        int total = offSurfaces + surfaces.Sum(SurfaceBytes);
        var buf = new byte[total];
        using var ms = new MemoryStream(buf);
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        w.Write("IDP3"u8.ToArray());
        w.Write(15);
        WriteName(w, name, 64);
        w.Write(0);                   // flags
        w.Write(1);                   // numFrames
        w.Write(0);                   // numTags
        w.Write(surfaces.Count);
        w.Write(0);                   // numSkins
        w.Write(HeaderSize);          // ofsFrames
        w.Write(offSurfaces);         // ofsTags (aucun)
        w.Write(offSurfaces);         // ofsSurfaces
        w.Write(total);               // ofsEnd

        // Frame
        W(w, min); W(w, max); W(w, Vector3.Zero); w.Write(radius);
        WriteName(w, "frame0", 16);

        foreach (Surface s in surfaces)
        {
            int nT = s.Indices.Length / 3, nV = s.Positions.Length;
            int ofsTri = SurfaceHeaderSize, ofsShader = ofsTri + nT * 12, ofsSt = ofsShader + ShaderSize, ofsXyz = ofsSt + nV * 8;
            int end = ofsXyz + nV * 8;
            w.Write("IDP3"u8.ToArray());
            WriteName(w, s.Name, 64);
            w.Write(0);               // flags
            w.Write(1);               // numFrames
            w.Write(1);               // numShaders
            w.Write(nV);
            w.Write(nT);
            w.Write(ofsTri); w.Write(ofsShader); w.Write(ofsSt); w.Write(ofsXyz); w.Write(end);
            foreach (int i in s.Indices) w.Write(i);
            WriteName(w, s.Shader, 64);
            w.Write(0);               // shaderIndex
            foreach (Vector2 uv in s.Uv) { w.Write(uv.X); w.Write(1f - uv.Y); }
            for (int i = 0; i < nV; i++)
            {
                Vector3 p = s.Positions[i];
                w.Write(Fixed(p.X)); w.Write(Fixed(p.Y)); w.Write(Fixed(p.Z));
                w.Write(EncodeNormal(s.Normals[i]));
            }
        }
        return buf;
    }

    private sealed record Surface(string Name, string Shader, Vector3[] Positions, Vector3[] Normals, Vector2[] Uv, int[] Indices);

    private static int SurfaceBytes(Surface s) =>
        SurfaceHeaderSize + s.Indices.Length / 3 * 12 + ShaderSize + s.Positions.Length * 16;

    private static void Validate(ModelPart p)
    {
        if (p.Indices.Length == 0 || p.Indices.Length % 3 != 0)
            throw new ArgumentException($"Part '{p.Texture}': the index count must be a positive multiple of 3.");
        if (p.Normals.Length != p.Positions.Length || p.Uv.Length != p.Positions.Length)
            throw new ArgumentException($"Part '{p.Texture}': positions, normals and UV must have the same length.");
        foreach (int i in p.Indices)
            if (i < 0 || i >= p.Positions.Length) throw new ArgumentException($"Part '{p.Texture}': index {i} out of range.");
        foreach (Vector3 v in p.Positions)
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)
                || MathF.Abs(v.X) > MaxCoordinate || MathF.Abs(v.Y) > MaxCoordinate || MathF.Abs(v.Z) > MaxCoordinate)
                throw new ArgumentException($"Part '{p.Texture}': coordinate ({v.X}, {v.Y}, {v.Z}) out of the MD3 range (+-{MaxCoordinate}).");
    }

    /// <summary>Decoupe une partie en surfaces de 4096 sommets et 8192 triangles au plus (sommets recopies, ordre des triangles conserve).</summary>
    private static IEnumerable<Surface> Split(ModelPart p, string shader)
    {
        if (p.Positions.Length <= MaxVerticesPerSurface && p.Indices.Length / 3 <= MaxTrianglesPerSurface)
        {
            yield return new Surface(p.Texture, shader, p.Positions, p.Normals, p.Uv, p.Indices);
            yield break;
        }
        var remap = new Dictionary<int, int>();
        var pos = new List<Vector3>(); var nor = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
        for (int t = 0; t < p.Indices.Length; t += 3)
        {
            int fresh = 0;
            for (int k = 0; k < 3; k++) if (!remap.ContainsKey(p.Indices[t + k])) fresh++;
            if (pos.Count + fresh > MaxVerticesPerSurface || idx.Count / 3 + 1 > MaxTrianglesPerSurface)
            {
                yield return new Surface(p.Texture, shader, [.. pos], [.. nor], [.. uv], [.. idx]);
                remap.Clear(); pos.Clear(); nor.Clear(); uv.Clear(); idx.Clear();
            }
            for (int k = 0; k < 3; k++)
            {
                int src = p.Indices[t + k];
                if (!remap.TryGetValue(src, out int dst))
                {
                    dst = pos.Count; remap[src] = dst;
                    pos.Add(p.Positions[src]); nor.Add(p.Normals[src]); uv.Add(p.Uv[src]);
                }
                idx.Add(dst);
            }
        }
        if (idx.Count > 0) yield return new Surface(p.Texture, shader, [.. pos], [.. nor], [.. uv], [.. idx]);
    }

    private static short Fixed(float v) => (short)Math.Clamp((int)MathF.Round(v * 64f), short.MinValue, short.MaxValue);

    /// <summary>Normale MD3 : octet bas = acos(z) * 255 / 2pi, octet haut = atan2(y, x) * 255 / 2pi (q3map2 NormalToLatLong).</summary>
    private static short EncodeNormal(Vector3 n)
    {
        byte lng, lat;
        if (n.X == 0 && n.Y == 0) { lng = n.Z > 0 ? (byte)0 : (byte)128; lat = 0; }
        else
        {
            double z = Math.Clamp(n.Z, -1f, 1f);
            lng = (byte)(((int)Math.Round(Math.Acos(z) * 255.0 / (2 * Math.PI))) & 255);
            lat = (byte)(((int)Math.Round(Math.Atan2(n.Y, n.X) * 255.0 / (2 * Math.PI))) & 255);
        }
        return unchecked((short)((lat << 8) | lng));
    }

    private static void W(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }

    private static void WriteName(BinaryWriter w, string s, int size)
    {
        var b = new byte[size];
        Encoding.ASCII.GetBytes(s, 0, Math.Min(s.Length, size - 1), b, 0);
        w.Write(b);
    }
}
