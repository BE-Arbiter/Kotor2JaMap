using Jk.Core;
using Jk.Core.Cog;
using Jk.Core.Gob;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline.Entities;
using Xunit;

namespace Jk.Pipeline.Tests;

// Musique : PlaySong(debut, fin, retour) des COG -> un wav 22 050 Hz stereo (pistes MUSIC\TrackNN.ogg decodees) et la cle « music » du worldspawn.
// Classe partielle de JkThingConverterTests pour reutiliser la fabrication de niveaux et de GOB synthetiques.
public sealed partial class JkThingConverterTests
{
    private const string SoundtrackCog = """
        symbols
            message startup
            int starttrack  desc=track_to_start_on
            int endtrack    desc=end_of_range
            int loopto      desc_track_to_loop_back_to
        end
        code
        startup:
            playsong(starttrack, endtrack, loopto);
            return;
        end
        """;

    private const string FadedSoundtrackCog = """
        symbols
            message startup
            message crossed
            message pulse
            int starttrack
            int endtrack
            int loopto
            float currVol local
        end
        code
        startup:
            PlaySong(starttrack, endtrack, loopto);
            SetMusicVol(0.0);
            currVol = 0.0;
            return;
        crossed:
            SetPulse(0.1);
            return;
        pulse:
            currVol = currVol + 0.05;
            SetMusicVol(currVol);
            return;
        end
        """;

    private const string LevelSongCog = """
        symbols
            message startup
            int cdtrack desc=cd_track_to_play
        end
        code
        startup:
            playsong(cdtrack);
            stop;
        end
        """;

    private const string DuckingCog = """
        symbols
            message startup
            message activated
        end
        code
        startup:
            return;
        activated:
            SetMusicVol(0.3);
            sleep(2);
            SetMusicVol(1.0);
            return;
        end
        """;

    private static SyntheticLevel MusicLevel(string file, string script, string instance, params (string File, string Script, string Instance)[] more)
    {
        var scripts = new Dictionary<string, string> { [file] = script };
        var cogs = new List<string> { file + " " + instance };
        foreach (var m in more) { scripts[m.File] = m.Script; cogs.Add(m.File + " " + m.Instance); }
        return new SyntheticLevel { Sectors = 2, Cogs = cogs, CogScripts = scripts };
    }

