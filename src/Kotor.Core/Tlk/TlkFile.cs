using System.Buffers.Binary;
using System.Text;

namespace Kotor.Core.Tlk;

/// <summary>Table de dialogue TLK V3.0 (texte lu a la demande, en Windows-1252).</summary>
public sealed class TlkFile
{
    private static readonly Encoding Win1252 = Register();

    private readonly byte[] _data;
    private readonly uint _count;
    private readonly uint _entriesOff;

    private static Encoding Register()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    private TlkFile(byte[] data)
    {
        _data = data;
        if (data.Length < 20 || Encoding.ASCII.GetString(data, 0, 8) != "TLK V3.0")
            throw new InvalidDataException("TLK V3.0 attendu.");
        _count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        _entriesOff = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16));
    }

    public static TlkFile Open(string path) => new(File.ReadAllBytes(path));

    /// <summary>Texte de l'entree, ou null si la reference est hors table (ex. 0xFFFFFFFF).</summary>
    public string? Get(uint strRef)
    {
        if (strRef >= _count) return null;
        long e = 20 + (long)strRef * 40;
        if (e + 40 > _data.Length) return null;
        uint off = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan((int)e + 28));
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan((int)e + 32));
        long start = _entriesOff + (long)off;
        if (start + size > _data.Length) return null;
        return Win1252.GetString(_data, (int)start, (int)size);
    }
}
