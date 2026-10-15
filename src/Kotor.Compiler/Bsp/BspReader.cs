// Format : RBSP version 1 (OpenJK, codemp/qcommon/qfiles.h), meme disposition que BspWriter. Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;
using System.Text;

namespace Kotor.Compiler.Bsp;

/// <summary>Surface triangle soup relue. <c>Colors</c> : RGBA, 4 octets par sommet (couleur de style 0). Indices locaux a la surface.</summary>
public sealed record BspSurface(string Shader, int SurfaceType, Vector3[] Positions, Vector3[] Normals, Vector2[] Uv0, Vector2[] Uv1,
    byte[] Colors, int[] Indices);

public sealed record BspBrushInfo(int BrushCount, int BrushSideCount, int PlaneCount);

/// <summary>Modele du lump des modeles (0 = monde, 1.. = sous-modeles <c>*N</c>).</summary>
public sealed record BspModelInfo(Vector3 Mins, Vector3 Maxs, int FirstSurface, int SurfaceCount, int FirstBrush, int BrushCount);

public sealed record BspFile(IReadOnlyList<string> Shaders, IReadOnlyList<BspSurface> Surfaces, string Entities, BspBrushInfo Collision)
{
    /// <summary>Lump des modeles : le monde, puis les sous-modeles (portes).</summary>
    public IReadOnlyList<BspModelInfo> Models { get; init; } = [];
    /// <summary>Nombre de cotes de chaque brush du lump des brushes.</summary>
    public IReadOnlyList<int> BrushSideCounts { get; init; } = [];
    /// <summary>Contenu du lump des leaf brushes : indices de brushes references par les feuilles de l arbre.</summary>
    public IReadOnlyList<int> LeafBrushes { get; init; } = [];
}

/// <summary>Relit un .bsp ecrit par <see cref="BspWriter"/> (entites, shaders, surfaces de rendu, comptes de collision).</summary>
public static class BspReader
{
    private const int ShaderSize = 72, DrawVertSize = 80, SurfaceSize = 148, PlaneSize = 16, BrushSize = 12, BrushSideSize = 12, ModelSize = 40;

    public static BspFile Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < BspWriter.HeaderSize) throw new InvalidDataException("BSP file too short to hold the header.");
        if (Encoding.ASCII.GetString(data, 0, 4) != "RBSP")
            throw new InvalidDataException("Unknown BSP header: \"RBSP\" expected.");
        int version = BitConverter.ToInt32(data, 4);
        if (version != 1) throw new InvalidDataException($"BSP version {version} is not supported (RBSP version 1 expected).");

        var lumps = new (int Ofs, int Len)[BspWriter.LumpCount];
        for (int i = 0; i < lumps.Length; i++)
        {
            int ofs = BitConverter.ToInt32(data, 8 + i * 8), len = BitConverter.ToInt32(data, 12 + i * 8);
            if (ofs < BspWriter.HeaderSize || len < 0 || (long)ofs + len > data.Length)
                throw new InvalidDataException($"Lump {i} outside the file (offset {ofs}, length {len}, file {data.Length} bytes).");
            lumps[i] = (ofs, len);
        }

        int Count(int lump, int size)
        {
            if (lumps[lump].Len % size != 0)
                throw new InvalidDataException($"Lump {lump}: length {lumps[lump].Len} is not a multiple of {size}.");
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
            if (shaderNum < 0 || shaderNum >= shaderCount) throw new InvalidDataException($"Surface {s}: shader {shaderNum} does not exist.");
            if (firstVert < 0 || numVerts < 0 || (long)firstVert + numVerts > vertCount)
                throw new InvalidDataException($"Surface {s}: vertices {firstVert}+{numVerts} outside the lump ({vertCount}).");
            if (firstIndex < 0 || numIndexes < 0 || (long)firstIndex + numIndexes > indexCount)
                throw new InvalidDataException($"Surface {s}: indexes {firstIndex}+{numIndexes} outside the lump ({indexCount}).");

            if (numVerts == 0) throw new InvalidDataException($"Surface {s}: no vertex.");
            if (numIndexes == 0) throw new InvalidDataException($"Surface {s}: no index.");
            if (numIndexes % 3 != 0) throw new InvalidDataException($"Surface {s}: {numIndexes} indexes, not a multiple of 3.");

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
                    throw new InvalidDataException($"Surface {s}: index {ix} outside its {numVerts} vertices.");
                idx[k] = ix;
            }
            surfaces.Add(new BspSurface(shaders[shaderNum], type, pos, nor, uv0, uv1, col, idx));
        }

        var models = new List<BspModelInfo>();
        int modelCount = Count(BspWriter.LumpModels, ModelSize), mo = lumps[BspWriter.LumpModels].Ofs;
        for (int m = 0; m < modelCount; m++)
        {
            int o = mo + m * ModelSize;
            models.Add(new BspModelInfo(new Vector3(F(o), F(o + 4), F(o + 8)), new Vector3(F(o + 12), F(o + 16), F(o + 20)),
                I(o + 24), I(o + 28), I(o + 32), I(o + 36)));
        }
        int brushCount = Count(BspWriter.LumpBrushes, BrushSize), bo = lumps[BspWriter.LumpBrushes].Ofs;
        var sideCounts = new int[brushCount];
        for (int b = 0; b < brushCount; b++) sideCounts[b] = I(bo + b * BrushSize + 4);
        int leafBrushCount = Count(BspWriter.LumpLeafBrushes, 4), lbo = lumps[BspWriter.LumpLeafBrushes].Ofs;
        var leafBrushes = new int[leafBrushCount];
        for (int i = 0; i < leafBrushCount; i++) leafBrushes[i] = I(lbo + i * 4);

        var collision = new BspBrushInfo(Count(BspWriter.LumpBrushes, BrushSize), Count(BspWriter.LumpBrushSides, BrushSideSize),
            Count(BspWriter.LumpPlanes, PlaneSize));
        return new BspFile(shaders, surfaces, entities, collision) { Models = models, BrushSideCounts = sideCounts, LeafBrushes = leafBrushes };
    }
}
