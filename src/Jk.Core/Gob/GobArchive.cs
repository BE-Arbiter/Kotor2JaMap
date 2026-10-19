using System.Text;

namespace Jk.Core.Gob;

/// <summary>Entree d'un GOB : chemin (avec <c>\</c> tel quel), position absolue et taille des donnees.</summary>
public sealed record GobEntry(string Path, long Offset, int Size);

/// <summary>
/// Archive GOB de Jedi Knight : en-tete "GOB " + version 0x14 + offset de l'index ; l'index contient un u32 (nombre d'entrees)
/// puis, par entree, u32 offset, u32 taille, char[128] nom. Lecture par flux : seul l'index est charge en memoire.
/// </summary>
public sealed class GobArchive : IDisposable
{
    private const int NameLength = 128;
    private const int EntryLength = 8 + NameLength;
    private readonly FileStream _stream;
    private readonly object _lock = new();

    public IReadOnlyList<GobEntry> Entries { get; }

    private GobArchive(FileStream stream, List<GobEntry> entries)
    {
        _stream = stream;
        Entries = entries;
    }

    public static GobArchive Open(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.RandomAccess);
        try
        {
            return new GobArchive(fs, ReadIndex(fs, path));
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    private static List<GobEntry> ReadIndex(FileStream fs, string path)
    {
        var header = new byte[12];
        if (fs.Length < header.Length || fs.Read(header, 0, header.Length) != header.Length
            || header[0] != 'G' || header[1] != 'O' || header[2] != 'B' || header[3] != ' ')
            throw new InvalidDataException($"Not a GOB archive: {path}");
        int version = BitConverter.ToInt32(header, 4);
        if (version != 0x14) throw new InvalidDataException($"Unsupported GOB version 0x{version:X} in {path}");
        long indexOffset = BitConverter.ToUInt32(header, 8);
        if (indexOffset + 4 > fs.Length) throw new InvalidDataException($"GOB index offset out of range in {path}");

        fs.Position = indexOffset;
        var cnt = new byte[4];
        fs.ReadExactly(cnt);
        uint count = BitConverter.ToUInt32(cnt, 0);
        if (indexOffset + 4 + (long)count * EntryLength > fs.Length) throw new InvalidDataException($"GOB index truncated in {path}");

        var list = new List<GobEntry>((int)count);
        var buf = new byte[EntryLength];
        for (uint i = 0; i < count; i++)
        {
            fs.ReadExactly(buf);
            uint off = BitConverter.ToUInt32(buf, 0);
            uint size = BitConverter.ToUInt32(buf, 4);
            int len = Array.IndexOf(buf, (byte)0, 8, NameLength);
            len = len < 0 ? NameLength : len - 8;
            string name = Encoding.Latin1.GetString(buf, 8, len);
            if (size > int.MaxValue || off + (long)size > fs.Length) throw new InvalidDataException($"GOB entry '{name}' out of range in {path}");
            list.Add(new GobEntry(name, off, (int)size));
        }
        return list;
    }

    /// <summary>Lit les octets d'une entree (acces au flux protege par un verrou).</summary>
    public byte[] Read(GobEntry e)
    {
        var data = new byte[e.Size];
        lock (_lock)
        {
            _stream.Position = e.Offset;
            _stream.ReadExactly(data);
        }
        return data;
    }

    public void Dispose() => _stream.Dispose();
}
