using System.Globalization;
using System.Text;
using Jk.Core.Cog;
using Jk.Core.Gob;
using Jk.Core.Jkl;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline.Entities;
using NVorbis;

namespace Jk.Pipeline;

/// <summary>Son decode : echantillons flottants entrelaces (-1..1), <see cref="Channels"/> voies, <see cref="SampleRate"/> Hz.</summary>
internal sealed record PcmAudio(int SampleRate, int Channels, float[] Samples);

/// <summary>
/// Musique d'un niveau JK -> musique de fond de JA.
/// <para><b>Dans JK.</b> La musique est de l'audio redbook : le COG du niveau appelle <c>PlaySong(debut, fin, retour)</c> (numeros de pistes du CD) dans son message
/// <c>startup</c> (<c>00_soundtrack.cog</c> ou <c>01_SoundTrack.cog</c> : arguments de l'instance ; <c>06a_startlevel.cog</c> et <c>09_startlevel.cog</c> : valeurs litterales ;
/// <c>levelsong.cog</c> : une seule piste). Le moteur joue <c>debut</c>, <c>debut+1</c>... jusqu'a <c>fin</c>, puis revient a <c>retour</c> et recommence jusqu'a <c>fin</c> :
/// quand <c>retour &lt;= debut &lt;= fin</c> c'est donc le cycle <c>retour..fin</c> lu a partir de <c>debut</c>. Dans l'installation Steam, les pistes sont les fichiers
/// <c>MUSIC\TrackNN.ogg</c> (Vorbis stereo 44 100 Hz, ~320 kbit/s) ; il n'y a que les pistes 12 a 18 et 22 a 32.</para>
/// <para><b>Dans JA (SWGL).</b> La cle <c>music</c> du worldspawn (<c>g_spawn.cpp</c> : <c>CS_MUSIC</c>, lue par <c>CG_StartMusic</c>) donne un chemin ; un fichier existant avec
/// un <c>/</c> dans son nom est joue en boucle, sans <c>.mus</c> ni <c>dynamicmusic</c> (<c>snd_dma.cpp:S_StartBackgroundTrack</c>). Le moteur lit du MP3 ou du wav PCM
/// (il ne sait pas lire l'ogg). Un wav de musique est lu en flux, a n'importe quelle frequence (reechantillonnage au plus proche voisin, donc on le fournit a 22 050 Hz,
/// la frequence attendue par le moteur) et en stereo.</para>
/// <para><b>Conversion.</b> Les pistes du cycle sont decodees (NVorbis, bibliotheque Vorbis geree), ramenees a 22 050 Hz stereo 16 bits par un filtre sinc fenetre, et mises bout a bout
/// dans l'ordre de lecture de JK en un seul wav <c>music/jk/&lt;niveau&gt;.wav</c> que le worldspawn joue en boucle : c'est exactement la suite jouee par JK, sans doublon.</para>
/// </summary>
internal static class JkMusic
{
    public const string Folder = "music/jk/";

    /// <summary>Frequence du wav produit : celle que le moteur de JA attend pour la musique (un MP3 de musique y est decode aussi).</summary>
    public const int OutputRate = 22050;

    /// <summary>Appel <c>PlaySong</c> d'un niveau. <see cref="StartsMuted"/> : le meme message <c>startup</c> coupe le volume (<c>SetMusicVol(0)</c>).</summary>
    public sealed record Song(int Start, int End, int LoopTo, string Script, bool StartsMuted);

    // ---------------------------------------------------------------- lecture des COG

    /// <summary>
    /// <c>PlaySong</c> du message <c>startup</c> des COG du niveau (le dernier appel l'emporte, comme dans le moteur), ou null. Les arguments sont resolus avec les
    /// valeurs de l'instance (un argument non numerique rend l'appel inutilisable).
    /// </summary>
    public static Song? Find(JklLevel level, Func<string, CogScript?> script)
    {
        Song? found = null;
        foreach (JklCogInstance inst in level.Cogs)
        {
            CogScript? cog = script(inst.CogFile);
            if (cog is null || cog.Handler("startup") is null) continue;
            IReadOnlyList<CogCallSite> calls = cog.Expand("startup");
            CogCallSite? play = calls.LastOrDefault(c => c.Function.Equals("PlaySong", StringComparison.OrdinalIgnoreCase) && c.Args.Length is 1 or 3);
            if (play is null) continue;
            BoundCog bound = CogBinding.Bind(cog, inst);
            double?[] n = [.. play.Args.Select(bound.Number)];
            if (n.Any(v => v is null)) continue;
            int start = (int)n[0]!.Value, end = n.Length == 3 ? (int)n[1]!.Value : start, loop = n.Length == 3 ? (int)n[2]!.Value : start;
            bool muted = calls.Any(c => c.Function.Equals("SetMusicVol", StringComparison.OrdinalIgnoreCase) && c.Args.Length == 1 && bound.Number(c.Args[0]) == 0);
            found = new Song(start, end, loop, inst.CogFile, muted);
        }
        return found;
    }

