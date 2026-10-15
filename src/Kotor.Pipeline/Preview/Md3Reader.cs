using System.Numerics;
using System.Text;

namespace Kotor.Pipeline.Preview;

/// <summary>Une surface d'un MD3 (premiere image). UV tels qu'ecrits dans le fichier (t = 0 en haut, comme les images).</summary>
public sealed record Md3Surface(string Name, string Shader, Vector3[] Positions, Vector3[] Normals, Vector2[] Uv, int[] Indices);

/// <summary>
/// Lecteur MD3 minimal (IDP3, version 15), premiere image seulement, premier shader de chaque surface. Format : Quake 3 qfiles.h.
/// Positions en 1/64 d'unite, normales sur deux octets (longitude/latitude sur 256 pas de 2 pi).
/// </summary>
public static class Md3Reader
{
    /// <exception cref="InvalidDataException">Fichier tronque ou en-tete incorrect.</exception>
    public static IReadOnlyList<Md3Surface> Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 108 || data[0] != 'I' || data[1] != 'D' || data[2] != 'P' || data[3] != '3')
            throw new InvalidDataException("Not an MD3 file (IDP3 header missing).");
        int version = I32(data, 4);
        if (version != 15) throw new InvalidDataException($"Unsupported MD3 version {version}.");
        int numSurfaces = I32(data, 84), ofsSurfaces = I32(data, 100);
        if (numSurfaces < 0 || numSurfaces > 1000) throw new InvalidDataException($"Invalid MD3 surface count {numSurfaces}.");

        var list = new List<Md3Surface>(numSurfaces);
        int pos = ofsSurfaces;
        for (int s = 0; s < numSurfaces; s++)
        {
            Need(data, pos, 108);
            int numShaders = I32(data, pos + 76), numVerts = I32(data, pos + 80), numTris = I32(data, pos + 84);
            int ofsTri = I32(data, pos + 88), ofsShader = I32(data, pos + 92), ofsSt = I32(data, pos + 96), ofsXyz = I32(data, pos + 100), end = I32(data, pos + 104);
            if (numVerts < 0 || numTris < 0 || numShaders < 0 || end < 108) throw new InvalidDataException("Invalid MD3 surface header.");
            Need(data, pos + ofsTri, numTris * 12);
            Need(data, pos + ofsSt, numVerts * 8);
            Need(data, pos + ofsXyz, numVerts * 8);

            string shader = numShaders > 0 ? Name(data, pos + ofsShader, 64) : "";
            var idx = new int[numTris * 3];
            for (int i = 0; i < idx.Length; i++)
            {
                idx[i] = I32(data, pos + ofsTri + i * 4);
                if (idx[i] < 0 || idx[i] >= numVerts) throw new InvalidDataException("MD3 triangle index out of range.");
            }
            var uv = new Vector2[numVerts];
            var p = new Vector3[numVerts];
            var n = new Vector3[numVerts];
            for (int i = 0; i < numVerts; i++)
            {
                uv[i] = new Vector2(BitConverter.ToSingle(data, pos + ofsSt + i * 8), BitConverter.ToSingle(data, pos + ofsSt + i * 8 + 4));
                int o = pos + ofsXyz + i * 8;
                p[i] = new Vector3(BitConverter.ToInt16(data, o), BitConverter.ToInt16(data, o + 2), BitConverter.ToInt16(data, o + 4)) / 64f;
                float lat = data[o + 7] * (2f * MathF.PI / 256f), lng = data[o + 6] * (2f * MathF.PI / 256f);
                n[i] = new Vector3(MathF.Cos(lat) * MathF.Sin(lng), MathF.Sin(lat) * MathF.Sin(lng), MathF.Cos(lng));
            }
            list.Add(new Md3Surface(Name(data, pos + 4, 64), shader, p, n, uv, idx));
            pos += end;
        }
        return list;
    }

    private static int I32(byte[] d, int o)
    {
        Need(d, o, 4);
        return BitConverter.ToInt32(d, o);
    }

    private static void Need(byte[] d, int o, int n)
    {
        if (o < 0 || n < 0 || (long)o + n > d.Length) throw new InvalidDataException("Truncated MD3 file.");
    }

    private static string Name(byte[] d, int o, int size)
    {
        Need(d, o, size);
        int len = 0;
        while (len < size && d[o + len] != 0) len++;
        return Encoding.ASCII.GetString(d, o, len);
    }
}
