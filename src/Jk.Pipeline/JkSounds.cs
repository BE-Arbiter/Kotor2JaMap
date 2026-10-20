using System.Text;
using Jk.Core.Gob;
using Kotor.Core.Diagnostics;
using Kotor.Pipeline.Entities;

namespace Jk.Pipeline;

/// <summary>
/// Fichiers son de JK -> sons JA. Les <c>.wav</c> viennent de <c>Resource\Res1hi.gob</c> (dossier <c>sound\</c>) ; ils sont copies sous <c>sound/jk/</c>.
/// Le moteur de JA ne joue pas un wav stereo (snd_mem.cpp : « is a stereo wav file », rejet) : un wav PCM stereo est ramene en mono par la moyenne des voies
/// (8 ou 16 bits). Les ambiances de JK (<c>00wind02.wav</c>, <c>00techamb02.wav</c>...) sont en partie stereo.
/// </summary>
internal static class JkSounds
{
    public const string Folder = "sound/jk/";

    /// <summary>Son <paramref name="name"/> du jeu pret pour JA, ou null (journalise <c>JK_SOUND_MISSING</c> / <c>JK_SOUND_UNREADABLE</c>).</summary>
    public static SoundAsset? Load(JkFileSystem fs, string name, ImportLog log)
    {
        string file = name.Replace('/', '\\').Split('\\')[^1];
        byte[]? raw = fs.TryRead(@"sound\" + file) ?? fs.TryRead(file);
        if (raw is null)
        {
            log.Warn("JK_SOUND_MISSING", $"Sound {file}: not found in the game archives; the sound is not placed.");
            return null;
        }
        byte[] wav;
        try { wav = ToMono(raw); }
        catch (InvalidDataException ex)
        {
            log.Warn("JK_SOUND_UNREADABLE", $"Sound {file}: {ex.Message}; the file is copied unchanged.");
            wav = raw;
        }
        return new SoundAsset(Folder + file.ToLowerInvariant(), wav);
    }

    /// <summary>Un wav mono est rendu tel quel ; un wav PCM stereo est ramene en mono. Autre format : <see cref="InvalidDataException"/>.</summary>
    public static byte[] ToMono(byte[] wav)
    {
        if (wav.Length < 12 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F' || wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E')
            throw new InvalidDataException("not a RIFF WAVE file");
        int pos = 12, dataOfs = -1, dataLen = 0;
        int format = 0, channels = 0, rate = 0, bits = 0;
        while (pos + 8 <= wav.Length)
        {
            int len = BitConverter.ToInt32(wav, pos + 4);
            if (len < 0) throw new InvalidDataException("invalid chunk size");
            string id = Encoding.ASCII.GetString(wav, pos, 4);
            if (id == "fmt " && pos + 8 + 16 <= wav.Length)
            {
                format = BitConverter.ToUInt16(wav, pos + 8);
                channels = BitConverter.ToUInt16(wav, pos + 10);
                rate = BitConverter.ToInt32(wav, pos + 12);
                bits = BitConverter.ToUInt16(wav, pos + 22);
            }
            else if (id == "data")
            {
                dataOfs = pos + 8;
                dataLen = (int)Math.Min((long)len, wav.Length - dataOfs);
                break;
            }
            pos += 8 + len + (len & 1);
        }
        if (dataOfs < 0 || channels == 0) throw new InvalidDataException("missing fmt or data chunk");
        if (channels == 1) return wav;
        if (format != 1 || channels != 2 || bits is not (8 or 16)) throw new InvalidDataException($"unsupported format (tag {format}, {channels} channel(s), {bits} bits)");

        int bytes = bits / 8, frames = dataLen / (2 * bytes);
        byte[] data = new byte[frames * bytes];
        for (int i = 0; i < frames; i++)
        {
            int at = dataOfs + i * 2 * bytes;
            if (bits == 16)
            {
                int mix = (BitConverter.ToInt16(wav, at) + BitConverter.ToInt16(wav, at + 2)) / 2;
                BitConverter.GetBytes((short)mix).CopyTo(data, i * 2);
            }
            else data[i] = (byte)((wav[at] + wav[at + 1]) / 2);
        }
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + data.Length); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * bytes); w.Write((short)bytes); w.Write((short)bits);
        w.Write("data"u8.ToArray()); w.Write(data.Length); w.Write(data);
        w.Flush();
        return ms.ToArray();
    }
}
