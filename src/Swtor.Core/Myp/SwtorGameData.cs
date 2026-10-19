namespace Swtor.Core.Myp;

/// <summary>
/// Mode « depuis le jeu » : lit les archives .tor de l'installation et materialise ce dont on a besoin dans un dossier de cache
/// (meme disposition que le dossier extrait, donc lu par le reste du code sans changement).
/// <list type="bullet">
/// <item><see cref="EnsureBase"/> : base de donnees du jeu (GOM), chaines de texte et <c>area.dat</c> de chaque zone (catalogue).</item>
/// <item><see cref="EnsureArea"/> : les fichiers d'une zone (rooms), hors cartes d'interface.</item>
/// <item>Le reste (modeles, materiaux, textures) est extrait a la demande par <see cref="SwtorAssets"/> (<see cref="SwtorAssets.Source"/>).</item>
/// </list>
/// Les noms viennent de la liste de Jedipedia, telechargee au premier besoin puis gardee en cache ; une mise a jour du jeu (empreinte des
/// archives) refait l'extraction de la base et des zones.
/// </summary>
public sealed class SwtorGameData : IDisposable
{
    public const string AreasPrefix = "/resources/world/areas/";
    public const string StampFileName = "swtor_game.stamp";

    private readonly object _gate = new();
    private readonly string _namesCache;
    private readonly bool _offline;
    private readonly Func<string, bool> _keepNames;
    private MypNameList? _names;
    private bool _baseDone;
    private readonly HashSet<string> _areasDone = new(StringComparer.OrdinalIgnoreCase);
    private string? _fingerprint;
    private string? _stored;