    private static CogScript? ScriptOf(Env e, string file) =>
        e.Fs.TryRead(@"cog\" + file) is { } raw ? CogReader.Read(System.Text.Encoding.Latin1.GetString(raw)) : null;

    /// <summary>Piste factice : [niveau, secondes]. Le decodeur factice la rend en 44 100 Hz stereo constant (niveau / 100) de la duree donnee.</summary>
    private static byte[] FakeTrack(int level, int seconds) => [(byte)level, (byte)seconds];

    private static PcmAudio FakeDecode(byte[] data)
    {
        if (data.Length != 2) throw new InvalidDataException("not a fake track");
        var samples = new float[data[1] * 44100 * 2];
        Array.Fill(samples, data[0] / 100f);
        return new PcmAudio(44100, 2, samples);
    }

    private static short[] Pcm(byte[] wav)
    {
        var pcm = new short[(wav.Length - 44) / 2];
        Buffer.BlockCopy(wav, 44, pcm, 0, pcm.Length * 2);
        return pcm;
    }

    // --- sequence de pistes ---

    [Theory]
    [InlineData(12, 13, 12, "12,13")]      // debut = retour : le cycle seul
    [InlineData(13, 13, 12, "13,12")]      // JK joue 13 puis 12,13,12,13... : le cycle (12,13) commence a 13
    [InlineData(15, 15, 14, "15,14")]
    [InlineData(23, 24, 22, "23,24,22")]
    [InlineData(24, 24, 22, "24,22,23")]
    [InlineData(25, 27, 25, "25,26,27")]
    [InlineData(27, 27, 25, "27,25,26")]
    [InlineData(28, 28, 28, "28")]
    [InlineData(2, 4, 2, "2,3,4")]
    public void Sequence_IsTheCycleStartedAtTheFirstTrack(int start, int end, int loopTo, string expected) =>
        Assert.Equal(expected, string.Join(',', JkMusic.Sequence(new JkMusic.Song(start, end, loopTo, "x.cog", false), out bool exact)));

    [Fact]
    public void Sequence_StartOutsideTheCycle_PlaysTheRangeAndFlagsAnApproximation()
    {
        var seq = JkMusic.Sequence(new JkMusic.Song(5, 7, 6 + 4, "x.cog", false), out bool exact);
        Assert.False(exact);
        Assert.Equal("5,6,7", string.Join(',', seq));
    }

    [Theory]
    [InlineData(5, 8, 7, "5,6,7,8")]      // retour apres debut : pas dans le cycle, on joue debut..fin
    [InlineData(3, 6, 9, "3,4,5,6")]      // retour au-dela de la fin
    [InlineData(2, 4, 3, "2,3,4")]
    public void Sequence_LoopAfterStart_PlaysTheRangeAndFlagsAnApproximation(int start, int end, int loopTo, string expected)
    {
        var seq = JkMusic.Sequence(new JkMusic.Song(start, end, loopTo, "x.cog", false), out bool exact);
        Assert.False(exact);
        Assert.Equal(expected, string.Join(',', seq));
    }

    [Fact]
    public void Sequence_EndBeforeStart_PlaysTheStartTrackOnly()
    {
        var seq = JkMusic.Sequence(new JkMusic.Song(9, 7, 7, "x.cog", false), out bool exact);
        Assert.False(exact);
        Assert.Equal("9", string.Join(',', seq));
    }

    [Fact]
    public void Convert_StartOutsideTheCycle_LogsTheApproximation()
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "5 7 9"));
        JkMusic.Convert(e.Prepared, e.Fs, e.Opt, e.Log, n => FakeTrack(10, 1), FakeDecode);
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_MUSIC_LOOP_APPROX" && w.Message.Contains("5, 6, 7"));
    }

    // Comportement reel : le message startup de chaque instance est lu dans l'ordre du niveau et le DERNIER PlaySong l'emporte (comme le moteur).
    [Fact]
    public void Find_SeveralInstancesWithPlaySong_TheLastOneWins()
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "12 13 12", ("levelsong.cog", LevelSongCog, "21")));
        Assert.Equal(new JkMusic.Song(21, 21, 21, "levelsong.cog", false), JkMusic.Find(e.Prepared.Level, f => ScriptOf(e, f)));
    }

    [Fact]
    public void Find_SameScriptTwiceWithDifferentArguments_TheLastInstanceWins()
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "12 13 12", ("00_soundtrack.cog", SoundtrackCog, "14 15 14")));
        Assert.Equal(new JkMusic.Song(14, 15, 14, "00_soundtrack.cog", false), JkMusic.Find(e.Prepared.Level, f => ScriptOf(e, f)));
    }

    // --- la musique n'est jamais fatale ---

    [Theory]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(IndexOutOfRangeException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Convert_UnexpectedExceptionWhileReadingATrack_IsLoggedAndTheLevelContinues(Type exception)
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "12 13 12"));
        ConvertedEntities c = JkMusic.Convert(e.Prepared, e.Fs, e.Opt, e.Log,
            n => n == 13 ? throw (Exception)Activator.CreateInstance(exception)! : FakeTrack(20, 1), FakeDecode);

        Assert.Equal(22050 * 2, Pcm(Assert.Single(c.Sounds).Data).Length);
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_MUSIC_UNREADABLE" && w.Message.Contains("13"));
    }

    [Fact]
    public void Convert_UnexpectedExceptionWhileDecoding_IsLoggedAndTheLevelContinues()
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "12 13 12"));
        ConvertedEntities c = JkMusic.Convert(e.Prepared, e.Fs, e.Opt, e.Log, n => FakeTrack(20, 1),
            d => throw new NullReferenceException("corrupt stream"));

        Assert.Empty(c.Sounds);
        Assert.Equal(2, e.Log.Warnings.Count(w => w.Code == "JK_MUSIC_UNREADABLE"));
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_MUSIC_NONE");
    }

    // --- limite RIFF ---

    [Fact]
    public void Convert_TrackThatWouldOverflowTheWavLimit_IsLeftOutAndLogged()
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "13 13 12"));
        // 100 000 octets de donnees au plus : une seconde (88 200 octets) tient, la seconde piste non.
        ConvertedEntities c = JkMusic.Convert(e.Prepared, e.Fs, e.Opt, e.Log, n => FakeTrack(20, 1), FakeDecode, maxDataBytes: 100_000);

        Assert.Equal(22050 * 2, Pcm(Assert.Single(c.Sounds).Data).Length);
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_MUSIC_UNREADABLE" && w.Message.Contains("12") && w.Message.Contains("too long"));
    }

    [Fact]
    public void Convert_FirstTrackAloneOverflowsTheWavLimit_NoMusic()
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "13 13 12"));
        ConvertedEntities c = JkMusic.Convert(e.Prepared, e.Fs, e.Opt, e.Log, n => FakeTrack(20, 1), FakeDecode, maxDataBytes: 1000);

        Assert.Empty(c.Sounds);
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_MUSIC_NONE");
    }

    [Fact]
    public void Wav_RefusesMoreThanTheRiffLimit()
    {
        Assert.Throws<InvalidDataException>(() => JkMusic.WavFile([new short[8]], 22050, maxDataBytes: 8));
    }

    [Fact]
    public void Resample_NaNIsSilence()
    {
        short[] pcm = JkMusic.ToStereo16(new PcmAudio(22050, 1, [float.NaN, float.PositiveInfinity, float.NegativeInfinity]), 22050);
        Assert.Equal([0, 0, 32767, 32767, -32768, -32768], pcm.Select(v => (int)v));
    }

    // --- cache du decodage ---

    [Fact]
    public void Convert_SameLevelTwice_DecodesTheTracksOnlyOnceUntilAFileChanges()
    {
        string dir = Path.Combine(Path.GetTempPath(), "jkmusic_cache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "MUSIC"));
        try
        {
            string file = Path.Combine(dir, "MUSIC", "Track12.ogg");
            File.WriteAllBytes(file, FakeTrack(20, 1));
            using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "12 12 12"));
            ExportOptions opt = e.Opt with { JediKnightDir = dir };
            int decoded = 0;
            Func<byte[], PcmAudio> decode = d => { decoded++; return FakeDecode(d); };

            ConvertedEntities a = JkMusic.Convert(e.Prepared, e.Fs, opt, e.Log, decode: decode);
            ConvertedEntities b = JkMusic.Convert(e.Prepared, e.Fs, opt, new ImportLog(), decode: decode);
            Assert.Equal(1, decoded);
            Assert.Same(Assert.Single(a.Sounds).Data, Assert.Single(b.Sounds).Data);

            File.WriteAllBytes(file, FakeTrack(30, 2));   // piste modifiee : taille differente
            ConvertedEntities c = JkMusic.Convert(e.Prepared, e.Fs, opt, new ImportLog(), decode: decode);
            Assert.Equal(2, decoded);
            Assert.Equal(2 * 22050 * 2, Pcm(Assert.Single(c.Sounds).Data).Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Convert_CacheHit_KeepsTheJournal()
    {
        string dir = Path.Combine(Path.GetTempPath(), "jkmusic_cache_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "MUSIC"));
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "MUSIC", "Track12.ogg"), FakeTrack(20, 1));
            using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "12 13 12"));   // la piste 13 manque
            ExportOptions opt = e.Opt with { JediKnightDir = dir };
            JkMusic.Convert(e.Prepared, e.Fs, opt, new ImportLog(), decode: FakeDecode);
            var log = new ImportLog();
            JkMusic.Convert(e.Prepared, e.Fs, opt, log, decode: FakeDecode);
            Assert.Contains(log.Warnings, w => w.Code == "JK_MUSIC_TRACK_MISSING");
            Assert.Contains(log.Warnings, w => w.Code == "JK_MUSIC");
        }
        finally { Directory.Delete(dir, true); }
    }

    // --- lecture des COG ---

    [Fact]
    public void Find_ReadsPlaySongArgumentsFromTheInstance()
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "13 13 12"));
        JkMusic.Song? song = JkMusic.Find(e.Prepared.Level, f => ScriptOf(e, f));
        Assert.Equal(new JkMusic.Song(13, 13, 12, "00_soundtrack.cog", false), song);
    }

    [Fact]
    public void Find_SingleArgumentPlaySong_IsOneTrack()
    {
        using Env e = Make(MusicLevel("levelsong.cog", LevelSongCog, "21"));
        Assert.Equal(new JkMusic.Song(21, 21, 21, "levelsong.cog", false), JkMusic.Find(e.Prepared.Level, f => ScriptOf(e, f)));
    }

    [Fact]
    public void Find_LiteralPlaySong_IsRead()
    {
        const string literal = "symbols\nmessage startup\nend\ncode\nstartup:\nPlaySong(6, 7, 6);\nreturn;\nend\n";
        using Env e = Make(MusicLevel("06a_startlevel.cog", literal, ""));
        Assert.Equal(new JkMusic.Song(6, 7, 6, "06a_startlevel.cog", false), JkMusic.Find(e.Prepared.Level, f => ScriptOf(e, f)));
    }

    [Fact]
    public void Find_ScriptThatMutesAtStartup_IsFlagged()
    {
        using Env e = Make(MusicLevel("01_SoundTrack.cog", FadedSoundtrackCog, "12 13 12"));
        Assert.True(JkMusic.Find(e.Prepared.Level, f => ScriptOf(e, f))!.StartsMuted);
    }

    [Fact]
    public void Find_NoPlaySong_ReturnsNull()
    {
        using Env e = Make(MusicLevel("duck.cog", DuckingCog, ""));
        Assert.Null(JkMusic.Find(e.Prepared.Level, f => ScriptOf(e, f)));
    }

    // --- DSP et wav ---

    [Fact]
    public void Resample_44100To22050_HalvesTheLengthAndKeepsDc()
    {
        float[] src = new float[44100 * 2];
        Array.Fill(src, 0.5f);
        short[] dst = JkMusic.ToStereo16(new PcmAudio(44100, 2, src), 22050);
        Assert.InRange(dst.Length / 2, 22049, 22051);
        for (int i = 400; i < dst.Length - 400; i++) Assert.InRange(dst[i], 16300, 16470); // 0,5 * 32767
    }

    [Fact]
    public void Resample_KeepsALowToneAndRemovesAToneAboveTheNewNyquist()
    {
        static float[] Tone(double hz, int frames)
        {
            var s = new float[frames * 2];
            for (int i = 0; i < frames; i++) s[2 * i] = s[2 * i + 1] = (float)(0.5 * Math.Sin(2 * Math.PI * hz * i / 44100));
            return s;
        }
        static double Peak(short[] pcm) { double p = 0; for (int i = 2000; i < pcm.Length - 2000; i += 2) p = Math.Max(p, Math.Abs(pcm[i])); return p / 32767; }
        double low = Peak(JkMusic.ToStereo16(new PcmAudio(44100, 2, Tone(1000, 44100)), 22050));
        double high = Peak(JkMusic.ToStereo16(new PcmAudio(44100, 2, Tone(16000, 44100)), 22050));   // 16 kHz : replie sans filtre
        Assert.InRange(low, 0.45, 0.52);
        Assert.True(high < 0.03, $"16 kHz tone must be filtered out, peak {high}");
    }

    [Fact]
    public void Resample_MonoSourceBecomesStereo_AndClips()
    {
        short[] pcm = JkMusic.ToStereo16(new PcmAudio(22050, 1, [2f, -2f, 0.25f]), 22050);
        Assert.Equal(6, pcm.Length);
        Assert.Equal([32767, 32767, -32768, -32768, 8192, 8192], [pcm[0], pcm[1], pcm[2], pcm[3], (int)pcm[4], (int)pcm[5]]);
    }

    [Fact]
    public void Wav_HasAPcmStereoHeader()
    {
        byte[] wav = JkMusic.WavFile([1, -2, 3, -4], 22050);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal("WAVEfmt ", System.Text.Encoding.ASCII.GetString(wav, 8, 8));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));       // PCM
        Assert.Equal(2, BitConverter.ToInt16(wav, 22));       // stereo
        Assert.Equal(22050, BitConverter.ToInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(8, BitConverter.ToInt32(wav, 40));
        Assert.Equal(52, wav.Length);
    }

    // --- conversion d'un niveau ---

    [Fact]
    public void Convert_TwoTrackCycle_WritesOneWavInPlayingOrderAndSetsTheWorldKey()
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "13 13 12"));
        var tracks = new Dictionary<int, byte[]> { [12] = FakeTrack(20, 2), [13] = FakeTrack(60, 3) };
        ConvertedEntities c = JkMusic.Convert(e.Prepared, e.Fs, e.Opt, e.Log, n => tracks.GetValueOrDefault(n), FakeDecode);

        SoundAsset wav = Assert.Single(c.Sounds);
        Assert.Equal("music/jk/synthetic.wav", wav.Path);
        Assert.Equal("music/jk/synthetic.wav", c.WorldKeys["music"]);
        short[] pcm = Pcm(wav.Data);
        Assert.Equal(5 * 22050 * 2, pcm.Length);                       // 3 s de la piste 13 puis 2 s de la piste 12
        Assert.InRange(pcm[22050], 19500, 19700);                      // 0,6 * 32767 : la piste 13 d'abord
        Assert.InRange(pcm[3 * 22050 * 2 + 22050], 6450, 6650);        // 0,2 * 32767 : puis la piste 12
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_MUSIC" && w.Message.Contains("13, 12") && w.Message.Contains("music/jk/synthetic.wav"));
    }

    [Fact]
    public void Convert_MissingTrack_IsSkippedAndLogged()
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "13 13 12"));
        ConvertedEntities c = JkMusic.Convert(e.Prepared, e.Fs, e.Opt, e.Log, n => n == 12 ? FakeTrack(20, 2) : null, FakeDecode);

        Assert.Equal(2 * 22050 * 2, Pcm(Assert.Single(c.Sounds).Data).Length);
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_MUSIC_TRACK_MISSING" && w.Message.Contains("13"));
    }

    [Fact]
    public void Convert_NoTrackFile_PlacesNothingAndSaysSo()
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "6 7 6"));
        ConvertedEntities c = JkMusic.Convert(e.Prepared, e.Fs, e.Opt, e.Log, _ => null, FakeDecode);

        Assert.Empty(c.Sounds);
        Assert.Empty(c.WorldKeys);
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_MUSIC_TRACK_MISSING" && w.Message.Contains("6, 7"));
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_MUSIC_NONE" && w.Message.Contains("no track file"));
    }

    [Fact]
    public void Convert_UnreadableTrack_IsSkippedAndLogged()
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "12 13 12"));
        var tracks = new Dictionary<int, byte[]> { [12] = FakeTrack(20, 1), [13] = [1, 2, 3] };
        ConvertedEntities c = JkMusic.Convert(e.Prepared, e.Fs, e.Opt, e.Log, n => tracks.GetValueOrDefault(n), FakeDecode);

        Assert.Equal(22050 * 2, Pcm(Assert.Single(c.Sounds).Data).Length);
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_MUSIC_UNREADABLE" && w.Message.Contains("13"));
    }

    [Fact]
    public void Convert_LevelWithoutMusicScript_LogsNone()
    {
        using Env e = Make(new SyntheticLevel { Sectors = 2 });
        ConvertedEntities c = JkMusic.Convert(e.Prepared, e.Fs, e.Opt, e.Log, _ => throw new InvalidOperationException("no track must be read"), FakeDecode);

        Assert.Empty(c.Sounds);
        Assert.Empty(c.WorldKeys);
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_MUSIC_NONE" && w.Message.Contains("no music script"));
    }

    [Fact]
    public void Convert_MutedStartAndDuckingScripts_AreLogged()
    {
        using Env e = Make(MusicLevel("01_SoundTrack.cog", FadedSoundtrackCog, "12 13 12", ("duck.cog", DuckingCog, "")));
        JkMusic.Convert(e.Prepared, e.Fs, e.Opt, e.Log, n => FakeTrack(10, 1), FakeDecode);

        var w = Assert.Single(e.Log.Warnings, x => x.Code == "JK_MUSIC_VOLUME_SCRIPT");
        Assert.Contains("2 COG script(s)", w.Message);
        Assert.Contains("starts muted", w.Message);
    }

    [Fact]
    public void Objects_WithoutTheGame_KeepTheLevelAndLogMissingTracks()
    {
        using Env e = Make(MusicLevel("00_soundtrack.cog", SoundtrackCog, "13 13 12"));   // opt.JediKnightDir est vide : aucune piste lisible
        var (converted, _) = Run(e);

        Assert.DoesNotContain(converted.Sounds, s => s.Path.StartsWith("music/", StringComparison.Ordinal));
        Assert.False(converted.WorldKeys.ContainsKey("music"));
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_MUSIC_TRACK_MISSING");
    }

    // --- donnees reelles ---

    private static readonly string[] RealSequences =
    [
        "01narshadda=12,13", "02narshadda=13,12", "03katarn=14,15", "04escapehouse=15,14", "06bbarons=12,13", "07yun=18",
        "08escape88=17,16", "10cargo=23,24,22", "11gorc=28", "12escape=24,22,23", "14tower=25,26,27", "15maw=29",
        "16aescapeship=28", "16bescapeship=28", "17asarris=30", "17bsarris=30", "18ascend=26,27,25", "19a=27,25,26", "19b=27,25,26",
        "20aboc=31", "20bboc=31", "21ajerec=32", "21bjerec=32",
    ];

    [SkippableFact]
    public void RealGame_Track12_DecodesAsStereo44k()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "MUSIC", "Track12.ogg")), "Jedi Knight music is not installed");
        PcmAudio pcm = JkMusic.DecodeOgg(File.ReadAllBytes(Path.Combine(JkDir, "MUSIC", "Track12.ogg")));
        Assert.Equal(44100, pcm.SampleRate);
        Assert.Equal(2, pcm.Channels);
        Assert.InRange(pcm.Samples.Length / 2 / 44100.0, 253.0, 254.5);   // 253,8 s (derniere page Ogg)
        Assert.True(pcm.Samples.Max(Math.Abs) > 0.1f, "the track must not be silent");
    }

    [SkippableFact]
    public void RealGame_EveryLevelPlaysTheExpectedTracks()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        var expected = RealSequences.ToDictionary(s => s.Split('=')[0], s => s.Split('=')[1]);
        int withMusic = 0, withoutScript = 0, missingTracks = 0;
        foreach (JkLevelRef level in JkInstall.ListLevels(JkDir))
        {
            using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, level.EpisodeGob);
            var jkl = Jk.Core.Jkl.JklReader.Read(System.Text.Encoding.Latin1.GetString(fs.TryRead($@"jkl\{level.Name}.jkl")!));
            var scripts = new Dictionary<string, CogScript?>(StringComparer.OrdinalIgnoreCase);
            CogScript? Script(string f) => scripts.TryGetValue(f, out var s) ? s : scripts[f] = fs.TryRead(@"cog\" + f) is { } raw ? CogReader.Read(System.Text.Encoding.Latin1.GetString(raw)) : null;
            JkMusic.Song? song = JkMusic.Find(jkl, Script);
            if (song is null) { withoutScript++; Assert.DoesNotContain(level.Name, expected.Keys); continue; }
            string seq = string.Join(',', JkMusic.Sequence(song, out _));
            if (expected.TryGetValue(level.Name, out string? want))
            {
                Assert.Equal(want, seq);
                foreach (int t in JkMusic.Sequence(song, out _)) Assert.True(File.Exists(Path.Combine(JkDir, "MUSIC", $"Track{t}.ogg")), $"{level.Name}: Track{t}.ogg");
                withMusic++;
            }
            else
            {
                // 06abarons (pistes 6 et 7) et 09fuelstation (pistes 2 a 4) : les pistes ne sont pas dans MUSIC.
                Assert.Contains(level.Name, new[] { "06abarons", "09fuelstation" });
                foreach (int t in JkMusic.Sequence(song, out _)) Assert.False(File.Exists(Path.Combine(JkDir, "MUSIC", $"Track{t}.ogg")), $"{level.Name}: Track{t}.ogg");
                missingTracks++;
            }
        }
        Assert.Equal(23, withMusic);
        Assert.Equal(2, missingTracks);
        Assert.Equal(9, withoutScript);   // 6 multijoueur + 3 CTF
    }

    [SkippableFact]
    public void RealGame_07Yun_ProducesOneWavOfTheTrackLength()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "MUSIC", "Track18.ogg")), "Jedi Knight music is not installed");
        JkLevelRef level = JkInstall.ListLevels(JkDir).Single(l => l.Name == "07yun");
        var log = new ImportLog();
        using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, level.EpisodeGob);
        JkPreparedScene prepared = JkSceneBuilder.Build(fs, level, log);
        var opt = new ExportOptions("", Path.Combine(Path.GetTempPath(), "jkmusic_unused"), Path.GetTempPath(), Game: Kotor.Core.KotorGame.JediKnight) { JediKnightDir = JkDir };

        ConvertedEntities c = JkMusic.Convert(prepared, fs, opt, log);

        SoundAsset wav = Assert.Single(c.Sounds);
        Assert.Equal("music/jk/07yun.wav", wav.Path);
        Assert.Equal(wav.Path, c.WorldKeys["music"]);
        Assert.InRange((wav.Data.Length - 44) / (22050.0 * 4), 130.0, 131.5);   // Track18 : 130,7 s
        Assert.Equal(22050, BitConverter.ToInt32(wav.Data, 24));
    }
}
