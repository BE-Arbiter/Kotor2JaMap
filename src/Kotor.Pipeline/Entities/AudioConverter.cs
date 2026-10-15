using System.Globalization;
using System.Numerics;
using Kotor.Core.Audio;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Modules;

namespace Kotor.Pipeline.Entities;

/// <summary>
/// Musique et sons d'un module -> <c>worldspawn "music"</c> et <c>target_speaker</c> (OpenJK-SWGL <c>g_spawn.cpp:1499</c>, <c>g_target.cpp:150-260</c>) :
/// <list type="bullet">
/// <item>musique de zone (<c>MusicDay</c>) : cle <c>music</c> du worldspawn, un seul fichier = jouee en boucle (<c>snd_dma.cpp:4686</c>) ;</item>
/// <item>ambiance de zone (<c>AmbientSndDay</c>) : un haut-parleur en boucle (<c>spawnflags 1</c>) au centre de chaque piece ;</item>
/// <item>sons places (UTS) : en boucle (<c>spawnflags 1</c>) ou ponctuels (<c>wait</c>/<c>random</c> en secondes, rejoues par le client) ;</item>
/// <item>portes : le haut-parleur d'ouverture est la cible <c>target</c> de la porte (tiree au debut du mouvement, <c>g_mover.cpp:836</c>),
/// celui de fermeture sa <c>closetarget</c> (tiree a l'arrivee, <c>g_mover.cpp:791</c>).</item>
/// </list>
/// JA ne regle ni le volume ni la portee d'un haut-parleur : les valeurs UTS correspondantes sont ignorees.
/// </summary>
internal static class AudioConverter
{
    /// <summary>Plafond de haut-parleurs d'ambiance de zone (un par piece).</summary>
    public const int MaxAmbientSpeakers = 32;
    private const float AmbientHeight = 1.5f; // metres au-dessus du centre de la piece

    /// <summary>Musique (si <paramref name="includeMusic"/>), ambiance et sons places d'un module ; positions en metres KOTOR, repere de la carte.</summary>
    public static ConvertedEntities Convert(AudioSources src, ModuleInfo info, IReadOnlyList<ModuleEntity> sounds,
        IReadOnlyList<Vector3> roomCenters, bool includeMusic, float scale, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(sounds);
        ArgumentNullException.ThrowIfNull(roomCenters);
        ArgumentNullException.ThrowIfNull(log);
        var entities = new List<IReadOnlyDictionary<string, string>>();
        var assets = new Dictionary<string, SoundAsset>(StringComparer.Ordinal);
        var world = new Dictionary<string, string>(StringComparer.Ordinal);

        AreaAudio area = src.Catalog.ReadArea(info);
        if (includeMusic && area.Music is { } musicName)
        {
            SoundAsset? music = src.Music(musicName);
            if (music is null) log.Warn("MUSIC_MISSING", $"{info.AreaResRef}: music '{musicName}' not found or not MP3; the map has no music.");
            else { assets[music.Path] = music; world["music"] = music.Path; }
        }

        int ambient = 0;
        if (area.Ambient is { } ambientName)
        {
            SoundAsset? snd = src.Sound(ambientName);
            if (snd is null) log.Warn("AMBIENT_MISSING", $"{info.AreaResRef}: ambient sound '{ambientName}' not found; no area ambience.");
            else
            {
                assets[snd.Path] = snd;
                foreach (Vector3 c in roomCenters.Take(MaxAmbientSpeakers))
                {
                    entities.Add(Speaker(snd.Path, c + new Vector3(0, 0, AmbientHeight), scale, looping: true));
                    ambient++;
                }
            }
        }

        int placed = 0, skipped = 0;
        foreach (ModuleEntity e in sounds)
        {
            if (e.Kind != EntityKind.Sound) continue;
            SoundObject? uts = src.Catalog.ReadSoundObject(e.Template);
            if (uts is null) { Skip(e, $"UTS '{e.Template}' not found", log, ref skipped); continue; }
            if (!uts.Active) { Skip(e, "inactive", log, ref skipped); continue; }
            SoundAsset? snd = uts.Sounds.Select(src.Sound).FirstOrDefault(a => a is not null);
            if (snd is null) { Skip(e, uts.Sounds.Count == 0 ? "no sound in the UTS" : $"sound '{uts.Sounds[0]}' not found", log, ref skipped); continue; }
            if (!uts.Looping && uts.IntervalMs <= 0) { Skip(e, "one-shot sound without interval", log, ref skipped); continue; }
            assets[snd.Path] = snd;
            entities.Add(Speaker(snd.Path, e.Position, scale, uts.Looping, uts.IntervalMs / 1000f, uts.IntervalVariationMs / 1000f));
            placed++;
        }

        if (placed + skipped + ambient > 0 || world.Count > 0)
            log.Warn("AUDIO", $"{info.AreaResRef}: music {(world.Count > 0 ? "yes" : "no")}, {ambient} ambient speaker(s), {placed} placed sound(s), {skipped} skipped.");
        return new ConvertedEntities(entities, [], [])
        {
            Sounds = [.. assets.Values.OrderBy(a => a.Path, StringComparer.Ordinal)],
            WorldKeys = world,
        };
    }

