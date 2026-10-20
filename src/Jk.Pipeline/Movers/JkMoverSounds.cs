using System.Text.RegularExpressions;
using Jk.Core.Gob;
using Jk.Core.Jkl;
using Kotor.Core.Diagnostics;
using Kotor.Pipeline.Entities;

namespace Jk.Pipeline.Movers;

/// <summary>
/// Sons d'un mobile : ses wav (deja ramenes en mono, sous <c>sound/jk/</c>) prets pour des <c>target_speaker</c>.
/// <see cref="Start"/> : joue quand le mobile part (<c>startmove</c> de la classe de sons) ; <see cref="Stop"/> : quand il arrive (<c>stopmove</c>).
/// <see cref="StartFromLoop"/> : la classe n'a que <c>moving</c> (boucle), joue une fois au depart (06stone_door, 19stone_door, 06drawbridge...) ;
/// <see cref="DropsMovingLoop"/> : la classe a un <c>moving</c> en plus d'un <c>startmove</c>, que JA ne sait pas jouer (voir <see cref="JkMoverSounds"/>).
/// </summary>
public sealed record MoverSoundSet(string Class, SoundAsset? Start, SoundAsset? Stop, bool StartFromLoop, bool DropsMovingLoop);

/// <summary>
/// Classes de sons des mobiles de JK. Un gabarit de thing donne <c>soundclass=med_door.snd</c> ; le fichier <c>misc\snd\med_door.snd</c> associe a chaque mode un wav :
/// <code>
/// startmove  df_door2-1.wav 0x0    0.1 1.0
/// stopmove   df_door2-3.wav 0x0    0.1 1.0
/// moving     df_door2-2.wav 0x0001 0.1 1.0 0.5   (0x1 = en boucle pendant le mouvement)
/// </code>
/// Les COG des portes (<c>00_door</c>) ne jouent aucun son de mouvement : tout vient de la classe. Ceux qu'ils jouent eux-memes (clic d'interrupteur, alarme) n'appartiennent pas au mobile.
/// <para>JA n'a pas de cle de son sur ses mobiles : <c>soundSet</c> nomme un groupe de <c>sound/sound.txt</c> (fichier unique du jeu, que le pk3 ne peut pas completer) et un nom inconnu fait
/// echouer le chargement (<c>AS_ParseSets</c>). Le repli est un <c>target_speaker</c> tire par les cles <c>target</c> (depart), <c>opentarget</c> et <c>closetarget</c> (arrivee) de la porte
/// (<c>g_mover.cpp</c>, <c>Use_BinaryMover_Go</c> et <c>Reached_BinaryMover</c>). Aucun evenement ne marque le depart du retour ni ne permet d'arreter une boucle : le <c>moving</c> d'une classe
/// qui a un <c>startmove</c> n'est pas joue.</para>
/// </summary>
internal sealed class JkMoverSounds
{
    private readonly JkPreparedScene _prepared;
    private readonly JkFileSystem _fs;
    private readonly ImportLog _log;
    private readonly Dictionary<string, MoverSoundSet?> _classes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SoundAsset?> _wavs = new(StringComparer.OrdinalIgnoreCase);

    public JkMoverSounds(JkPreparedScene prepared, JkFileSystem fs, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(log);
        _prepared = prepared; _fs = fs; _log = log;
    }

    /// <summary>Sons du thing <paramref name="thing"/> (indice dans le niveau), ou null s'il n'a pas de classe de sons ou si elle est illisible (journal <c>JK_MOVER_SOUND_SKIPPED</c>, une fois par classe).</summary>
    public MoverSoundSet? For(int thing)
    {
        JklThing? t = _prepared.Level.Things.FirstOrDefault(x => x.Index == thing);
        if (t is null) return null;
        string? soundClass = t.Params.TryGetValue("soundclass", out string? own) ? own : _prepared.Templates.Resolve(t.Template).GetValueOrDefault("soundclass");
        if (string.IsNullOrWhiteSpace(soundClass) || soundClass.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        soundClass = soundClass.Trim();
        if (!_classes.TryGetValue(soundClass, out MoverSoundSet? set)) _classes[soundClass] = set = Load(soundClass);
        return set;
    }

    private MoverSoundSet? Load(string soundClass)
    {
        byte[]? raw = _fs.TryRead(@"misc\snd\" + soundClass);
        if (raw is null)
        {
            _log.Warn("JK_MOVER_SOUND_SKIPPED", $"Sound class {soundClass} is not in the game archives: the things that use it keep no sound.");
            return null;
        }
        var modes = ParseClass(System.Text.Encoding.Latin1.GetString(raw));
        modes.TryGetValue("startmove", out string? start);
        modes.TryGetValue("stopmove", out string? stop);
        modes.TryGetValue("moving", out string? moving);
        bool fromLoop = start is null && moving is not null;
        SoundAsset? startAsset = Wav(start ?? moving), stopAsset = Wav(stop);
        return new MoverSoundSet(soundClass, startAsset, stopAsset, fromLoop, DropsMovingLoop: start is not null && moving is not null);
    }

    private SoundAsset? Wav(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!_wavs.TryGetValue(name, out SoundAsset? asset)) _wavs[name] = asset = JkSounds.Load(_fs, name, _log);
        return asset;
    }

    /// <summary>
    /// Mode (minuscules : <c>startmove</c>, <c>stopmove</c>, <c>moving</c>) -> nom du wav, d'apres le texte d'une classe de sons. Les lignes <c>#</c> et les blancs sont ignores,
    /// la premiere ligne de chaque mode compte.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseClass(string text)
    {
        var modes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;
            string[] parts = Regex.Split(trimmed, @"\s+");
            if (parts.Length >= 2) modes.TryAdd(parts[0].ToLowerInvariant(), parts[1]);
        }
        return modes;
    }
}