    /// <summary>
    /// Pistes dans l'ordre de lecture d'une boucle : le cycle <c>retour..fin</c> commence a <c>debut</c> (JK joue <c>debut..fin</c> puis <c>retour..fin</c> sans fin, ce qui est ce cycle tourne a
    /// partir de <c>debut</c>). <paramref name="exact"/> est faux quand <c>debut</c> n'est pas dans le cycle (aucun niveau reel) : on joue alors <c>debut..fin</c>.
    /// </summary>
    public static IReadOnlyList<int> Sequence(Song song, out bool exact)
    {
        (int start, int end, int loop) = (song.Start, song.End, song.LoopTo);
        if (end < start) { exact = false; return [start]; }
        if (loop <= start && loop >= 0)
        {
            exact = true;
            var order = new List<int>();
            for (int t = start; t <= end; t++) order.Add(t);
            for (int t = loop; t < start; t++) order.Add(t);
            return order;
        }
        exact = false;
        return [.. Enumerable.Range(start, end - start + 1)];
    }

    // ---------------------------------------------------------------- conversion d'un niveau

    /// <summary>
    /// Musique du niveau : un wav stereo 22 050 Hz sous <c>music/jk/</c> et la cle <c>music</c> du worldspawn. <paramref name="readTrack"/> rend le fichier ogg de la piste
    /// (defaut : <c>MUSIC\TrackNN.ogg</c> du dossier du jeu) ; <paramref name="decode"/> le decode (defaut : <see cref="DecodeOgg"/>).
    /// </summary>
    public static ConvertedEntities Convert(JkPreparedScene prepared, JkFileSystem fs, ExportOptions opt, ImportLog log,
        Func<int, byte[]?>? readTrack = null, Func<byte[], PcmAudio>? decode = null, long maxDataBytes = MaxWavDataBytes)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(opt);
        ArgumentNullException.ThrowIfNull(log);
        // Cache seulement avec la lecture par defaut des fichiers du jeu (la cle depend de leur taille et date) ; un lecteur fourni (tests) n'est jamais mis en cache.
        string? cacheKey = readTrack is null ? CacheKey(prepared.Scene.Info.Module, opt.JediKnightDir, maxDataBytes) : null;
        readTrack ??= n => ReadTrack(opt.JediKnightDir, n);
        decode ??= DecodeOgg;

