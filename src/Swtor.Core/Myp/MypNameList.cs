using System.Net.Http.Headers;

namespace Swtor.Core.Myp;

/// <summary>
/// Liste des noms de fichiers SWTOR (hash -> chemin). Les archives .tor ne gardent que le hash du chemin : sans cette liste on ne
/// peut pas nommer les fichiers. Source : la liste de Jedipedia (la meme que celle d'extracTOR), un chemin par ligne
/// (<c>/resources/art/...</c>). Le hash de chaque ligne est calcule avec <see cref="MypHash"/>.
/// </summary>
public sealed class MypNameList
{
    public const string JedipediaUrl = "https://swtor.jedipedia.net/ajax/getFileNames.php?env=all&format=txt";

    private readonly Dictionary<ulong, string> _names;

    private MypNameList(Dictionary<ulong, string> names) { _names = names; }

    public int Count => _names.Count;

    /// <summary>Chemin (« /resources/... ») du fichier de ce hash, ou null s'il est inconnu.</summary>
    public string? NameOf(ulong hash) => _names.TryGetValue(hash, out string? n) ? n : null;

    /// <param name="keep">Filtre sur le chemin (« /resources/... »), pour ne garder en memoire qu une partie de la liste (1,5 million de lignes en entier).</param>
    public static MypNameList Parse(TextReader reader, Func<string, bool>? keep = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var names = new Dictionary<ulong, string>(1 << 20);
        while (reader.ReadLine() is { } raw)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] != '/') continue; // commentaires, lignes parasites
            if (keep is not null && !keep(line)) continue;
            names.TryAdd(MypHash.Compute(line), line);
        }
        return new MypNameList(names);
    }

    public static MypNameList Load(string path, Func<string, bool>? keep = null)
    {
        using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
        return Parse(reader, keep);
    }

    /// <summary>Resultat de <see cref="RefreshAsync"/>.</summary>
    public enum RefreshStatus { Downloaded, UpToDate, KeptCacheOffline }

    /// <summary>
    /// Met a jour le fichier de cache depuis <paramref name="url"/>. Telecharge si le cache manque, si <paramref name="force"/>, ou si la date
    /// <c>Last-Modified</c> du serveur est plus recente que celle du cache. Hors ligne, un cache existant est conserve ; sans cache,
    /// l'erreur reseau est propagee. Le fichier est ecrit dans un temporaire puis deplace : un telechargement interrompu ne casse rien.
    /// </summary>
    public static async Task<RefreshStatus> RefreshAsync(string cachePath, string url = JedipediaUrl, bool force = false,
        HttpClient? client = null, CancellationToken ct = default)
    {
        bool own = client is null;
        client ??= new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        try
        {
            if (!client.DefaultRequestHeaders.UserAgent.Any()) client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Kotor2Ja", "1.0"));
            bool exists = File.Exists(cachePath) && new FileInfo(cachePath).Length > 0;
            try
            {
                if (exists && !force)
                {
                    using var head = new HttpRequestMessage(HttpMethod.Head, url);
                    using HttpResponseMessage hr = await client.SendAsync(head, ct).ConfigureAwait(false);
                    hr.EnsureSuccessStatusCode();
                    DateTimeOffset? remote = hr.Content.Headers.LastModified;
                    // Sans date, on ne sait pas : on garde le cache (l'utilisateur peut forcer).
                    if (remote is null || remote.Value.UtcDateTime <= File.GetLastWriteTimeUtc(cachePath)) return RefreshStatus.UpToDate;
                }
                string? dir = Path.GetDirectoryName(Path.GetFullPath(cachePath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = cachePath + ".part";
                DateTimeOffset? stamp;
                using (HttpResponseMessage resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    stamp = resp.Content.Headers.LastModified;
                    await using Stream src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await using var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
                    await src.CopyToAsync(dst, ct).ConfigureAwait(false);
                }
                if (new FileInfo(tmp).Length == 0) { File.Delete(tmp); throw new InvalidDataException("Empty file name list received."); }
                File.Move(tmp, cachePath, overwrite: true);
                if (stamp is not null) File.SetLastWriteTimeUtc(cachePath, stamp.Value.UtcDateTime);
                return RefreshStatus.Downloaded;
            }
            catch (Exception e) when (exists && e is HttpRequestException or TaskCanceledException or IOException && !ct.IsCancellationRequested)
            {
                return RefreshStatus.KeptCacheOffline;
            }
        }
        finally
        {
            if (own) client.Dispose();
        }
    }
}
