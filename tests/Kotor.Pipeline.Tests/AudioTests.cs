using System.IO.Compression;
using System.Numerics;
using System.Text.RegularExpressions;
using Kotor.Compiler.Bsp;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline;
using Kotor.Pipeline.Entities;
using Xunit;

namespace Kotor.Pipeline.Tests;

public sealed class AudioTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "k2j_audio_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static ConvertedEntities With(string soundPath, string worldKey = "music") =>
        new([new Dictionary<string, string> { ["classname"] = "target_speaker" }], [], [])
        {
            Sounds = [new SoundAsset(soundPath, [1, 2, 3])],
            WorldKeys = new Dictionary<string, string> { [worldKey] = soundPath },
        };

    [Fact]
    public void Merge_keeps_each_sound_once_and_the_first_world_key()
    {
        var a = With("sound/kotor/a.mp3");
        var b = With("sound/kotor/a.mp3");
        var c = With("music/kotor/m.mp3");
        ConvertedEntities m = a.Merge(b).Merge(c);
        Assert.Equal(["music/kotor/m.mp3", "sound/kotor/a.mp3"], m.Sounds.Select(s => s.Path));
        Assert.Equal("sound/kotor/a.mp3", m.WorldKeys["music"]);
        Assert.Equal(3, m.Entities.Count);
    }

    [Fact]
    public void Merge_with_only_sounds_is_not_ignored()
    {
        var onlySounds = new ConvertedEntities([], [], []) { Sounds = [new SoundAsset("sound/kotor/x.wav", [1])] };
        Assert.Single(ConvertedEntities.Empty.Merge(onlySounds).Sounds);
    }

    [SkippableFact]
    public void Real_tar_m02aa_exports_music_ambience_and_door_sounds()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR n'est pas installe.");
        var opt = new ExportOptions(TestEnv.KotorDir!, Path.Combine(_root, "base"), Path.Combine(_root, "work"));
        Directory.CreateDirectory(opt.JaBaseDir);
        var log = new ImportLog();
        ExportResult result = ModuleExporter.Export("tar_m02aa", null, opt, log);
        Assert.True(result.Ok, result.Error);

        using var z = ZipFile.OpenRead(result.Pk3Path!);
        var names = z.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
        using var ms = new MemoryStream();
        using (var s = z.GetEntry("maps/tar_m02aa.bsp")!.Open()) s.CopyTo(ms);
        string entities = BspReader.Read(ms.ToArray()).Entities;

        // Musique : MusicDay = 15 -> ambientmusic.2da -> streammusic.
        string music = Regex.Match(entities, "\"music\" \"([^\"]+)\"").Groups[1].Value;
        Assert.StartsWith("music/kotor/", music);
        Assert.Contains(music, names);
        Assert.Equal(0xFF, z.GetEntry(music)!.Open().ReadByte());

        // Tous les sons references existent dans le pk3.
        var noises = Regex.Matches(entities, "\"noise\" \"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(noises);
        Assert.All(noises, n => Assert.Contains(n, names));

        // Les portes mobiles ont un son d'ouverture (target) vers un haut-parleur qui existe.
        var doors = Regex.Matches(entities, @"\{[^}]*""classname"" ""func_door""[^}]*\}").Select(m => m.Value).ToList();
        var withTarget = doors.Where(d => d.Contains("\"target\" \"kd")).ToList();
        Assert.NotEmpty(withTarget);
        foreach (string d in withTarget)
        {
            string target = Regex.Match(d, @"""target"" ""(kd\d+o)""").Groups[1].Value;
            Assert.Contains($"\"targetname\" \"{target}\"", entities);
        }
        Assert.DoesNotContain(doors, d => d.Contains("\"spawnflags\" \"16\"") && d.Contains("\"target\""));
        Assert.Contains(log.Warnings, w => w.Code == "AUDIO");
    }
}