    /// <param name="source">Archives du jeu.</param>
    /// <param name="cacheRoot">Dossier des ressources extraites (racine « resources »).</param>
    /// <param name="namesCachePath">Fichier de cache de la liste de noms.</param>
    /// <param name="offline">Vrai : ne jamais contacter Jedipedia (cache seulement).</param>
    public SwtorGameData(SwtorGameSource source, string cacheRoot, string namesCachePath, bool offline = false)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        CacheRoot = cacheRoot;
        _namesCache = namesCachePath;
        _offline = offline;
        Assets = new SwtorAssets(cacheRoot, source);
        _keepNames = IsNeeded;
    }

    /// <summary>Client HTTP de test (null : un client par defaut).</summary>
    public HttpClient? Http { get; init; }

    public SwtorGameSource Source { get; }
    public string CacheRoot { get; }
    public SwtorAssets Assets { get; }

    private static bool IsNeeded(string path) =>
        path.StartsWith(AreasPrefix, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/resources/systemgenerated/", StringComparison.OrdinalIgnoreCase)
        || IsStrings(path);

    private static bool IsStrings(string path)
    {
        // /resources/<langue>/str/... et /resources/gamedata/str/...
        string[] p = path.Split('/', 5); // "", "resources", dossier, "str", reste
        return p.Length == 5 && p[3].Equals("str", StringComparison.OrdinalIgnoreCase) && p[4].Length > 0
            && (p[2].Contains('-') || p[2].Equals("gamedata", StringComparison.OrdinalIgnoreCase));
    }

    private string Fingerprint => _fingerprint ??= Source.Fingerprint();

    private string StampPath => Path.Combine(CacheRoot, StampFileName);

    // Empreinte enregistree a la derniere extraction complete de la base ; differente de l'actuelle = jeu mis a jour.
    private bool IsStale
    {
        get
        {
            _stored ??= File.Exists(StampPath) ? File.ReadAllText(StampPath).Trim() : "";
            return _stored != Fingerprint;
        }
    }

    private MypNameList Names(IProgress<string>? progress, CancellationToken ct)
    {
        if (_names is not null) return _names;
        if (!_offline)
        {
            progress?.Report("Checking the SWTOR file name list (Jedipedia)...");
            try
            {
                var status = MypNameList.RefreshAsync(_namesCache, MypNameList.JedipediaUrl, false, Http, ct).GetAwaiter().GetResult();
                progress?.Report(status switch
                {
                    MypNameList.RefreshStatus.Downloaded => "File name list downloaded.",
                    MypNameList.RefreshStatus.KeptCacheOffline => "Jedipedia unreachable: using the cached file name list.",
                    _ => "File name list up to date.",
                });
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
            {
                throw new IOException($"Cannot download the SWTOR file name list ({e.Message}). Check the connection, or use \"From extracted files\".", e);
            }
        }
        if (!File.Exists(_namesCache)) throw new FileNotFoundException("No SWTOR file name list available.", _namesCache);
        progress?.Report("Reading the file name list...");
        return _names = MypNameList.Load(_namesCache, _keepNames);
    }

    /// <summary>Prepare la base (une fois par session, et seulement si le jeu a change depuis la derniere fois). Thread-safe.</summary>
    public void EnsureBase(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_baseDone) return;
            bool stale = IsStale;
            if (stale || !Directory.Exists(Path.Combine(CacheRoot, "systemgenerated")))
            {
                progress?.Report("Preparing the SWTOR data from the game archives (first time only)...");
                MypNameList names = Names(progress, ct);
                SwtorExtractResult r = Source.Extract(names, CacheRoot,
                    ["systemgenerated/", "gamedata/str/", "fr-fr/str/", "en-us/str/", "de-de/str/"], overwrite: stale, progress, ct);
                SwtorExtractResult a = Source.Extract(names, CacheRoot, ["world/areas/"], overwrite: stale, progress, ct,
                    accept: rel => rel.EndsWith("/area.dat", StringComparison.OrdinalIgnoreCase));
                if (r.Failed + a.Failed > 0)
                    throw new IOException($"Cannot extract the SWTOR base data ({r.Failed + a.Failed} file(s) failed): {string.Join("; ", r.Errors.Concat(a.Errors).Take(3))}");
                if (r.Selected + a.Selected == 0)
                    throw new InvalidDataException("No SWTOR base data found in the game archives (file name list out of date?).");
                progress?.Report($"SWTOR base ready: {r.Selected + a.Selected} file(s).");
                Directory.CreateDirectory(CacheRoot);
                File.WriteAllText(StampPath, Fingerprint);
                _stored = Fingerprint;
                _areasDone.Clear();
            }
            _baseDone = true;
        }
    }

    /// <summary>Extrait les fichiers de la zone <paramref name="areaId"/> (rooms, hors images de cartes). Une fois par zone et par version du jeu.</summary>
    public void EnsureArea(string areaId, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (areaId.Length == 0 || areaId.IndexOfAny(['/', '\\']) >= 0 || areaId.Contains("..")) return;
        lock (_gate)
        {
            EnsureBase(progress, ct);
            if (_areasDone.Contains(areaId)) return;
            string marker = Path.Combine(CacheRoot, "world", "areas", areaId, ".extracted");
            if (File.Exists(marker) && File.ReadAllText(marker).Trim() == Fingerprint) { _areasDone.Add(areaId); return; }
            progress?.Report($"Extracting area {areaId} from the game archives...");
            SwtorExtractResult r = Source.Extract(Names(progress, ct), CacheRoot, ["world/areas/" + areaId + "/"], overwrite: false, progress, ct,
                accept: rel => !rel.EndsWith(".dds", StringComparison.OrdinalIgnoreCase));
            if (r.Failed > 0)
                throw new IOException($"Cannot extract area {areaId} ({r.Failed} file(s) failed): {string.Join("; ", r.Errors.Take(3))}");
            progress?.Report($"Area {areaId}: {r.Selected} file(s) ({r.Written} written).");
            if (r.Selected > 0)
            {
                File.WriteAllText(marker, Fingerprint);
                _areasDone.Add(areaId);
            }
        }
    }

    public void Dispose() => Source.Dispose();
}
