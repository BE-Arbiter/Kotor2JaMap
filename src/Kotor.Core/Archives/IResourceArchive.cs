namespace Kotor.Core.Archives;

public interface IResourceArchive : IDisposable
{
    /// <summary>Nom de la source, pour le journal (ex. "modules\\tar_m02aa.rim").</summary>
    string SourceName { get; }
    IEnumerable<ResourceKey> Keys { get; }
    bool TryRead(ResourceKey key, out byte[] data);
}

internal static class ArchiveIo
{
    /// <summary>Lit un resref de 16 octets (ASCII, termine par NUL).</summary>
    public static string ReadResRef(ReadOnlySpan<byte> b)
    {
        int n = b.IndexOf((byte)0);
        if (n < 0) n = b.Length;
        return System.Text.Encoding.ASCII.GetString(b[..n]);
    }

    public static byte[] ReadAt(FileStream fs, long offset, int size)
    {
        var buf = new byte[size];
        fs.Seek(offset, SeekOrigin.Begin);
        fs.ReadExactly(buf, 0, size);
        return buf;
    }

    public static void CheckSig(ReadOnlySpan<byte> header, string path, params string[] accepted)
    {
        string sig = System.Text.Encoding.ASCII.GetString(header[..8]);
        if (Array.IndexOf(accepted, sig) < 0)
            throw new InvalidDataException($"Signature inattendue '{sig}' dans {path}");
    }
}
