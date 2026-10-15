using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Audio;

namespace Kotor.Pipeline.Entities;

/// <summary>
/// Charge les sons d'un module pour JA : archives du jeu (Override, module, BIF) puis dossiers <c>streamsounds</c>/<c>streamwaves</c>
/// (sons) ou <c>streammusic</c> (musiques), convertis par <see cref="KotorAudio"/>. Un resref introuvable ou illisible donne null.
/// </summary>
internal sealed class AudioSources(ResourceResolver res, KotorGame game)
{
    private static readonly string[] SoundFolders = ["streamsounds", "streamwaves"];
    private static readonly string[] MusicFolders = ["streammusic"];
    private readonly string _folder = KotorGames.AssetFolder(game);
    private readonly Dictionary<string, SoundAsset?> _cache = new(StringComparer.Ordinal);

    public AudioCatalog Catalog { get; } = new(res);

    public SoundAsset? Sound(string resRef) => Load("sound", resRef, SoundFolders);

    public SoundAsset? Music(string resRef) => Load("music", resRef, MusicFolders);

    private SoundAsset? Load(string kind, string resRef, string[] folders)
    {
        string name = resRef.Trim().ToLowerInvariant();
        if (name.Length == 0 || name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))) return null;
        string key = kind + "/" + name;
        if (_cache.TryGetValue(key, out SoundAsset? hit)) return hit;
        byte[]? raw = kind == "music" ? null : res.Find(name, ResourceType.Wav);
        for (int i = 0; raw is null && i < folders.Length; i++)
        {
            string file = Path.Combine(res.KotorDir, folders[i], name + ".wav");
            if (File.Exists(file)) raw = File.ReadAllBytes(file);
        }
        JaAudio? audio = raw is null ? null : KotorAudio.Normalize(raw);
        if (audio is not null && kind == "music" && audio.Extension != "mp3") audio = null; // la musique de fond JA est du MP3
        SoundAsset? asset = audio is null ? null : new SoundAsset($"{kind}/{_folder}/{name}.{audio.Extension}", audio.Data);
        _cache[key] = asset;
        return asset;
    }
}
