using System.Buffers.Binary;
using System.IO.Compression;
using ZstdSharp;

namespace Swtor.Core.Terrain;

/// <summary>
/// Une dalle de terrain SWTOR (objet de room <c>\engine\heightmap.hms</c>) : grille de hauteurs, centree sur la position de l'objet.
/// Format decode sur les ressources reelles (voir swtor-formats.md, section Terrain) : le blob du champ de type 9 est compresse
/// (zlib, gzip ou zstd) ; le flux decompresse est
/// <c>u8 version (1 ou 2)</c>, <c>u32 lignes</c>, <c>u32 colonnes</c>, <c>u8 drapeau</c> (0 ou 1), si drapeau = 1 un masque de trous
/// (un bit par echantillon, <c>lignes*colonnes</c> bits arrondis a l'octet, bit de poids fort d'abord, 1 = echantillon present), puis le
/// pourtour de la grille en <c>f32</c> (<c>2*(lignes-1) + 2*(colonnes-1)</c> valeurs) puis l'interieur en <c>u16</c>
/// (<c>(lignes-2) * (colonnes-2)</c> valeurs, rangees le long de Z), hauteur = valeur / 512. Le reste (carte de melange des
/// materiaux, maillage de rendu, magique 0xABCDEFAB) n'est pas lu.
/// </summary>
public sealed class HeightmapTile
{
    /// <summary>Pas entre deux echantillons (unites du monde SWTOR) : le pas du terrain est constant, la dalle de 65 echantillons couvre 12,8.</summary>
    public const float CellSize = 0.2f;

    /// <summary>Une valeur u16 de l'interieur vaut 1/512 unite.</summary>
    public const float HeightStep = 1f / 512f;

    /// <summary>Nombre d'echantillons le long de X (rangees).</summary>
    public int Rows { get; }

    /// <summary>Nombre d'echantillons le long de Z (colonnes).</summary>
    public int Columns { get; }

    public int Version { get; }

    /// <summary>Hauteurs (unites du monde, relatives a l'origine de l'objet), <c>Rows * Columns</c>, indice <c>ligne * Columns + colonne</c>.</summary>
    public float[] Heights { get; }

    /// <summary>Masque des echantillons (<c>Rows * Columns</c>, meme indice que <see cref="Heights"/>) : faux = trou ; null = aucun trou.</summary>
    public bool[]? VertexPresent { get; }

    public HeightmapTile(int version, int rows, int columns, float[] heights, bool[]? vertexPresent)
    {
        if (rows < 2 || columns < 2) throw new ArgumentOutOfRangeException(nameof(rows), "Une dalle a au moins 2 x 2 echantillons.");
        if (heights.Length != rows * columns) throw new ArgumentException("Nombre de hauteurs incoherent.", nameof(heights));
        if (vertexPresent is not null && vertexPresent.Length != rows * columns) throw new ArgumentException("Masque d'echantillons incoherent.", nameof(vertexPresent));
        Version = version;
        Rows = rows;
        Columns = columns;
        Heights = heights;
        VertexPresent = vertexPresent;
    }

    public float HeightAt(int row, int column) => Heights[row * Columns + column];

    /// <summary>Une cellule (carre entre la ligne/colonne donnee et la suivante) est dessinee si ses quatre sommets sont presents.</summary>
    public bool IsCellPresent(int row, int column) =>
        VertexPresent is null || (VertexPresent[row * Columns + column] && VertexPresent[row * Columns + column + 1]
            && VertexPresent[(row + 1) * Columns + column] && VertexPresent[(row + 1) * Columns + column + 1]);

    /// <summary>Emplacement local (unites du monde, X le long des rangees, Z le long des colonnes) d'un echantillon : la dalle est centree sur l'origine de l'objet.</summary>
    public (float X, float Z) LocalXZ(int row, int column) =>
        ((row - (Rows - 1) / 2f) * CellSize, (column - (Columns - 1) / 2f) * CellSize);

    /// <summary>Decompresse puis decode le blob d'un objet heightmap (champ de type 9).</summary>
    public static HeightmapTile ReadBlob(byte[] blob) => Read(Decompress(blob));

