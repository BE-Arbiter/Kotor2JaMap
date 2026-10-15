using System.Buffers.Binary;
using System.Text;

namespace Kotor.Core.TwoDa;

/// <summary>2DA binaire "2DA V2.b" : colonnes separees par tab, nombre de lignes, etiquettes, offsets u16, bloc de textes.</summary>
public sealed class TwoDaFile
{
    private readonly Dictionary<string, int> _columns = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] _cells;
    private readonly int _colCount;

    public int RowCount { get; }

    private TwoDaFile(string[] columns, int rows, string[] cells)
    {
        for (int i = 0; i < columns.Length; i++) _columns.TryAdd(columns[i], i);
        _colCount = columns.Length;
        RowCount = rows;
        _cells = cells;
    }

    public static TwoDaFile Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < 10 || Encoding.ASCII.GetString(data, 0, 8) != "2DA V2.b" || data[8] != '\n')
            throw new InvalidDataException("2DA V2.b expected.");
        int pos = 9;
        var cols = new List<string>();
        while (true)
        {
            if (pos >= data.Length) throw new InvalidDataException("2DA truncated (columns).");
            if (data[pos] == 0) { pos++; break; }
            int end = Array.IndexOf(data, (byte)'\t', pos);
            if (end < 0) throw new InvalidDataException("2DA: unterminated column name.");
            cols.Add(Encoding.ASCII.GetString(data, pos, end - pos));
            pos = end + 1;
        }
        if (pos + 4 > data.Length) throw new InvalidDataException("2DA truncated (row count).");
        long rows = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos)); pos += 4;
        for (long r = 0; r < rows; r++)
        {
            int end = Array.IndexOf(data, (byte)'\t', pos);
            if (end < 0) throw new InvalidDataException("2DA: unterminated row label.");
            pos = end + 1;
        }
        long cellCount = rows * cols.Count;
        long offBytes = cellCount * 2;
        if (pos + offBytes + 2 > data.Length) throw new InvalidDataException("2DA truncated (offsets).");
        int offStart = pos;
        pos += (int)offBytes + 2;           // saute aussi la taille du bloc
        int dataStart = pos;
        var cells = new string[cellCount];
        for (long i = 0; i < cellCount; i++)
        {
            int o = dataStart + BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offStart + (int)i * 2));
            if (o >= data.Length) throw new InvalidDataException("2DA: cell offset outside the file.");
            int end = Array.IndexOf(data, (byte)0, o);
            if (end < 0) end = data.Length;
            cells[i] = Encoding.ASCII.GetString(data, o, end - o);
        }
        return new TwoDaFile(cols.ToArray(), (int)rows, cells);
    }

    /// <summary>Valeur de la cellule, ou null si ligne ou colonne inconnue.</summary>
    public string? Get(int row, string column)
    {
        if (row < 0 || row >= RowCount || !_columns.TryGetValue(column, out int c)) return null;
        return _cells[row * _colCount + c];
    }
}