    /// <summary>
    /// Sons d'une porte (<paramref name="targetBase"/> = prefixe unique) : haut-parleurs a ajouter et cles <c>target</c>/<c>closetarget</c> de la porte.
    /// Null si la porte n'a aucun son lisible.
    /// </summary>
    public static (IReadOnlyList<IReadOnlyDictionary<string, string>> Speakers, IReadOnlyDictionary<string, string> DoorKeys, IReadOnlyList<SoundAsset> Assets)?
        DoorSpeakers(AudioSources src, ModuleEntity door, string targetBase, float scale)
    {
        DoorSounds names = src.Catalog.ReadDoorSounds(door.Template);
        var speakers = new List<IReadOnlyDictionary<string, string>>();
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var assets = new List<SoundAsset>();
        foreach (var (name, targetKey, suffix) in new[] { (names.Open, "target", "o"), (names.Close, "closetarget", "c") })
        {
            if (name is null || src.Sound(name) is not { } snd) continue;
            string targetName = targetBase + suffix;
            var speaker = (Dictionary<string, string>)Speaker(snd.Path, door.Position, scale, looping: false);
            speaker["targetname"] = targetName;
            speakers.Add(speaker);
            keys[targetKey] = targetName;
            assets.Add(snd);
        }
        return speakers.Count == 0 ? null : (speakers, keys, assets);
    }

    /// <summary><c>target_speaker</c> ; <paramref name="wait"/>/<paramref name="random"/> en secondes (rejeu automatique si wait &gt; 0).</summary>
    private static IReadOnlyDictionary<string, string> Speaker(string noise, Vector3 position, float scale, bool looping, float wait = 0f, float random = 0f)
    {
        var ent = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["classname"] = "target_speaker",
            ["noise"] = noise,
            ["origin"] = $"{PlaceableConverter.Num(position.X * scale)} {PlaceableConverter.Num(position.Y * scale)} {PlaceableConverter.Num(position.Z * scale)}",
        };
        if (looping) ent["spawnflags"] = "1";
        if (wait > 0f) ent["wait"] = wait.ToString("0.###", CultureInfo.InvariantCulture);
        if (random > 0f) ent["random"] = random.ToString("0.###", CultureInfo.InvariantCulture);
        return ent;
    }

    private static void Skip(ModuleEntity e, string reason, ImportLog log, ref int skipped)
    {
        skipped++;
        log.Warn("SOUND_SKIPPED", $"Sound '{e.Name}' (template '{e.Template}') at ({PlaceableConverter.Num(e.Position.X)}, {PlaceableConverter.Num(e.Position.Y)}, {PlaceableConverter.Num(e.Position.Z)}): {reason}; skipped.");
    }
}
