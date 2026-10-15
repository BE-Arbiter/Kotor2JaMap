using System.Text;
using Kotor.Core.Audio;
using Xunit;

namespace Kotor.Core.Tests.Audio;

public sealed class KotorAudioTests
{
    private static readonly byte[] Mp3Frame = [0xFF, 0xFB, 0x90, 0x40, 0, 0, 0, 0x4B, 0, 0, 0, 0];

    private static byte[] Riff(ushort tag, byte[] data, bool emptyData = false)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms, Encoding.ASCII);
        w.Write("RIFF"u8.ToArray()); w.Write(0); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write(tag); w.Write((ushort)1); w.Write(22050); w.Write(44100); w.Write((ushort)2); w.Write((ushort)16);
        w.Write("data"u8.ToArray()); w.Write(emptyData ? 0 : data.Length); w.Write(data);
        return ms.ToArray();
    }

    [Fact]
    public void Bare_mp3_is_kept()
    {
        byte[] raw = [.. Mp3Frame, .. new byte[40]];
        JaAudio a = KotorAudio.Normalize(raw)!;
        Assert.Equal("mp3", a.Extension);
        Assert.Equal(raw, a.Data);
    }

    [Fact]
    public void Mp3_behind_a_fake_riff_header_is_unwrapped()
    {
        // 58 octets : RIFF/WAVE/fmt (18)/fact/data de taille 0, puis le flux MP3 (streammusic).
        var header = new byte[58];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(header, 8);
        header[16] = 18; header[20] = 1; header[22] = 1;
        Encoding.ASCII.GetBytes("fact").CopyTo(header, 38);
        header[42] = 4;
        Encoding.ASCII.GetBytes("data").CopyTo(header, 50);
        byte[] raw = [.. header, .. Mp3Frame, .. new byte[20]];
        JaAudio a = KotorAudio.Normalize(raw)!;
        Assert.Equal("mp3", a.Extension);
        Assert.Equal(Mp3Frame, a.Data[..Mp3Frame.Length]);
        Assert.Equal(raw.Length - 58, a.Data.Length);
    }

    [Fact]
    public void Leading_id3v2_tag_is_stripped()
    {
        byte[] tag = [(byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 5, 1, 2, 3, 4, 5]; // 10 octets d'en-tete + 5 de contenu
        byte[] raw = [.. tag, .. Mp3Frame, .. new byte[20]];
        JaAudio a = KotorAudio.Normalize(raw)!;
        Assert.Equal("mp3", a.Extension);
        Assert.Equal(Mp3Frame, a.Data[..Mp3Frame.Length]);
        Assert.Equal(raw.Length - tag.Length, a.Data.Length);
    }

    [Fact]
    public void Pcm_wav_is_kept()
    {
        byte[] raw = Riff(1, new byte[64]);
        JaAudio a = KotorAudio.Normalize(raw)!;
        Assert.Equal("wav", a.Extension);
        Assert.Equal(raw, a.Data);
    }

    [Fact]
    public void Wav_behind_a_470_byte_sfx_header_is_unwrapped()
    {
        byte[] wav = Riff(1, new byte[64]);
        byte[] raw = [.. new byte[470], .. wav];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(raw, 0);   // le faux en-tete commence aussi par RIFF mais n'a pas de WAVE
        JaAudio a = KotorAudio.Normalize(raw)!;
        Assert.Equal("wav", a.Extension);
        Assert.Equal(wav, a.Data);
    }

    [Fact]
    public void Unknown_data_gives_null()
    {
        Assert.Null(KotorAudio.Normalize(new byte[64]));
        Assert.Null(KotorAudio.Normalize(new byte[3]));
        Assert.Null(KotorAudio.Normalize(Riff(1, [], emptyData: true)));
    }
}
