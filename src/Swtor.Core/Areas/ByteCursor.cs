using System.Text;

namespace Swtor.Core.Areas;

/// <summary>Lecteur little-endian sur un tableau d'octets, avec verification des bornes.</summary>
internal sealed class ByteCursor(byte[] data)
{
    public byte[] Data { get; } = data;
    public int Position { get; private set; }

    public bool AtEnd => Position >= Data.Length;

    public void Seek(long offset)
    {
        if (offset < 0 || offset > Data.Length) throw new GameFormatException("Offset hors du fichier", offset);
        Position = (int)offset;
    }

    private void Need(int n)
    {
        if (n < 0 || Position + n > Data.Length) throw new GameFormatException($"Fin de fichier ({n} octets attendus)", Position);
    }

    public byte U8() { Need(1); return Data[Position++]; }
    public ushort U16() { Need(2); ushort v = BitConverter.ToUInt16(Data, Position); Position += 2; return v; }
    public uint U32() { Need(4); uint v = BitConverter.ToUInt32(Data, Position); Position += 4; return v; }
    public int I32() { Need(4); int v = BitConverter.ToInt32(Data, Position); Position += 4; return v; }
    public ulong U64() { Need(8); ulong v = BitConverter.ToUInt64(Data, Position); Position += 8; return v; }
    public float F32() { Need(4); float v = BitConverter.ToSingle(Data, Position); Position += 4; return v; }

    /// <summary>Compteur 32 bits borne par la taille du fichier (evite les allocations absurdes sur un fichier corrompu).</summary>
    public int Count(int max)
    {
        int n = I32();
        if (n < 0 || n > max) throw new GameFormatException($"Compteur invalide {n}", Position - 4);
        return n;
    }

    public void Skip(int n) { Need(n); Position += n; }

    /// <summary>Chaine ASCII/UTF-8 precedee de sa longueur en octets (terminateur nul compris, retire).</summary>
    public string LengthPrefixedAscii()
    {
        int len = I32();
        if (len < 0) throw new GameFormatException("Longueur de chaine negative", Position - 4);
        Need(len);
        int n = len;
        if (n > 0 && Data[Position + n - 1] == 0) n--;
        string s = Encoding.UTF8.GetString(Data, Position, n);
        Position += len;
        return s;
    }

    /// <summary>Chaine UTF-16LE precedee de sa longueur en octets (terminateur nul retire).</summary>
    public string LengthPrefixedUtf16()
    {
        int len = I32();
        if (len < 0 || (len & 1) != 0) throw new GameFormatException("Longueur de chaine UTF-16 invalide", Position - 4);
        Need(len);
        string s = Encoding.Unicode.GetString(Data, Position, len).TrimEnd('\0');
        Position += len;
        return s;
    }
}
