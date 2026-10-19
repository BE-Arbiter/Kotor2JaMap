namespace Swtor.Core.Myp;

/// <summary>
/// Les archives .tor d'une installation de SWTOR, indexees par hash de chemin. Sert a lire ou extraire un fichier dont on connait le
/// chemin (aucune liste de noms necessaire) et, avec une <see cref="MypNameList"/>, a extraire des dossiers entiers.
/// L'index est construit a la premiere utilisation (quelques secondes pour ~100 archives) ; en cas de doublon, la derniere archive
/// (ordre de la liste) l'emporte. Utilisable depuis plusieurs fils.
/// </summary>
public sealed class SwtorGameSource : IDisposable
{
    private readonly IReadOnlyList<string> _paths;
    private readonly object _gate = new();
    private List<MypArchive>? _archives;
    private Dictionary<ulong, (int Archive, MypEntry Entry)>? _index;
    private int _rawEntries;
    private readonly List<string> _openErrors = [];

    public SwtorGameSource(IReadOnlyList<string> archives)
    {
        ArgumentNullException.ThrowIfNull(archives);
        _paths = archives;
    }

    /// <summary>Source des archives trouvees sous <paramref name="gameDir"/> ; null s'il n'y en a aucune.</summary>
    public static SwtorGameSource? Create(string gameDir, bool pts = false)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir)) return null;
        IReadOnlyList<string> archives = SwtorGameLocator.FindArchives(gameDir, pts);
        return archives.Count == 0 ? null : new SwtorGameSource(archives);
    }

    public int ArchiveCount => _paths.Count;

    /// <summary>Empreinte des archives (noms, tailles, dates) : change quand le jeu est mis a jour.</summary>
    public string Fingerprint()
    {
        var sb = new System.Text.StringBuilder();
        foreach (string p in _paths)
        {
            var fi = new FileInfo(p);
            sb.Append(fi.Name).Append('|').Append(fi.Exists ? fi.Length : -1).Append('|').Append(fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0).Append('\n');
        }
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    /// <summary>Nombre d'entrees lues (doublons compris) et erreurs d'ouverture ; construit l'index si besoin.</summary>
    public (int RawEntries, IReadOnlyList<string> Errors) Stats()
    {
        EnsureIndex();
        return (_rawEntries, _openErrors);
    }

    private Dictionary<ulong, (int Archive, MypEntry Entry)> EnsureIndex()
    {
        lock (_gate)
        {
            if (_index is not null) return _index;
            var archives = new List<MypArchive>(_paths.Count);
            var index = new Dictionary<ulong, (int, MypEntry)>(1 << 20);
            int raw = 0;
            foreach (string path in _paths)
            {
                MypArchive a;
                try { a = MypArchive.Open(path); }
                catch (Exception e) when (e is GameFormatException or IOException or UnauthorizedAccessException)
                {
                    _openErrors.Add($"{Path.GetFileName(path)}: {e.Message}");
                    continue;
                }
                archives.Add(a);
                int i = archives.Count - 1;
                foreach (MypEntry e in a.Entries) { raw++; index[e.Hash] = (i, e); }
            }
            _rawEntries = raw;
            _archives = archives;
            return _index = index;
        }
    }

    /// <summary>Chemin de ressource tel que le jeu le hashe : « /resources/ » devant, minuscules, separateurs « / ».</summary>
    public static string HashPath(string resourcePath) => "/resources/" + SwtorAssets.Normalize(resourcePath).ToLowerInvariant();

    public bool Contains(string resourcePath) => EnsureIndex().ContainsKey(MypHash.Compute(HashPath(resourcePath)));

    /// <summary>Octets decompresses du fichier, ou null s'il n'est dans aucune archive (ou illisible).</summary>
    public byte[]? TryRead(string resourcePath) => TryRead(MypHash.Compute(HashPath(resourcePath)));

    private byte[]? TryRead(ulong hash)
    {
        var index = EnsureIndex();
        if (!index.TryGetValue(hash, out var hit)) return null;
        MypArchive a = _archives![hit.Archive];
        try
        {
            lock (a) return a.Read(hit.Entry); // MypArchive n'est pas thread-safe
        }
        catch (Exception e) when (e is GameFormatException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Ecrit le fichier sous <paramref name="root"/> (chemin en minuscules, comme dans le jeu). Vrai s'il existe deja ou vient d'etre ecrit.
    /// </summary>
    public bool Materialize(string resourcePath, string root)
    {
        string? rel = SwtorExtractor.RelativePath(HashPath(resourcePath));
        if (rel is null) return false;
        string dest = Path.Combine(Path.GetFullPath(root), rel.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(dest)) return true;
        byte[]? data = TryRead(resourcePath);
        if (data is null) return false;
        WriteAtomic(dest, data, replace: false);
        return true;
    }

    /// <summary>
    /// Ecrit via un fichier temporaire unique puis deplacement. Avec <paramref name="replace"/> faux, un fichier deja present est garde :
    /// deux fils qui materialisent le meme fichier (ou un lecteur qui l ouvre) ne se genent pas (Windows refuse de remplacer un fichier ouvert).
    /// </summary>
    internal static void WriteAtomic(string dest, byte[] data, bool replace = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        string tmp = dest + "." + Guid.NewGuid().ToString("N")[..8] + ".part";
        File.WriteAllBytes(tmp, data);
        try
        {
            if (!replace && File.Exists(dest)) { File.Delete(tmp); return; }
            File.Move(tmp, dest, overwrite: replace);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Un autre fil a pose le fichier entre-temps : c est bon.
            try { File.Delete(tmp); } catch (Exception d) when (d is IOException or UnauthorizedAccessException) { }
            if (!File.Exists(dest)) throw;
        }
    }

    /// <summary>
    /// Extrait les fichiers nommes de <paramref name="names"/> dont le chemin commence par un des <paramref name="prefixes"/> (vide = tout)
    /// et que <paramref name="accept"/> (si fourni) retient, vers <paramref name="outRoot"/>.
    /// </summary>
    public SwtorExtractResult Extract(MypNameList names, string outRoot, IEnumerable<string>? prefixes = null, bool overwrite = false,
        IProgress<string>? progress = null, CancellationToken ct = default, Func<string, bool>? accept = null)
    {
        ArgumentNullException.ThrowIfNull(names);
        string[] filters = prefixes?.Select(SwtorExtractor.NormalizePrefix).Where(p => p.Length > 0).ToArray() ?? [];
        string root = Path.GetFullPath(outRoot);
        Directory.CreateDirectory(root);
        if (_index is null) progress?.Report($"Indexing {_paths.Count} game archive(s)...");
        var index = EnsureIndex();
        progress?.Report($"{index.Count:N0} entries indexed; selecting files by name...");

        var chosen = new List<(string Rel, int Archive, MypEntry Entry)>();
        int unnamed = 0;
        foreach (var (hash, hit) in index)
        {
            if (names.NameOf(hash) is not { } full) { unnamed++; continue; }
            string? rel = SwtorExtractor.RelativePath(full);
            if (rel is null) continue;
            if (filters.Length > 0 && !filters.Any(f => rel.StartsWith(f, StringComparison.OrdinalIgnoreCase))) continue;
            if (accept is not null && !accept(rel)) continue;
            chosen.Add((rel, hit.Archive, hit.Entry));
        }
        progress?.Report($"{chosen.Count} file(s) to extract.");

        int written = 0, skipped = 0, failed = 0, done = 0;
        long bytes = 0;
        var errors = new List<string>(_openErrors);
        var errLock = new object();
        Parallel.ForEach(chosen.GroupBy(c => c.Archive),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2)), CancellationToken = ct }, group =>
        {
            MypArchive a = _archives![group.Key];
            foreach (var (rel, _, e) in group)
            {
                ct.ThrowIfCancellationRequested();
                string dest = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    // Un nom venant d'une liste telechargee ne doit jamais sortir du dossier de sortie.
                    if (!Path.GetFullPath(dest).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Path outside the output folder: {rel}");
                    if (!overwrite && File.Exists(dest) && new FileInfo(dest).Length == e.UncompressedSize) { Interlocked.Increment(ref skipped); continue; }
                    byte[] data;
                    lock (a) data = a.Read(e);
                    WriteAtomic(dest, data);
                    Interlocked.Increment(ref written);
                    Interlocked.Add(ref bytes, data.Length);
                }
                catch (Exception ex) when (ex is GameFormatException or IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    Interlocked.Increment(ref failed);
                    lock (errLock) { if (errors.Count < 20) errors.Add($"{rel}: {ex.Message}"); }
                }
                int n = Interlocked.Increment(ref done);
                if (n % 500 == 0 || n == chosen.Count) progress?.Report($"{n} / {chosen.Count} file(s)");
            }
        });
        return new SwtorExtractResult(_archives!.Count, _rawEntries, unnamed, chosen.Count, written, skipped, failed, bytes, errors);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_archives is null) return;
            foreach (MypArchive a in _archives) a.Dispose();
            _archives = null;
            _index = null;
        }
    }
}
