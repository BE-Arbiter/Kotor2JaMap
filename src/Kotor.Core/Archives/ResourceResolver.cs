namespace Kotor.Core.Archives;

/// <summary>
/// Resout une ressource dans l'ordre du jeu : Override, module.rim, module_s.rim,
/// (Tpc/Txi) TexturePacks\swpc_tex_tpa.erf, puis chitin.key + BIF. Les archives absentes sont ignorees.
/// </summary>
public sealed class ResourceResolver : IDisposable
{
    private readonly string _overrideDir;
    private readonly List<IResourceArchive> _archives = new();
    private IResourceArchive? _texturePack;
    private IResourceArchive? _key;

    private static readonly Dictionary<ResourceType, string> Ext = new()
    {
        [ResourceType.Tga] = "tga", [ResourceType.TwoDa] = "2da", [ResourceType.Mdl] = "mdl",
        [ResourceType.Are] = "are", [ResourceType.Ifo] = "ifo", [ResourceType.Wok] = "wok",
        [ResourceType.Txi] = "txi", [ResourceType.Git] = "git", [ResourceType.Utd] = "utd",
        [ResourceType.Utp] = "utp", [ResourceType.Dwk] = "dwk", [ResourceType.Pwk] = "pwk",
        [ResourceType.Lyt] = "lyt", [ResourceType.Vis] = "vis", [ResourceType.Tpc] = "tpc",
        [ResourceType.Mdx] = "mdx",
    };

    private ResourceResolver(string overrideDir) => _overrideDir = overrideDir;

    public static ResourceResolver ForModule(string kotorDir, string module)
    {
        var r = new ResourceResolver(Path.Combine(kotorDir, "Override"));
        try
        {
            string modules = Path.Combine(kotorDir, "modules");
            foreach (string name in new[] { module + ".rim", module + "_s.rim" })
            {
                string p = Path.Combine(modules, name);
                if (File.Exists(p)) r._archives.Add(RimArchive.Open(p));
            }
            string tp = Path.Combine(kotorDir, "TexturePacks", "swpc_tex_tpa.erf");
            if (File.Exists(tp)) r._texturePack = ErfArchive.Open(tp);
            if (File.Exists(Path.Combine(kotorDir, "chitin.key"))) r._key = KeyBifArchive.Open(kotorDir);
            return r;
        }
        catch { r.Dispose(); throw; }
    }

    private static string FileName(string resRef, ResourceType type) =>
        Ext.TryGetValue(type, out var e) ? resRef + "." + e : resRef + "." + (ushort)type;

    private IEnumerable<IResourceArchive> Chain(ResourceType type)
    {
        foreach (var a in _archives) yield return a;
        if (type is ResourceType.Tpc or ResourceType.Txi && _texturePack is not null) yield return _texturePack;
        if (_key is not null) yield return _key;
    }

    public byte[]? Find(string resRef, ResourceType type) => Resolve(resRef, type, out _);

    public string? FindSource(string resRef, ResourceType type)
    {
        Resolve(resRef, type, out string? src);
        return src;
    }

    private byte[]? Resolve(string resRef, ResourceType type, out string? source)
    {
        var key = new ResourceKey(resRef, type);
        string name = FileName(key.ResRef, type);
        string file = Path.Combine(_overrideDir, name);
        if (File.Exists(file))
        {
            source = Path.Combine("Override", name);
            return File.ReadAllBytes(file);
        }
        foreach (var a in Chain(type))
        {
            if (a.TryRead(key, out var data))
            {
                source = a is KeyBifArchive kb ? "chitin.key:" + kb.BifNameOf(key) : a.SourceName;
                return data;
            }
        }
        source = null;
        return null;
    }

    public void Dispose()
    {
        foreach (var a in _archives) a.Dispose();
        _texturePack?.Dispose();
        _key?.Dispose();
    }
}
