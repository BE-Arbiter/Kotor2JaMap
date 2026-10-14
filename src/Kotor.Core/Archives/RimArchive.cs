using System.Buffers.Binary;

namespace Kotor.Core.Archives;

/// <summary>Archive RIM V1.0 (lue a la demande, sans charger le fichier entier).</summary>
public sealed class RimArchive : IResourceArchive
{
    private readonly FileStream _fs;
    private readonly Dictionary<ResourceKey, (uint off, uint size)> _entries = new();

    public string SourceName { get; }
    public IEnumerable<ResourceKey> Keys => _entries.Keys;

    private RimArchive(string path, FileStream fs)
    {
        SourceName = path;
        _fs = fs;
    }

    public static RimArchive Open(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var rim = new RimArchive(path, fs);
            rim.Load(path);
            return rim;
        }
        catch { fs.Dispose(); throw; }
    }

    private void Load(string path)
    {
        var h = ArchiveIo.ReadAt(_fs, 0, 0x14);
        ArchiveIo.CheckSig(h, path, "RIM V1.0");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(0x0C));
        uint keysOff = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(0x10));
        var keys = ArchiveIo.ReadAt(_fs, keysOff, checked((int)count * 32));
        for (int i = 0; i < count; i++)
        {
            var k = keys.AsSpan(i * 32, 32);
            string rr = ArchiveIo.ReadResRef(k[..16]);
            var type = (ResourceType)(ushort)BinaryPrimitives.ReadUInt32LittleEndian(k[16..]);
            uint off = BinaryPrimitives.ReadUInt32LittleEndian(k[24..]);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(k[28..]);
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
