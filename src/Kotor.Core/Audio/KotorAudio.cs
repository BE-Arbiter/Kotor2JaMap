using System.Buffers.Binary;
using System.Text;

namespace Kotor.Core.Audio;

/// <summary>Son pret pour JA : <see cref="Extension"/> est <c>mp3</c> ou <c>wav</c>.</summary>
public sealed record JaAudio(string Extension, byte[] Data);

/// <summary>
/// Les <c>.wav</c> de KOTOR sont souvent deguises : streammusic = en-tete RIFF factice de 58 octets suivi d'un MP3 ; streamsounds = MP3 nu
/// (LAME) ; sounds.bif = RIFF avec 470 octets d'en-tete factice, puis un vrai WAV. Cette classe rend ce que JA sait lire (MP3 ou WAV PCM/ADPCM).
/// </summary>
public static class KotorAudio
{
    private const int FakeRiffHeader = 58;
    private const int SfxHeader = 470;

    public static JaAudio? Normalize(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length < 12) return null;
        if (IsMp3(raw, 0)) return Mp3(raw, 0);
        if (IsRiff(raw, 0))
        {
            ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(20));
            // MP3 enveloppe : format 0x55, ou chunk data vide (taille 0) suivi directement du flux.
            if (tag == 0x55 || (Chunk(raw, 0, "data") is { Size: 0 } && IsMp3(raw, FakeRiffHeader)))
                return IsMp3(raw, FakeRiffHeader) ? Mp3(raw, FakeRiffHeader) : null;
            if (tag is 1 or 0x11 && Chunk(raw, 0, "data") is { Size: > 0 }) return new JaAudio("wav", raw);
            return null;
        }
        if (raw.Length > SfxHeader + 12 && IsRiff(raw, SfxHeader))
        {
            byte[] inner = raw[SfxHeader..];
            ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(inner.AsSpan(20));
            return tag is 1 or 0x11 && Chunk(inner, 0, "data") is { Size: > 0 } ? new JaAudio("wav", inner) : null;
        }
        return null;
    }

    /// <summary>Flux MP3 a partir de <paramref name="at"/>, sans balise ID3v2 de tete (le decodeur de JA ne saute que l'ID3v1 de fin : <c>towave.c</c>).</summary>
    private static JaAudio? Mp3(byte[] d, int at)
    {
        if (d[at] == 'I' && d[at + 1] == 'D' && d[at + 2] == '3')
        {
            if (d.Length < at + 10) return null;
            int size = (d[at + 6] << 21) | (d[at + 7] << 14) | (d[at + 8] << 7) | d[at + 9]; // entier "synchsafe"
            at += 10 + size;
            if (at + 4 > d.Length || !IsMp3(d, at) || d[at] == 'I') return null;
        }
        return new JaAudio("mp3", at == 0 ? d : d[at..]);
    }

    private static bool IsRiff(byte[] d, int at) =>
        d.Length >= at + 12 && Encoding.ASCII.GetString(d, at, 4) == "RIFF" && Encoding.ASCII.GetString(d, at + 8, 4) == "WAVE";

    /// <summary>Synchro de trame MPEG (11 bits a 1) ou balise ID3.</summary>
    private static bool IsMp3(byte[] d, int at) =>
        d.Length >= at + 4 && ((d[at] == 0xFF && (d[at + 1] & 0xE0) == 0xE0) || (d[at] == 'I' && d[at + 1] == 'D' && d[at + 2] == '3'));

    private readonly record struct ChunkInfo(int Offset, int Size);

    private static ChunkInfo? Chunk(byte[] d, int at, string id)
    {
        int p = at + 12;
        while (p + 8 <= d.Length)
        {
            int size = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p + 4)));
            if (Encoding.ASCII.GetString(d, p, 4) == id) return new ChunkInfo(p + 8, size);
            p += 8 + size + (size & 1);
        }
        return null;
    }
}