    /// <summary>Decode le flux decompresse.</summary>
    public static HeightmapTile Read(byte[] data)
    {
        if (data.Length < 10) throw new GameFormatException("Terrain trop court", 0);
        int version = data[0];
        if (version is not (1 or 2)) throw new GameFormatException($"Version de terrain {version} non geree", 0);
        uint rowsU = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(1));
        uint colsU = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(5));
        if (rowsU is < 4 or > 1024 || colsU is < 4 or > 1024) throw new GameFormatException($"Dimensions de terrain {rowsU} x {colsU} hors limites", 1);
        int rows = (int)rowsU, cols = (int)colsU;
        byte flag = data[9];
        if (flag > 1) throw new GameFormatException($"Variante de terrain non geree (octet 0x{flag:X2})", 9);

        int pos = 10;
        bool[]? present = null;
        if (flag == 1)
        {
            int count = rows * cols;
            int maskBytes = (count + 7) / 8;
            if (pos + maskBytes > data.Length) throw new GameFormatException("Masque de trous tronque", pos);
            present = new bool[count];
            for (int i = 0; i < count; i++)
                present[i] = (data[pos + i / 8] & (0x80 >> (i % 8))) != 0;
            pos += maskBytes;
        }

        int ringCount = 2 * (rows - 1) + 2 * (cols - 1);
        int innerRows = rows - 2, innerCols = cols - 2;
        long need = pos + 4L * ringCount + 2L * innerRows * innerCols;
        if (need > data.Length) throw new GameFormatException($"Terrain tronque ({need} octets attendus, {data.Length} presents)", pos);

        float[] ring = new float[ringCount];
        for (int i = 0; i < ringCount; i++) ring[i] = BitConverter.ToSingle(data, pos + 4 * i);
        pos += 4 * ringCount;

        var heights = new float[rows * cols];
        for (int r = 0; r < innerRows; r++)
            for (int c = 0; c < innerCols; c++)
            {
                heights[(r + 1) * cols + c + 1] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos + 2 * (r * innerCols + c))) * HeightStep;
            }

        // Pourtour : deux moities de paires (bord -Z, bord +Z) le long de X, puis (bord -X, bord +X) le long de Z. L'element 0 de chaque bord est un coin ;
        // l'element k >= 1 est l'echantillon de la rangee (ou colonne) k du pourtour.
        int alongX = rows - 1, alongZ = cols - 1;
        for (int k = 0; k < alongX; k++)
        {
            float minusZ = ring[2 * k], plusZ = ring[2 * k + 1];
            if (k >= 1) heights[k * cols + 0] = minusZ;
            heights[k * cols + (cols - 1)] = plusZ;
        }
        int half = 2 * alongX;
        for (int k = 0; k < alongZ; k++)
        {
            float minusX = ring[half + 2 * k], plusX = ring[half + 2 * k + 1];
            heights[k] = minusX;
            heights[(rows - 1) * cols + k] = plusX;
        }
        // Les quatre coins : (0,0) = element 0 du bord -X, (0, dernier) = element 0 du bord +Z, (dernier, 0) = element 0 du bord +X ; le dernier coin
        // (+X, +Z) n'est stocke nulle part : extrapole des trois voisins.
        heights[(rows - 1) * cols + (cols - 1)] = heights[(rows - 2) * cols + (cols - 1)] + heights[(rows - 1) * cols + (cols - 2)] - heights[(rows - 2) * cols + (cols - 2)];

        foreach (float h in heights)
            if (!float.IsFinite(h) || MathF.Abs(h) > 100000f) throw new GameFormatException("Hauteur de terrain non finie ou aberrante", 10);

        return new HeightmapTile(version, rows, cols, heights, present);
    }

    /// <summary>Decompresse le blob : zlib (<c>78</c>), gzip (<c>1F 8B</c>) ou zstd (<c>28 B5 2F FD</c>).</summary>
    public static byte[] Decompress(byte[] blob)
    {
        if (blob.Length < 8) throw new GameFormatException("Blob de terrain trop court", 0);
        try
        {
            using var output = new MemoryStream();
            if (blob[0] == 0x78)
            {
                using var z = new ZLibStream(new MemoryStream(blob), CompressionMode.Decompress);
                z.CopyTo(output);
            }
            else if (blob[0] == 0x1F && blob[1] == 0x8B)
            {
                using var z = new GZipStream(new MemoryStream(blob), CompressionMode.Decompress);
                z.CopyTo(output);
            }
            else if (blob[0] == 0x28 && blob[1] == 0xB5 && blob[2] == 0x2F && blob[3] == 0xFD)
            {
                using var z = new DecompressionStream(new MemoryStream(blob));
                z.CopyTo(output);
            }
            else throw new GameFormatException($"Compression de terrain inconnue (octets {blob[0]:X2} {blob[1]:X2})", 0);
            return output.ToArray();
        }
        catch (InvalidDataException ex)
        {
            throw new GameFormatException("Blob de terrain illisible: " + ex.Message, 0);
        }
    }
}
