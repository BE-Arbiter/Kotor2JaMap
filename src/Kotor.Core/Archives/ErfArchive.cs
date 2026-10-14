using System.Buffers.Binary;

namespace Kotor.Core.Archives;

/// <summary>Archive ERF/MOD V1.0.</summary>
public sealed class ErfArchive : IResourceArchive
{
    private readonly FileStream _fs;
    private readonly Dictionary<ResourceKey, (uint off, uint size)> _entries = new();

    public string SourceName { get; }
    public IEnumerable<ResourceKey> Keys => _entries.Keys;

    private ErfArchive(string path, FileStream fs)
    {
        SourceName = path;
        _fs = fs;
    }

    public static ErfArchive Open(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var erf = new ErfArchive(path, fs);
            erf.Load(path);
            return erf;
        }
        catch { fs.Dispose(); throw; }
    }

    private void Load(string path)
    {
        var h = ArchiveIo.ReadAt(_fs, 0, 32);
        ArchiveIo.CheckSig(h, path, "ERF V1.0", "MOD V1.0");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(16));
        uint keysOff = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(24));
        uint resOff = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(28));
        var keys = ArchiveIo.ReadAt(_fs, keysOff, checked((int)count * 24));
        var res = ArchiveIo.ReadAt(_fs, resOff, checked((int)count * 8));
        for (int i = 0; i < count; i++)
        {
            var k = keys.AsSpan(i * 24, 24);
            string rr = ArchiveIo.ReadResRef(k[..16]);
            var type = (ResourceType)BinaryPrimitives.ReadUInt16LittleEndian(k[20..]);
            uint off = BinaryPrimitives.ReadUInt32LittleEndian(res.AsSpan(i * 8));
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(res.AsSpan(i * 8 + 4));
            _entries.TryAdd(new ResourceKey(rr, type), (off, size));
        }
    }

    public bool TryRead(ResourceKey key, out byte[] data)
    {
        if (_entries.TryGetValue(key, out var e))
        {
            data = ArchiveIo.ReadAt(_fs, e.off, checked((int)e.size));
            return true;
        }
        data = Array.Empty<byte>();
        return false;
    }

    public void Dispose() => _fs.Dispose();
}