        var scripts = new Dictionary<string, CogScript?>(StringComparer.OrdinalIgnoreCase);
        CogScript? Script(string file)
        {
            if (!scripts.TryGetValue(file, out CogScript? s))
            {
                byte[]? raw = fs.TryRead(@"cog\" + file);
                scripts[file] = s = raw is null ? null : CogReader.Read(Encoding.Latin1.GetString(raw));
            }
            return s;
        }

        Song? song = Find(prepared.Level, Script);
        if (song is null)
        {
            log.Warn("JK_MUSIC_NONE", "Music: no music script (PlaySong) in this level, so the map has no music.");
            return ConvertedEntities.Empty;
        }

        int volumeScripts = prepared.Level.Cogs.Count(i => Script(i.CogFile) is { } c
            && c.AllCalls().Any(x => x.Function.Equals("SetMusicVol", StringComparison.OrdinalIgnoreCase)));
        if (volumeScripts > 0)
            log.Warn("JK_MUSIC_VOLUME_SCRIPT", $"Music: {volumeScripts} COG script(s) change the music volume (SetMusicVol: fades and lowering during dialogue)"
                + (song.StartsMuted ? $"; {song.Script} starts muted and only fades the music in when the player crosses certain sectors" : "")
                + ". Jedi Academy has no such volume script: the music plays at one constant volume everywhere in the map.");

        IReadOnlyList<int> sequence = Sequence(song, out bool exact);
        string call = song.Start == song.End && song.End == song.LoopTo ? $"PlaySong({song.Start})" : $"PlaySong({song.Start}, {song.End}, {song.LoopTo})";
        if (!exact)
            log.Warn("JK_MUSIC_LOOP_APPROX", $"Music: {call} of {song.Script} does not start inside its loop; the tracks {string.Join(", ", sequence)} are played in order and looped.");

        string? sequenceKey = cacheKey is null ? null : cacheKey + "|" + string.Join(',', sequence);
        Built? built = sequenceKey is null ? null : CacheGet(opt.JediKnightDir!, sequenceKey);
        if (built is null)
        {
            built = Build(sequence, readTrack, decode, maxDataBytes);
            if (sequenceKey is not null && built.Wav is not null) CachePut(opt.JediKnightDir!, sequenceKey, built);
        }

        if (built.Missing.Count > 0)
            log.Warn("JK_MUSIC_TRACK_MISSING", $"Music: {call} of {song.Script} plays CD track(s) {string.Join(", ", sequence)}, but MUSIC\\Track*.ogg of the game has no file for "
                + $"{string.Join(", ", built.Missing)}" + (built.Used.Count > 0 ? "; they are left out of the loop." : "."));
        foreach (var (track, why) in built.Unreadable)
            log.Warn("JK_MUSIC_UNREADABLE", $"Music: Track{track}.ogg cannot be used ({why}); it is left out of the loop.");

        if (built.Wav is null)
        {
            log.Warn("JK_MUSIC_NONE", $"Music: {call} of {song.Script} has no track file I can read, so the map has no music.");
            return ConvertedEntities.Empty;
        }

        string path = Folder + prepared.Scene.Info.Module.ToLowerInvariant() + ".wav";
        log.Warn("JK_MUSIC", $"Music: {call} of {song.Script} -> CD track(s) {string.Join(", ", built.Used)} in one loop, "
            + $"{path} ({built.Seconds.ToString("0.0", CultureInfo.InvariantCulture)} s, {(built.Wav.Length / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)} MB, {OutputRate} Hz 16-bit stereo wav), "
            + "played in a loop by the worldspawn 'music' key.");
        return new ConvertedEntities([], [], [])
        {
            Sounds = [new SoundAsset(path, built.Wav)],
            WorldKeys = new Dictionary<string, string>(StringComparer.Ordinal) { ["music"] = path },
        };
    }

    // ---------------------------------------------------------------- construction du wav et cache

    /// <summary>Donnees d'un wav au plus : le champ RIFF « taille » est un entier 32 bits signe, en-tete de 36 octets compris.</summary>
    public const long MaxWavDataBytes = int.MaxValue - 36;

    /// <summary>Resultat de la lecture des pistes : le wav (null si aucune piste utilisable) et ce qui a ete omis, pour que le journal soit identique avec ou sans cache.</summary>
    private sealed record Built(byte[]? Wav, IReadOnlyList<int> Used, IReadOnlyList<int> Missing, IReadOnlyList<(int Track, string Why)> Unreadable, double Seconds);

