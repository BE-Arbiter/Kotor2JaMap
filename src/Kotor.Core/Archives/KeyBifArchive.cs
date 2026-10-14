using System.Buffers.Binary;

namespace Kotor.Core.Archives;

/// <summary>chitin.key + fichiers BIF (ouverts a la demande, donnees lues sans charger tout le BIF).</summary>
public sealed class KeyBifArchive : IResourceArchive
{
    private readonly string _kotorDir;
    private readonly string[] _bifNames;
    private readonly Dictionary<ResourceKey, uint> _ids = new();
    private readonly FileStream?[] _bifs;
    private readonly (uint off, uint size)[]?[] _tables;

    public string SourceName { get; }
    public IEnumerable<ResourceKey> Keys => _ids.Keys;

    private KeyBifArchive(string kotorDir, string[] bifNames)
    {
        _kotorDir = kotorDir;
        _bifNames = bifNames;
        _bifs = new FileStream?[bifNames.Length];
        _tables = new (uint, uint)[]?[bifNames.Length];
        SourceName = "chitin.key";
    }

    public static KeyBifArchive Open(string kotorDir)
    {
        string path = Path.Combine(kotorDir, "chitin.key");
        byte[] k = File.ReadAllBytes(path);
        ArchiveIo.CheckSig(k, path, "KEY V1  ");
        int bifCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(k.AsSpan(8));
        int keyCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(k.AsSpan(12));
        int fileOff = (int)BinaryPrimitives.ReadUInt32LittleEndian(k.AsSpan(16));
        int keyOff = (int)BinaryPrimitives.ReadUInt32LittleEndian(k.AsSpan(20));

        var names = new string[bifCount];
        for (int i = 0; i < bifCount; i++)
        {
            int e = fileOff + i * 12;
            int nameOff = (int)BinaryPrimitives.ReadUInt32LittleEndian(k.AsSpan(e + 4));
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(k.AsSpan(e + 8));
            string n = System.Text.Encoding.ASCII.GetString(k, nameOff, nameLen).TrimEnd('\0');
            names[i] = n.Replace('\\', Path.DirectorySeparatorChar);
        }

        var arc = new KeyBifArchive(kotorDir, names);
        for (int i = 0; i < keyCount; i++)
        {
            var s = k.AsSpan(keyOff + i * 22, 22);
            string rr = ArchiveIo.ReadResRef(s[..16]);
            var type = (ResourceType)BinaryPrimitives.ReadUInt16LittleEndian(s[16..]);
            uint id = BinaryPrimitives.ReadUInt32LittleEndian(s[18..]);
            arc._ids.TryAdd(new ResourceKey(rr, type), id);
        }
        return arc;
    }

    /// <summary>Nom du BIF contenant la ressource (ex. "data\models.bif"), ou null.</summary>
    public string? BifNameOf(ResourceKey key)
    {
        if (!_ids.TryGetValue(key, out uint id)) return null;
        int bif = (int)(id >> 20);
        return bif < _bifNames.Length ? _bifNames[bif] : null;
    }

    public bool TryRead(ResourceKey key, out byte[] data)
    {
        data = Array.Empty<byte>();
        if (!_ids.TryGetValue(key, out uint id)) return false;
        int bif = (int)(id >> 20);
        int idx = (int)(id & 0xFFFFF);
        if (bif >= _bifNames.Length) return false;

        var fs = _bifs[bif];
        if (fs is null)
        {
            string p = Path.Combine(_kotorDir, _bifNames[bif]);
            if (!File.Exists(p)) return false;
            fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                var h = ArchiveIo.ReadAt(fs, 0, 20);
                ArchiveIo.CheckSig(h, p, "BIFFV1  ");
                int varCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(8));
                uint varOff = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(16));
                var t = ArchiveIo.ReadAt(fs, varOff, varCount * 16);
                var table = new (uint, uint)[varCount];
                for (int i = 0; i < varCount; i++)
                    table[i] = (BinaryPrimitives.ReadUInt32LittleEndian(t.AsSpan(i * 16 + 4)),
                                BinaryPrimitives.ReadUInt32LittleEndian(t.AsSpan(i * 16 + 8)));
                _tables[bif] = table;
            }
            catch { fs.Dispose(); throw; }
            _bifs[bif] = fs;
        }

        var tbl = _tables[bif]!;
        if (idx >= tbl.Length) return false;
        data = ArchiveIo.ReadAt(fs, tbl[idx].off, checked((int)tbl[idx].size));
        return true;
    }

    public void Dispose()
    {
        foreach (var f in _bifs) f?.Dispose();
    }
}
