// Format : RBSP version 1 (OpenJK, codemp/qcommon/qfiles.h), meme disposition que BspWriter. Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;
using System.Text;

namespace Kotor.Compiler.Bsp;

/// <summary>Surface triangle soup relue. <c>Colors</c> : RGBA, 4 octets par sommet (couleur de style 0). Indices locaux a la surface.</summary>
public sealed record BspSurface(string Shader, int SurfaceType, Vector3[] Positions, Vector3[] Normals, Vector2[] Uv0, Vector2[] Uv1,
    byte[] Colors, int[] Indices);

public sealed record BspBrushInfo(int BrushCount, int BrushSideCount, int PlaneCount);

public sealed record BspFile(IReadOnlyList<string> Shaders, IReadOnlyList<BspSurface> Surfaces, string Entities, BspBrushInfo Collision);

/// <summary>Relit un .bsp ecrit par <see cref="BspWriter"/> (entites, shaders, surfaces de rendu, comptes de collision).</summary>
public static class BspReader
{
    private const int ShaderSize = 72, DrawVertSize = 80, SurfaceSize = 148, PlaneSize = 16, BrushSize = 12, BrushSideSize = 12;

    public static BspFile Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < BspWriter.HeaderSize) throw new InvalidDataException("Fichier BSP trop court pour contenir l'en-tete.");
        if (Encoding.ASCII.GetString(data, 0, 4) != "RBSP")
            throw new InvalidDataException("En-tete BSP inconnu : \"RBSP\" attendu.");
        int version = BitConverter.ToInt32(data, 4);
        if (version != 1) throw new InvalidDataException($"Version BSP {version} non prise en charge (RBSP version 1 attendue).");

        var lumps = new (int Ofs, int Len)[BspWriter.LumpCount];
        for (int i = 0; i < lumps.Length; i++)
        {
            int ofs = BitConverter.ToInt32(data, 8 + i * 8), len = BitConverter.ToInt32(data, 12 + i * 8);
            if (ofs < BspWriter.HeaderSize || len < 0 || (long)ofs + len > data.Length)
                throw new InvalidDataException($"Lump {i} hors du fichier (offset {ofs}, longueur {len}, fichier {data.Length} octets).");
            lumps[i] = (ofs, len);
        }

        int Count(int lump, int size)
        {
            if (lumps[lump].Len % size != 0)
                throw new InvalidDataException($"Lump {lump} : longueur {lumps[lump].Len} non multiple de {size}.");
            return lumps[lump].Len / size;
        }

        var (eo, el) = lumps[BspWriter.LumpEntities];
        int entLen = el;
        while (entLen > 0 && data[eo + entLen - 1] == 0) entLen--;
        string entities = Encoding.ASCII.GetString(data, eo, entLen);

        int shaderCount = Count(BspWriter.LumpShaders, ShaderSize);
        var shaders = new string[shaderCount];
        for (int i = 0; i < shaderCount; i++)
        {
            int o = lumps[BspWriter.LumpShaders].Ofs + i * ShaderSize;
            int n = 0;
            while (n < 64 && data[o + n] != 0) n++;
            shaders[i] = Encoding.ASCII.GetString(data, o, n);
        }

        int vertCount = Count(BspWriter.LumpDrawVerts, DrawVertSize);
        int indexCount = Count(BspWriter.LumpDrawIndexes, 4);
        int surfCount = Count(BspWriter.LumpSurfaces, SurfaceSize);
        int vo = lumps[BspWriter.LumpDrawVerts].Ofs, io = lumps[BspWriter.LumpDrawIndexes].Ofs, so = lumps[BspWriter.LumpSurfaces].Ofs;

        float F(int o) => BitConverter.ToSingle(data, o);
        int I(int o) => BitConverter.ToInt32(data, o);

        var surfaces = new List<BspSurface>(surfCount);
        for (int s = 0; s < surfCount; s++)
        {
            int o = so + s * SurfaceSize;
            int shaderNum = I(o), type = I(o + 8), firstVert = I(o + 12), numVerts = I(o + 16), firstIndex = I(o + 20), numIndexes = I(o + 24);
            if (shaderNum < 0 || shaderNum >= shaderCount) throw new InvalidDataException($"Surface {s} : shader {shaderNum} inexistant.");
            if (firstVert < 0 || numVerts < 0 || (long)firstVert + numVerts > vertCount)
                throw new InvalidDataException($"Surface {s} : sommets {firstVert}+{numVerts} hors du lump ({vertCount}).");
            if (firstIndex < 0 || numIndexes < 0 || (long)firstIndex + numIndexes > indexCount)
                throw new InvalidDataException($"Surface {s} : index {firstIndex}+{numIndexes} hors du lump ({indexCount}).");

            if (numVerts == 0) throw new InvalidDataException($"Surface {s} : aucun sommet.");
            if (numIndexes == 0) throw new InvalidDataException($"Surface {s} : aucun index.");
            if (numIndexes % 3 != 0) throw new InvalidDataException($"Surface {s} : {numIndexes} index, non multiple de 3.");

            var pos = new Vector3[numVerts];
            var nor = new Vector3[numVerts];
            var uv0 = new Vector2[numVerts];
            var uv1 = new Vector2[numVerts];
            var col = new byte[numVerts * 4];
            for (int v = 0; v < numVerts; v++)
            {
                int p = vo + (firstVert + v) * DrawVertSize;
                pos[v] = new Vector3(F(p), F(p + 4), F(p + 8));
                uv0[v] = new Vector2(F(p + 12), F(p + 16));
                uv1[v] = new Vector2(F(p + 20), F(p + 24));
                nor[v] = new Vector3(F(p + 52), F(p + 56), F(p + 60));
                Array.Copy(data, p + 64, col, v * 4, 4);
            }
            var idx = new int[numIndexes];
            for (int k = 0; k < numIndexes; k++)
            {
                int ix = I(io + (firstIndex + k) * 4);
                if (ix < 0 || ix >= numVerts)
                    throw new InvalidDataException($"Surface {s} : index {ix} hors de ses {numVerts} sommets.");
                idx[k] = ix;
            }
            surfaces.Add(new BspSurface(shaders[shaderNum], type, pos, nor, uv0, uv1, col, idx));
        }

        var collision = new BspBrushInfo(Count(BspWriter.LumpBrushes, BrushSize), Count(BspWriter.LumpBrushSides, BrushSideSize),
            Count(BspWriter.LumpPlanes, PlaneSize));
        return new BspFile(shaders, surfaces, entities, collision);
    }
}