    /// <summary>
    /// Lit et decode les pistes. La musique n'est JAMAIS fatale : toute exception de lecture ou de decodage d'une piste (hors erreurs critiques de memoire ou de processus) la
    /// fait omettre (<c>JK_MUSIC_UNREADABLE</c>), y compris les exceptions inattendues de NVorbis sur un flux corrompu. Une piste qui ferait depasser <paramref name="maxDataBytes"/>
    /// (limite RIFF de 2 Go) est omise de la meme facon, avec la raison « too long ».
    /// </summary>
    private static Built Build(IReadOnlyList<int> sequence, Func<int, byte[]?> readTrack, Func<byte[], PcmAudio> decode, long maxDataBytes)
    {
        var missing = new List<int>();
        var unreadable = new List<(int Track, string Why)>();
        var used = new List<int>();
        var pcm = new List<short[]>();
        long total = 0;                                              // en octets de donnees wav (long : pas de debordement au-dela de 1 Go)
        foreach (int track in sequence)
        {
            try
            {
                byte[]? ogg = readTrack(track);
                if (ogg is null) { missing.Add(track); continue; }
                short[] data = ToStereo16(decode(ogg), OutputRate);
                long bytes = (long)data.Length * 2;
                if (total + bytes > maxDataBytes)
                {
                    unreadable.Add((track, $"too long: the wav would exceed {maxDataBytes / 1048576} MB"));
                    continue;
                }
                total += bytes;
                pcm.Add(data);
                used.Add(track);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or AccessViolationException or ThreadAbortException))
            {
                unreadable.Add((track, ex.Message));
            }
        }
        if (pcm.Count == 0) return new Built(null, used, missing, unreadable, 0);
        return new Built(WavFile(pcm, OutputRate, maxDataBytes), used, missing, unreadable, total / 4.0 / OutputRate);
    }

    // Cache simple du dernier wav par dossier du jeu : Import (puis Export) consecutifs de la meme carte ne redecodent pas 300 a 400 Mo d'audio. Un seul wav (25 a 45 Mo) reste en memoire.
    // L'apercu (PipelineSteps.Preview) n'appelle pas la conversion des objets : seul Import la lance.
    private static readonly Dictionary<string, (string Key, Built Value)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    private static Built? CacheGet(string dir, string key)
    {
        lock (CacheLock) return Cache.TryGetValue(dir, out var e) && e.Key == key ? e.Value : null;
    }

    private static void CachePut(string dir, string key, Built value)
    {
        lock (CacheLock) Cache[dir] = (key, value);
    }

    /// <summary>Cle de cache : niveau, limite, et pour chaque fichier du dossier <c>MUSIC</c> sa taille et sa date ; null si le dossier du jeu n'est pas utilisable.</summary>
    private static string? CacheKey(string module, string? jkDir, long maxDataBytes)
    {
        if (string.IsNullOrWhiteSpace(jkDir) || !Directory.Exists(jkDir)) return null;
        try
        {
            string? dir = Directory.EnumerateDirectories(jkDir).FirstOrDefault(d => string.Equals(Path.GetFileName(d), "MUSIC", StringComparison.OrdinalIgnoreCase));
            if (dir is null) return null;
            var sb = new StringBuilder(module.ToLowerInvariant()).Append('|').Append(maxDataBytes);
            foreach (string f in Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var info = new FileInfo(f);
                sb.Append('|').Append(info.Name.ToLowerInvariant()).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks);
            }
            return sb.ToString();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary><c>MUSIC\Track&lt;n&gt;.ogg</c> du dossier du jeu (dossier cherche sans tenir compte de la casse), ou null.</summary>
    private static byte[]? ReadTrack(string? jkDir, int track)
    {
        if (string.IsNullOrWhiteSpace(jkDir) || !Directory.Exists(jkDir)) return null;
        string? dir = Directory.EnumerateDirectories(jkDir).FirstOrDefault(d => string.Equals(Path.GetFileName(d), "MUSIC", StringComparison.OrdinalIgnoreCase));
        if (dir is null) return null;
        string name = $"Track{track}.ogg";
        string? file = Directory.EnumerateFiles(dir).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
        return file is null ? null : File.ReadAllBytes(file);
    }

    // ---------------------------------------------------------------- decodage, reechantillonnage, wav

    /// <summary>Decode un fichier Ogg Vorbis. <see cref="InvalidDataException"/> si le fichier n'est pas lisible.</summary>
    public static PcmAudio DecodeOgg(byte[] ogg)
    {
        try
        {
            using var reader = new VorbisReader(new MemoryStream(ogg, writable: false), closeOnDispose: true);
            int channels = reader.Channels, rate = reader.SampleRate;
            if (channels < 1 || rate < 8000) throw new InvalidDataException("unsupported Vorbis stream");
            long expected = Math.Max(0, reader.TotalSamples) * channels;
            if (expected > int.MaxValue / 2) throw new InvalidDataException("Vorbis stream too long");
            var samples = new float[Math.Max(expected, channels * 4096L)];
            int filled = 0;
            while (true)
            {
                if (filled == samples.Length) Array.Resize(ref samples, (int)Math.Min(int.MaxValue / 2, samples.LongLength * 2));
                int read = reader.ReadSamples(samples, filled, samples.Length - filled);
                if (read <= 0) break;
                filled += read;
            }
            if (filled < samples.Length) Array.Resize(ref samples, filled);
            return new PcmAudio(rate, channels, samples);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or OverflowException)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Echantillons entrelaces stereo 16 bits a <paramref name="dstRate"/> Hz. Une source mono est doublee, au-dela de deux voies on garde les deux premieres.
    /// Reechantillonnage par un filtre sinc fenetre (Blackman, 12 lobes par cote a l'echelle de la source, coupure a 93 % de la nouvelle frequence de Nyquist) pour ne rien replier ;
    /// ecretage a +-1.
    /// </summary>
    public static short[] ToStereo16(PcmAudio audio, int dstRate)
    {
        int ch = audio.Channels, frames = audio.Samples.Length / ch;
        bool stereo = ch > 1;
        if (audio.SampleRate == dstRate)
        {
            var same = new short[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                same[2 * i] = Clip(audio.Samples[i * ch]);
                same[2 * i + 1] = Clip(audio.Samples[i * ch + (stereo ? 1 : 0)]);
            }
            return same;
        }

        int g = Gcd(audio.SampleRate, dstRate);
        long period = dstRate / g, step = audio.SampleRate / g;       // sortie j <-> entree j * step / period
        double ratio = (double)audio.SampleRate / dstRate;
        int half = (int)Math.Ceiling(12 * Math.Max(1.0, ratio));
        double cutoff = 0.5 / Math.Max(1.0, ratio) * 0.93;           // cycles par echantillon source
        var taps = new float[period][];
        for (int p = 0; p < period; p++)
        {
            double frac = (double)p / period, sum = 0;
            var t = new double[2 * half];
            for (int k = -half + 1; k <= half; k++)
            {
                double x = k - frac, a = 2 * cutoff * x;
                double sinc = Math.Abs(a) < 1e-12 ? 1 : Math.Sin(Math.PI * a) / (Math.PI * a);
                double w = 0.42 + 0.5 * Math.Cos(Math.PI * x / half) + 0.08 * Math.Cos(2 * Math.PI * x / half);
                t[k + half - 1] = 2 * cutoff * sinc * w;
                sum += t[k + half - 1];
            }
            taps[p] = [.. t.Select(v => (float)(v / sum))];   // gain continu 1
        }

        long outFrames = frames * period / step;
        var result = new short[outFrames * 2];
        float[] src = audio.Samples;
        for (long j = 0; j < outFrames; j++)
        {
            long num = j * step, i0 = num / period;
            float[] tap = taps[num % period];
            float left = 0, right = 0;
            long from = Math.Max(0, i0 - half + 1), to = Math.Min(frames - 1, i0 + half);
            for (long i = from; i <= to; i++)
            {
                float w = tap[i - i0 + half - 1];
                left += w * src[i * ch];
                if (stereo) right += w * src[i * ch + 1];
            }
            result[2 * j] = Clip(left);
            result[2 * j + 1] = Clip(stereo ? right : left);
        }
        return result;
    }

    /// <summary>Echantillon flottant -> 16 bits, ecrete ; NaN (flux corrompu) donne le silence, les infinis sont ecretes.</summary>
    private static short Clip(float v) => float.IsNaN(v) ? (short)0 : (short)MathF.Round(Math.Clamp(v * 32768f, short.MinValue, short.MaxValue));

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return a; }

    /// <summary>Fichier wav PCM 16 bits stereo (en-tete de 44 octets : <c>RIFF</c>, <c>fmt </c>, <c>data</c>) : le format que le lecteur de musique de JA attend.</summary>
    public static byte[] WavFile(short[] interleavedStereo, int rate) => WavFile([interleavedStereo], rate);

    /// <summary>
    /// Wav des blocs mis bout a bout, ecrit directement dans un tableau de taille connue (pas de copie intermediaire).
    /// <see cref="InvalidDataException"/> au-dela de <paramref name="maxDataBytes"/> (limite RIFF 2 Go).
    /// </summary>
    public static byte[] WavFile(IReadOnlyList<short[]> blocks, int rate, long maxDataBytes = MaxWavDataBytes)
    {
        long bytes = 0;
        foreach (short[] b in blocks) bytes += (long)b.Length * 2;
        if (bytes > maxDataBytes) throw new InvalidDataException($"wav data too long ({bytes} bytes, limit {maxDataBytes})");
        var wav = new byte[44 + bytes];
        Span<byte> h = wav;
        "RIFF"u8.CopyTo(h); BitConverter.TryWriteBytes(h[4..], (int)(36 + bytes)); "WAVE"u8.CopyTo(h[8..]);
        "fmt "u8.CopyTo(h[12..]); BitConverter.TryWriteBytes(h[16..], 16); BitConverter.TryWriteBytes(h[20..], (short)1); BitConverter.TryWriteBytes(h[22..], (short)2);
        BitConverter.TryWriteBytes(h[24..], rate); BitConverter.TryWriteBytes(h[28..], rate * 4); BitConverter.TryWriteBytes(h[32..], (short)4); BitConverter.TryWriteBytes(h[34..], (short)16);
        "data"u8.CopyTo(h[36..]); BitConverter.TryWriteBytes(h[40..], (int)bytes);
        int at = 44;
        foreach (short[] b in blocks)
        {
            Buffer.BlockCopy(b, 0, wav, at, b.Length * 2);
            at += b.Length * 2;
        }
        return wav;
    }
}
