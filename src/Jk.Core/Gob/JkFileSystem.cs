namespace Jk.Core.Gob;

/// <summary>
/// Systeme de fichiers virtuel de JK : GOB de l'episode, puis <c>Resource\Res2.gob</c>, puis <c>Resource\Res1hi.gob</c>.
/// Les chemins sont insensibles a la casse (separateur <c>\</c>) ; la premiere archive qui contient le chemin gagne.
/// </summary>
public sealed class JkFileSystem : IDisposable
{
    private readonly List<(GobArchive Gob, Dictionary<string, GobEntry> Index)> _layers = new();

    private JkFileSystem() { }

    /// <param name="jkDir">Dossier d'installation de JK.</param>
    /// <param name="episodeGob">Nom du GOB d'episode dans <c>Episode\</c> (ex. <c>JK1.GOB</c>).</param>
    public static JkFileSystem ForEpisode(string jkDir, string episodeGob)
    {
        var fs = new JkFileSystem();
        try
        {
            string episode = FindCaseInsensitive(Path.Combine(jkDir, "Episode"), episodeGob);
            if (episode.Length == 0)
                throw new FileNotFoundException($"Episode GOB not found: {Path.Combine(jkDir, "Episode", episodeGob)}");
            fs.AddLayer(episode);
            foreach (string res in new[] { "Res2.gob", "Res1hi.gob" })
            {
                string p = FindCaseInsensitive(Path.Combine(jkDir, "Resource"), res);
                if (p.Length > 0) fs.AddLayer(p);
            }
            return fs;
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    private static string FindCaseInsensitive(string dir, string name)
    {
        string direct = Path.Combine(dir, name);
        if (File.Exists(direct)) return direct;
        if (!Directory.Exists(dir)) return "";
        foreach (string f in Directory.EnumerateFiles(dir))
            if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) return f;
        return "";
    }

    private void AddLayer(string path)
    {
        var gob = GobArchive.Open(path);
        var index = new Dictionary<string, GobEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (GobEntry e in gob.Entries) index.TryAdd(Normalize(e.Path), e);
        _layers.Add((gob, index));
    }

    private static string Normalize(string p) => p.Replace('/', '\\');

    public byte[]? TryRead(string path)
    {
        string key = Normalize(path);
        foreach (var (gob, index) in _layers)
            if (index.TryGetValue(key, out GobEntry? e)) return gob.Read(e);
        return null;
    }

    public bool Exists(string path)
    {
        string key = Normalize(path);
        foreach (var (_, index) in _layers)
            if (index.ContainsKey(key)) return true;
        return false;
    }

    /// <summary>Chemins (sans doublon, casse de la premiere archive) du dossier <paramref name="folder"/> ayant l'extension donnee (ex. <c>.mat</c>).</summary>
    public IEnumerable<string> List(string folder, string extension)
    {
        string prefix = Normalize(folder).TrimEnd('\\') + "\\";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, index) in _layers)
            foreach (GobEntry e in index.Values)
            {
                string p = Normalize(e.Path);
                if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && p.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && seen.Add(p))
                    yield return p;
            }
    }

    public void Dispose()
    {
        foreach (var (gob, _) in _layers) gob.Dispose();
        _layers.Clear();
    }
}
